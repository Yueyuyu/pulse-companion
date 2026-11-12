import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync, spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const source = fileURLToPath(new URL('./preflight.mjs', import.meta.url));
function fixture(action) {
  const cwd = fs.mkdtempSync(path.join(os.tmpdir(), 'preflight-fixture-'));
  const git = (...args) => execFileSync('git', args, { cwd, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
  const write = (name, data) => { fs.mkdirSync(path.dirname(path.join(cwd, name)), { recursive: true }); fs.writeFileSync(path.join(cwd, name), data); };
  const check = (...args) => spawnSync(process.execPath, [source, ...args], { cwd, encoding: 'utf8' });
  try {
    git('init', '-q'); git('config', 'user.name', 'Fixture'); git('config', 'user.email', 'fixture@example.invalid');
    git('config', 'core.hooksPath', '.disabled-hooks'); git('config', 'commit.gpgsign', 'false');
    write('.github/preflight.json', JSON.stringify({ version: 1, checks: [['node', '-e', 'process.exit(0)']], programChecks: [['node', '-e', 'process.exit(0)']], packageScripts: ['package.json'] }));
    write('package.json', JSON.stringify({ scripts: { test: 'node script.mjs' } })); write('script.mjs', 'console.log("fixture");\n');
    git('add', '.'); git('commit', '-qm', 'fixture');
    action({ cwd, git, write, check });
  } finally { fs.rmSync(cwd, { recursive: true, force: true }); }
}
function failed(result, message) { assert.notEqual(result.status, 0, result.stdout); assert.match(result.stderr, message); }
test('读取暂存快照：本地有脚本但漏暂存仍拒绝', () => fixture(({ write, git, check }) => {
  write('package.json', JSON.stringify({ scripts: { test: 'node missing.mjs' } })); write('missing.mjs', ''); git('add', 'package.json');
  failed(check('check', '--staged'), /missing\.mjs/);
}));
test('重命名只提交脚本、工作流仍指旧文件会拒绝', () => fixture(({ write, git, check }) => {
  write('.github/workflows/ci.yml', 'name: CI\non: push\njobs:\n  test:\n    runs-on: ubuntu-latest\n    steps:\n      - run: node script.mjs\n'); git('add', '.'); git('commit', '-qm', 'workflow');
  git('mv', 'script.mjs', 'renamed.mjs'); write('.github/workflows/ci.yml', 'name: CI\non: push\njobs:\n  test:\n    runs-on: ubuntu-latest\n    steps:\n      - run: node renamed.mjs\n');
  failed(check('check', '--staged'), /script\.mjs/);
}));
test('同一文件部分暂存会明确拦截', () => fixture(({ write, git, check }) => {
  write('script.mjs', '// staged\n'); git('add', 'script.mjs'); write('script.mjs', '// not staged\n');
  failed(check('check', '--staged'), /未暂存修改/);
}));
test('缺失 npm script 与本地 action 会拒绝', () => fixture(({ write, git, check }) => {
  write('.github/workflows/ci.yml', 'name: CI\non: push\njobs:\n  test:\n    runs-on: ubuntu-latest\n    steps:\n      - uses: ./.github/actions/missing\n      - run: npm run not-found\n'); git('add', '.');
  failed(check('check', '--staged'), /本地 action 不存在/);
  failed(check('check', '--staged'), /not-found/);
}));
test('引用有空格的脚本路径仍会检查', () => fixture(({ write, git, check }) => {
  write('package.json', JSON.stringify({ scripts: { test: 'node "scripts/missing file.mjs"' } })); git('add', '.');
  failed(check('check', '--staged'), /missing file\.mjs/);
}));
test('无关未暂存修改不影响静态检查，但不能混入测试结果', () => fixture(({ write, git, check }) => {
  write('README.md', 'scope\n'); git('add', 'README.md'); write('script.mjs', '// other task\n');
  assert.equal(check('check', '--staged').status, 0);
  failed(check('verify', '--staged'), /Git 快照不一致/);
}));
test('检查失败不产生验证记录', () => fixture(({ cwd, write, git, check }) => {
  write('.github/preflight.json', JSON.stringify({ version: 1, checks: [['node', '-e', 'process.exit(7)']] })); git('add', '.');
  failed(check('verify', '--staged'), /检查失败/);
  assert.equal(fs.existsSync(path.join(cwd, '.git/preflight')), false);
}));
test('验证绑定精确 Git 树，改动后旧记录不能放行', () => fixture(({ write, git, check }) => {
  write('script.mjs', '// ready\n'); git('add', '.');
  const verified = check('verify', '--staged'); assert.equal(verified.status, 0, verified.stderr);
  assert.equal(check('commit', '--staged').status, 0);
  write('script.mjs', '// newer\n'); git('add', '.');
  failed(check('commit', '--staged'), /还没有通过/);
}));
test('动态生成文件只有显式登记原因后才允许', () => fixture(({ write, git, check }) => {
  write('package.json', JSON.stringify({ scripts: { test: 'node dist/generated.mjs' } })); git('add', '.');
  failed(check('check', '--staged'), /dist\/generated/);
  write('.github/preflight.json', JSON.stringify({ version: 1, checks: [['node', '-e', '']], packageScripts: ['package.json'], generated: { 'dist/generated.mjs': '构建步骤生成，由运行测试验证' } })); git('add', '.');
  assert.equal(check('check', '--staged').status, 0);
}));
test('不覆盖已有 Git hooks', () => fixture(({ check }) => {
  failed(check('install'), /已有 hooksPath/);
}));

test('推送检查拒绝未验证提交，验证过的精确提交才放行', () => fixture(({ cwd, write, git, check }) => {
  const old = git('rev-parse', 'HEAD');
  write('script.mjs', '// changed\n'); git('add', '.'); git('commit', '-qm', 'unverified');
  const current = git('rev-parse', 'HEAD');
  const push = () => spawnSync(process.execPath, [source, 'push'], { cwd, encoding: 'utf8', input: `refs/heads/main ${current} refs/heads/main ${old}\n` });
  failed(push(), /还没有通过/);
  const verified = check('verify', 'HEAD'); assert.equal(verified.status, 0, verified.stderr);
  assert.equal(push().status, 0);
}));

test('多个静态脚本分别检查，不把整个命令误当文件名', () => fixture(({ write, git, check }) => {
  write('package.json', JSON.stringify({ scripts: { test: 'node --test script.mjs missing.mjs' } })); git('add', '.');
  failed(check('check', '--staged'), /missing\.mjs/);
  write('missing.mjs', ''); git('add', '.'); assert.equal(check('check', '--staged').status, 0);
}));
