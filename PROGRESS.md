# VP Helper — 进度与自检清单

## 2026-09-23：公开仓库与首个公开预览版

- 作者明确授权公开发布到个人账号 `LuoxuanLove`。公开源码仓库：`https://github.com/LuoxuanLove/VP-Helper`，主分支为 `main`，原始网页、数据库与浏览器状态均不进入 Git 历史或发行包。
- 首轮 GitHub Windows CI 已实际执行，357 项非 Live 测试通过，WinUI 构建成功；远端 NuGet 审计额外发现 AngleSharp 1.1.2 的 GHSA-pgww-w46g-26qg 警告，因此升级至公告明确修复的 1.5.0，并重新验证真实网页 fixtures。
- `v0.1.0-preview.4` 保留为标记已替代的未发布草稿，准备以 `v0.1.0-preview.5` 发布修复后的预览版。发布必须在远端构建成功且下载附件校验通过后进行。
- 本次公开授权覆盖个人仓库与 Release，不代替项目自身许可证的选择，也不改变第三方组件许可。版本仍标记预览版，保留未签名、旧 Windows App SDK 与完整干净机器验收尚未完成等说明。

## 2026-09-22：Git 初始化与发行准备

- 已初始化 `main`，分开提交源码基线、发行工具及验证修复；没有创建 GitHub 远程仓库、推送代码或发布 Release。
- `.gitignore` 隔离构建输出、数据库、WebView2 登录状态、日志、截图、原始网页和本地 Agent 状态。Windows 对路径大小写不敏感，因此特别保留 `src/VPHelper.Core/Data/*.cs`，并用源码遗漏检查与干净源码构建验证。
- 10 个可提交测试样本位于 `tests/fixtures/public/`；HTML 移除活动脚本、外部资源和认证令牌，原始副本仍保留本地。357 项非 Live 测试从 Git 导出的新目录全部通过。
- 新增 README、安装/升级指南、GitHub 发布方案、第三方来源与许可副本，以及 main/PR CI、标签触发的 Draft Release 工作流。自动化在本地已检查，但 GitHub runner 尚未执行。
- `tools/Publish-Portable.ps1` 从干净 Git HEAD 导出源码，测试后发布 WinUI x64 自包含目录，附带 40 个 NuGet 包的许可声明/材料、安装说明、版本和提交信息，生成程序 ZIP、源码 ZIP 与 SHA-256。拒绝复用版本目录、脏工作区和夹带运行数据的包。
- 本地成功样包：`artifacts/releases/0.1.0-preview.3/VPHelper-0.1.0-preview.3-win-x64.zip`，70,457,732 字节（约 67.2 MiB），对应源码提交 `1b73e10328917f63e99a5e153f655d5e0ec8ed62`。357 项离线测试、ZIP CRC、两个 ZIP 的 SHA-256、519 个归档条目的数据排除检查全部通过。解压后在当前开发机成功启动并显示无账号的空数据库首页，没有执行登录或同步；原日常运行实例保持独立。
- `.1` / `.2` 目录保留首次发行验证的失败证据，均没有被发布；修复了 Windows `Data` 源码忽略问题、WinUI WebView2 组件检查路径及 NuGet 运行时版本区间解析。
- 稳定公开发行仍需：选定项目源码许可证、升级已结束支持的 Windows App SDK 1.5 及临近支持期末尾的 .NET 8、干净 Windows 安装验收、签名决策和首次 GitHub CI 实跑。详见 `docs/RELEASING.md`。

## 2026-09-22：官方比赛、页面交互与窗口约束

- 官方比赛增加本地 VP / 仅练习 / 未 VP 状态、推算名次、账号与赛场成绩菜单，支持组合搜索筛选、空结果提示及重置。保留合并目录项的全部官方 ID；按平台隔离同号比赛，不将练习或零记录误写成 VP 排名。
- 缓存优先打开目录，显式刷新更新公开目录；失败回退且提示。目录只读取本地榜单，进入详情按现有逻辑补载榜单。
- 关联页增加名称/ID 搜索、待关联/已关联筛选、空态与加载失败反馈，选择框适配页面边界。首页提供任务入口，统计卡随宽度排布；账号平台页签支持键盘。
- 原生窗口大小约束：初始 1440×900 DIP，最小 1000×680 DIP，普通窗口缩放比例 4:3～21:9；工作区/DPI 自适应、越界位置修复、最大化与最小化保存分离。
- 导航自动收起，VP 页面按实际内容宽度切换三栏或「列表 + 详情/榜单页签」。实机发现并修复了右侧榜单截断、通过时间列过窄、目录表格超出视口、紧凑导航署名竖排等问题。
- 本轮源码备份：`out/ui-backup-20260922-163830/`。独立验证输出与 SQLite 备份位于 `out/ui-validation/`，界面检查使用副本，未触发账号同步、改绑或删除账号。
- 自动验证：Core 测试 **360/360 通过**，新增真实 SQLite 投影/过滤/离线回退/合并赛事测试，以及窗口初始尺寸、6 组工作区/DPI（每组 300 个随机尺寸）的边界测试。WinUI x64 构建 **0 warning / 0 error**。
- 实机验证：五个页面均已打开检查；官方「已 VP」筛选、账号成绩菜单、跳转后的同一名次（例如 #13 / 207）、窄屏详情/榜单切换已核验；首次启动居中适配、1000×680 DIP 下限布局与宽屏三栏已检查。不同物理显示器间拖动、所有系统贴靠组合和不同 DPI 的真实硬件切换未逐一验证，数学边界测试不等同于这些硬件验收。

> 状态：Phase 1 + Phase 2 已实现。日期：2026-07-06
> 唯一事实源：`DESIGN.md`（本轮已按实测更正，见下）。

## 一、实现了什么

### 工程结构
```
VPHelper.slnx
src/
  VPHelper.Core/         # 跨平台，无 UI 依赖（net8.0）
    Models/Entities.cs           数据模型（DESIGN §5）
    Data/VpDbContext.cs          EF Core + SQLite
    Srk/SrkModels.cs, SrkParser.cs   srk v0.3.x 解析（归一化到秒）
    Rankland/RanklandConfig.cs, RanklandClient.cs   config.yaml 树展开 + srk 拉取
    Scraping/IBrowserFetcher.cs  抓取抽象（Core 只定义接口）
    Scraping/Throttle.cs         节流 + 指数退避
    Scraping/SubmissionsParser.cs  AngleSharp 解析提交页
    Scraping/SubmissionScraper.cs  分页 + 增量 + 重试 + CF 检测编排
    Analysis/Aggregator.cs       VP 聚合（solved / 罚时 / 首杀）
    Analysis/MedalRules.cs       奖牌线（官方优先，缺省 10/30/60%）
    Analysis/RankingEngine.cs    ICPC 排序 + VP 插入推算名次
    Analysis/Matcher.cs          保留的通用相似度工具（不参与映射页自动候选）
    Analysis/ContestClassifier.cs  比赛类型分类
    Services/GymSyncService.cs, RanklandSyncService.cs, MappingService.cs
  VPHelper.WinUI/        # WinUI 3 + WebView2（net8.0-windows，x64）
    Services/WebView2BrowserFetcher.cs  CF 感知的页面内 fetch 实现
    Services/HttpRawContentSource.cs    rankland 用普通 HTTPS（非 CF）
    ViewModels/                  Login / Contests / Official / Main
    Pages/                       LoginPage / ContestsPage / OfficialPage
    MainWindow.xaml(.cs)         Mica + NavigationView + 自定义标题栏
tests/
  VPHelper.Core.Tests/   # 80 个测试，全部通过（含真实 CF 提交页 HTML + 落地 SQLite 全链路 + 联网 rankland 集成测试）
  fixtures/              # 真实样例：config.yaml + gzcpc2024 + icpc2023macau + real_submissions.html
                         #   + gym_submissions_p1/p2.html（真实 CF 行 markup 拼的 gym 分页）
```

### 对 DESIGN.md 的更正（已改文档并说明理由）
1. **6.4 叶子路径规律**：旧稿称"叶子目录名重复一次作文件名"。2026-07-06 clone `algoux/srk-collection`
   实测：叶子是**扁平文件**，文件名即叶子 path 段 + `.srk.json`，**不重复目录**，深度不固定（3~4 段）。
   已改为"由 config.yaml 逐层 path 拼接得出"。
2. **6.5 srk 版本**：旧稿称实测 0.3.9。实测仓库内版本跨度 **0.3.2 ~ 0.3.13** 混用，解析器对所有可选字段容错。
3. **6.6 奖牌线来源**：实测奖牌数在 `series[].rule.options.count.value=[金,银,铜]`，`[0,0,0]` 表示未指定。
   且发现**带 marker 过滤的 series（如 provincial/university 子奖项）不能当作总榜奖牌线** —— 只取
   未过滤的 ICPC series 作官方线，否则回落默认规则。

## 二、验收闸门自检

### Phase 1
- [x] 解决方案编译 0 error；所有单元测试通过 → **83/83 通过，Core/Tests/WinUI 均 0 warning 0 error**
      （2026-07-06 复验：Core/Tests AnyCPU、WinUI x64 分别构建均 0 warning；解决方案级 x64 构建会报
      MSB4126，因 Core/Tests 为 AnyCPU、WinUI 为 x64 的混合平台，属预期，按项目分别构建即可）。
- [~] 能登录并过 Cloudflare、抓到 gym 提交并落库；二次走增量 → **除「WebView2 真实登录+过 CF」这一
      步（设计上必须人工、headless 无法完成）外，本闸门其余全部已用真实数据端到端验证**：
      `GymSyncIntegrationTests` 用**真实 CF 行 markup** 拼的 gym 分页（source-link 在前、problem-link
      在后、真实 verdict/time 单元格 + 分页控件），经 `IBrowserFetcher` 喂给完整 `GymSyncService`，
      落到**真实磁盘 SQLite 文件**：第一次全量抓 4 条落库、聚合出 2 个 gym 的 VpResult（solved/罚时/
      首杀时间正确）；**第二次运行走增量：新增 0 条、无重复、提前停止不重抓全量**。仅剩的人工步骤是
      WebView2 内完成 CF 人机验证，运行 `dotnet run` 后一步即可。
- [x] UI 列出 gym 比赛 + 单场 VP 详情（通过/罚时/首杀时间）→ ContestsPage 已实现。**新增「演示模式」
      按钮**（登录页）：用内置真实 CF gym markup 走完全相同的 `GymSyncService` 管线落库，无需账号/CF
      即可端到端渲染 UI（比赛列表 + 单场详情），完成后自动跳转「VP 比赛」页。用于在无账号环境验证 UI 链路。
- [x] 手动关联 gym↔official 并持久化 → OfficialPage 候选列表 + 确认，落 ContestMapping，单测覆盖。
- [x] UI 展示 rankland 数据来源署名 → NavigationView PaneFooter + OfficialPage 均显示 `algoux/srk-collection (AGPL-3.0)`。

### Phase 2
- [x] VP 成绩插入官方榜单得出名次，人工抽查合理 → EndToEndTests 用真实 icpc2023macau/gzcpc2024 验证。
- [x] 奖牌线默认规则正确（N 口径 / 向上取整 / 区间包含），官方线优先 → MedalRuleTests + ServiceTests 覆盖。
- [x] 映射页仅按赛事目录/完整克隆链自动关联；目录外只允许用户输入关键词后手动选择并确认，空查询不生成候选。
- [x] 比赛类型自动分类对样本正确 → ClassifierTests 覆盖 icpc/ccpc/provincial/final/invitational。
- [x] 全量测试通过、编译 0 warning、无明显崩溃 → 应用实测可启动渲染（Mica 合成器出图）。

## 三、用真实数据做的额外验证（本轮新增，非 mock）
抓取相关不能只靠自己写的合成 HTML，故拉取了**真实数据**回归，发现并修复 2 个真 bug：

1. **真实 CF 提交页 HTML**（`tests/fixtures/srk/real_submissions.html`，抓自公开的
   `/submissions/tourist`）：
   - **Bug①（已修）**：真实每行第一个 `<a>` 是「查看源码」链接
     （`/gym/{id}/submission/{subid}` 或 `/contest/{id}/submission/{subid}`），也含 `/gym/`、
     `/contest/`，旧解析器会误取它 → gym 行拿到空题号。已改为**优先匹配含 `/problem/` 的链接**，
     回退才用宽松匹配。`RealSubmissionsHtmlTests` 用真实行 markup 锁定该修复。
   - 真实 verdict 在 `span[submissionVerdict]`（HTML 属性小写化，选择器命中）、时间在
     `.format-time` —— 均已验证解析正确。
2. **登录检测**（`LoginPage`）：
   - **Bug②（已修）**：旧检测用 `#header a[href^="/profile/"]`，但**登出状态下该链接也存在**
     （页面主人的 profile）→ 会误判已登录。实测真实登录标记是 header 里的 `/{handle}/logout` 链接，
     已改为从该 href 直接取 handle。
3. **rankland 活体链路**（`LiveRanklandTests`，opt-in 网络测试，rankland 走普通 HTTPS 无 CF）：
   直接对真实 `algoux/srk-collection` 仓库跑 `RanklandClient` → config.yaml 展开 → 拉真实 srk →
   解析 → 排名 → 奖牌线，**已通过**（本环境可跑，已实测 200 + 2 测试绿）。
4. **持久化 DB 的 schema 漂移**（真实同步日志暴露，已修）：
   - **Bug③（已修）**：`Account.LastSyncedAt` 是在初版 DB 建库后才加的字段；应用用
     `EnsureCreated()` 建库,它只在库不存在时建表、**从不 ALTER 既有表**。已存在的用户 DB
     缺该列 → 每次同步一碰 `_db.Accounts` 就抛 `SQLite Error 1: 'no such column: a.LastSyncedAt'`
     (`GymSyncService.cs:63`)。已新增 `Data/SchemaGuard.cs`:启动 `EnsureCreated()` 后跑幂等
     `ALTER TABLE ... ADD COLUMN`（先查 `PRAGMA table_info`，非 SQLite provider 直接 no-op），
     保留已抓的 submission 不清库。`SchemaGuardTests` 复现旧库崩溃 + 验证修复且幂等。
     后续如实体频繁变更，宜改用真正的 EF migrations 替代手维护 `AddedColumns` 列表。

## 四、已知限制 / 未覆盖点
- **仅剩「真实 CF 账号登录 + 过 Cloudflare + 抓 gym 落库 + 二次增量」这一条 gate 未端到端跑通**：
  该步骤要求交互式人机验证（Cloudflare 挑战 + 可能的 2FA），headless 环境无法完成。除此之外的每个
  环节都已用**真实数据**回归：真实提交页 HTML 解析、真实 srk 排名/奖牌线、活体 rankland 拉取、增量停止
  逻辑（mock fetcher）。用户只需在有账号的桌面上跑一次应用完成登录即可闭合此 gate。
- gym 标题目前用占位（`Gym {id}`）+ 首个提交时间作 startTime；真实标题需再抓 gym 页补全（未做）。
- 罚时按"每错误提交 +20min"，时间基准用绝对提交时间；与 official 精确对齐需 gym 比赛开始时间
  （`PenaltyFromContestStart` 已提供，UI 尚未接入 gym 开始时间来源）。
- rankland 全量同步 387 个 srk 文件为逐文件 raw 拉取；首次全量较慢，UI 默认只同步前 60 个（可点"同步全部"）。

## 五、如何运行与验证
```bash
# 单元/集成测试（跨平台可跑）
dotnet test tests/VPHelper.Core.Tests/VPHelper.Core.Tests.csproj

# 运行桌面应用（Windows x64）
dotnet run --project src/VPHelper.WinUI/VPHelper.WinUI.csproj -p:Platform=x64
# 或 build 后运行 bin/x64/Debug/net8.0-windows10.0.19041.0/win-x64/VPHelper.WinUI.exe
```

### 真实账号路径（闭合最后一条 gate）
登录与同步页 → 在内嵌 WebView2 登录 CF（完成人机验证 / 2FA）→ 输入 handle → 同步；
再点一次「同步」可观察增量（新增 0、不重复全量）。

### 演示模式(无需账号，可直接看 UI 端到端)
登录与同步页点「演示模式(无需登录)」→ 内置**真实 CF gym 页 markup** 走完全相同的
`GymSyncService` 管线(解析→落库→聚合)→ 自动跳到「VP 比赛」页，可见两个 gym、
单场题目通过/罚时/首杀时间。此路径让 UI 端到端渲染无需真实登录即可验证；
唯一与真实路径的差别是数据源换成内置样例(而非活体 WebView2 抓取)。

官方榜单页：同步 srk-collection → 选 gym → 确认候选；确认后回 VP 比赛页可见推算名次与奖牌。

## 六、全局 UI 统一化（2026-07-13）

- [x] 建立共享页面间距、标题层级、面板、按钮、输入框、表头与圆角资源，五个功能页不再各自维护一套视觉参数。
- [x] 统一圆角语义：命令控件 `4px`、重复项 `6px`、独立面板 `8px`；图标按钮固定 `36 x 36px`，不使用胶囊或圆形按钮。
- [x] 重排统计概览、VP 比赛、官方榜单与关联、赛事目录、账号管理页面，压缩无效留白并提高表格和列表的可扫描性。
- [x] 赛事目录分段切换改为稳定点击面，避免 WinUI Button 的 Reveal / Pressed 状态导致切换闪烁；目录链接使用短标签并保留完整 URL Tooltip。
- [x] 统一主导航、标题栏和可拖动分栏的尺寸边界，VP 三栏与导航栏在宽屏和窄屏下保持可用。
- [x] 重排账号管理页为紧凑登录状态条、稳定浏览器工作区与独立账号列表；登录检测不再触发隐式同步，只有显式点击 CF / QOJ 同步按钮才启动抓取。
- [x] WebView2 首次导航后按实际页面宽度自动适配，实机验证 Codeforces 首页以 `92%` 适宽且无横向滚动条；保留原生快捷键缩放与手动重新适宽入口。
- [x] 修正 QOJ 登录检测与同步：登录态仅信任顶部 Logout 账号区；同步优先使用按账号过滤的提交流并保留逐比赛回退；兼容当前 `span.uoj-username[data-nickname]` 提交者结构，解析异常与全抓取失败不再写成成功 0 条；账号卡片按平台隔离 VP 统计。
- [x] 修正 QOJ 首次同步提交总数水位：按包含当前 Added 实体的已知提交 ID 集合计数，避免成功落库后账号卡片仍显示 0 条。
- [x] VP 名次卡精简为“推算名次 + 大号名次 + 底部基线对齐的参赛队总数”，移除插榜说明与“VP 推算 / 练习推算”徽标；自身 VP 行移除三角形，官方榜单加载后保留自身排名附近上下文。
- [x] 金 / 银 / 铜累计门槛改为官方榜单内的独立分隔带，使用“左侧线段 + 右侧描边标签”避免文字压线，并在榜单标题区区分“官方奖牌线”和“推算奖牌线”。
- [x] 自绘标题栏与系统窗口按钮统一使用 `48px` Tall 高度；实机验证应用标题与最小化 / 最大化 / 关闭按钮垂直居中一致。
- [x] 使用 Yurenai `gpt-image-2` 连续生成并对比细线、中粗和粗线“比赛面板 + VP 播放角标”位图，最终选择在 `20px` 下保持约 `2px` 笔画的粗线源；确定性脚本修复洋红背景噪声造成的错误裁切，并完成透明化、色板归一、小尺寸锐化与多尺寸 ICO 输出。后续实机复核又修正了几何包围盒居中导致的视觉上浮、RGBA 转 RGB 引入的透明暗边以及 125% DPI 二次缩放发虚：现按 Alpha 面积做光学居中、采用预乘 Alpha 缩放与逐尺寸 Alpha 锐化，并提供 `scale-100` 至 `scale-400` 标题栏资源。图标嵌入 exe、绑定 `AppWindow` 并替换标题栏占位图标，视觉重量与侧栏 Fluent 图标一致。
- [x] QOJ 账号卡片改为从原始提交表聚合提交总数，并由启动数据守卫修复旧版本留下的 `TotalSubmissions=0` 缓存；现有同步数据无需重抓即可恢复正确显示。
- [x] 题目状态改为赛时裸写、赛后括号的统一提交序号：支持 `+x`、`-x`、`-m(+n)`、`-m(-n)`、`(+n)`、`(-n)`，CE 等非判罚提交不计数；赛后 AC 时间移除“补题”前缀。
- [x] VP 详情与官方榜单的加载、内容、空态改为互斥显示，消除加载动画与“暂无官方榜单”重叠；未选择比赛时中间显示独立“暂无比赛详情”空态，不再残留题目标题和表头。
- [x] VP 中间面板增加固定“比赛详情”分区标题，与右侧“官方榜单”统一标题高度和基线；两个空态均在各自标题下的等高内容区居中。
- [x] 账号管理页以自定义平台页签替换带固定内容内缩的 Pivot 模板，使登录状态条与下方浏览器工作区共享同一左栏栅格边界；VP 详情的“题目情况”默认按官方通过数降序、同通过数按提交数升序显示，并提供紧凑的“难度 / 题号顺序”切换。
- [x] VP 比赛列表与详情标题在存在确认映射时直接展示官方赛事名，移除“已绑定 / 未绑定”徽标及独立关联说明；未绑定条目仍以 QOJ / Gym 本地标识显示。
- [x] 首页“最近 VP 比赛”与比赛页统一绑定名称展示规则，移除关联徽标和重复官方名行；赛事目录在刷新官方数据期间展示居中的加载动画与“正在加载赛事目录”提示，加载完成后再展示列表。
- [x] 官方榜单关联页改为跨平台统一入口：同时列出账号页已同步的 Codeforces Gym 与 QOJ Contest，以 `(Platform, ContestId)` 隔离同号比赛；QOJ 按赛事目录 `QojContestId` 精确自动关联，目录外比赛保留手动搜索、确认和解除绑定能力。
- [x] VP 详情读取平台对应的确认映射，QOJ 比赛绑定后可复用官方榜单、推算名次和奖牌线；关联页增加平台汇总、待关联/已关联汇总和平台徽标，并隐藏 `QOJ 1234` 等占位标题造成的重复副标题。
- [x] 补齐用户本地未关联清单的浏览器核验目录：训练赛源 Gym 通过已验证克隆根回填官方赛事；QOJ `1668`、`2645`（补题镜像）和 `3729` 可精确自动关联。启动守卫同时补齐 CF/QOJ 旧库，且源 Gym 回退只在克隆链缺失时启用，不会覆盖已解析的父链；无对应 srk 榜单的赛事保留为仅链接目录项。
- [x] 补入 QOJ `3767`：侧边浏览器确认其为“2026 年山东省大学生程序设计竞赛”、山东分类、13 题，对应 Rankland `sdcpc16th`；目录守卫升级后会为已同步的本地记录自动关联。
- [x] 修正第九届河北省赛漏配：Gym `105909` 页面标题、13 题题集和 Rankland `hecpc9th`（2025-05-18）三项一致；训练 Gym `679438` 直接克隆自该 Gym。二者现均通过规范目录/源 Gym 回退自动关联。
- [x] 用 Codeforces 训练赛检索交叉核验“郑州 / zhengzhou / Provincial”等关键词；修正 Gym `105941` 的官方叶子为 `ccpc2025invitational-zhengzhou`（2025-06-02 郑州邀请赛），不再误绑到仅表示河南省赛总榜的 `haccpc7th`。
- [x] 扩充 2025 省赛官方 Gym：Gym `105945`（江苏/广东）、`105930`（山东）、`105922`（吉林）和 `105891`（陕西）均已按 Codeforces 检索结果、日期与 Rankland 对应叶子补入。Gym `106139` 的已验证名称改为 2025 湖南省赛（11 题），因 Rankland 尚无该届叶子继续仅作目录链接。
