#!/bin/bash
set -euo pipefail
src="$(cd "$(dirname "$0")" && pwd -P)"
tmp="$(mktemp -d "${TMPDIR:-/tmp}/pulse-companion-test.XXXXXXXX")"
trap 'rm -rf -- "$tmp"' EXIT
if [[ "$(uname -s)" == Darwin ]]; then
  swiftc=(xcrun --sdk macosx swiftc)
else
  command -v swiftc >/dev/null || { echo '核心测试需要 Swift 5.7+；完整 .app/DMG 验收须 macOS。' >&2; exit 1; }
  swiftc=(swiftc)
fi
"${swiftc[@]}" -swift-version 5 -parse-as-library "$src/Tests/StdioFixture.swift" -o "$tmp/stdio-fixture"
"${swiftc[@]}" -swift-version 5 -parse-as-library "$src/Sources/CompanionModel.swift" "$src/Sources/CodexLifecycle.swift" \
  "$src/Sources/CodexRPC.swift" "$src/Tests/CoreTests.swift" -o "$tmp/core-tests"
"$tmp/core-tests" "$tmp/stdio-fixture"
