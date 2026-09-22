# VP Helper 设计文档

> Codeforces Gym VP 成绩分析工具 —— 抓取某账号所有 gym 提交，结合 rankland 官方榜单，
> 用于总结区域赛 / 省赛 / 邀请赛的 Virtual Participation 情况。

- 状态：草案 v0.2（rankland 数据接入已核实落定）
- 日期：2026-07-06
- 目标平台：Windows（原生 Fluent Design）

> v0.2 变更：第 9 节「rankland 数据接入方式」已核实并定案——rankland 榜单数据即公开
> GitHub 仓库 `algoux/srk-collection` 的 srk JSON 文件集，可经 raw URL 直接读取，无需
> 逆向抓取 rankland SPA。相关内容见新增第 6.4 节与更新后的第 5、6.3 节。

---

## 1. 项目目标

给定一个 Codeforces 账号（handle），工具应能：

1. 抓取该账号的**全部 gym 提交记录**（含私有 gym）。
2. 按 gym 比赛聚合出 VP 成绩：过题数、罚时、每题首次 AC 时间。
3. 将 gym 比赛关联到 **rankland 官方标准榜单**（区域赛 / 省赛 / 邀请赛）。
4. 把 VP 成绩插入官方榜单，推算「如果打现场排第几 / 是否达到奖牌线」。
5. 以 Fluent 风格桌面 UI 呈现比赛列表与单场详情。

非目标（明确排除，避免范围蔓延）：

- 不做题解 / 代码托管。
- 不做实时 rating 预测。
- 首阶段不做多账号 / 队伍对比与趋势图表（见第 8 节阶段规划）。

---

## 2. 核心约束与技术难点

这些约束直接决定了架构，务必先理解。

### 2.1 官方 API 不覆盖 gym

已实测确认：公开 API `user.status?handle=X` **只返回常规 rated 比赛的提交，不含 gym**。
即使带 `apiKey` / `apiSig` 签名，官方 API 对 gym（尤其私有 gym）支持极差，常返回
Unauthorized 或空数据。

**结论**：gym 提交的唯一可靠来源是**带登录态抓 HTML 页面**
（`/submissions/{handle}`、`/gym/{id}/...`）。

### 2.2 私有 gym 必须登录

私有 gym 只有账号被授权后、且携带有效 session 才能访问。因此：

- 登录态既是「解锁私有 gym」的钥匙，又是「抓取」本身的载体，两件事合并。
- 选型必须围绕「谁能最干净地拿到并持有一个 CF 登录 session」。

### 2.3 「复用本地浏览器已登录 cookie」不可行

- **Chrome / Edge（Chromium）**：2024 年 Chrome 127 起引入 App-Bound Encryption，
  cookie 密钥绑定浏览器自身并经系统服务保护，外部进程基本无法解出；浏览器运行时
  cookie 库被锁；此行为易被杀软标记。**不采用。**
- **Firefox**：`cookies.sqlite` 明文，理论可读，但仅覆盖 FF 用户，维护成本高。**不采用。**

**替代方案（最终采用）**：应用内嵌 WebView，用户在其中登录一次，持久化该 WebView
自己的 session；抓取时从 WebView 的 CookieManager 导出 cookie 喂给 HttpClient
（或直接在 WebView 内抓页面）。访问用户自己账号的 session 完全正当。

### 2.4 CF 限流

- 官方 API：约 1 request / 2s。
- HTML 页面：更需节流，必须内置请求间隔、重试、指数退避、本地缓存。
- 抓取器必须支持**增量**：第二次只拉新提交。

### 2.5 gym ↔ rankland 映射脏

gym 标题与 rankland 官方命名往往对不上。首阶段采用**半自动**：
程序给候选，人工确认，确认结果落库复用。

---

## 3. 技术栈选型

### 3.1 结论

| 层 | 选型 |
|---|---|
| UI | **WinUI 3 + Windows App SDK**（真 Fluent：Mica/Acrylic、NavigationView） |
| 登录 / session | **WebView2**（`CoreWebView2.CookieManager` 读写、导出 cookie） |
| 运行时 / 业务 | **.NET 8** |
| 数据 | **SQLite**（EF Core 或 Dapper） |
| 抓取 | **HttpClient**（cookie 来自 WebView2）+ HTML 解析（AngleSharp） |

### 3.2 选型判据

新需求「私有 gym + 登录 + 内嵌浏览器 session 控制」是决定性判据：

| 栈 | 内嵌浏览器 | cookie 控制 | Fluent 质感 | 跨平台 |
|---|---|---|---|---|
| **WinUI 3 + WebView2** | 一流 | CookieManager 可导出喂 HttpClient | 真 Fluent | UI 仅 Win |
| Electron + Web 前端 | 一流 | session 完整控制 | 仿 Fluent | 全平台 |
| Tauri + Rust | 系统 WebView | 弱、平台差异大 | 仿 Fluent | 全平台 |
| Avalonia | 第三方 WebView 不成熟 | 弱 | 近似 Fluent | 全平台 |

Tauri / Avalonia 因内嵌浏览器与 cookie 控制薄弱被排除。选 **WinUI 3 + WebView2**：
满足「Windows + 真 Fluent + 私有 gym 登录抓取」，且 WebView2 的 cookie 导出足以支撑
抓取，无需为此上 Electron 牺牲原生质感。

### 3.3 跨平台预留

业务核心全部抽入跨平台 .NET 类库；未来要跨平台只需换 UI 壳，不会白写。

```text
VPHelper.Core     # 跨平台：CF 抓取、rankland、聚合分析、SQLite —— 无 UI 依赖
VPHelper.WinUI    # Windows UI：WinUI 3 + Fluent + WebView2 登录
（未来可选）
VPHelper.Cli      # 命令行版，跨平台
VPHelper.Desktop  # Avalonia 跨平台桌面
```

### 3.4 WinUI 视觉与交互规范

#### 2026-09 官方比赛与响应式工作台

- 导航顺序为统计概览、VP 比赛、官方比赛、比赛关联，账号管理位于底部。原「赛事目录」是当前「官方比赛」页，原「官方榜单与关联」改为职责单一的「比赛关联」。
- 官方比赛默认显示全部类别，支持名称/年份/比赛 ID 搜索、类别及参赛状态的组合筛选。显示「已 VP / 仅练习 / 未 VP」及推算排名；状态只代表已同步本地账号，零过题 Virtual 仍属于已 VP，Practice 不冒充 VP。读取失败显示未知状态。
- 推算排名仅使用已确认映射与本地榜单，沿用 MappingService 的正式队伍、并列名次和无正式标记时回退全榜的口径；未加载榜单和未关联分别提示。多账号、多个 Gym/QOJ 赛场保留各自成绩与账号入口，不自动挑最高成绩代替用户选择。此列表是浏览投影，不改变首页累计统计的多账号选择规则。
- 目录合并展示的官方赛事保留全部原始 official ID，不能因合并名称丢失另一份榜单上的 VP 记录。只有已验证的直接比赛链接才可在尚未确认关联时展示本地状态，此时不推算排名。
- 进入目录优先使用本地缓存，并重新读取当前参赛信息；「刷新目录」显式联网更新公开目录，失败保留缓存及警告。仅首次没有缓存时尝试加载网络目录。目录页不逐场下载榜单，用户进入 VP 详情后复用按需加载流程。
- 初始窗口目标为 **1440×900 DIP（16:10）**，按当前 DPI 换算，优先居中适配工作区的 90%。普通窗口最小 **1000×680 DIP**，手动缩放的宽高比范围 **4:3～21:9**，使用原生 WM_GETMINMAXINFO / WM_SIZING 在缩放过程中约束，避免事后反复回弹。工作区不足时以屏幕可容纳为优先，最大化沿用系统工作区。历史窗口尺寸和位置恢复时重新裁入当前可用屏幕，最小化状态不能覆盖正常窗口位置。
- 页面基于可用宽度响应，不能只依赖窗口宽高比：导航在窗口宽度不足 1280 DIP 时收起；VP 内容宽度不足 1180 DIP 时改为左侧比赛列表和右侧「比赛详情 / 官方榜单」页签，宽屏恢复三栏。题目表格为时间、状态等字段保留完整列宽；窄窗口切到榜单后定位到自身排名附近。
- 首页统计卡按可用宽度采用 7 / 4 / 2 列；官方列表保持列对齐并在小于工作台下限的特殊屏幕上提供横向滚动兜底。比赛关联选择框限制在页面边界内，窗口变化时关闭过期浮层。平台切换使用可键盘聚焦的按钮。
- 首页和 VP 列表以「通过 N 题」表达成绩，避免将本地已发现题数误当赛事总题数。首页刷新时间明确表示统计刷新，不能标成账号数据同步时间。

桌面端采用紧凑、可扫描的工作台布局，所有页面共享同一组尺寸与控件语义：

- 页面内容区水平留白 `24px`，宽屏内容最大宽度 `1560px`；面板内边距 `16px`，页面主区之间间隔 `18-24px`。
- 页面标题 / 分区标题 / 正文 / 表头字号分别为 `30 / 18 / 14 / 12px`，表格与重复项优先保证信息密度和列对齐。
- 自绘窗口标题栏采用系统 Tall 高度（`48px`），拖拽区与最小化 / 最大化 / 关闭按钮共享同一垂直中心线。
- 应用图标与侧栏 Segoe Fluent 图标使用同一套中粗线性语言：以 Yurenai 生成的“比赛面板 + VP 播放角标”位图为源，目标笔画在 `20px` 视觉框内保持约 `2px`。后处理必须使用高置信度前景裁切、Alpha 面积光学居中、预乘 Alpha 缩放、白/青色板归一和逐尺寸 Alpha 边缘锐化，避免几何居中造成的视觉上浮、透明 RGB 暗边及小尺寸发虚。`.ico` 嵌入可执行文件并显式绑定 `AppWindow`；自绘标题栏使用 `20 x 20 DIP`，同时提供 `scale-100` 至 `scale-400` 的原生物理像素 PNG，由 Windows 按显示 DPI 选择资源。不使用字母徽标、独立底板或 SVG 重绘。
- 普通按钮、输入框和状态标签使用 `4px` 圆角；重复列表项使用 `6px`；独立页面面板使用 `8px`。不使用胶囊按钮或圆形图标按钮。
- 图标命令固定为 `36 x 36px` 的近方形按钮，并配 Tooltip；文本按钮只承载明确命令，最小高度 `36px`。
- 分段切换控件仅整组外轮廓使用 `6px` 圆角，内部接缝保持方正，切换时只改变背景与前景色，不做位移、缩放或焦点闪烁动画。
- 表格链接显示短标签（如 `Gym 106252`、`QOJ 2641`），完整 URL 放在 Tooltip；固定格式的列表、面板和分栏必须使用稳定尺寸，避免内容加载导致布局跳动。
- VP 三栏、导航栏和主要数据面板允许拖动调整宽度，并设置合理的最小 / 最大值；窗口缩放后仍应保持标题、按钮和表格内容完整可见。
- 账号页的登录检测只更新登录状态，不能隐式触发同步；CF / QOJ 数据同步必须由用户点击对应的“同步该账号”命令启动。
- QOJ 登录态必须由顶部账号控件中的 `Logout` 与同容器用户链接共同识别，不能把公告作者、榜单用户或页脚作者误认为当前账号。QOJ 同步优先抓取 `submissions?submitter={handle}` 的账号过滤流，并用个人主页的虚拟参赛开始时间恢复相对比赛时间；账号流不可用时才逐比赛回退。所有账号卡片统计以 `(Platform, Handle)` 为键，禁止同名 CF / QOJ 账号串数。
- QOJ 同步只有在至少一个提交数据页成功读取且页面结构可解析后才能推进同步时间；全量抓取失败、账号筛选失效或提交者字段无法识别都必须显示失败，不能伪装成“新增 0 条”。提交者兼容旧版 `a.uoj-username` 与新版 `span.uoj-username[data-nickname]`。
- QOJ 账号卡片的提交总数必须包含当前事务中新加入的提交；首次同步不得因 EF 尚未执行 `SaveChanges` 而把 `TotalSubmissions` 写成 0。展示时以原始 `Submissions` 表按 `(Platform, Handle)` 聚合的结果为事实源，并在启动时校正旧版本遗留的账号缓存。
- 内嵌浏览器首次完成导航后按页面实际内容自动适宽，默认消除横向滚动条；WebView2 原生缩放仍保持启用，用户可继续用快捷键调整缩放，也可手动重新适宽。

---

## 4. 系统架构

```text
┌─────────────────────────────────────────────┐
│ VPHelper.WinUI  (Fluent UI + WebView2 登录)   │
└───────────────┬─────────────────────────────┘
                │ 调用
┌───────────────▼─────────────────────────────┐
│ VPHelper.Core                                 │
│  ┌─────────────┐  ┌──────────────┐            │
│  │ AuthService │  │ Scraper       │            │
│  │ (cookie 持有)│→ │ (HttpClient   │            │
│  └─────────────┘  │  + AngleSharp)│            │
│                    └──────┬───────┘            │
│  ┌──────────────┐  ┌──────▼───────┐  ┌───────┐ │
│  │ RanklandClient│ │ Aggregator   │  │ Repo  │ │
│  │ (官方榜单)     │ │ (VP 成绩计算) │  │(SQLite)│ │
│  └──────────────┘  └──────────────┘  └───────┘ │
│         └──────── Matcher (gym↔official) ──────┘│
└─────────────────────────────────────────────┘
```

### 数据流

```text
handle
  → Scraper 抓 /submissions/{handle} 全量分页
  → 解析出 (gymId, problem, verdict, submitTimeSeconds)
  → 存 SQLite（增量）
  → Aggregator 按 gymId 分组 → 每场 solved / 罚时 / 首杀时间
  → Matcher 把 gymId 映射到 rankland 官方比赛（半自动）
  → 把 VP 成绩插入官方榜单 → 推算名次 / 奖牌线
  → UI 展示
```

---

## 5. 数据模型（SQLite）

```text
Account            账号
  handle           TEXT PK
  lastSyncedAt     INTEGER   -- 增量抓取水位

Submission         提交（抓取原始层）
  id               INTEGER PK   -- CF submission id
  handle           TEXT FK
  gymId            INTEGER      -- gym contest id
  problemIndex     TEXT         -- A / B / ...
  problemName      TEXT
  verdict          TEXT         -- OK / WRONG_ANSWER / ...
  submitTime       INTEGER      -- unix seconds
  UNIQUE(id)

GymContest         gym 比赛（抓取聚合层）
  gymId            INTEGER PK
  title            TEXT
  startTime        INTEGER
  problemCount     INTEGER

VpResult           单账号单场 VP 成绩（派生，可重算）
  handle           TEXT
  gymId            INTEGER
  solvedCount      INTEGER
  penalty          INTEGER
  firstAcPerProblem JSON        -- {index: unixSeconds}
  PK(handle, gymId)

OfficialContest    rankland 官方比赛（来自 srk-collection）
  id               TEXT PK      -- 用 srk-collection 叶子 path，如 icpc2026invitational-shenzhen
  name             TEXT         -- config.yaml 的 name，如 "2026-04-11 深圳邀请赛"
  categoryPath     TEXT         -- 顶层 category path，如 icpc / ccpc
  category         TEXT         -- 区域赛 / 省赛 / 邀请赛（Phase 2 自动，Phase 1 可空）
  srkUrl           TEXT         -- raw.githubusercontent 的 .srk.json 地址
  startAt          INTEGER      -- srk contest.startAt 归一化为 unix seconds
  durationSeconds  INTEGER      -- srk contest.duration 归一化
  problemCount     INTEGER
  medalRules       JSON         -- 金/银/铜线（若有；srk 未必带，可人工录入；
                                --   缺省时按默认 10%/30%/60% 规则算，见 6.6）
  fetchedAt        INTEGER      -- 缓存水位

OfficialStanding   官方榜单单行（解析 srk rows[]）
  officialId       TEXT FK
  rank             INTEGER      -- 按 srk sorter 计算或原序
  teamName         TEXT
  organization     TEXT
  solvedCount      INTEGER
  penalty          INTEGER      -- 归一化为秒
  perProblem       JSON         -- {alias: {result, timeSeconds, tries}}
  PK(officialId, teamName)

ContestMapping     gym ↔ official 关联（半自动，落库复用）
  gymId            INTEGER
  officialId       TEXT
  confidence       REAL         -- 候选置信度
  confirmed        INTEGER      -- 0 候选 / 1 人工确认
  PK(gymId, officialId)
```

---

## 6. 抓取与登录策略

### 6.1 登录流程

1. UI 内嵌 WebView2，打开 `https://codeforces.com/enter`。
2. 用户手动登录（含验证码 / 二步验证，交由用户在真实页面完成）。
3. 用持久化 UserDataFolder，让 WebView2 记住 session（登录态 + Cloudflare `cf_clearance` 一并保留）。
4. 抓取直接复用这个已登录、已过盾的 WebView2 上下文执行（见 6.2），**不导出 cookie 到裸 HttpClient**。
5. 私有 gym：账号已被授权时，登录态即可访问。

### 6.2 抓取执行方式（Cloudflare 定案：走 WebView2 内抓，不用裸 HttpClient）

**已确认：CF 会弹 Cloudflare 挑战页。** 这否决了「导出 cookie 喂裸 HttpClient」的主路径——
Cloudflare 的 `cf_clearance` 与 TLS/JA3 指纹、User-Agent、浏览器执行环境强绑定，把 cookie
搬到独立 HttpClient 上会被判定不一致而重新触发挑战。因此抓取**在 WebView2 已过盾的浏览器上下文内执行**：

- **主路径（采用）**：用同一个已登录、已过 Cloudflare 的 WebView2 实例发起页面请求。两种等价手段：
  1. `CoreWebView2.Navigate` 到目标页 → 等 `NavigationCompleted` → `ExecuteScriptAsync` 取
     `document.documentElement.outerHTML`，再交 AngleSharp 解析；或
  2. 在页面上下文里 `ExecuteScriptAsync("fetch(url).then(r=>r.text())")`，复用页面的
     cookie / 指纹 / 挑战态,直接拿 HTML 或 JSON。
- **回退路径**：仅当某端点确认不触发 Cloudflare（如部分公开 API）时，才允许用导出 cookie 的
  HttpClient，以减轻 UI 线程压力。默认不假设可行。
- **挑战检测**：识别响应是否为 Cloudflare 质询页（标题 / `cf-mitigated` 头 / 特征 DOM）。命中则
  把该 WebView2 显示给用户完成一次人机验证，过盾后继续；不静默重试硬闯。
- **登录 / handle 检测（2026-07-06 实测更正）**：起初假设 CF 登出链接是 `/{handle}/logout`，
  据此从 href 首段取 handle——**真实环境证伪**：登出链接实际是 `/{csrfToken}/logout`，首段是
  一串十六进制 CSRF token，不是 handle（曾把 handle 框自动填成乱码）。**已更正**为:登出链接
  仅用作「已登录」标志,真实 handle 从 header 里的 `a[href^="/profile/"]` 链接末段取
  （如 `/profile/LuoxuanLove` → `LuoxuanLove`）。自动识别值可被后续更优识别覆盖,但用户手输后不再覆盖。

抓取器通用要求：

- **节流**：请求间固定最小间隔（HTML 建议 ≥ 2s，可配置），即便在 WebView2 内也串行限速。
- **重试**：失败指数退避；区分「限流」「Cloudflare 质询」「真错误」，分别处理。
- **增量**：记录 `lastSyncedAt`，第二次只拉新提交，遇到已存在 id 提前停止。
- **缓存**：抓过的页面 / 提交落库，避免重复请求。
- **解析**：AngleSharp 解析 `/submissions/{handle}` 分页表格，提取
  `(submissionId, gymId, problemIndex, problemName, verdict, submitTime)`。
- 需要精确 verdict / 时间时，对已知 gymId 用 `contest.status?contestId=<gymId>&handle=X`
  API 补全（公开 gym 有效；同样优先在 WebView2 内取以规避挑战）。

**架构影响**：Scraper 不再是「纯 HttpClient 组件」，而需依赖一个 WebView2 执行器抽象
（`IBrowserFetcher`：给 URL 返回 HTML/JSON）。`VPHelper.Core` 只定义该接口，WebView2 实现落在
`VPHelper.WinUI`（因 WebView2 是 Windows/宿主相关）。这样 Core 仍可跨平台，将来换其他内嵌浏览器
只需另做一个 `IBrowserFetcher` 实现。

### 6.3 本地比赛 ↔ rankland 映射（目录自动 + 搜索手动）

1. RanklandClient 拉取官方比赛列表（见 6.4，来源为 `algoux/srk-collection`）。
2. 本地比赛、VP 结果与映射均以 `(Platform, ContestId)` 唯一标识；相同数字 ID 的 Codeforces
   Gym 与 QOJ Contest 必须独立存储、独立关联，不能跨平台串联。
3. Codeforces 优先按赛事目录中已核实的 Gym 链接匹配；克隆比赛沿完整的
   `CloneParentGymId` 链逐级查找，链上任一节点命中目录即可。QOJ 优先按赛事目录中已核实的
   `QojContestId` 精确匹配。目录外比赛不得靠标题、日期或题数相似度生成候选或自动确认。
4. 关联页统一列出账号页已经同步落库的 Codeforces Gym 与 QOJ 比赛；账号页负责抓取提交，
   关联页只负责自动匹配、手动选择和解除绑定，不在页面切换时隐式抓取账号数据。
5. 自动匹配写入 `ContestMapping`（`confirmed=2`），人工确认写入 `confirmed=1`；已有人工映射
   优先级最高，批量自动匹配不得覆盖。目录外比赛保留未关联状态，并允许在同一行输入关键词
   搜索官方比赛。空输入不显示候选、不进行匹配；用户必须点击候选后再确认，回车不得默认选择第一项。
6. VP 详情按当前比赛平台读取确认映射，因此 CF 与 QOJ 均可加载同一套官方榜单、名次和奖牌线。

### 6.4 rankland 数据接入（已核实定案）

**关键结论**：rankland（`rl.algoux.cn`）是一个 React SPA，但其榜单数据的**真正来源**是公开
GitHub 仓库 **`algoux/srk-collection`**（"长期维护的历年算竞榜单合集数据"）。因此
RanklandClient **不抓 SPA**，而是直接读取该仓库的 srk JSON 文件。这把「逆向抓 SPA」降级为
「读 Git 仓库里的静态 JSON」，稳定性与实现难度都大幅改善。

已核实的硬事实（2026-07-06 实测）：

- **仓库**：`github.com/algoux/srk-collection`，**默认分支 `master`**（非 `main`），
  License **AGPL-3.0**，官方要求使用时**注明数据来源**。
- **目录树配置**：`official/config.yaml`，嵌套 YAML，描述整棵榜单目录树。结构示意：

  ```yaml
  root:
    children:
      - path: icpc            # 顶层 category
        name: ICPC
        children:
          - path: icpc2026    # 赛季分组
            name: ICPC 2026 (51st)
            children:
              - path: icpc2026invitational-shenzhen
                format: srk.json
                name: 2026-04-11 深圳邀请赛   # 叶子：含日期 + 中文名
  ```

- **叶子文件真实路径规律**（2026-07-06 clone 实测更正）：叶子文件**扁平**放在其
  所属分组目录下，文件名即叶子 `path` 加 `.srk.json`，**不重复叶子目录名**。构造
  规则为：把从 `official` 到该叶子的所有祖先 `path` 段用 `/` 连接，末尾接
  `<leaf>.srk.json`：

  ```text
  https://raw.githubusercontent.com/algoux/srk-collection/master/
    official/<seg1>/<seg2>/.../<leaf>.srk.json
  ```

  实测例（HTTP 200）：
  `official/icpc/icpc2024/icpc2024invitational-wuhan.srk.json`、
  `official/icpc/icpc2023/icpc2023nanjing.srk.json`、
  `official/ucup/ucup3rdfinals.srk.json`（category 直挂叶子，深度 3）。
  层级深度不固定（多数为 `official/<category>/<group>/<leaf>` 4 段，也存在
  `official/<category>/<leaf>` 3 段如 ucup、部分 provincial 更深）。**因此路径必须
  由 config.yaml 树上逐层 `path` 拼接得出，不能假设固定深度，也不重复叶子目录名。**
  旧稿所述「叶子目录名重复一次」为早期误判，已作废。

- **类别可从名称/路径推断**：`name` 字段含「邀请赛 / 网络赛 / Regional / Final / 省赛」等
  关键词，`path` 含 `invitational` / `regional` / `preliminary` 等，足以支撑 Phase 2 的
  区域赛 / 省赛 / 邀请赛自动分类。

**RanklandClient 取数流程**：

1. 拉 `official/config.yaml`，解析嵌套树，展开出所有叶子（category 路径 + name + path）。
2. 对每个叶子按上述路径规律拼出 raw URL，取 `<leaf>.srk.json`。
3. 解析 srk（见 6.5 字段结构），落入 `OfficialContest` + `OfficialStanding`。
4. **缓存**：config.yaml 与各 srk 文件按 ETag / 本地时间戳缓存，避免重复拉全量。
   可选优化：记录仓库最新 commit，只在变化时刷新。

**署名要求**：UI 中需展示数据来源 `algoux/srk-collection`（AGPL-3.0）。

### 6.5 srk 格式字段（v0.3.x，已核实）

srk（standard-ranklist）由 `algoux/standard-ranklist` 定义（`schema.json` / `index.d.ts`）。
本工具需要用到的核心字段：

```text
Ranklist (顶层)
  type, version
  contest    : Contest
  problems   : Problem[]
  rows       : RanklistRow[]
  series, markers, sorter, contributors, remarks 等（可选）

Contest
  title      : Text                 # string 或 i18n {fallback, <lang>: string}
  startAt    : ISO8601 字符串
  duration   : [number, TimeUnit]   # TimeUnit: ms|s|min|h|d
  frozenDuration, banner, refLinks（可选）

Problem
  alias      : 'A' | 'B' | ...（可选）
  title, link, style（可选）
  statistics : { accepted, submitted }（可选）

RanklistRow
  user       : User
  score      : { value, time?:[number,TimeUnit] }
  statuses   : RankProblemStatus[]  # 顺序对应 problems[]

RankProblemStatus
  result     : 'FB' | 'AC' | 'RJ' | '?' | null
  time       : [number, TimeUnit]（可选，AC 用时）
  tries      : number（ICPC：截至首个 AC 的有效提交数）
  solutions  : Solution[]（可选，按提交时间升序）

User（含队伍）
  id, name:Text
  official, avatar, organization, location（可选）
  teamMembers : ExternalUser[]（队伍成员，可选）
```

**建模注意**：

- `Text` 可能是 i18n 对象 → 存 JSON 列或取 `fallback`。
- `TimeDuration` 是 `[值, 单位]` 元组 → **统一归一化为秒/毫秒**入库，避免单位混乱。
- 问题级 `accepted/submitted` 在 `Problem.statistics`；每队每题结果在 `RankProblemStatus`。
- 版本字段存在 `@since`（0.3.6/0.3.7/0.3.9…），解析时对可选字段做**存在性判断**。
  实测 srk-collection 内文件版本跨度为 **0.3.2 ～ 0.3.13**（同一仓库混用多版本），
  解析器必须对所有可选字段容错，不得假设某固定版本。

### 6.6 VP 成绩插入官方榜单（Phase 2 算法要点）

一旦某 gym 与某 official 确认映射，「推算名次」的算法为：

1. 从 `OfficialStanding` 取该官方赛全部真实队伍的 `(solved, penalty/score.time)`。
2. 取本账号该 gym 的 VP 成绩 `(solvedCount, penalty)`（见第 5 节 `VpResult`）。
3. **口径对齐**（关键坑）：
   - 罚时口径需一致（ICPC 通常每错误提交 +20min，且只对最终 AC 的题计罚时）。
   - 时间基准：VP 的题目用时以「VP 开始」为零点，官方以「比赛开始」为零点，二者天然一致，
     但需确认 gym 的 ghost/时间与 srk 的 duration 对齐。
   - 封榜（frozenDuration）：官方榜可能封榜，推算名次时应基于**完整榜**（srk 通常给完整数据）。
4. 将 VP 成绩按官方 sorter 规则插入，得到名次；结合 `medalRules` 判断奖牌线。

**名次与奖牌线 UI 口径**：名次卡只展示“推算名次”、插入后的大号名次和底部基线对齐的参赛队总数，不重复解释插榜过程，也不显示“VP 推算 / 练习推算”徽标或“金 / 银 / 铜牌”文字。金、银、铜累计名次门槛直接画在完整官方榜单对应行之后；每条分界使用“左侧线段 + 右侧独立描边标签”的分隔带，文字与线段不得叠放。榜单标题区同时标明奖牌线来自 srk 官方规则还是缺省比例推算，避免把推算线误认为官方发布结果。自身 VP 行不使用三角形前缀，并自动滚动到保留附近正式队伍的上下文位置。

**题目提交状态 UI 口径**：赛时状态直接显示，`+x` 表示经过 `x` 发有效失败提交后通过，`-x` 表示已有 `x` 发有效失败提交但未通过；首发 AC 显示 `+`。赛后提交使用同样规则并加括号。若赛时未通过且赛后继续提交，显示为 `-m(+n)` / `-m(-n)`；若赛时没有有效提交，则直接显示 `(+n)` / `(-n)`，赛后首发 AC 显示 `(+)`。CE、Testing、Skipped 等不计入有效失败提交数。赛后首次 AC 的“通过时间”直接显示实际时间，不再添加“补题”前缀，括号状态已经足以区分来源。

**详情加载与空态 UI 口径**：中间详情、右侧官方榜单均采用“固定分区标题 + 剩余内容区”，标题分别为“比赛详情”和“官方榜单”，使用相同字号、高度与顶部基线；内容区使用互斥的“加载 / 内容 / 空态”视图，不以半透明加载层覆盖旧内容。未选择比赛时，中间仅在标题下显示居中的“暂无比赛详情 / 请从左侧选择一场比赛”，不显示具体 Gym 标题、题目分区标题或空表头。

**奖牌线规则（已定案）**：

- 若该 official 有明确奖牌线，以之为准。**实测**官方奖牌数落在
  `series[]` 中 `rule.preset === "ICPC"` 那一项的 `rule.options.count.value`，
  为三元组 `[金, 银, 铜]`（累计名次门槛，非区间人数；实测 wuhan 邀请赛该三元组
  各段互相包含，如 `[30,60,90]` 表示金≤30、银≤60、铜≤90）。**约定**：三元组为
  `[0,0,0]` 视为「未提供官方线」，落到下面的默认规则；顶层 `markers` 字段实测多为
  `null`，不作为主来源。人工录入到 `medalRules` 亦可覆盖。
- **否则按默认规则划线**：以「**正式参赛选手**」（srk `rows[]` 中 `user.official === true`）中
  **有效提交队伍数** `N` 为基数，取 **10% / 30% / 60%**，各自**向上取整**得到金 / 银 / 铜的名次门槛：
  - 金牌线 rank ≤ `ceil(N × 0.10)`
  - 银牌线 rank ≤ `ceil(N × 0.30)`（含金牌区间）
  - 铜牌线 rank ≤ `ceil(N × 0.60)`（含金银区间）
- 「有效提交」定义：该队伍在榜单中**至少有一次有效提交**（`statuses[]` 中存在非空 `result`，
  即产生过 AC 或罚时的队伍）；纯挂零、无任何提交的队伍不计入基数 `N`。
- VP 名次落在哪个门槛内即判定对应奖牌（金优先，其次银、铜）。
- 该默认规则封装为可配置的 `MedalRuleProvider`，未来若某类赛事有不同官方比例可覆盖。

---

## 7. 安全与合规注意

- 只访问用户自己账号的 session，属正当使用；**不撬取其他浏览器的 cookie**。
- cookie / session 仅存本地（WebView2 UserDataFolder + 本地 SQLite），不外传。
- 抓取遵守限流，避免对 CF 造成压力。
- rankland 数据遵守其使用条款，注明来源。

---

## 8. 分阶段实现计划

### Phase 1 —— 打通数据，UI 最小（首要交付）

目标：把一个账号的 gym VP 数据完整落库并能看。不追求映射自动化与精美报表。

1. **抓取层**：内嵌 WebView2 登录 → 抓 `/submissions/{handle}` 全量分页 →
   解析 `(gymId, problem, verdict, time)` → 存 SQLite（限流 + 重试 + 增量）。
2. **聚合层**：按 gym 分组，算 solved 数、罚时、每题首次 AC 时间。
3. **rankland 接入**：拉官方比赛列表 + 提供「手动关联」界面（gym → 官方赛）。
4. **最小 Fluent UI**：NavigationView，左侧比赛列表，右侧单场 VP 详情
   （题目通过情况 + 若已绑定则显示推算名次）。

Phase 1 明确不含：自动标题匹配、比赛类型自动分类、多账号 / 队伍对比、趋势图表。

### Phase 2 —— 智能化与官方榜单推算

- gym ↔ official 自动候选匹配（标题 / 题集 / 时间）。
- 把 VP 成绩插入官方榜单，推算名次与奖牌线。
- 区域赛 / 省赛 / 邀请赛自动分类。

### Phase 3 —— 分析与对比

- 多账号 / 队伍对比。
- VP 趋势、过题分布、罚时分析等图表。
- 导出报表。

---

## 9. 已核实结论与待确认事项

### 已核实（2026-07-06）

- **rankland 数据接入已定案**：不抓 SPA，直接读公开仓库 `algoux/srk-collection`
  的 srk JSON 文件（见 6.4）。分支 `master`、AGPL-3.0、需署名。
- srk 为 v0.3.x，字段结构已确认（见 6.5）。
- convert-to 存在 `CodeforcesGymGhostDATConverter`（srk→CF Gym Ghost，单向），
  佐证「官方榜单 ↔ CF Gym」映射本就是 algoux 生态内的已知关系，利好 Matcher。

- **CF 弹 Cloudflare 已确认**：抓取必须在 WebView2 已过盾的浏览器上下文内执行
  （见 6.2），纯 HttpClient 方案作废。
- **奖牌线缺省规则已定案**：srk 未带奖牌线时，按「正式参赛队伍有效提交」的
  10% / 30% / 60% 向上取整划金 / 银 / 铜线（见 6.6）。

### 待确认

- srk-collection 全量文件数量级：2026-07-06 clone 实测 `official/` 下约 **387** 个
  `.srk.json` 叶子，单文件几十 KB～1.6 MB。逐文件 raw 拉首次同步偏慢（数百请求），
  Phase 1 可接受（带缓存 + 后台增量）；后续可选优化为记录仓库最新 commit、只在变化时刷新，
  或直接 `git` 稀疏拉取。
- srk 的 `series` 奖牌线：实测**大量文件带官方线**（`series[].rule.preset==="ICPC"` 且
  `options.count.value=[金,银,铜]`），也有相当比例为 `[0,0,0]`（未标注，落默认规则）。
  故 `MedalRuleProvider` 逻辑：先读 `series` 官方线，全 0 或缺失才用默认 10/30/60 规则。
