# PaperTodo 架构

> 本文记录 **PaperTodo 当前有效的技术选型、架构结构和已经确立的技术方向**。
>
> - 它回答“系统现在按什么原则组织、各层由谁负责、后续实现应沿什么边界继续”。
> - 它不是代码目录、历史日志、PR 复盘或未来路线草案；任务入口与阅读顺序见 [`AGENTS.md`](AGENTS.md)，历史取舍和踩坑见 [`DECISIONS.md`](DECISIONS.md)。
> - 具体执行细节仍以当前代码为准。若本文、代码或 Decisions 冲突，先核对当前实现、提交历史和可观察行为，再统一修正。

## 1. 架构目标与当前方向

PaperTodo 是 Windows 桌面“纸片”应用。当前技术路线围绕几个长期方向组织：

- **paper 是主要写入对象和交互边界，全局视图是投影。** Todo、Markdown/Note、任务看板、插件正文和 Edge Capsule 都围绕 `PaperData` / `PaperWindow` 组合；全局单例 Board paper 可以聚合查看所有待办并导航回原纸片，但不拥有第二份任务状态或独立 mutation 规则。
- **每个职责尽量只有一个 authority。** 状态、几何、队列 placement、presentation、持久化和外部 mutation 不各自复制第二套“近似真相”。
- **复杂 UI 状态优先走显式状态与单通道 reconcile。** Edge Capsule 使用 Intent → Reducer → Presenter；窗口和 controller 不通过并行 bool/临时 setter 绕过它。
- **WPF 是主 UI / shape owner，native/DirectComposition 只承担确有必要的 Windows 边界能力。** 不把 compositor 扩成第二套 UI renderer。
- **持久化按数据生命周期和失败语义分域。** 核心状态、图片资产、插件状态分别由各自 store 管理；破坏性恢复/回收采用保守策略。
- **当前 Architecture 只记录已经确立的方向。** 未确认的未来方案、实验候选和一次性 workaround 不写成当前架构。

技术基础：

- .NET 10，目标 `net10.0-windows10.0.17763.0`。
- WPF 是主 UI；Windows Forms 只作为兼容依赖。
- 进程 DPI 策略：`PerMonitorV2,PerMonitor`。
- 主项目入口为根目录 `PaperTodo.csproj`。

## 2. 系统形态与 ownership

正常 GUI 模式由 `App` 建立一个单实例 WPF 主宿主；`AppController` 是应用级协调器。相同的 `PaperTodo.exe` 还支持独立 `--mcp` bridge 模式，该模式在 GUI 单实例协议之前分流，不拥有第二份 `AppState`。

高层关系：

```text
PaperTodo.exe
├─ --mcp
│   └─ McpBridge
│       └─ stdio MCP ↔ GUI-side MCP runtime
└─ GUI App
    └─ AppController
        ├─ AppState / StateStore
        ├─ NoteImageStore (LMDB)
        ├─ PaperBodyPluginRegistry / PaperBodyPluginDataStore
        ├─ PaperCommandService
        ├─ PaperWindow[paperId]
        │   ├─ paper shell / Todo / built-in Note / Board projection
        │   ├─ PaperBodyHost
        │   └─ EdgeCapsulePresenter + EdgeCapsuleHost
        ├─ MasterCapsuleWindow[queue]
        ├─ EdgeCapsuleDragWindow (process-global pooled host)
        ├─ tray / hotkeys / reminders / fullscreen / virtual desktop runtime
        └─ edge queue coordination / preview session / visual transaction /
           DirectComposition proxy lifecycle
```

主要 authority：

| 领域 | 当前 authority | 结构性职责 |
| --- | --- | --- |
| GUI 启动与进程生命周期 | `App` + `SingleInstanceHelper` | GUI 单实例、启动命令转发、全局异常边界、创建 `AppController` |
| 应用级业务协调 | `AppController` | `AppState`、窗口集合、保存调度、托盘、全局 runtime、跨纸片协调 |
| 核心持久化 | `StateStore` | `data.json` / backup 的加载、恢复和版本化写入 |
| 图片资产 | `NoteImageStore` | LMDB 生命周期、串行访问、图片编号、缓存和回收 |
| 插件状态 | `PaperBodyPluginDataStore` | provider settings 与 per-paper plugin state 的独立保存/恢复 |
| 外部 Paper/Todo/Note 命令 | `PaperCommandService` | 插件/MCP 共用的验证、mutation、同步提交/回滚和事件发布 |
| 单纸片 UI | `PaperWindow` | paper WPF shell、普通交互、provider 选择、子系统适配 |
| Todo 任务实体化 | `TodoTaskLifecycle` | 统一判定占位行何时首次成为任务并记录调用方提供的创建时刻；GUI、批量与外部命令复用同一语义 |
| Todo 撤销历史 | `TodoUndoHistory` | 以 WPF 无关的任务快照统一维护单张待办纸的 Undo/Redo；快照包含任务核心字段和跨纸片关系 |
| 全局 Todo 投影 | `TodoBoardProjection` + `TodoBoardActivityCalendarLayout` / `TodoBoardPlanningTimelineLayout` + `TodoBoardFilterState` / `TodoBoardSortRules` + Board 类型的 `PaperWindow` body + `AppController` | 纯投影模块从 `State.Papers[].Items` 收集任务并统一执行跨视图搜索、结构化筛选、表格多级排序、活动月历和计划时间线查询；Board body 只编辑查询状态、渲染与导航，仍不保存任务副本 |
| paper-body session | `PaperBodyHost` | 当前 `IPaperBodySession` 的 attach / invoke / commit / dispose，以及 opt-in 完整 Markdown 导出的 commit / lifecycle 校验 |
| 插件发现与合同 | `PaperBodyPluginRegistry` | builtin / Native / Web provider 发现、校验、激活 |
| Edge 单纸片业务状态 | `EdgeCapsuleReducer` + `EdgeCapsuleModel` | 单纸片 typed intent 到完整 model 的原子变化 |
| Edge 单纸片呈现 | `EdgeCapsulePresenter` | desired model、target plan、transition、applied frame、reconcile |
| Edge 队列级协调 | `AppController` edge partials | preview owner/corridor、arrange、visual transaction、proxy lifecycle |
| Edge 队列 placement | `EdgeCapsuleQueueCoordinator` | queue index、master offset、slot count |
| Edge 物理几何 | `EdgeCapsuleGeometry` | monitor/edge/DIP 到 wall-pinned physical rectangles |
| docked Edge surface | `EdgeCapsuleHost` | 每纸片 bounded HWND 和完整 WPF visual tree |
| 同队列 compositor translation | `EdgeCapsuleQueueCompositionProxy` | live HWND surface 的 X/Y translation 与 visual-authority handoff |
| floating drag | `EdgeCapsuleDragWindow` | 独立 floating pill HWND |
| 同 Dispatcher 动画节拍 | `EdgeCapsuleFrameScheduler` | Rendering cadence、统一 pointer/time sample、liveness rescue |

## 3. 进程与运行时边界

### 3.1 GUI 单实例

正常 GUI 启动使用按当前 Windows 用户 SID 隔离命名的 `SingleInstanceHelper` Mutex + named pipe。每个 Windows 用户只有一个主 GUI 实例建立 `AppController`；同一用户的后续 GUI 启动只把参数转发给主实例后退出，不会被其他账户或受限运行身份启动的实例静默拦截。

`AppController` 尚未完成启动时收到的单实例命令先排队，待 controller 可用后再执行。普通纸片窗口全部关闭不等于退出应用，进程使用显式 shutdown 生命周期。

正常退出在进入 `Exiting` 生命周期、停止运行时或释放窗口之前，先集中收集所有脏任务备注会话并暂存用户选择；任一取消都会恢复全部会话并保持 `Running`。选择保存的草稿只在整批决策齐全后临时投影到 authoritative items，同步保存失败会回滚整批投影并保留可重试草稿；只有同步保存成功后才提交会话并跨过 shutdown boundary。Windows 注销/关机若触发了仍需交互的 preflight，会取消当次系统 session-ending 请求，避免系统绕过该边界。crash boundary 不复用这条普通退出 preflight，仍不做最终强存。

### 3.2 MCP

`--mcp` 是同一可执行文件的独立 bridge 模式。它在 GUI Mutex 之前分流，通过 stdio 暴露 MCP server；GUI 主宿主内部的 MCP runtime 由 `AppController` 管理。

MCP 的 transport、权限策略和 bridge 生命周期不拥有 Paper/Todo/Note 的第二套业务写入逻辑；真正的业务 mutation 仍回到 GUI 主宿主和共享命令边界。

### 3.3 辅助进程

Web 插件使用 WebView2 runtime；脚本胶囊可以启动 PowerShell 子进程。这些进程/运行时只提供对应能力，不成为核心 `AppState` authority。

## 4. 状态与持久化架构

### 4.1 三个数据域

当前长期数据按语义拆成三个主要域：

| 数据域 | 当前存储 | authority | 方向 |
| --- | --- | --- | --- |
| 核心应用与纸片状态 | `data.json` + `data.backup.json` | `StateStore` | 保持可迁移、可恢复的结构化业务状态 |
| Note 图片二进制 | `note-assets.lmdb` | `NoteImageStore` / `LmdbImageDatabase` | 大体积二进制与 JSON 分离，独立做引用/容量管理 |
| 插件 settings / per-paper state | `plugins/data/*.json` | `PaperBodyPluginDataStore` | 插件生命周期与核心状态解耦，独立迁移和恢复 |

这三类数据不能因为“都属于一张纸”就合并成一个写入协议。核心状态保存、图片回收和插件状态恢复具有不同失败语义，因此保持各自 authority。

### 4.2 核心状态

`AppState` 是核心持久化根；`PaperData` 是单纸片模型；Todo 行使用 `PaperItem`。空白占位行没有创建时间，首次获得正文、备注、提醒、路径或纸片关联时由 `TodoTaskLifecycle` 成为任务并记录该次操作的时刻；完成状态与计划日期不能单独实体化占位行。任务建立后，后续正文/备注编辑、完成、恢复和计划修改不会重置创建时间。每条真实 Todo 的正文、可选备注、创建时间、完成状态、完成时间、可选计划开始日和可选截止日都随 `PaperItem` 进入 `data.json`；未实体化占位行省略创建时间。计划日期是无时区的日历日期，合法状态包括都空、仅一端或开始日不晚于截止日的完整范围，并且不随正文、备注、完成或恢复变化。

从旧数据加载时，`StateStore` 对有任务内容但缺失创建时间的条目使用同一次可控迁移时刻保守补齐；`AppState.TodoTaskLifecycleVersion` 只用于区分早期“每行预写创建时间”的协议，旧空白行即使曾被误写时间也恢复为未实体化占位行，而当前协议中已经实体化后再清空正文的任务仍保留身份。缺少计划字段保持未排期；所有完成/恢复操作继续通过 `PaperItem.SetDone` 同步维护完成时间。

单张待办纸的任务快照 Undo/Redo 由 WPF 无关的 `TodoUndoHistory` 维护，`PaperWindow` 只在现有用户操作边界记录和应用快照。计划日期从 owning Todo paper 的任务入口设置、校验和清除；Board 仍只读，不拥有计划日期写入路径。

删除、隐藏、折叠是不同语义：

- 删除从 `State.Papers` 移除对象。
- 隐藏保留对象，仅改变可见性。
- 折叠仍是可见纸片，只切换到 capsule presentation。

普通窗口 `X/Y/Width/Height` 与 Edge Capsule 的 queue / expanded recovery geometry 不是同一套状态，不能由 parked/hidden shell 相互覆盖。

`StateStore` 的方向是保守恢复与版本化写入：主文件失败后可从 backup 恢复；需要保护失败源时先保留证据再允许正常保存覆盖。保存阶段只修复序列化无效值，不重新解释业务不变量。

全局 crash boundary 不执行普通“最后强行保存”。正常 durability 由常规保存、同步退出保存和 backup 提供。

### 4.3 图片资产

图片二进制不进入 `data.json`。`NoteImageStore` 统一串行化 LMDB 访问，外部业务代码不直接拥有 LMDB transaction authority。

Markdown 中的 Note 图片只通过 PaperTodo 内部 `i:` asset URI 引用宿主管理的图片；网络图片或任意外部图片不是当前 Note 图片资产协议的一部分。

图片 GC / id reuse 是破坏性操作，因此 reachability 采用 fail-closed：无法可靠证明当前状态和需要保护的 recovery snapshot 都可扫描时，本轮不回收。

### 4.4 插件状态

插件 settings 与 per-paper state 由 `PaperBodyPluginDataStore` 独立保存，不塞回 `data.json`。插件数据读失败时保留原始问题源，并通过受控 recovery 路径继续；插件数据故障不应把核心 Paper 数据变成不可加载。

## 5. Paper 与 paper-body 插件

### 5.1 Paper shell

`PaperWindow` 是单纸片 UI owner，负责普通 paper shell、Todo/Note 交互、标题/工具栏、窗口行为和各子系统适配。

每张 Todo paper 至多持有一个 `TodoNoteEditorSession`。任务备注编辑器是由 `PaperWindow` 逻辑持有、但不设置 WPF `Owner` 的独立非模态 surface，因此 owning paper 折叠或隐藏不会连带隐藏草稿；切换、关闭、删除与退出意图由 session 统一决策，实际保存仍按稳定 `PaperItem.Id` 回到 `PaperWindow` 的撤销、持久化和行同步边界。GUI 删除在 mutation 前完成草稿决策；外部命令若先成功删除 authoritative 任务或纸片，原编辑器会脱离 active session、变为只读失效结果，不创建替代对象。

Edge Capsule 启用后，一张纸的可见 surface 不再等价于一个 `PaperWindow` HWND：docked capsule 由 `EdgeCapsuleHost` 提供，跨队列/脱墙拖拽可以临时使用 `EdgeCapsuleDragWindow`；这些 surface 仍引用同一 `PaperData`，不复制业务对象。

内置 Markdown Note 的编辑态和浏览态复用同一个 `MarkdownTextBox`，通过 interaction/presentation 状态切换，而不是维护两套正文 surface。

### 5.2 Provider / session 分层

Provider 当前分三类：

- Built-in Markdown。
- fully trusted / unsandboxed Native .NET/WPF plugin。
- 本地 Web plugin，通过宿主 WebView2 运行。

`PaperBodyPluginRegistry` 负责 provider 发现和合同校验；`PaperBodyHost` 负责一张纸当前 session 的 attach / invoke / commit / dispose，并在同一个 live session 上协调可选的完整 Markdown 导出；`PaperWindow` 仍拥有窗口 placement、paper chrome 和 provider 选择。

Native assembly 一旦载入 CLR，不按 Web provider 的方式做进程内热替换；需要重启才能稳定切换已加载版本。

### 5.3 外部读写

插件 Host API 与 GUI 侧 MCP 对 Paper/Todo/Note 的共享业务 mutation 统一进入 `PaperCommandService`。该边界负责：

- 参数和类型约束；
- mutation 前提交仍停留在 UI/provider session 的待提交内容；
- 保存成功才完成外部 mutation；
- 保存失败回滚内存状态；
- 提交后刷新必要 UI 并发布外部变更事件。

Todo 外部快照与创建/更新合同直接携带 authoritative `PaperItem` 的计划开始日和截止日；更新请求只有显式提供 planning patch 时才改变两端日期。日期字符串解析属于 transport 映射，最终范围验证、同步保存、失败回滚、Board 刷新和 `TodoChanged` 事件仍由共享命令边界统一完成。

transport 权限、Web/Native surface 生命周期和 MCP protocol 不下沉到 `PaperCommandService`；反过来，transport 层也不建立另一套核心 mutation 实现。

### 5.4 Edge mini

插件可以提供专属 mini、允许迁移的纯 WPF 正文 View、custom/standard capsule presentation 或 plain-text fallback，但 **Edge 的窗口、queue placement、外层尺寸会话和输入 authority 始终属于宿主**。

当前技术方向是“插件贡献内容能力，宿主决定如何安全呈现”：

- Native mini 只接纳 fresh / unparented / pure-WPF tree。
- Web `miniEntry` 使用独立 Web mini surface；它自己的 ready/publication 时序属于 Web session 实现，不把 WebView2 当作可迁移 WPF child。
- 正文 View migration 只对 provider 明确声明且宿主可以安全接管的纯 WPF View 启用。
- 没有专属能力时由宿主降级到 capsule/plain text。

具体 fallback 次序、尺寸和 ready 时序属于当前 contract/代码实现；为什么形成这些边界见 D-018。

### 5.5 Todo 全局投影与纸片导出

`PaperTypes.Board` 是全局单例的纸片类型，使用普通 `PaperData` / `PaperWindow` shell，因此沿用显示、移动、尺寸、折叠胶囊、托盘、主题和删除等纸片生命周期。`TodoBoardProjection` 是不依赖 WPF 的只读投影 authority：从 authoritative Todo `PaperData.Items` 收集非占位任务，以多词 AND / 引号短语解析、状态/所属纸片/历史日期/计划重叠/备注筛选生成跨视图 `QueryEntries`，再只为表格应用有优先级的多级排序；活动月历继续从相同查询结果读取创建到完成（未完成到显式当天）的跨度。比较文化、显示文化、时区和当天都由调用方注入，投影条目携带搜索与渲染共用的状态、历史时间和计划日期文本。`TodoBoardActivityCalendarLayout` 负责把查询结果按周切成连续活动条、确定性分配 lane，并为每个日期给出准确的隐藏任务集合；`TodoBoardPlanningTimelineLayout` 则从同一查询结果构造周/月计划窗口、裁剪连续计划条、生成单日标记并单独返回未排期任务。WPF Board body 只选择视图与窗口、渲染只读结果并导航回 owning Todo paper。

Board 自有持久化字段包括普通纸片状态、`BoardView`、`BoardTimelineScale`、`BoardFilters` 与 `BoardSortRules`，不复制任何任务事实；`StateStore` 规范化未知/重复条件，并把旧 `BoardSort` 单排序偏好迁移为规则列表。既有 `calendar` 视图值继续表示活动月历；时间线只保存周/月尺度，当前窗口锚点和搜索词都属于 WPF 会话，不进入 `data.json`。引用已删除待办纸的筛选值由投影按当前 authoritative paper 集合忽略，仍存在但为空的待办纸不会被误判为已删除。表格列头通过 `TodoBoardSortRules.SetPrimary` 变更第一规则，WPF 不复制比较或筛选算法。

看板中的行和日历任务只负责展开、聚焦 owning Todo `PaperWindow` 并定位原任务；实际编辑、撤销、保存和外部事件仍沿用原纸片边界。`AppController.MarkDirty` 调度现有 Board paper 刷新，不把看板变成新的 state authority。创建入口返回现存 Board paper，避免产生多个全局看板。

纸张 Markdown 导出由纸片类型的真实内容 authority 提供。Todo 由 `PaperMarkdownExporter` 导出勾选状态、备注、创建/完成时间、计划开始日和截止日；内置 Note 导出当前 `MarkdownTextBox` 正文；Board 按 owning Todo paper 分组导出全部任务，不受当前查询或视图影响。插件纸只有在协议 1.10 manifest / Native 合同显式声明 `FullMarkdownExport` 时显示入口，宿主先 commit 当前 live session，再通过 `IPaperMarkdownExportProvider`（Web 由 body bridge adapter 实现）取得完整 Markdown；失败、空缺 provider 或 session 被替换时不写文件，也不回退到 capsule summary、`BodyCapsuleText` 或核心正文缓存。导出始终是显式文件快照，不参与 `StateStore` 或插件状态主写入，也不成为可回读的第二份 authoritative 数据。

## 6. Edge Capsule V3 Lite

V3 Lite 的当前方向不是“再叠一个更聪明的代理”，而是保持 **单一 per-paper presentation authority + 极薄 native/compositor 边界**。

### 6.1 单纸片状态与呈现

主链：

```text
OS / WPF / controller event
        ↓
EdgeCapsuleIntent
        ↓
EdgeCapsuleReducer
        ↓
EdgeCapsuleModel
        ↓
EdgeCapsuleTargetPlanner
        ↓
EdgeCapsulePresentationPlan
        ↓
EdgeCapsulePresenter reconcile / transition
        ↓
EdgeCapsulePresentationFrame
        ↓
EdgeCapsuleHost.Apply(frame)
```

`EdgeCapsuleReducer` 决定单纸片业务状态；`EdgeCapsulePresenter` 是该纸 desired model、target、transition、applied presentation 和 dirty/deferred work 的唯一 presentation authority。

`EdgeCapsuleTargetPlanner` 是纯 desired-model → shape/layout planner，一次生成完整 `EdgeCapsulePresentationPlan`。Docked surface 与 `FloatingFree` 是互斥外形；floating 的宽度、圆角、关闭区和其他 shape 语义不由窗口构造参数或拖拽路径另行拼装。

`AppController` 可以协调跨纸片 session、向多张纸 dispatch intent、捕获事务 frame，但不维护第二份 per-paper desired model。

Measure / display-metrics 也是同一 presentation reconcile 的输入，而不是第二套状态入口：非拖拽时更新 layout snapshot 并从当前已呈现帧 retarget；正在 docked/floating drag 时相关 refresh 延后到 gesture 边界后处理，不反向改写 Hover / Active / slot / gesture 语义。

### 6.2 Queue placement 与 geometry

队列由 monitor + edge 标识。`EdgeCapsuleQueueCoordinator` 只负责 index、master offset 和 slot count；`EdgeCapsuleGeometry` 只负责 monitor/edge/DIP 到物理像素矩形。

`EdgeCapsuleLayoutSnapshot` 捕获的是**目标 monitor** 的 `MonitorGeometry` 与 DPI；docked 物理矩形必须基于这份目标显示器事实计算，不能退回主 `PaperWindow` 的当前 DPI 或在动画/measure 路径重新复制一套换算。共享 capsule 尺寸和队列布局参数从 `PaperLayoutDefaults` / `EdgeCapsuleLayout` 等统一来源进入 layout/planner。

队列保持完整顺序，不引入分页/自动隐藏 overflow。分页会把 placement 问题升级成另一套 visibility/state ownership，因此当前方向仍是连续完整队列。

Presentation contract 区分：

- `Bounds`：当前真正可见的 capsule rectangle。
- `HostBounds`：bounded docked HWND 的 native capacity。
- `InteractiveBounds`：当前真实输入区域。

透明 capacity 不属于交互区域。

### 6.3 Surface 切分

每张 docked capsule 由独立 `EdgeCapsuleHost` 长期拥有真实 HWND 和完整 WPF visual tree。Host 是 **bounded live host**：native capacity 稳定且有限，可见 shape 在其中由 WPF 变化。

跨队列/脱墙拖拽使用独立、进程级复用的 `EdgeCapsuleDragWindow`，不把 docked host 变形成自由 floating pill。

开启 collapse-all master 时，每个队列的 `MasterCapsuleWindow` 占 slot 0，只拥有自身 presentation/gesture，不持有真实 paper 的第二套 presenter state。

### 6.4 WPF 与 DirectComposition

当前明确的职责切分：

**WPF / bounded host owns shape；DirectComposition owns translation。**

WPF / Presenter 负责：

- Resting / Hover / Active / Preview 的可见宽高；
- rounded geometry；
- 内容布局与 opacity；
- `InteractiveBounds` 等 presentation contract。

DirectComposition queue proxy 负责：

- 从真实 HWND 建立 live surface；
- 保持 surface identity / size；
- 只做 X/Y translation；
- 在真实 HWND 已受 cover 保护时帮助 queue 成员完成位置移动和 visual-authority handoff。

Production translation backend 不承担 snapshot、clip/scale/effect resize 或另一套 deferred-resize presentation model。需要 shape/size 变化时，回到 WPF bounded host 或明确 native fallback 边界。

### 6.5 Visual authority 与 handoff

真实 docked HWND、queue compositor cover、floating drag HWND 是显式 visual authority。任何 publication、successor、handoff 或 rollback 边界都必须保证至少有一个可见 authority。

同队列 successor 继承 predecessor 当前 live authority 和可见 sample，而不是 dispose 后冷启动另一套互不相关 proxy。

Proxy 动画逻辑结束不等于 real WPF 已经可以接管。只有 terminal real/WPF presentation 已完成必要的 apply/layout/render/verify 边界后，cover 才能释放；completion timer 只负责发起完成尝试，不作为 correctness proof。

Display/DPI、z-order、drag 结束、隐藏/关闭 Edge 模式等生命周期边界如果会让现有 surface/queue 失效，先结束或恢复当前 visual authority，再清理 preview、retraction、临时 placement/transaction 等 transient state；这些临时状态不能跨失效边界残留到下一次显示或重新启用。

### 6.6 Pointer、Preview corridor 与帧节拍

Hover/Preview 的最终物理 truth 来自当前 presented/applied `InteractiveBounds`。WPF/native enter/leave 主要负责唤醒采样，透明 `HostBounds` 和 proxy envelope 不能扩大 hit area。

Preview session 建立后，当前 owner 是 queue-wide 的 pointer arbiter：owner、候选 target、transfer corridor 和 outside 都由同一 controller 路径解析，host/WPF 输入适配层只提供物理采样，不复制另一套 preview 状态机。owner 与可浏览候选的 `InteractiveBounds` 是真实命中区；连续可交互成员之间的 transfer corridor 只是允许指针跨空白移动的临时连续区域，不是新的 capsule hit area。指针真实离开合法 transfer region 时属于硬边界，预测逻辑不能把 outside 改写成 inside；pointer capture 期间则暂停这类离场判断，避免正在进行的交互被 corridor watcher 抢走。

首次没有 preview session 时，经过验证的真实物理命中可以直接建立 owner；已有 session 内的 A→B transfer 则继续使用当前 residence/stability/predictor policy。具体毫秒数和灵敏度属于实现参数，留在代码。

同一 Dispatcher 的 presenters 共用 `EdgeCapsuleFrameScheduler`。正常 transition 由 `CompositionTarget.Rendering` 推进；watchdog 只在 Rendering 没有及时推进 active transition 时做 demand-driven rescue，不成为第二套长期动画时钟。

这些原则的历史原因、失败路线和不可回退点见 D-005～D-014。

## 7. OS 与全局集成

`AppController` 还协调：

- Hardcodet tray icon / context menu；
- 全局快捷键；
- foreground fullscreen 检测和 topmost avoidance；
- display metrics / DPI 更新；
- Todo reminders；
- virtual desktop integration；
- 可选窗口 magnetism / tether 等实验 runtime；
- GUI 侧 MCP runtime。

全局 watcher 可以触发 visibility、z-order、monitor placement 等变化，但进入具体 Paper/Edge surface 后，仍应回到对应 subsystem authority，而不是在 watcher 中复制 geometry 或 presentation state。

托盘当前基于仓库固定的 `vendor/wpf-notifyicon` 和 WPF `IconSource`；选择该路线的历史原因见 D-017。

## 8. 仓库结构

- `src/`：主程序 C# 源码。
- `PaperTodo.Tests/`：不依赖 WPF surface 的核心行为测试；当前覆盖任务/Board 纯投影边界。
- `Resources/`：中文默认资源及 en/ja/ko 本地化 `.resx`。
- `PaperTodo.Plugin.Abstractions/`：插件 ABI / host contract。
- `plugins/`：可直接加载的插件产物；`plugins/data/` 保存宿主管理的插件状态。
- `plugin-samples/`：插件源码、示例和构建说明。
- `native/`：PaperTodo 自有 native 组件，例如 LMDB bridge。
- `vendor/`：固定版本 vendored dependency / submodule。
- `assets/`：图标和静态资源。
- `docs/`：GitHub Pages 站点资源，不作为内部架构文档默认目录。
- `.github/workflows/`：CI / Release。

根目录保留项目入口和仓库级知识入口：`README`、`CHANGELOG`、`AGENTS.md`、`ARCHITECTURE.md`、`DECISIONS.md`。
