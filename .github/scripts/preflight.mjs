// 跨仓库提交门禁。只读取 Git 快照；验证记录留在 .git，不自动暂存或提交文件。
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { execFileSync, spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const CONFIG = '.github/preflight.json';
function git(...args) { return execFileSync('git', args, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 }).trimEnd(); }
function list(...args) { return git(...args).split('\0').filter(Boolean); }
function fail(message) { throw new Error(message); }
function tree(ref) { return ref === '--staged' ? git('write-tree') : git('rev-parse', `${ref}^{tree}`); }
function read(snapshot, name) { return git('show', `${snapshot}:${name}`); }
function changed(snapshot) {
  return list('diff', '--name-only', '--no-renames', '-z', 'HEAD', snapshot);
}
function cleanFor(snapshot) {
  // 允许静态检查脏工作树，但运行测试必须与将提交的快照完全一致。
  // 有其他任务未提交时使用隔离工作树，不能自动把它们一起提交。
  const differences = list('diff', '--name-only', '-z', snapshot);
  const extra = list('ls-files', '--others', '--exclude-standard', '-z');
  if (differences.length || extra.length) fail(`测试源与 Git 快照不一致，请精确暂存或在隔离工作树验证：\n${[...differences, ...extra].join('\n')}`);
}
function executable(command, args) {
  // npm/pnpm 的 Windows 入口是 cmd，参数来自已审核的仓库配置，不来自网络输入。
  if (process.platform === 'win32' && ['npm', 'pnpm'].includes(command)) {
    if (args.some(value => /[^\w:./=@-]/.test(value))) fail('包管理器参数必须为静态安全参数');
    return { command: process.env.ComSpec || 'cmd.exe', args: ['/d', '/s', '/c', `${command} ${args.join(' ')}`] };
  }
  return { command: command === 'node' ? process.execPath : command, args };
}
function run(command, args, options = {}) {
  const resolved = executable(command, args);
  const result = spawnSync(resolved.command, resolved.args, { stdio: 'inherit', ...options });
  if (result.error || result.status !== 0) fail(`检查失败：${command} ${args.join(' ')} (${result.error?.message || result.status})`);
}
function commandsFromWorkflow(text) {
  const result = [];
  const lines = text.split(/\r?\n/);
  for (let i = 0; i < lines.length; i++) {
    const match = lines[i].match(/^(\s*)(?:-\s+)?run:\s*(.*)$/);
    if (!match) continue;
    if (!/^[|>][+-]?\s*$/.test(match[2])) result.push(match[2].replace(/^(["'])(.*)\1$/, '$2'));
    else {
      const indent = lines[i].search(/run:/);
      while (i + 1 < lines.length && (!lines[i + 1].trim() || lines[i + 1].search(/\S/) > indent)) result.push(lines[++i].trim());
    }
  }
  return result.join('\n');
}
function validate(snapshot) {
  const files = new Set(list('ls-tree', '-r', '--name-only', '-z', snapshot));
  const config = JSON.parse(read(snapshot, CONFIG));
  if (config.version !== 1 || !Array.isArray(config.checks) || !config.checks.length) fail('提交检查配置缺少有效检查命令');
  const errors = [];
  const requireFile = (name, source) => {
    const normalized = name.replaceAll('\\', '/').replace(/^\.\//, '');
    if (config.generated?.[normalized]) return;
    if (normalized.includes('node_modules/')) return; // 安装产物由锁文件安装及真实测试验证。
    if (/[${}*?]/.test(normalized)) return;
    if (!files.has(normalized)) errors.push(`${source} 引用了未提交或不存在的文件：${normalized}`);
  };
  const scan = (command, source) => {
    // 只解析可静态确定的本地脚本；动态表达式、下载产物须经运行检查。
    const pattern = /"([^"\n]+)"|'([^'\n]+)'|([^\s"';&|`]+)/g;
    for (const match of command.matchAll(pattern)) {
      const token = (match[1] || match[2] || match[3]).replaceAll('\\', '/');
      if (!/^(?:\.?\.?\/)?[\w@.-]+(?:\/[\w@. -]+)*\.(?:mjs|cjs|js|ts|tsx|ps1|sh|py)$/.test(token)) continue;
      const base = config.workingDirectories?.[source] || '.';
      requireFile(path.posix.normalize(path.posix.join(base, token)), source);
    }
  };
  for (const name of config.required || []) requireFile(name, CONFIG);
  for (const name of files) {
    if (!/^\.github\/workflows\/[^/]+\.ya?ml$/.test(name)) continue;
    if (config.excludedWorkflows?.[name]) continue;
    const text = read(snapshot, name);
    scan(commandsFromWorkflow(text), name);
    for (const match of text.matchAll(/\buses:\s*["']?(\.\/[^\s"']+)/g)) {
      const local = match[1].slice(2);
      if (/\.ya?ml$/.test(local)) requireFile(local, name);
      else if (!files.has(`${local}/action.yml`) && !files.has(`${local}/action.yaml`)) errors.push(`${name} 的本地 action 不存在：${local}`);
    }
    // 根目录 npm run 引用；工作区命令由 packageScripts 显式检查与项目测试覆盖。
    const rootPackage = files.has('package.json') ? JSON.parse(read(snapshot, 'package.json')) : {};
    for (const match of commandsFromWorkflow(text).matchAll(/\bnpm run ([\w:-]+)/g)) {
      if (!rootPackage.scripts?.[match[1]]) errors.push(`${name} 引用了不存在的 npm script：${match[1]}`);
    }
  }
  for (const name of config.packageScripts || []) {
    requireFile(name, CONFIG);
    if (!files.has(name)) continue;
    const pkg = JSON.parse(read(snapshot, name));
    // 根目录脚本的路径按根目录解释；子包入口由 required 和项目检查约束。
    if (name === 'package.json') for (const [key, command] of Object.entries(pkg.scripts || {})) {
      scan(command, `${name}#${key}`);
      for (const match of command.matchAll(/\bnpm run ([\w:-]+)/g)) if (!pkg.scripts?.[match[1]]) errors.push(`${name}#${key} 缺少脚本 ${match[1]}`);
    }
  }
  if (errors.length) fail([...new Set(errors)].join('\n'));
  git('diff', '--check', 'HEAD', snapshot);
  console.log(`快照引用检查通过：${snapshot.slice(0, 12)}`);
  return config;
}
function receiptPath(snapshot) {
  return path.resolve(git('rev-parse', '--git-path', `preflight/${snapshot}.json`));
}
function requireReceipt(snapshot) {
  let receipt;
  try { receipt = JSON.parse(fs.readFileSync(receiptPath(snapshot), 'utf8')); } catch { fail('这份代码还没有通过提交前验证。请运行 node .github/scripts/preflight.mjs verify --staged，再提交。'); }
  if (receipt.tree !== snapshot || receipt.version !== 1) fail('验证记录与当前代码不一致');
}
function lintWorkflows(snapshot, config) {
  const configured = spawnSync('git', ['config', '--get', 'preflight.actionlint'], { encoding: 'utf8' }).stdout.trim();
  const executablePath = process.env.ACTIONLINT || configured || 'actionlint';
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'repo-preflight-'));
  try {
    const files = list('ls-tree', '-r', '--name-only', '-z', snapshot).filter(name => /^\.github\/workflows\/[^/]+\.ya?ml$/.test(name) && !config.excludedWorkflows?.[name]);
    const inputs = files.map(name => { const dest = path.join(temp, path.basename(name)); fs.writeFileSync(dest, read(snapshot, name)); return dest; });
    if (inputs.length) run(executablePath, ['-shellcheck=', '-pyflakes=', ...inputs]);
  } finally { fs.rmSync(temp, { recursive: true, force: true }); }
}
function verify(snapshot) {
  cleanFor(snapshot);
  const config = validate(snapshot);
  lintWorkflows(snapshot, config);
  const paths = changed(snapshot);
  // 已提交代码再次验证时无法仅凭空 diff 判定范围，保守执行完整项目检查。
  const documentation = config.documentation || {};
  const maintenance = paths.length && paths.every(name => name.startsWith('.github/') || name.startsWith('.githooks/') || name === 'AGENTS.md' ||
    (!documentation.runtimeDocuments?.includes(name) && (documentation.files?.includes(name) ||
    (documentation.rootMarkdown && /^[^/]+\.md$/.test(name)) ||
    (name.endsWith('.md') && documentation.markdownDirectories?.some(directory => name.startsWith(directory))))));
  const checks = [...config.checks, ...(maintenance ? [] : config.programChecks || [])];
  if (!maintenance && !config.programChecks?.length) fail('未配置程序检查，不能签发程序改动的验证记录');
  fs.rmSync(receiptPath(snapshot), { force: true });
  for (const [command, ...args] of checks) run(command, args);
  cleanFor(snapshot);
  if (tree('--staged') !== snapshot) fail('验证期间暂存区已变化，请重新检查');
  const destination = receiptPath(snapshot);
  fs.mkdirSync(path.dirname(destination), { recursive: true });
  fs.writeFileSync(destination, JSON.stringify({ version: 1, tree: snapshot, checkedAt: new Date().toISOString(), checks, maintenance: Boolean(maintenance) }, null, 2));
  console.log('验证通过，记录已绑定 Git 树；修改暂存内容后必须重新验证。');
}
function install() {
  const current = spawnSync('git', ['config', '--get', 'core.hooksPath'], { encoding: 'utf8' }).stdout.trim();
  if (current && current !== '.githooks') fail(`已有 hooksPath=${current}，请人工整合，未覆盖`);
  const existing = path.resolve(git('rev-parse', '--git-path', 'hooks'));
  for (const name of ['pre-commit', 'pre-push']) if (!current && fs.existsSync(path.join(existing, name))) fail(`已有 ${name}，请人工整合，未覆盖`);
  git('config', '--local', 'core.hooksPath', '.githooks');
  if (process.env.ACTIONLINT) git('config', '--local', 'preflight.actionlint', process.env.ACTIONLINT);
  console.log('已启用本仓库 pre-commit / pre-push；未改变全局 Git 设置。');
}
function main() {
  process.chdir(git('rev-parse', '--show-toplevel'));
  const [command = 'check', ref = 'HEAD'] = process.argv.slice(2);
  if (command === 'install') return install();
  if (command === 'push') {
    for (const line of fs.readFileSync(0, 'utf8').trim().split('\n').filter(Boolean)) {
      const [, local, , remote] = line.trim().split(/\s+/);
      if (/^0+$/.test(local)) continue;
      const commits = (/^0+$/.test(remote) ? git('rev-list', local, '--not', '--remotes') : git('rev-list', `${remote}..${local}`)).split('\n').filter(Boolean);
      if (!commits.length) commits.push(git('rev-parse', `${local}^{commit}`));
      for (const commit of commits) { const snapshot = tree(commit); validate(snapshot); requireReceipt(snapshot); }
    }
    return;
  }
  const snapshot = tree(ref);
  if (command === 'verify') return verify(snapshot);
  validate(snapshot);
  if (ref === '--staged') {
    const partial = list('diff', '--name-only', '-z').filter(name => changed(snapshot).includes(name));
    if (partial.length) fail(`这些文件仍有未暂存修改，可能漏提交：\n${partial.join('\n')}`);
  }
  if (command === 'commit') requireReceipt(snapshot);
  else if (command !== 'check') fail(`未知命令：${command}`);
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try { main(); } catch (error) { console.error(`提交门禁未通过：${error.message}`); process.exitCode = 1; }
}
