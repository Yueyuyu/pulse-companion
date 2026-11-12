# 提交与推送门禁

每个逻辑改动单独提交。验证对象是 Git 暂存快照，不是可能含有未提交修正的工作目录。

## 一次性启用

需要 Node.js 24、Git 和 actionlint 1.7.12。actionlint 放在 PATH；也可通过 ACTIONLINT 环境变量指定绝对路径，install 会将其保存到本仓库 Git 配置。

```sh
node .github/scripts/preflight.mjs install
```

仅设置本仓库 core.hooksPath，不覆盖已有其他 hooks。新克隆、其他电脑均须执行一次；Git 不会自动启用仓库中的 hooks。

## 每次提交

1. 查看 git status --short 和相关 diff，明确这次授权的文件。
2. 用 git add -- <具体路径> 精确暂存。重命名同时核对所有引用；任何命令失败立即停止，不能接着提交。
3. 查看 git diff --cached --stat 及完整暂存 diff，确认没有夹带或遗漏。
4. 执行下面的检查，通过后再 git commit。
5. 推送前核对待推送提交与远端；git push 自动检查每个新增提交的验证记录。推送后确认对应 SHA 的 Actions，不能把旧提交通过当成新结果。

```sh
node .github/scripts/preflight.mjs check --staged
node .github/scripts/preflight.mjs verify --staged
```

pre-commit 检查暂存版本的工作流静态脚本路径、npm script、本地 action、必需入口及部分暂存文件；必须存在本次 Git 树对应的验证记录。pre-push 再核对待推送的实际提交。验证记录仅保存在 .git/preflight，修改任何暂存内容后旧记录自动失效。已提交版本可运行 verify HEAD 重新执行完整项目检查。

verify 先运行 actionlint 和门禁回归，再根据已登记的文档范围选择项目检查。未知程序路径默认执行 programChecks，不会自动安装、发布、启动用户应用或修改本机代理。完整发布资格验收仍由原发布流程负责；门禁不能代替签名、实机或兼容性验收。

## 保留其他任务的修改

静态 check 允许无关未暂存修改，但 verify 要求测试源码与提交快照一致，且没有未跟踪源码；否则应使用隔离工作树完成验证，不能为了通过检查把其他任务一起提交或清除。node_modules、构建输出等已忽略文件保留。验证不是可重现构建证明，云端仍须用锁文件干净安装。

## 检查边界与维护

配置集中在 .github/preflight.json。引用检查覆盖可静态确定的命令入口，无法证明动态 shell 表达式和运行时生成文件正确；生成产物仅允许有原因的精确路径登记，并须由实际运行检查覆盖。工作区命令的相对目录在 workingDirectories 显式配置。上游停用工作流在 excludedWorkflows 标明原因，重新启用前必须移出排除范围。

不使用 --no-verify 绕过失败，不通过删除测试或放宽范围掩盖错误。Git hooks 是防误操作门禁，不是防恶意修改的安全边界。CI 同时运行提交内容检查与独立 Node 回归；测试文件使用 .checks.mjs，避免被业务 Vitest 自动收集。

PowerShell 调用 git 等外部命令后必须检查 $LASTEXITCODE；不能只依赖 $ErrorActionPreference。没有执行通过的项目检查不登记为完成。
