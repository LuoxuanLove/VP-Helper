# VP Helper

Windows 桌面的 Codeforces Gym / QOJ 虚拟参赛成绩分析工具。同步提交、查看 VP 过题与罚时，将成绩与官方榜单比较，浏览已 VP 的官方比赛及推算排名。

## 下载与使用

公开仓库：[LuoxuanLove/VP-Helper](https://github.com/LuoxuanLove/VP-Helper)。

**[下载 Windows x64 预览版](https://github.com/LuoxuanLove/VP-Helper/releases)** · [安装与升级指南](docs/INSTALL.md)

发布后，从仓库右侧 **Releases** 下载 `VPHelper-<版本>-win-x64.zip`，不要把 GitHub 自动生成的 `Source code.zip` 当成安装包。

1. 使用 Windows 11 x64；Windows 10 的编译最低要求为 1809，但仅在仍受支持的系统上推荐使用。ARM64 / x86 尚未提供验证过的发行包。
2. 将 ZIP **完整解压**到可写目录，例如 `%LOCALAPPDATA%\Programs\VPHelper`。不能只复制 EXE，也不要直接从压缩软件内启动。
3. 运行 `VPHelper.WinUI.exe`。便携包包含 .NET 与 Windows App SDK，无需安装开发用 SDK。
4. 若账号页面提示缺少浏览器组件，安装微软官方 [WebView2 Evergreen Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)，然后重新打开应用。Microsoft Edge 浏览器本身不等同于此运行时。
5. 在「账号管理」选择平台并登录，再点击「同步该账号」；登录不会自动同步。同步后自动关联已核实的比赛并获取对应榜单。到「官方比赛」筛选已 VP，或到「VP 比赛」查看详情；“其中已 VP”随筛选结果变化。“更新目录和榜单”可手动刷新全部官方数据。

详细的安装、升级、备份和卸载方法见 [安装指南](docs/INSTALL.md)。名次为基于本地同步结果和官方榜单的推算，不是官方提交成绩。

## 本地开发

需要 Windows x64、.NET 10 SDK（由 `global.json` 选择稳定 10.0 系列）及 .NET 8 Runtime。文件审计和打包另需 Python 3.10+、PowerShell 7 与 Git。项目目前目标框架为 .NET 8，使用较新 SDK 不会自动升级应用运行时。

```powershell
dotnet test tests/VPHelper.Core.Tests/VPHelper.Core.Tests.csproj --filter "Category!=Live"
dotnet build src/VPHelper.WinUI/VPHelper.WinUI.csproj -p:Platform=x64
dotnet run --project src/VPHelper.WinUI/VPHelper.WinUI.csproj -p:Platform=x64
```

本机开发构建后，也可以通过标准 `.NET MSBuild Run` 启动已编译的应用：

```powershell
pwsh -File tools/Start-Dev.ps1
```

此入口沿用同一构建目录、数据库和浏览器登录状态，适合开发时避免反复通过资源管理器打开未签名的 EXE；不调整 Windows 安全策略。没有构建产物时需先构建；脚本优先使用项目指定的 SDK，指定 SDK 不可用时使用本机已安装 SDK 运行现有构建，不修改版本约束。正式发行版如要获得可验证发布者，应使用有效的代码签名证书。

Core/Tests 与 WinUI 是混合平台，请按项目构建。普通测试使用已脱敏的真实网页结构和榜单样本，无需登录平台。只有显式设置 `VPHELPER_LIVE=1` 并选择 Live 测试时才执行真实联网测试。

## 版本发布

采用 **公开源码仓库 + Windows x64 便携 ZIP + GitHub Releases**。标签工作流先创建草稿，验收附件后发布预览版。项目源码许可证尚未选定，当前公开可见不等于已授予 MIT 等开源许可；依赖升级和干净机器验收仍是稳定版前的工作。

```powershell
# 要求工作区干净，直接从 Git HEAD 导出的源码构建，不能夹带本机登录数据
pwsh -File tools/Publish-Portable.ps1 -Version 0.1.0-preview.1
```

[发布方案](docs/RELEASING.md)说明首次推送、版本标签、自动构建、发布草稿和后续安装器路线。自动化配置位于 `.github/workflows/`；未连接 GitHub 前不能视为已完成远端 CI 验证。

## 目录与数据

- `src/VPHelper.Core`：抓取、解析、关联、排名与 SQLite 数据逻辑。
- `src/VPHelper.WinUI`：WinUI 3 / WebView2 桌面界面。
- `tests/fixtures/public`：可提交的已脱敏解析样本；原始网页副本留在本地。
- `DESIGN.md`：设计事实源。`PROGRESS.md`：历史实现与验收记录。
- 账号数据库、WebView2 登录状态、日志、构建产物、截图与备份均被 Git 忽略。

数据来源：[algoux/srk-collection (AGPL-3.0)](https://github.com/algoux/srk-collection)。其他许可说明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。项目自身的源码许可证尚待作者选定，当前不要将其描述为已按 MIT 或其他许可证开源。
