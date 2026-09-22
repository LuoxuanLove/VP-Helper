# 可提交的测试样本

本目录包含来自真实 Codeforces / QOJ HTML 的解析回归样本及 srk-collection 榜单样本。

- HTML 由 `tools/prepare_public_fixtures.py` 从原始样本生成，保留表格、分页、提交属性和账号控件的真实结构，去除脚本、样式、外部资源及 CSRF/登出令牌。
- 用户名、比赛 ID、公开提交记录和时间仍用于解析断言。这些是测试输入，不是应用预置用户数据。公开仓库前仍应审阅可识别账号的样本。
- 原始 HTML 和浏览器保存的 `_files` 目录留在上级目录，仅保存在本地，已被 Git 忽略。
- `manifest.json` 记录最初生成样本的 SHA-256。后续修改样本时应更新对应摘要，并保留真实结构的来源说明。
- `srk/` 数据来自 [algoux/srk-collection](https://github.com/algoux/srk-collection)，遵循其 AGPL-3.0 许可；许可证副本见仓库 `LICENSES/srk-collection-AGPL-3.0.txt`。
- Codeforces 与 QOJ 页面内容的权利归各自权利人；本项目没有替它们授予新的开源许可证。

普通测试仅复制本目录。发布程序 ZIP 不包含这些 HTML、榜单、个人数据库或登录状态。
