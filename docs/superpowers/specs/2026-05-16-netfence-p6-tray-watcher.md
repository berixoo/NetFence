# NetFence P6 — 托盘常驻 + 进程守护 + 卸载

## 目标

将 NetFence 从"手动工具"升级为"后台守护"——系统托盘常驻、实时监听进程创建自动补规则、开机自启、完整卸载功能。

## 技术方案

- 托盘：`System.Windows.Forms.NotifyIcon`（项目已引用 WinForms）
- 进程监听：WMI `Win32_ProcessStartTrace` + `ManagementEventWatcher`
- 开机自启：注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\NetFence`
- 卸载：生成临时批处理脚本→退出→删除所有文件和配置

## 新增/修改文件

| 文件 | 操作 | 职责 |
|---|---|---|
| `NetFence.Core/ProcessWatcher.cs` | 新增 | WMI 进程创建监听、自动补规则 |
| `NetFence.App/App.xaml.cs` | 修改 | 托盘创建、菜单事件、启动/停止守护 |
| `NetFence.App/MainWindow.xaml.cs` | 修改 | 关闭→最小化到托盘 |
| `NetFence.App/Services/LocaleService.cs` | 修改 | 托盘翻译键 |

## 托盘菜单结构

```
显示 NetFence
─────────────
✓ 启用守护
✓ 开机自启
─────────────
卸载 NetFence
─────────────
退出
```

## ProcessWatcher API

```csharp
public static class ProcessWatcher
{
    bool IsRunning { get; }
    void Start();                                    // 订阅 WMI 事件
    void Stop();                                     // 取消订阅
    event Action<WatcherEventArgs>? ProcessStarted;  // 新进程事件
}
```

## 卸载流程

1. 确认弹窗
2. 生成 `%TEMP%\netfence-uninstall.bat`：等待 2 秒→删除注册表 Run 键→删除 `%LOCALAPPDATA%\NetFence` 配置目录→删除应用目录→自删除
3. `Process.Start("cmd", "/c ...")` 以 `ShellExecute=true` 启动
4. `Application.Current.Shutdown()`

## 翻译键

en-US / zh-CN：
- `trayShow` / "Show NetFence" / "显示 NetFence"
- `trayEnableWatcher` / "Enable Guardian" / "启用守护"
- `trayAutoStart` / "Start with Windows" / "开机自启"
- `trayUninstall` / "Uninstall" / "卸载"
- `trayExit` / "Exit" / "退出"
- `trayAutoBlocked` / "Auto-blocked: {0}" / "已自动阻断：{0}"
- `uninstallConfirm` / "Uninstall NetFence? All files and settings will be deleted." / "确定卸载 NetFence？将删除所有文件和配置"

## 验证标准

1. 托盘图标显示，右键菜单完整
2. 关闭窗口→最小化到托盘（托盘菜单"显示"恢复）
3. 进程守护：阻断某程序 A→启动 A→A 的子进程自动被阻断+气泡通知
4. 开机自启：勾选→注册表写入；取消→注册表删除
5. 卸载：确认→生成 bat→退出→自动删除所有文件
6. Build 0 错误 0 警告
