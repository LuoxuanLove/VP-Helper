# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 语言要求

所有对用户可见的对话回复都必须使用简体中文，包括：工具调用之间发送的进度说明、任务中途的消息总结、以及任务结束后的最终回复。代码注释、提交信息等沿用仓库既有风格（当前为英文）。

## 项目简介

VP Helper 是一个 Codeforces Gym VP（Virtual Participation）成绩分析工具：抓取某账号的全部 gym 提交，聚合出每场比赛的过题数/罚时/首杀时间，关联到 rankland 官方标准榜单（数据源为公开仓库 `algoux/srk-collection`），推算「如果打现场排第几 / 是否达到奖牌线」，并以 WinUI 3 Fluent 桌面 UI 呈现。

`DESIGN.md` 是唯一的设计事实源，`PROGRESS.md` 记录已实现内容与验收自检。改动涉及抓取策略、srk 解析、奖牌线规则、rankland 路径规律等核心决策时，先读 DESIGN.md 对应小节。

## 常用命令

```bash
# 跨平台单元/集成测试（Core.Tests 为 AnyCPU，可在非 Windows 跑）
dotnet test tests/VPHelper.Core.Tests/VPHelper.Core.Tests.csproj

# 单个测试类 / 单个测试
dotnet test tests/VPHelper.Core.Tests/VPHelper.Core.Tests.csproj --filter "FullyQualifiedName~SrkParserTests"
dotnet test tests/VPHelper.Core.Tests/VPHelper.Core.Tests.csproj --filter "DisplayName~<测试方法名>"

# 构建 Core（AnyCPU）
dotnet build src/VPHelper.Core/VPHelper.Core.csproj

# 运行桌面应用（仅 Windows x64）
dotnet run --project src/VPHelper.WinUI/VPHelper.WinUI.csproj -p:Platform=x64
```

注意：不要在解决方案级用 `-p:Platform=x64` 一次性构建全部项目——Core/Tests 是 AnyCPU、WinUI 是 x64 的混合平台，解决方案级 x64 构建会报 MSB4126（预期行为）。按项目分别构建。

`LiveRanklandTests` 是 opt-in 的联网测试，会真实访问 `algoux/srk-collection` 仓库。

## 架构

两个项目 + 一个测试项目，核心约束是「跨平台业务核心 + Windows UI 壳」的分层：

- **VPHelper.Core**（`net8.0`，无 UI 依赖，可跨平台复用）——全部业务逻辑。
- **VPHelper.WinUI**（`net8.0-windows`，x64）——WinUI 3 + WebView2 的 UI 壳，实现 Core 定义的平台相关接口。
- **VPHelper.Core.Tests**——xUnit 测试，含真实 CF HTML / srk 样例 fixtures 与落地 SQLite 全链路集成测试。

### 决定架构的关键约束（务必先理解）

1. **官方 API 不覆盖 gym**：`user.status` API 不返回 gym 提交。gym 数据的唯一可靠来源是带登录态抓 HTML 页面（`/submissions/{handle}`）。
2. **CF 有 Cloudflare 盾**：`cf_clearance` 与 TLS/JA3 指纹强绑定，把 cookie 搬到裸 HttpClient 会重新触发挑战。因此**所有 Codeforces 抓取都在已过盾的 WebView2 浏览器上下文内执行**（页面内 `fetch`），不用裸 HttpClient。这是 `IBrowserFetcher` 抽象存在的原因。
3. **rankland 不抓 SPA**：榜单数据即公开仓库 `algoux/srk-collection` 的 srk JSON 文件，经 raw.githubusercontent 直接读取（走普通 HTTPS，无 CF）。叶子文件路径必须由 `official/config.yaml` 的树逐层 `path` 拼接得出，深度不固定（不能假设固定深度、不重复叶子目录名）。

### 抓取抽象（跨平台的关键接缝）

`IBrowserFetcher`（Core 定义）给一个 URL 返回 HTML/JSON，并区分 `Ok` / `CloudflareChallenge` / `RateLimited` / `Error`。

- `WebView2BrowserFetcher`（WinUI）是真实实现：在已登录 WebView2 里跑页面内 `fetch`，复用 cookie/指纹/挑战态，并检测 Cloudflare 质询页。
- 换其他内嵌浏览器只需另做一个 `IBrowserFetcher` 实现，Core 不动。

### 数据流

```
handle → SubmissionScraper（分页 + 增量 + 重试 + CF 检测，经 IBrowserFetcher）
       → SubmissionsParser（AngleSharp 解析提交表格）
       → SQLite（增量落库）
       → Aggregator（按 gymId 聚合 solved/罚时/首杀）
       → Matcher（gym↔official 候选打分，半自动，人工确认后落 ContestMapping 复用）
       → RankingEngine + MedalRules（VP 成绩插入官方榜单推算名次/奖牌）
       → UI
```

### 组合根与数据访问

- **AppHost.cs**（WinUI）是唯一的 composition root，用 `Microsoft.Extensions.DependencyInjection` 装配 Core 服务、SQLite、ViewModels。
- **EF Core DbContext 非线程安全**：用 `AddDbContextFactory` 注入 `IDbContextFactory<VpDbContext>`，每个工作单元创建短生命周期 context，不共享单例。
- **BrowserFetcherHolder**：登录 WebView2 就绪后才注入实际 `IBrowserFetcher`，并据此构造 `SubmissionScraper`/`GymSyncService`。
- **SchemaGuard**：应用 `EnsureCreated()` 只在库不存在时建表、从不 ALTER 既有表。给已存在的用户 DB 补新增列必须走 `SchemaGuard.Reconcile`（幂等 `ALTER TABLE ... ADD COLUMN`，非 SQLite provider no-op）。给实体新增字段时，同步维护 SchemaGuard 的列清单，否则老用户 DB 会因缺列崩溃。

### UI 分层

WinUI 用 MVVM（`CommunityToolkit.Mvvm`）：`Pages/*.xaml` + `ViewModels/*`，三个功能页——LoginPage（WebView2 登录 + 演示模式）、ContestsPage（gym 列表 + 单场 VP 详情）、OfficialPage（rankland 同步 + gym↔official 手动关联）。UI 需展示数据来源署名 `algoux/srk-collection (AGPL-3.0)`。

## 编码约定要点

- srk 格式版本在仓库内跨 0.3.2 ~ 0.3.13 混用，解析器对所有可选字段做存在性判断容错，不假设固定版本。
- srk 的 `TimeDuration` 是 `[值, 单位]` 元组，`Text` 可能是 i18n 对象——入库前统一归一化（时间转秒，Text 取 fallback 或存 JSON）。
- 奖牌线：优先读 srk `series[]` 中未过滤的 ICPC series 的 `rule.options.count.value=[金,银,铜]`；为 `[0,0,0]` 或缺失时回落默认规则（正式参赛且有效提交队伍数 N 的 10%/30%/60% 向上取整）。带 marker 过滤的子奖项 series 不能当总榜奖牌线。
- 抓取器必须：串行限速（HTML ≥ 2s）、失败指数退避、增量（遇已存在 id 提前停止）。
- 抓取相关的解析改动应用真实 CF HTML fixtures 回归（`tests/fixtures/`），不要只靠自己写的合成 HTML——历史上真实 markup 暴露过多个解析 bug。

## Skills

`skills/yurenai-image-generation/` 提供通过 Yurenai OpenAI 兼容图像 API 生成/编辑图像的能力，用于生成 UI 风格样张等。API key 从环境变量 `YURENAI_API_KEY` 读取，切勿写入文件或回显。
