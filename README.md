# NetFence

NetFence 是一个 Windows 图形界面工具，用来在不断开电脑网络的情况下，让指定软件不能联网。通过持久化的 Windows Defender 防火墙规则实现阻断，关闭 NetFence 后规则仍然生效，重启 Windows 后也会继续生效。

## 功能

### 核心能力
- 阻断单个 `.exe` 或整个文件夹下的所有 `.exe`
- 扫描并阻断目标程序的关联进程（子进程、同目录程序、命令行引用）
- 实时进程守护：被阻断程序的子进程启动时自动补规则
- 为每个目标创建入站 + 出站阻断规则

### 联网监控（侧栏 → 联网监控）
- 实时展示所有 TCP/UDP 连接：进程名、PID、远程 IP/端口、协议、连接状态
- 自动标记已被 NetFence 阻断的程序
- 可配置 1s / 2s / 5s / 10s 自动刷新

### 服务与计划任务（侧栏 → 服务/计划任务）
- 扫描与目标软件关联的 Windows 服务，支持停止服务、禁用自启动
- 扫描关联的计划任务，支持禁用任务或阻止其 exe 联网
- 系统关键服务自动保护，不可操作

### 规则档案（侧栏 → 规则档案）
- 保存/加载当前规则为配置档案（SQLite 本地存储）
- 导入/导出 JSON 配置，支持跨机器迁移
- 操作前自动备份规则状态，支持一键回滚到任意快照
- 四种联网模式：
  - **禁止全部** — 出站 + 入站 Block
  - **允许全部** — 删除所有 NetFence 规则
  - **仅局域网** — 出站 Block 全部 + 出站 Allow LAN（127.0.0.0/8, 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16）
  - **指定 IP** — 出站 Block 全部 + 出站 Allow 自定义 IP 列表

### 托盘常驻
- 关闭窗口 → 最小化到系统托盘
- 右键菜单：显示窗口 / 启用守护 / 开机自启 / 卸载 / 退出
- 进程守护自动阻断时弹出气泡通知

### 设置（侧栏 → 设置）
- 深色 / 浅色 / 跟随系统三种主题
- 中文 / English 界面语言切换
- 启用/禁用进程守护、开机自启
- 打开操作日志
- 卸载（删除所有规则、配置、程序文件）

## 系统要求

- Windows 10 或更高版本
- .NET 9 Runtime（框架依赖版本）或自包含发布（无需安装运行时）
- 阻断/解除操作需要**管理员权限**

## 构建

```bash
# Debug
dotnet build NetFence.sln -c Debug

# Release
dotnet build NetFence.sln -c Release
```

输出：`dotnet\NetFence.App\bin\Release\net9.0-windows\NetFence.exe`

## 自包含发布

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-release.ps1
```

输出：`dist\NetFence-win-x64\` 和 `dist\NetFence-win-x64.zip`

## 测试

```bash
dotnet run --project dotnet\NetFence.Core.Tests\NetFence.Core.Tests.csproj -c Release
```

## 数据存储

- 防火墙规则：Windows Defender 防火墙（持久化）
- 配置档案、操作快照、历史：`%LOCALAPPDATA%\NetFence\NetFence.db`（SQLite）
- 用户设置：`%LOCALAPPDATA%\NetFence\settings.json`
- 操作日志：`%LOCALAPPDATA%\NetFence\NetFence.log`

## 重要说明

- Windows 防火墙规则按程序路径生效。如果软件在新路径生成新的辅助 `.exe`，需要再次阻断
- 规则持久保留，关闭 NetFence 不会解除阻断，必须使用「解除禁止」或「解除全部」删除规则
- NetFence 拒绝阻断 Windows 系统目录（`C:\Windows\`）下的程序
- 关联进程扫描是辅助判断，阻断前建议检查候选列表避免误封
