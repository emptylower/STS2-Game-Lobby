# Alpha 1 续局修复回滚记录

- 原始 Alpha 1 基线：`736a88c`。
- 本次开始前的工作区快照：`cba4604`，包含此前未提交的续局状态修复。
- 回滚分支：`codex/rollback-alpha1-before-resume-review`。
- 修复工作分支：`codex/alpha1-resume-retry`。
- 历史临时目录备份：`/Users/mac/Desktop/STS2-rollback-20260912/untracked-evidence-and-tem.tar.gz`。
- 本轮安装前存档、配置和 MOD 备份：`.omc/artifacts/alpha1-resume-20260912/backups/`。

## 回滚方法

保留当前修复分支，使用 `git switch codex/rollback-alpha1-before-resume-review` 返回本轮开始前的源码。若要重现尚无续局状态修复的原始 Alpha 1，可在 `736a88c` 新建工作树，避免覆盖当前改动。

客户端回退须先退出游戏，再从备份恢复 MOD；测试前存档和配置也已单独归档。不要在游戏运行时覆盖存档。

## 验收边界

修复版候选包保存在 `/Users/mac/Desktop/STS2-alpha1-resume-20260912/`，客户端和服务端保持 `0.6.2-alpha.1`。自动测试通过不代表真实双端首战与 S/L 通过；只有实际完成后才能更新验收结论。当前没有创建 GitHub Release 或更新 Steam 创意工坊。
