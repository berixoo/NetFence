# NetFence P3 — 实时联网监控

## 目标

将 NetworkMonitorPage 从占位页升级为实时网络连接监控面板，显示所有 TCP/UDP 连接的详细信息，包括进程信息、远程地址端口、连接状态，以及是否被 NetFence 阻断。

## 技术方案

使用 P/Invoke 调用 Windows `iphlpapi.dll` 的 `GetExtendedTcpTable` / `GetExtendedUdpTable` 获取连接表，避免 PowerShell 进程开销，支持每秒级实时刷新。

### API
- `GetExtendedTcpTable` — TCP 连接（含 PID、本地/远程地址端口、状态）
- `GetExtendedUdpTable` — UDP 端点（含 PID、本地地址端口）
- `Process.GetProcessById` — 解析进程名和可执行文件路径

### 数据模型

```csharp
public sealed record NetworkConnection(
    int ProcessId,
    string ProcessName,
    string? ExecutablePath,
    string Protocol,          // "TCP" / "UDP"
    string LocalAddress,
    int LocalPort,
    string RemoteAddress,
    int RemotePort,
    string State,             // "Established" / "Listening" / "CloseWait" ...
    bool IsBlockedByNetFence
);
```

### NetFence 拦截状态判定

查询当前所有 NetFence 防火墙规则的 Program 路径集合，将每个连接的 `ExecutablePath` 与规则路径做大小写不敏感匹配。匹配成功则 `IsBlockedByNetFence = true`。

## 新增/修改文件

### Core（新增）

| 文件 | 职责 |
|---|---|
| `NetworkMonitor.cs` | P/Invoke 声明、TCP/UDP 表查询、连接枚举、拦截状态匹配 |

### App（修改）

| 文件 | 改动 |
|---|---|
| `Pages/NetworkMonitorPage.xaml` | 替换占位页：工具栏（刷新/自动刷新/连接计数）+ DataGrid（排序+筛选） |
| `Pages/NetworkMonitorPage.xaml.cs` | 数据绑定、`DispatcherTimer` 定时刷新、排序筛选逻辑 |
| `Services/LocaleService.cs` | 新增 NetworkMonitor 相关翻译键 |

## NetworkMonitorPage UI

**工具栏：**
- 「刷新」按钮（手动刷新一次）
- 「自动刷新」下拉框（关闭 / 1秒 / 2秒 / 5秒 / 10秒）
- 右侧显示 `共 N 条连接`

**DataGrid 列：**
| 列 | 绑定 | 排序 |
|---|---|---|
| 进程名 | ProcessName | ✓ |
| PID | ProcessId | ✓ |
| 程序路径 | ExecutablePath | ✓ |
| 协议 | Protocol | ✓ |
| 本地地址:端口 | LocalAddress + LocalPort | ✓ |
| 远程地址:端口 | RemoteAddress + RemotePort | ✓ |
| 连接状态 | State | ✓ |
| 拦截状态 | IsBlockedByNetFence | ✓ |

- 拦截状态列：阻断中 = 红色 "Blocked"，未阻断 = 绿色 "Allowed"
- 自动刷新用 `DispatcherTimer`，UI 线程安全
- 查询连接和匹配规则在后台线程执行（`Task.Run`）
- 页面不可见时暂停定时器，可见时恢复

## 翻译键（新增）

en-US / zh-CN：
- `networkConnections` / "Network Connections" / "网络连接"
- `columnProcess` / "Process" / "进程"
- `columnPID` / "PID" / "PID"
- `columnProtocol` / "Protocol" / "协议"
- `columnLocalAddress` / "Local Address" / "本地地址"
- `columnRemoteAddress` / "Remote Address" / "远程地址"
- `columnConnectionState` / "State" / "状态"
- `columnBlocked` / "Blocked" / "拦截状态"
- `autoRefresh` / "Auto-refresh" / "自动刷新"
- `autoRefreshOff` / "Off" / "关闭"
- `refreshInterval1s` / "1 second" / "1 秒"
- `refreshInterval2s` / "2 seconds" / "2 秒"
- `refreshInterval5s` / "5 seconds" / "5 秒"
- `refreshInterval10s` / "10 seconds" / "10 秒"
- `connectionCount` / "{0} connection(s)" / "{0} 条连接"
- `blockedStatus` / "Blocked" / "已阻断"
- `allowedStatus` / "Allowed" / "未阻断"

同时移除旧的占位键 `networkMonitorComing`。

## 验证标准

1. NetworkMonitorPage 页面显示实时网络连接列表
2. TCP 连接显示正确的远程地址、端口、状态（Established/Listening 等）
3. UDP 端点显示正确的本地地址和端口
4. 进程名和 PID 正确解析
5. 已被 NetFence 阻断的程序路径在拦截状态列显示 "Blocked"
6. 自动刷新按选定间隔执行，切换间隔立即生效
7. 点击列标题可排序
8. 切换到其他页面时定时器暂停，切回时恢复
9. 构建 0 错误 0 警告
10. 核心测试通过
