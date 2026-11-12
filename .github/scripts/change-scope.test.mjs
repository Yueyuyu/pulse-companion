import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {classify, changedFiles, checkDocuments} from './change-scope.mjs';
const config = JSON.parse(readFileSync(new URL('../ci-scope.json', import.meta.url)));
test('README 轻量，混合改动和未知输入运行程序检查', () => {
 assert.equal(classify(['README.md'], config).code, false);
 assert.equal(classify(['README.md', 'src/main.ts'], config).code, true);
 assert.equal(classify(['package-lock.json'], config).code, true);
 assert.equal(classify(['.github/workflows/ci.yml'], config).code, true);
 assert.equal(classify([], config).code, true);
 assert.equal(classify(['README.md'], config, true).code, true);
 for (const path of config.runtimeDocuments) assert.equal(classify([path], config).code, true, path);
});
test('大量文档之后的代码不会遗漏，重命名两侧都参与判断', () => {
 assert.equal(classify([...Array(501).fill('README.md'), 'src/main.ts'], config).code, true);
 assert.equal(classify(['src/config.md', 'docs/config.md'], config).code, true);
});
test('PR 使用 merge-base，push 使用整个提交范围，保留含空格的路径', () => {
 const base = 'a'.repeat(40), head = 'b'.repeat(40);
 const calls = [];
 const git = args => { calls.push(args); return args[0] === 'merge-base' ? 'c'.repeat(40) : 'README.md\0src/a b.ts\0'; };
 assert.deepEqual(changedFiles('pull_request', {pull_request:{base:{sha:base},head:{sha:head}}}, git), ['README.md','src/a b.ts']);
 assert.equal(calls[1][4], 'c'.repeat(40));
 assert.ok(calls[1].includes('--no-renames'));
 calls.length = 0;
 changedFiles('push', {before:base,after:head}, git);
 assert.equal(calls[0][4], base);
});
test('手动执行、新建分支和未知事件完整检查', () => {
 const git = () => { throw new Error('不应执行 diff'); };
 assert.equal(changedFiles('workflow_dispatch', {}, git), null);
 assert.equal(changedFiles('push', {before:'0'.repeat(40),after:'a'.repeat(40)}, git), null);
 assert.equal(changedFiles('merge_group', {}, git), null);
 assert.equal(checkDocuments(['deleted-not-present.md']), 0);
});
