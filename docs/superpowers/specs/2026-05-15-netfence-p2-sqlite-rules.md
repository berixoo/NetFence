# NetFence P2 — SQLite 数据层 + 规则管理增强

## 目标

引入 SQLite 本地数据库，实现规则配置档案的保存/加载/导入/导出（JSON 格式），操作前自动备份当前 NetFence 规则状态，支持快照回滚。

## SQLite 数据库

**文件位置：** `%LOCALAPPDATA%\NetFence\NetFence.db`

使用 `Microsoft.Data.Sqlite` NuGet 包（轻量、无额外依赖）。

### 表结构

**RuleProfiles** — 用户保存的规则配置档案

| 列 | 类型 | 说明 |
|---|---|---|
| Id | INTEGER PK AUTOINCREMENT | |
| Name | TEXT NOT NULL | 用户命名的配置名称 |
| PathsJson | TEXT | JSON 数组，目标路径列表 |
| ProgramsJson | TEXT | JSON 数组，额外程序列表 |
| Mode | TEXT | 联网模式（当前固定 "block_all"，P5 扩展） |
| CreatedAt | TEXT | ISO 8601 |
| UpdatedAt | TEXT | ISO 8601 |

**RuleSnapshots** — 操作前自动备份的规则状态

| 列 | 类型 | 说明 |
|---|---|---|
| Id | INTEGER PK AUTOINCREMENT | |
| ProfileName | TEXT | 规则组名 |
| Action | TEXT | Block / Unblock / UnblockAll |
| RulesJson | TEXT NOT NULL | 操作前 NetFence 规则的完整 JSON 数组 |
| Timestamp | TEXT | ISO 8601 |
| IsRollback | INTEGER | 0 = 未回滚, 1 = 已回滚 |

**OperationHistory** — 操作历史记录

| 列 | 类型 | 说明 |
|---|---|---|
| Id | INTEGER PK AUTOINCREMENT | |
| Action | TEXT | Block / Unblock / UnblockSelected / UnblockAll / Export / Import / Rollback |
| ProfileName | TEXT | 涉及的规则组名 |
| TargetCount | INTEGER | 涉及的目标数量 |
| Timestamp | TEXT | ISO 8601 |
| DetailsJson | TEXT | JSON，额外详情 |

## JSON 配置档案格式

### 导出格式

```json
{
  "version": "1.0",
  "exportedAt": "2026-05-15T10:30:00+08:00",
  "name": "Adobe Creative Cloud Block",
  "paths": ["D:\\Apps\\Adobe"],
  "programs": ["acrobat.exe", "photoshop.exe"],
  "mode": "block_all"
}
```

### 导入

读取 JSON 文件 → 验证结构 → 加载到 RuleProfiles 列表（可选是否立即执行阻断）。

## 备份回滚机制

### 自动备份触发点

在 `FirewallService` 的 Block、Unblock、UnblockAll 方法中，实际操作前：

1. 调用 `Get-NetFirewallRule` 获取当前所有 `NetFence:*` 规则
2. 序列化为 JSON
3. INSERT 到 `RuleSnapshots` 表
4. 然后执行实际操作

### 快照保留策略

- 保留最近 50 条快照
- 超出的旧快照自动清理

### 回滚操作

1. 用户在 RuleProfilesPage 选择一条快照
2. 确认弹窗
3. 删除当前所有 `NetFence:*` 防火墙规则
4. 从快照 JSON 重建规则（调用 `New-NetFirewallRule`）
5. 标记快照 `IsRollback = 1`

## 新增/修改文件

### Core 库（新增）

| 文件 | 职责 |
|---|---|
| `Database.cs` | 连接管理（懒初始化单例）、建表（EnsureCreated）、获取连接 |
| `RuleProfileStore.cs` | 配置档案 CRUD：`ListAll()`、`Save(profile)`、`Delete(id)`、`GetById(id)` |
| `RuleSnapshotStore.cs` | 快照管理：`CreateSnapshot(profileName, action, rulesJson)`、`ListRecent(limit)`、`MarkRolledBack(id)`、`CleanupOld(max)` |
| `OperationHistoryStore.cs` | 历史记录：`Record(action, profileName, targetCount, details)`、`QueryRecent(limit)` |

### Core 库（修改）

| 文件 | 改动 |
|---|---|
| `FirewallService.cs` | Block/Unblock/UnblockAll/UnblockSelectedPrograms 方法内调用 `RuleSnapshotStore.CreateSnapshot` 做操作前备份 |

### App UI（修改）

| 文件 | 改动 |
|---|---|
| `Pages/RuleProfilesPage.xaml` | 从占位 TextBlock 改为完整 UI：档案列表 DataGrid + 快照列表 DataGrid + 操作按钮 |
| `Pages/RuleProfilesPage.xaml.cs` | 档案管理逻辑 + 导入导出 + 回滚 |
| `Pages/SettingsPage.xaml/.cs` | 新增"打开数据库位置"按钮 |
| `NetFence.App.csproj` | 添加 `Microsoft.Data.Sqlite` PackageReference |

### 测试（修改）

| 文件 | 改动 |
|---|---|
| `NetFence.Core.Tests/Program.cs` | 新增测试：数据库建表、配置档案 CRUD、快照创建与查询、快照清理、JSON 导出导入 |

## 验证标准

1. 应用启动后自动创建 `NetFence.db` 和所有表
2. Block/Unblock/UnblockAll 操作前自动创建规则快照
3. 规则档案可保存、加载、删除、导出 JSON、从 JSON 导入
4. 快照回滚：选择快照 → 确认 → 删除当前规则 → 恢复快照规则
5. 操作历史记录每次操作自动写入
6. 快照超过 50 条时自动清理旧记录
7. 全部 Core 测试通过
8. Build Release 0 错误 0 警告
