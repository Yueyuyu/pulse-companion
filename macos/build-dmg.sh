#!/bin/bash
set -euo pipefail

if [[ "$(uname -s)" != Darwin || $# != 1 || -z "$1" ]]; then
  echo '用法（macOS/Xcode）: bash macos/build-dmg.sh <输出目录>' >&2
  exit 64
fi
src="$(cd "$(dirname "$0")" && pwd -P)"
root="$(cd "$src/.." && pwd -P)"
version="$(tr -d '\r\n' < "$root/VERSION")"
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo 'VERSION 不是三段数字' >&2; exit 1; }
mkdir -p "$1"
dest="$(cd "$1" && pwd -P)"
name="Pulse-Companion-${version}-macOS-universal.dmg"
[[ ! -e "$dest/$name" ]] || { echo '输出文件已存在，不覆盖' >&2; exit 1; }
tmp="$(mktemp -d "${TMPDIR:-/tmp}/pulse-companion-build.XXXXXXXX")"
mount="$tmp/mounted"
attached=0
cleanup() {
  # 若卷仍挂载，绝不递归清理挂载点，以免触及卷或 Applications 链接。
  if [[ "$attached" == 1 ]]; then
    if ! hdiutil detach "$mount" -quiet; then echo "卸载失败，保留临时目录: $tmp" >&2; return 1; fi
  fi
  rm -rf -- "$tmp"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
app="$tmp/stage/Pulse Companion.app"
res="$app/Contents/Resources"
exe="$app/Contents/MacOS/PulseCompanion"
mkdir -p "$app/Contents/MacOS" "$res" "$tmp/Pulse.iconset" "$mount"
cp "$src/Info.plist" "$app/Contents/Info.plist"
plutil -replace CFBundleShortVersionString -string "$version" "$app/Contents/Info.plist"
plutil -replace CFBundleVersion -string "$version" "$app/Contents/Info.plist"
plutil -lint "$app/Contents/Info.plist"
cp "$src/NOTICE.txt" "$root/docs/licenses/Pulse-Apache-2.0.txt" "$res/"

# 仅取公开品牌图标。新 SDK 可直接栅格化 SVG；部分系统 ImageIO 不支持 SVG，
# 此时使用同一图标已提交 ICO 里的原始 256 px PNG，不接触其他素材。
if ! sips -s format png "$root/assets/icons/upstream/pulse-icon.svg" --out "$tmp/mark.png" >/dev/null 2>&1 || [[ ! -s "$tmp/mark.png" ]]; then
  /usr/bin/python3 - "$root/assets/icons/pulse-app.ico" "$tmp/mark.png" <<'PY'
from pathlib import Path
import struct, sys
data = Path(sys.argv[1]).read_bytes()
count = struct.unpack_from('<H', data, 4)[0]
for index in range(count):
    width, height, _, _, _, _, size, offset = struct.unpack_from('<4B2H2I', data, 6 + 16 * index)
    frame = data[offset:offset + size]
    if width == height == 0 and frame.startswith(b'\x89PNG\r\n\x1a\n'):
        Path(sys.argv[2]).write_bytes(frame)
        break
else:
    raise SystemExit('Missing public 256px icon frame')
PY
fi
for base in 16 32 128 256 512; do
  sips -z "$base" "$base" "$tmp/mark.png" --out "$tmp/Pulse.iconset/icon_${base}x${base}.png" >/dev/null
  double=$((base * 2))
  sips -z "$double" "$double" "$tmp/mark.png" --out "$tmp/Pulse.iconset/icon_${base}x${base}@2x.png" >/dev/null
done
iconutil -c icns "$tmp/Pulse.iconset" -o "$res/Pulse.icns"

sdk="$(xcrun --sdk macosx --show-sdk-path)"
for arch in arm64 x86_64; do
  xcrun --sdk macosx swiftc -swift-version 5 -O -parse-as-library -sdk "$sdk" -target "${arch}-apple-macosx13.0" \
    "$src"/Sources/*.swift -o "$tmp/PulseCompanion-$arch"
done
xcrun lipo -create "$tmp/PulseCompanion-arm64" "$tmp/PulseCompanion-x86_64" -output "$exe"
xcrun lipo "$exe" -verify_arch arm64 x86_64
chmod 755 "$exe"
# 临时签名只用于完整性和 Apple Silicon 可执行性，不是 Developer ID，不声称已公证。
codesign --force --sign - --timestamp=none "$app"
codesign --verify --strict --all-architectures "$app"
ln -s /Applications "$tmp/stage/Applications"
hdiutil create -quiet -srcfolder "$tmp/stage" -volname "Pulse Companion" -fs HFS+ -format UDZO "$tmp/package.dmg"
hdiutil verify "$tmp/package.dmg"
hdiutil attach -quiet -readonly -nobrowse -mountpoint "$mount" "$tmp/package.dmg"
attached=1
installed="$mount/Pulse Companion.app"
mounted_exe="$installed/Contents/MacOS/PulseCompanion"
[[ -L "$mount/Applications" && "$(readlink "$mount/Applications")" == /Applications ]]
[[ "$(plutil -extract CFBundleShortVersionString raw "$installed/Contents/Info.plist")" == "$version" ]]
[[ -s "$installed/Contents/Resources/Pulse.icns" && -s "$installed/Contents/Resources/NOTICE.txt" && -s "$installed/Contents/Resources/Pulse-Apache-2.0.txt" ]]
xcrun lipo "$mounted_exe" -verify_arch arm64 x86_64
codesign --verify --strict --all-architectures "$installed"
"$mounted_exe" --self-test
hdiutil detach "$mount" -quiet
attached=0
cp "$tmp/package.dmg" "$dest/$name"
shasum -a 256 "$dest/$name"
