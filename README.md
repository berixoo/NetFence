<p align="center">
  <img src="dotnet/NetFence.App/Assets/NetFence.ico" width="80" alt="NetFence" />
</p>

<h1 align="center">NetFence</h1>

<p align="center">
  <strong>在不切断电脑网络的情况下，让指定软件不能联网。</strong><br>
  <sup>Block any program from accessing the network — without disconnecting your PC.</sup>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet" />
  <img src="https://img.shields.io/badge/Windows-10%2B-0078D6?logo=windows" />
  <img src="https://img.shields.io/badge/license-MIT-green" />
  <img src="https://img.shields.io/badge/WPF-UI-blueviolet" />
</p>

---

**中文** | [English](#english)

NetFence 是一个 Windows 桌面工具，通过 Windows Defender 防火墙规则来阻止特定程序联网。规则是**持久化**的——关闭 NetFence 后仍然生效，重启电脑后也会继续生效。

---

## 功能一览

| 模块 | 功能 |
|---|---|
| **扫描封禁** | 选择 `.exe` 或文件夹，一键扫描并阻断联网 |
| **关联进程** | 自动发现子进程、同目录程序、命令行引用的辅助程序 |
| **联网监控** | 实时显示所有 TCP/UDP 连接：远程 IP、端口、协议、状态、拦截状态 |
| **服务/计划任务** | 扫描与管理目标软件相关的 Windows 服务和计划任务 |
| **规则档案** | 保存/加载规则配置为 JSON，导入/导出，快照回滚 |
| **联网模式** | 四种模式：禁止全部 / 允许全部 / 仅局域网 / 指定 IP |
| **托盘常驻** | 最小化到托盘、子进程自动封禁、开机自启、气泡通知 |
| **卸载** | 一键清理：删除所有规则、配置、数据库和程序文件 |

## 系统要求

- Windows 10 或更高版本
- **管理员权限**（阻断/解除联网需要修改防火墙规则）
- 自包含发布版无需安装 .NET Runtime

## 安装

### 方式一：下载 Release

从 [Releases](https://github.com/berixoo/NetFence/releases) 下载最新的 `NetFence-win-x64.zip`，解压后运行 `NetFence.exe`。

**自包含发布，无需安装 .NET 运行时。**

### 方式二：源码构建

```bash
git clone https://github.com/berixoo/NetFence.git
cd NetFence
dotnet build NetFence.sln -c Release
```

输出位置：`dotnet\NetFence.App\bin\Release\net9.0-windows\NetFence.exe`

## 使用教程

### 1. 禁止程序联网

1. 以**管理员身份**运行 `NetFence.exe`
2. 在侧栏「扫描封禁」页，点击**选择 EXE** 选择单个程序，或**选择文件夹**阻断整个目录
3. 程序会自动生成一个**规则名称**，可以手动修改
4. 点击**扫描关联程序**，发现子进程、同目录 helper、有网络连接的进程
5. 检查候选列表，取消不想阻断的项
6. 点击**禁止联网**，入站+出站防火墙规则立即生效

### 2. 恢复联网

- **解除禁止** — 删除当前目标路径+规则名称对应的规则
- **解除选中** — 在防火墙规则列表中勾选特定规则，只删除选中的
- **解除全部** — 删除所有 NetFence 管理的规则（紧急恢复）

### 3. 实时联网监控

切换到「联网监控」页面，查看所有活跃的 TCP/UDP 连接：

- 进程名、PID、程序路径
- 远程 IP 地址和端口
- 协议（TCP/UDP）和连接状态
- **拦截状态** —— 已被 NetFence 阻断的程序显示"已阻断"

支持 1 秒 / 2 秒 / 5 秒 / 10 秒自动刷新。

### 4. 扫描服务与计划任务

切换到「服务/计划任务」页，输入目标路径后点击**扫描**：

- **服务标签页** — 列出 exe 位于目标目录下的 Windows 服务。可停止服务或禁用自启动。
- **计划任务标签页** — 列出引用目标路径的计划任务。可禁用任务或阻断其 exe。

系统关键服务（svchost、lsass 等）**受保护**，无法操作。

### 5. 管理规则档案

切换到「规则档案」页：

- **保存档案** — 将当前防火墙规则保存为命名档案
- **加载并阻断** — 加载已保存档案并重新应用规则
- **导出 JSON** — 导出为 `.json` 文件，用于备份或分享
- **导入 JSON** — 从 `.json` 文件导入档案

档案支持四种**联网模式**：

| 模式 | 行为 |
|---|---|
| 禁止全部 | 阻断所有出入站流量 |
| 允许全部 | 删除所有 NetFence 规则（恢复联网） |
| 仅局域网 | 阻断互联网但允许局域网（127/10/172.16/192.168） |
| 指定 IP | 只允许你指定的 IP 地址，其余全部阻断 |

### 6. 快照回滚

每次阻断/解除操作前会自动创建规则快照。如果操作出问题：

1. 打开「规则档案」→「操作快照」区域
2. 选中操作前的快照
3. 点击**回滚** → 确认 → 规则恢复

### 7. 托盘常驻与后台守护

- **最小化到托盘** — 关闭窗口时隐藏到系统托盘，不退出程序
- **后台守护** — 开启后，当被封禁程序启动子进程时，自动阻断子进程并弹出气泡通知
- **开机自启** — 在设置或托盘菜单中启用

### 8. 卸载

从设置页或托盘菜单 → **卸载**：

1. 弹出确认对话框
2. 删除所有 NetFence 防火墙规则
3. 删除 `%LOCALAPPDATA%\NetFence\` 全部数据
4. 程序目录将在下次重启后自动删除

## 数据存储

所有数据存储在本地：

| 数据 | 位置 |
|---|---|
| 防火墙规则 | Windows Defender 防火墙 |
| 规则档案 & 快照 | `%LOCALAPPDATA%\NetFence\NetFence.db`（SQLite） |
| 用户设置 | `%LOCALAPPDATA%\NetFence\settings.json` |
| 操作日志 | `%LOCALAPPDATA%\NetFence\NetFence.log` |

## 技术栈

- **.NET 9** — WPF 桌面应用
- **SQLite** — 本地数据库
- **Windows 防火墙** — `New-NetFirewallRule` + PowerShell
- **P/Invoke** — `GetExtendedTcpTable`/`GetExtendedUdpTable` 实时网络监控
- **WMI** — `Win32_ProcessStartTrace` 进程创建监听

## 构建

```bash
# Debug 构建
dotnet build NetFence.sln -c Debug

# Release 构建
dotnet build NetFence.sln -c Release

# 运行测试
dotnet run --project dotnet\NetFence.Core.Tests\NetFence.Core.Tests.csproj -c Release

# 自包含发布
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-release.ps1
```

## 注意事项

- **规则是持久化的。**关闭 NetFence 不会恢复联网，必须使用「解除禁止」删除规则
- NetFence **不会阻断** `C:\Windows\` 下的系统程序
- 关联进程扫描是**辅助判断**，阻断前建议检查候选列表
- **管理员权限**是执行阻断/解除操作的必要条件

---

## English {#english}

NetFence is a Windows desktop tool that blocks specific applications from accessing the internet using persistent Windows Defender Firewall rules. Rules survive closing NetFence and rebooting your PC.

### Features

| Module | What it does |
|---|---|
| **Scan & Block** | Select an `.exe` or folder, scan, and block with one click |
| **Related Processes** | Auto-discover child processes, same-dir helpers, and CLI-launched programs |
| **Network Monitor** | Real-time TCP/UDP connection view: remote IP, port, protocol, state, blocked status |
| **Services & Tasks** | Scan and manage Windows services and scheduled tasks tied to blocked software |
| **Rule Profiles** | Save/load rule configs as JSON, import/export, snapshot rollback |
| **Network Modes** | Block All / Allow All / LAN Only / Custom IPs |
| **System Tray** | Minimize to tray, auto-block child processes, startup with Windows, notifications |
| **Uninstall** | One-click cleanup of all rules, data, and program files |

### Quick Start

1. Run `NetFence.exe` **as Administrator**
2. **Scan & Block** → select an `.exe` or folder → click **Scan Related** → click **Block Network**
3. To restore networking: **Unblock**, **Unblock Selected**, or **Unblock All**
4. **Network Monitor** shows all active TCP/UDP connections in real time
5. **Rule Profiles** saves your blocking configs as JSON for backup and sharing
6. Closing the window minimizes to **system tray** — right-click for menu

### Network Modes

| Mode | Behavior |
|---|---|
| Block All | Block all inbound + outbound traffic |
| Allow All | Remove all NetFence rules |
| LAN Only | Block internet, allow LAN (127.0.0.0/8, 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16) |
| Custom IPs | Block all except specified IP addresses |

### Building

```bash
git clone https://github.com/berixoo/NetFence.git
cd NetFence
dotnet build NetFence.sln -c Release
dotnet run --project dotnet\NetFence.Core.Tests\NetFence.Core.Tests.csproj -c Release
```

### Notes

- Rules are persistent. Unblock to restore networking.
- `C:\Windows\` programs are protected and cannot be blocked.
- Admin privileges required for all firewall operations.

---

**License:** MIT
