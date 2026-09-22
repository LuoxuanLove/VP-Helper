# 安装、升级与备份

## 安装便携版

1. 在 GitHub 仓库的 **Releases** 选择一个版本。预览版会标记为 Pre-release；Draft 是维护者的草稿，还不是用户下载入口。
2. 下载 `VPHelper-<版本>-win-x64.zip` 和 `SHA256SUMS.txt`。源码 ZIP 用于开发，不包含可直接运行的程序。
3. 可用 PowerShell `Get-FileHash .\VPHelper-<版本>-win-x64.zip -Algorithm SHA256` 与清单比较。
4. 完整解压到可写位置，例如 `%LOCALAPPDATA%\Programs\VPHelper` 或个人软件目录；从解压后的目录运行 `VPHelper.WinUI.exe`。所有 DLL、语言资源和 Assets 都应保留在一起。
5. 无需安装 .NET SDK、.NET Desktop Runtime 或单独的 Windows App SDK。需要 **WebView2 Evergreen Runtime**；若缺少，到 [Microsoft 官方下载页](https://developer.microsoft.com/microsoft-edge/webview2/)安装对应运行时，随后重启应用。
6. 可自行给 EXE 创建桌面快捷方式。便携版不创建开始菜单项、不注册卸载器、不自动更新。

当前发行目标是 Windows x64。建议使用 Windows 11；项目编译最低版本是 Windows 10 1809，但这不代表对所有旧 Windows 版本做过安装验收或仍获系统厂商支持。ARM64 设备的 x64 模拟运行也尚未单独验收。

未经代码签名的预览包可能触发 Windows 的发布者验证提示。先核对来源、版本和哈希；不要关闭系统安全功能。代码签名是后续正式发行工作，不应把本地构建等同于已签名安装包。

## 首次使用

- 「账号管理」→ 选择 Codeforces 或 QOJ → 在内嵌网页登录并由本人完成人机验证 → 点击「同步该账号」。
- 「比赛关联」→ 自动关联目录内比赛，其余比赛手动搜索官方赛事并确认。
- 「官方比赛」→ 筛选“已 VP”，查看推算排名；未缓存榜单的比赛进入详情后按需加载。
- 排名仅是模拟插榜的结果，练习与 VP 分开记录。

## 数据位置

程序优先使用 EXE 同级的 `data/`。如果安装位置不可写，则回退到 `%LOCALAPPDATA%\VPHelper`。

其中包括 SQLite 数据库、窗口设置、诊断日志以及 WebView2 登录状态。**不要把整个应用目录重新压缩发给别人**；请分享原始发行 ZIP。

## 升级

当前数据库采用兼容性补列，不能承诺跨版本降级安全。推荐采用以下流程：

1. 退出 VP Helper，确认同步结束。
2. 将当前 `data/` 复制到个人备份目录；如启用了回退目录，备份实际回退目录。备份中含登录信息，请私下保存。
3. 将新版 ZIP 解压到一个新的可写目录。
4. 从旧版复制完整 `data/` 到新版 EXE 旁，再运行新版；不要同时运行两份指向同一个数据库的程序。
5. 检查账号和比赛记录正常后，再决定是否移除旧版。WebView2 登录状态不能保证跨机器恢复，必要时重新登录。

不要用新版数据库直接覆盖旧版备份。需要回退时，应恢复与旧版程序配套的升级前备份。

## 卸载

退出程序后移除解压目录和自己创建的快捷方式即可。移除其中的 `data/` 会同时删除比赛缓存与登录状态；需要保留历史时先备份。若使用过回退数据目录，应单独决定是否保留 `%LOCALAPPDATA%\VPHelper`。共享的 WebView2 Runtime 可能被其他应用使用，不属于 VP Helper 的私有数据。
