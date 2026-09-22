# 第三方组件和数据来源

VP Helper 的源码许可证尚未由作者选定。本文件是第三方来源说明，不是对项目源码的重新授权。

## 官方榜单

- 来源：[algoux/srk-collection](https://github.com/algoux/srk-collection)
- 上游许可：AGPL-3.0。原文副本位于 `LICENSES/srk-collection-AGPL-3.0.txt`。
- 应用在运行时下载公开目录和榜单；便携程序包不附带已同步的榜单数据库。
- 测试源码中的 `tests/fixtures/public/srk/` 保留了真实榜单样本。第三方数据的许可不因本仓库是否私有而改变。

## 网页测试样本

Codeforces 与 QOJ 的 HTML 仅作为去除活动脚本和认证令牌后的解析回归样本；仍保留必要的真实表格结构和公开账号字段。相应内容及商标归各权利人所有。本项目没有替上游授予新的许可。样本不打入应用 ZIP。

## 程序依赖

直接依赖和版本记录在各 `.csproj`，完整解析结果记录在发布时的 `project.assets.json`。打包脚本把实际使用的 NuGet 包许可声明及包中自带的许可文件复制到 `LICENSES/packages/`，生成 `dependencies.json`，并保留发布输出已有的许可文件。

- .NET Runtime：Microsoft / .NET Foundation 及其贡献者。
- Windows App SDK、Windows SDK BuildTools、WebView2 SDK：Microsoft；具体以各包许可为准。
- EF Core、Microsoft.Extensions.DependencyInjection、CommunityToolkit.Mvvm、AngleSharp、YamlDotNet，以及测试工具：以各包声明为准。
- WebView2 Evergreen Runtime：由 Microsoft 独立分发和维护，本项目不重新打包浏览器运行时。

公开发行前需复核生成包的许可材料、源码许可及实际依赖清单；源码仓库的许可证不能取代各运行时与第三方内容自身的许可。
