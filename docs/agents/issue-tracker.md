# Issue tracker: GitHub

PaperTodo 的需求、规格和任务记录在 `Tryanel/mytodo` 的 GitHub Issues 中。所有操作使用 `gh` CLI，并显式传入 `--repo Tryanel/mytodo`；不要依赖当前目录自动推断仓库，因为本地同时配置了上游 `origin` 和个人仓库 `mytodo`。

## Conventions

- 创建：`gh issue create --repo Tryanel/mytodo --title "..." --body "..."`
- 查看：`gh issue view <number> --repo Tryanel/mytodo --comments`
- 列表：`gh issue list --repo Tryanel/mytodo --state open --json number,title,body,labels,comments`
- 评论：`gh issue comment <number> --repo Tryanel/mytodo --body "..."`
- 标签：`gh issue edit <number> --repo Tryanel/mytodo --add-label "..."`
- 关闭：`gh issue close <number> --repo Tryanel/mytodo --comment "..."`

PR 不作为 triage 请求入口。

当 skill 要求“发布到 issue tracker”时，在 `Tryanel/mytodo` 创建 GitHub Issue；要求读取 ticket 时，从同一仓库读取对应 Issue。

`/wayfinder` 使用一个带 `wayfinder:map` 标签的 Issue 作为 map，并以子 Issue 表示任务。优先使用 GitHub 原生 sub-issue 和 dependency；不可用时，在正文中使用 `Part of #<map>` 与 `Blocked by: #<n>`。领取任务时分配给当前用户，解决后评论结论并关闭。
