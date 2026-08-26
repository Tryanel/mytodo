# Domain Docs

PaperTodo 采用 single-context，但已经有明确的知识 ownership。探索代码前按任务范围阅读：

1. `AGENTS.md`：任务路由、执行规则与不可破坏约束。
2. `ARCHITECTURE.md`：当前有效的架构、ownership 和技术方向。
3. `DECISIONS.md`：历史取舍、失败路线与原因。
4. 根目录 `CONTEXT.md`（如存在）：领域术语、定义和避免使用的同义词。
5. `docs/adr/`（如存在）：仅阅读与当前区域相关、且未被上述 Source of Truth 替代的局部 ADR。

缺失的可选文件无需提示或预先创建；由 `/domain-modeling` 在真正澄清领域术语时按需生成。

## Layout

这是 single-context 仓库。`CONTEXT.md` 只补充领域词汇，不复制完整架构；当前架构继续归 `ARCHITECTURE.md`，历史决策继续归 `DECISIONS.md`。不得为了套用通用工作流而建立并行的架构或决策说明。

输出中的领域概念应使用 `CONTEXT.md` 已定义词汇。若需要的概念尚未定义，应先判断是命名漂移还是需要通过 `/domain-modeling` 补充。

若方案与 `ARCHITECTURE.md`、`DECISIONS.md` 或现有 ADR 冲突，必须明确指出，不得静默覆盖。
