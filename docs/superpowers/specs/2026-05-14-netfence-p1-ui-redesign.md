# NetFence P1 — UI 重设计

## 目标

将 NetFence 从单窗口标签页架构升级为现代化侧栏导航架构，为后续所有功能模块（联网监控、服务/计划任务、规则档案、设置）提供统一的 UI 框架。同时实现深色/浅色/跟随系统三种主题。

## 导航架构

**侧栏导航（Sidebar）**，左侧固定宽度 180px，右侧内容区自适应。

```
NetFence (Logo/标题区)
├── 扫描封禁        ← 默认页面，主入口
├── 联网监控        ← P2 实现（当前为占位页）
├── 服务/计划任务   ← P3 实现（当前为占位页）
├── 规则档案        ← P4 实现（当前为占位页）
└── 设置/日志       ← 主题切换、语言、日志、关于
```

- 侧栏使用 `ListBox` 自定义 Style，选中项高亮
- 右侧内容区使用 `ContentControl` + DataTemplate 或 Frame 做页面切换
- 每个页面为独立 `UserControl`
- 底部状态栏常驻，显示管理员权限状态和操作进度

## 主题系统

三种主题，用户在设置页面切换，选择持久化：

| 主题 | 实现方式 |
|---|---|
| 跟随系统（默认） | 启动时检测 `SystemParameters` 注册表键，跟随 Windows 主题 |
| 深色 | 手动激活深色 ResourceDictionary |
| 浅色 | 手动激活浅色 ResourceDictionary |

- 两套 `ResourceDictionary` 定义所有颜色、画刷、样式
- 切换主题时动态替换 `Application.Current.Resources` 中的 MergedDictionary
- 主题选择存储到 `%LOCALAPPDATA%\NetFence\settings.json`

## 页面设计

### 扫描封禁页（ScanBlockPage.xaml）

**上下分栏布局**：

**操作面板（上方，固定高度）**：
- 目标路径输入框（可编辑，支持手动输入或粘贴路径）
- 「选择 EXE」按钮 + 「选择文件夹」按钮
- 规则名称输入框（自动生成，可手动修改）
- 「包含当前运行中的子进程可执行文件」复选框
- 操作按钮行：「扫描关联程序」「禁止联网」「解除禁止」
- 操作期间按钮和输入框禁用，底部进度条显示状态

**结果面板（下方，填充剩余空间）**：
- 子标签页切换：「关联候选」|「防火墙规则」
- 关联候选：DataGrid，列 = 阻断(CheckBox) | 程序路径 | 原因 | PID | 进程名
- 防火墙规则：DataGrid，列 = 规则 | 方向 | 启用 | 动作 | 程序路径
- 底部操作：「解除选中」「解除全部」「导出快照」「刷新」
- 「解除选中」和「解除全部」操作前弹出确认框

### 联网监控页（NetworkMonitorPage.xaml）
P2 实现，当前显示占位提示。

### 服务/计划任务页（ServicesTasksPage.xaml）
P3 实现，当前显示占位提示。

### 规则档案页（RuleProfilesPage.xaml）
P4 实现，当前显示占位提示。

### 设置/日志页（SettingsPage.xaml）

- 语言切换：下拉框（English / 中文）
- 主题切换：下拉框（跟随系统 / 深色 / 浅色）
- 打开日志按钮
- 关于信息

## 窗口配置

- 窗口默认尺寸：960×640（可调整）
- 最小尺寸：800×520
- 窗口标题：「NetFence」
- 图标：`Assets\NetFence.ico`
- 启动居中
- 保留 manifest 中的 `requestedExecutionLevel requireAdministrator`

## 文件变更清单

| 操作 | 文件 |
|---|---|
| 重写 | `dotnet/NetFence.App/MainWindow.xaml` — 新壳：侧栏 + 内容区 + 状态栏 |
| 重写 | `dotnet/NetFence.App/MainWindow.xaml.cs` — 导航逻辑、主题切换、语言切换 |
| 新增 | `dotnet/NetFence.App/Pages/ScanBlockPage.xaml` + `.cs` — 扫描封禁页 |
| 新增 | `dotnet/NetFence.App/Pages/NetworkMonitorPage.xaml` + `.cs` — P2 占位 |
| 新增 | `dotnet/NetFence.App/Pages/ServicesTasksPage.xaml` + `.cs` — P3 占位 |
| 新增 | `dotnet/NetFence.App/Pages/RuleProfilesPage.xaml` + `.cs` — P4 占位 |
| 新增 | `dotnet/NetFence.App/Pages/SettingsPage.xaml` + `.cs` — 设置页 |
| 新增 | `dotnet/NetFence.App/Themes/DarkTheme.xaml` — 深色资源字典 |
| 新增 | `dotnet/NetFence.App/Themes/LightTheme.xaml` — 浅色资源字典 |
| 新增 | `dotnet/NetFence.App/Services/ThemeService.cs` — 主题管理 |
| 新增 | `dotnet/NetFence.App/Services/SettingsService.cs` — 设置持久化 |
| 删除 | 当前 MainWindow 中内嵌的双语翻译字典（移至独立资源文件或保留在代码中精简） |

## 验证标准

1. 启动后显示侧栏+扫描封禁默认页
2. 侧栏切换页面无闪烁，状态保持
3. 深色/浅色/跟随系统三种主题正确切换
4. 语言切换（中/英）正确刷新所有页面文字
5. 扫描封禁页所有现有功能正常工作（回归）
6. 窗口缩放时布局不自毁，最小尺寸约束生效
7. 首次启动安全提示正常弹出
