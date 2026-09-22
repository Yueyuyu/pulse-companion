// 仅开发时重建图标；正常 Windows 构建使用已保存的 ICO，不依赖 sharp。
const fs = require('node:fs/promises');
const path = require('node:path');
const { createHash } = require('node:crypto');
const root = path.resolve(__dirname, '..');
let sharp;
try { sharp = require('sharp'); }
catch { sharp = require(path.join(root, '.local-cache/icon-tools/node_modules/sharp')); }

const appSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
const traySizes = [16, 20, 24, 28, 32, 40, 48, 64];
const iconRoot = path.join(root, 'assets/icons');
const proofRoot = path.join(root, 'artifacts/pulse-icons');

// 保留上游几何和配色；只移除 macOS 投影/大留白，并按最终像素补偿细线。
function smallArtwork(source, size) {
  const scale = 0.9375;
  const ringWidth = Math.max(56, 1.4 * 1024 / (size * scale));
  const traceWidth = Math.max(26, 1.1 * 1024 / (size * scale));
  return source.replace(/<!--[^]*?-->/g, '')
    .replace(/<filter\b[^]*?<\/filter>/, '')
    .replace(' filter="url(#shadow)"', '')
    .replaceAll('x="100" y="100" width="824" height="824" rx="185.4" ry="185.4"',
      'x="32" y="32" width="960" height="960" rx="216" ry="216"')
    .replace('translate(100 100) scale(0.8046875)', 'translate(32 32) scale(0.9375)')
    .replaceAll('stroke-width="56"', `stroke-width="${ringWidth.toFixed(4)}"`)
    .replace('stroke-width="26"', `stroke-width="${traceWidth.toFixed(4)}"`);
}

function encodeIco(frames) {
  const header = Buffer.alloc(6 + frames.length * 16);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(frames.length, 4);
  let offset = header.length;
  frames.forEach(({ size, payload }, index) => {
    const entry = 6 + index * 16;
    header[entry] = header[entry + 1] = size === 256 ? 0 : size;
    header.writeUInt16LE(1, entry + 4);
    header.writeUInt16LE(32, entry + 6);
    header.writeUInt32LE(payload.length, entry + 8);
    header.writeUInt32LE(offset, entry + 12);
    offset += payload.length;
  });
  return Buffer.concat([header, ...frames.map(frame => frame.payload)]);
}

async function iconPayload(png, size) {
  if (size === 256) return png;
  // .NET Framework 4.x 的 Icon.ToBitmap 对 PNG 帧处理错误；小尺寸必须用原生 32-bit DIB。
  // 保留完整 alpha，并为不支持 alpha 的 Windows 消费方提供逐行对齐的 AND mask。
  const rgba = await sharp(png).ensureAlpha().raw().toBuffer();
  const stride = Math.ceil(size / 32) * 4;
  const dib = Buffer.alloc(40 + size * size * 4 + stride * size);
  dib.writeUInt32LE(40, 0);
  dib.writeInt32LE(size, 4);
  dib.writeInt32LE(size * 2, 8);
  dib.writeUInt16LE(1, 12);
  dib.writeUInt16LE(32, 14);
  dib.writeUInt32LE(size * size * 4, 20);
  for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) {
    const from = (y * size + x) * 4;
    const to = 40 + ((size - 1 - y) * size + x) * 4;
    dib[to] = rgba[from + 2]; dib[to + 1] = rgba[from + 1];
    dib[to + 2] = rgba[from]; dib[to + 3] = rgba[from + 3];
    if (rgba[from + 3] === 0) dib[40 + size * size * 4 + (size - 1 - y) * stride + (x >> 3)] |= 0x80 >> (x & 7);
  }
  return dib;
}

async function render(source, size) {
  return sharp(Buffer.from(source)).resize(size, size, { kernel: 'lanczos3' }).png().toBuffer();
}

async function main() {
  const source = await fs.readFile(path.join(iconRoot, 'upstream/pulse-icon.svg'), 'utf8');
  for (const expected of ['translate(100 100) scale(0.8046875)', 'stroke-width="26"', 'stroke-dasharray="1894.4 2413.6"']) {
    if (!source.includes(expected)) throw new Error('上游图标结构改变，需人工核对适配：' + expected);
  }
  await fs.mkdir(proofRoot, { recursive: true });
  const results = {};
  for (const [name, sizes] of [['app', appSizes], ['tray', traySizes]]) {
    const frames = [];
    for (const size of sizes) {
      const artwork = name === 'tray' || size <= 40 ? smallArtwork(source, size) : source;
      const png = await render(artwork, size);
      frames.push({ size, payload: await iconPayload(png, size) });
      await fs.writeFile(path.join(proofRoot, `${name}-${size}.png`), png);
    }
    const ico = encodeIco(frames);
    await fs.writeFile(path.join(iconRoot, `pulse-${name}.ico`), ico);
    results[name] = { sizes, sha256: createHash('sha256').update(ico).digest('hex') };
  }
  // 这是从最终 ICO 的同一批像素生成的检查板，不是独立重画的效果图。
  const width = 760, height = 320;
  const board = `<svg width="${width}" height="${height}" xmlns="http://www.w3.org/2000/svg">
    <rect width="760" height="160" fill="#f4f4f5"/><rect y="160" width="760" height="160" fill="#202124"/>
    <g font-family="Segoe UI, sans-serif" font-size="14" fill="#555"><text x="24" y="28">PULSE · Windows tray / actual pixels</text></g>
    <g font-family="Segoe UI, sans-serif" font-size="12" fill="#777">${traySizes.map((s, i) => `<text x="${32 + i * 74}" y="130">${s}px</text>`).join('')}</g>
    <g font-family="Segoe UI, sans-serif" font-size="12" fill="#bbb">${traySizes.map((s, i) => `<text x="${32 + i * 74}" y="290">${s}px</text>`).join('')}</g>
    <g font-family="Segoe UI, sans-serif" font-size="12" fill="#888"><text x="647" y="130">App · 80px</text><text x="647" y="290">App · 80px</text></g>
  </svg>`;
  const layers = [];
  for (const row of [0, 160]) {
    for (const [index, size] of traySizes.entries()) {
      layers.push({ input: await fs.readFile(path.join(proofRoot, `tray-${size}.png`)), left: 34 + index * 74, top: row + 74 - Math.floor(size / 2) });
    }
    layers.push({ input: await render(source, 80), left: 645, top: row + 34 });
  }
  await sharp(Buffer.from(board)).composite(layers).png().toFile(path.join(proofRoot, 'windows-icons.png'));
  await fs.writeFile(path.join(proofRoot, 'generation.json'), JSON.stringify({ source: 'Pulse 2e17225ece661138de9ce9c73b322c7c1b16d753', sharp: sharp.versions.sharp, results }, null, 2));
  console.log(JSON.stringify({ generated: results, preview: path.join(proofRoot, 'windows-icons.png') }, null, 2));
}
main().catch(error => { console.error(error.message); process.exitCode = 1; });
