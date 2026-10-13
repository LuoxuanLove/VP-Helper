# 官方榜单维护工具

用于从已下载并核实版本的 `algoux/srk-collection` 快照批量更新指定数据库。日常用户直接使用应用中的“更新目录和榜单”。

关闭使用该数据库的 VP Helper 后执行：

```powershell
dotnet run --project tools/VPHelper.Maintenance -- "<数据库绝对路径>" "<快照根目录绝对路径>"
```

快照根目录必须包含 `official/config.yaml`。工具仅使用快照内文件，不联网；执行前通过 SQLite Backup API 生成带时间戳的数据库备份，按当前模型补齐缺列，然后逐场原子保存榜单并运行既有目录自动关联规则。手动确认优先，提交与 VP 派生结果不重算。任何下载/解析缺失都会列出官方 ID，并以非零状态退出。保留快照提交号和输出日志，以便追溯。
