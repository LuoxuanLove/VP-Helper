# GitHub 发布方案

## 推荐路线

按作者 LuoxuanLove 于 2026-09-23 的明确授权，使用 **公开源码仓库 [LuoxuanLove/VP-Helper](https://github.com/LuoxuanLove/VP-Helper) + Windows x64 自包含便携 ZIP + GitHub Releases**。工作流先创建草稿，核验后发布预览版。它与现有“EXE 同级 data 目录”的应用逻辑一致，无需管理员权限或安装器迁移。此授权仅用于本项目，不影响其他项目的私有仓库要求。

| 产物 | 放在哪里 | 面向谁 |
| --- | --- | --- |
| 源码、脱敏样本、构建脚本 | Git 主分支 | 开发者 |
| `VPHelper-<版本>-win-x64.zip` | Release 附件 | Windows 用户 |
| `VPHelper-<版本>-source.zip` | Release 附件 | 复现构建、对应源码 |
| `SHA256SUMS.txt` | Release 附件 | 校验下载文件 |
| 数据库、登录状态、原始网页、截图、构建缓存 | 本机；不上传 | 本机用户 |

私有仓库的 Release 也只有有权访问仓库的人能下载。若目标是任何人可下载，优先在许可与隐私检查完成后公开同一个仓库；不建议用公开空仓库转发来自私有源码的二进制，给对应源码和许可管理制造额外分歧。

## 首次连接 GitHub

本地已初始化 `main`。当前公开发布目标固定为 `LuoxuanLove/VP-Helper`；下列命令仅用于首次建仓，不要对已有仓库重复执行。

选定个人账号/组织及仓库名后，例如：

```powershell
gh auth login
gh repo create LuoxuanLove/VP-Helper --public --source . --remote origin --push
```

如已在 GitHub 网页创建空仓库：

```powershell
git remote add origin https://github.com/LuoxuanLove/VP-Helper.git
git push -u origin main
```

首次推送前执行 `python tools/check_repository.py`，并检查提交作者邮箱是否愿意用于远端历史。不要用 `git add -f` 强行添加被忽略的数据。`tests/fixtures/public` 已移除认证令牌，但仍含公开用户名和参赛记录；从私有改为公开时应再次审阅这些内容。

## 开发与版本管理

- `main` 保持可构建，功能修改放短期分支，通过 PR 合并；使用仓库计划支持的分支规则要求 CI 成功。
- 预览版本从 `v0.1.0-preview.1` 开始；稳定版本形如 `v1.0.0`。修改发布内容就增加版本，不复用已发布标签或覆盖已有附件。
- `.github/workflows/ci.yml` 对 main 推送与 PR 做文件审计、离线测试和 Windows x64 构建。
- `.github/workflows/release.yml` 在推送 `v*` 标签时，从该提交的干净源码重新测试、打包，创建 **Draft Release**。包含 `-` 的版本同时标为预览版。
- 草稿由维护者下载验证后手动点 Publish release。流程不会自动把未经查看的版本发布给用户；失败时保留 CI 日志和测试报告。
- Actions 固定到已核验的提交 SHA，默认只读权限，只有创建 Release 的任务具有 contents:write；不需要额外 PAT 或本机凭据。

## 本地生成发行包

需要 Windows x64、PowerShell 7、Python 3.10+、Git、.NET 10 SDK 与 .NET 8 Runtime。构建依赖由 NuGet 还原；包管理器不能使用私人本机包作为隐式前提。

```powershell
python tools/check_repository.py
git status --short
# 有修改时，审阅并提交后再运行；工具拒绝脏工作区
pwsh -File tools/Publish-Portable.ps1 -Version 0.1.0-preview.1
```

输出位于 `artifacts/releases/<版本>/`。该目录已被 Git 忽略。脚本：

1. 导出 Git HEAD 到新的源码目录，从导出的源码运行离线回归测试。
2. 按项目发布 WinUI x64，包含 .NET 与 Windows App SDK，关闭裁剪和单文件发布，避免破坏 XAML、EF Core 或本机组件。
3. 检查主程序和关键运行时文件存在，拒绝包含用户数据、浏览器状态、日志或重解析点的产物。
4. 收集依赖许可、安装说明、提交 SHA 和实际 SDK 版本，生成便携 ZIP、源码 ZIP 和 SHA-256。

脚本拒绝重用已存在的版本输出目录；失败记录留在原目录。修复后使用新的预览版本号重新打包，或自行检查旧的失败目录后再决定如何处理。

## 推送版本标签

在对应提交的 CI 成功后：

```powershell
git tag -a v0.1.0-preview.1 -m "VP Helper 0.1.0 preview 1"
git push origin v0.1.0-preview.1
```

首次在 GitHub 使用时确认仓库允许 GitHub Actions 运行，并允许该工作流申请写入 Release 的权限。工作流不应在 fork PR 或不可信输入中使用写入令牌。

## 稳定版发布前的具体条件

截至 2026-09-22，以下事项尚未完成，当前本地包只能算预览构建：

- **源码许可证**：作者尚未选择，不默认替作者授予 MIT 等许可。`srk-collection` 自身 AGPL-3.0 与网页样本的权利需分别保留和核查。详见 `THIRD_PARTY_NOTICES.md`。
- **依赖支持期**：当前引用 Windows App SDK `1.5.240607001`，1.5 系列已于 2025-02-28 结束支持；应用仍为 .NET 8，其支持于 2026-11-10 结束。稳定版前建议迁移到受支持的 Windows App SDK 稳定通道与 .NET 10 LTS，然后重新验证 WebView2、窗口、SQLite 和排名流程。
- **干净机器验证**：在无开发 SDK 的 Windows 11 x64 上确认便携包能启动；分别验证有/无 WebView2 的提示与安装流程、CF/QOJ 本人登录、同步、备份升级、中文目录路径、不同 DPI、普通用户权限。
- **发行身份**：尚未配置代码签名证书或可信签名服务，当前 EXE 不宣称已签名；SHA-256 校验也不能替代代码签名。
- **GitHub CI**：本地运行成功不等同于 GitHub runner 成功，第一次推送后须查看实际 CI 结果。

## 何时增加安装器

有较多非技术用户后，再增加按用户安装的 Inno Setup EXE：固定安装位置、开始菜单、版本升级、检查 WebView2 并引导官方安装。届时应先把用户数据位置固定为 `%LOCALAPPDATA%\VPHelper` 并提供从便携 `data/` 的显式迁移，避免卸载器误删账号数据。稳定签名、升级和卸载流程完成后再考虑 WinGet。

MSIX / Microsoft Store 是后续选项，需要明确身份、签名、数据迁移和更新渠道。当前不要同时维护 ZIP、MSIX、安装器、自动更新四条路径。

## 官方资料

- [GitHub Releases](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases)
- [Windows App SDK 自包含部署](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)
- [WebView2 Runtime 分发](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)
- [Windows App SDK 支持周期](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-channels)
- [.NET 支持政策](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
