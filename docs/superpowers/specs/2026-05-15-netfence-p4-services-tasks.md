# NetFence P4 — 服务识别 + 计划任务扫描

## 目标

将 ServicesTasksPage 从占位页升级为功能页，支持扫描与目标软件关联的 Windows 服务和计划任务，提供停止服务、禁用自启动、禁用计划任务、阻止联网等操作。

## 技术方案

- 服务扫描：`System.ServiceProcess.ServiceController`（内置 .NET，无需 NuGet）
- 服务启动类型修改：WMI `ChangeStartMode`
- 计划任务扫描：PowerShell `Get-ScheduledTask`
- 路径关联匹配：同 P1 的 `NetFenceRules.IsPathUnderDirectory`

## 数据模型

```csharp
public sealed record ServiceInfo(
    string Name,
    string DisplayName,
    string Status,         // Running / Stopped / Paused / ...
    string StartMode,      // Auto / Manual / Disabled
    string? ExecutablePath,
    bool IsSystemService);

public sealed record ScheduledTaskInfo(
    string Name,
    string Path,           // 任务路径
    string State,          // Ready / Disabled
    string Triggers,       // 触发器简述
    string Actions,        // 动作简述
    string? ExecutablePath // 主动作 exe 路径
);
```

## 系统服务白名单

以下服务显示但不允许停止/禁用：
`svchost.exe`, `lsass.exe`, `wininit.exe`, `services.exe`, `csrss.exe`,
`smss.exe`, `winlogon.exe`, `explorer.exe`, `RuntimeBroker.exe`,
`SearchHost.exe`, `System`, `Registry`, `spoolsv.exe`, `WSearch`

白名单服务 `IsSystemService = true`，UI 禁用操作按钮。

## 新增/修改文件

### Core（新增）

| 文件 | 职责 |
|---|---|
| `ServiceScanner.cs` | `ServiceController` 枚举 + WMI 修改启动类型 + 关联匹配 |

### App（修改）

| 文件 | 改动 |
|---|---|
| `Pages/ServicesTasksPage.xaml` | 占位→双标签页 UI |
| `Pages/ServicesTasksPage.xaml.cs` | 扫描逻辑、操作按钮 |
| `Services/LocaleService.cs` | 翻译键 |

## ServicesTasksPage UI

两个子标签页：`服务` | `计划任务`

### 服务标签页
- 工具栏：路径输入框 + 「扫描服务」按钮
- DataGrid：名称、显示名、状态、启动类型、exe路径
- 选中行操作按钮：「停止服务」「禁用自启动」
- 系统白名单服务操作按钮灰色禁用

### 计划任务标签页
- DataGrid：任务名、路径、状态、触发器、动作
- 选中行操作按钮：「禁用任务」「仅阻止联网」（→ 把 exe 加入阻断列表）

## 翻译键

en-US / zh-CN 新增：
- `servicesTab` / "Services" / "服务"
- `tasksTab` / "Scheduled Tasks" / "计划任务"
- `scanServices` / "Scan Services" / "扫描服务"
- `stopService` / "Stop Service" / "停止服务"
- `disableService` / "Disable Startup" / "禁用自启动"
- `disableTask` / "Disable Task" / "禁用任务"
- `blockTaskExe` / "Block Network" / "阻止联网"
- `columnServiceName` / "Name" / "名称"
- `columnDisplayName` / "Display Name" / "显示名称"
- `columnServiceStatus` / "Status" / "状态"
- `columnStartMode` / "Startup" / "启动类型"
- `columnServicePath` / "Path" / "路径"
- `columnTaskName` / "Task Name" / "任务名"
- `columnTaskPath` / "Path" / "路径"
- `columnTaskState` / "State" / "状态"
- `columnTaskTriggers` / "Triggers" / "触发器"
- `columnTaskActions` / "Actions" / "动作"
- `systemServiceProtected` / "System service — cannot modify" / "系统服务，不可修改"
- `stopServiceConfirm` / "Stop service '{0}'?" / "停止服务 '{0}'？"
- `disableServiceConfirm` / "Disable auto-start for service '{0}'?" / "禁用服务 '{0}' 的自启动？"
- `noServicesFound` / "No related services found." / "未发现关联服务。"
- `noTasksFound` / "No related scheduled tasks found." / "未发现关联计划任务。"

移除旧占位键 `servicesTasksComing`。

## 验证标准
1. 输入路径→扫描→显示关联服务列表（运行状态、启动类型正确）
2. 系统服务正确标记白名单
3. 「停止服务」弹出确认→停止成功
4. 「禁用自启动」弹出确认→启动类型变为 Disabled
5. 计划任务扫描显示关联任务
6. 「禁用任务」→ 任务状态变为 Disabled
7. 「仅阻止联网」→ 任务 exe 被添加到防火墙阻断规则
8. 构建 0 错误 0 警告，核心测试通过
