import {execFileSync} from 'node:child_process';
import {appendFileSync, existsSync, readFileSync} from 'node:fs';
import {pathToFileURL} from 'node:url';

// 只将项目明确登记的说明文档视为轻量改动；未知路径和构建输入默认完整检查。
export function classify(files, config, force = false) {
  const docs = path => config.files.includes(path) ||
    config.markdownDirectories.some(dir => path.startsWith(dir) && /\.md$/i.test(path)) ||
    (config.rootMarkdown && !path.includes('/') && /\.md$/i.test(path) && !config.runtimeDocuments.includes(path));
  return {code: force || files.length === 0 || files.some(path => !docs(path)), count: files.length};
}

export function changedFiles(eventName, event, git) {
  if (eventName === 'workflow_dispatch' || eventName === 'workflow_call') return null;
  let base, head;
  if (eventName === 'pull_request') {
    base = event.pull_request?.base.sha;
    head = event.pull_request?.head.sha;
  } else if (eventName === 'push') {
    base = event.before;
    head = event.after;
  } else return null;
  if (![base, head].every(sha => /^[a-f0-9]{40}$/.test(sha ?? '') && !/^0+$/.test(sha))) return null;
  if (eventName === 'pull_request') base = git(['merge-base', base, head]).trim();
  // 不识别重命名，确保从程序目录移到文档目录时旧路径仍参与判断。没有 API 文件数量上限。
  return git(['diff', '--no-renames', '--name-only', '-z', base, head, '--']).split('\0').filter(Boolean);
}

export function checkDocuments(files, root = '.') {
  let count = 0;
  for (const file of files.filter(path => /\.md$/i.test(path))) {
    const path = root + '/' + file;
    if (!existsSync(path)) continue; // 删除的文件无内容可检查。
    const content = new TextDecoder('utf-8', {fatal: true}).decode(readFileSync(path));
    if (/^(?:<{7} |={7}$|>{7} )/m.test(content)) throw new Error('文档含未解决的合并标记：' + file);
    count++;
  }
  return count;
}

export function main() {
  const config = JSON.parse(readFileSync('.github/ci-scope.json', 'utf8'));
  const event = process.env.GITHUB_EVENT_PATH ? JSON.parse(readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8')) : {};
  const git = args => execFileSync('git', args, {encoding: 'utf8', maxBuffer: 32 * 1024 * 1024});
  let files;
  try { files = changedFiles(process.env.GITHUB_EVENT_NAME, event, git); }
  catch { console.log('::warning::无法确定完整变更范围，保守运行程序检查。'); files = null; }
  const result = classify(files ?? [], config, files === null);
  const checked = checkDocuments(files ?? git(['ls-files', '-z']).split('\0').filter(Boolean));
  console.log(JSON.stringify({...result, documentsChecked: checked}));
  if (process.env.GITHUB_OUTPUT) appendFileSync(process.env.GITHUB_OUTPUT, 'code=' + result.code + '\n');
  if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY,
    '### 变更范围\n\n' + (result.code ? '运行程序检查（手动执行或未知范围也完整检查）。' : '仅说明文档：跳过程序构建，文档基础检查已完成。') +
    '\n\n变更文件：' + result.count + '；已检查文档：' + checked + '。\n');
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) main();
