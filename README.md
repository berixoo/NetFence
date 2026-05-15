# NetFence

NetFence 是一个 Windows 图形界面工具，用来在不断开电脑网络的情况下，让指定软件不能联网。它通过持久化的 Windows Defender 防火墙规则实现阻断，所以关闭 NetFence 后规则仍然生效，重启 Windows 后也会继续生效。只有再次运行 NetFence，并点击 `Unblock` 删除对应规则，软件才会恢复联网。

## 可以阻断什么

- 单个 `.exe` 可执行文件。
- 指定文件夹下的所有 `.exe` 文件，包括子文件夹。
- 可选：当前已经运行、并由目标程序启动的子进程可执行文件。

NetFence 会为每个目标可执行文件创建入站和出站阻断规则。它不会断开电脑网络，也不会全局阻断系统联网。

## 系统要求

- Windows 10 或更高版本。
- Windows PowerShell 5.1。
- 执行 `Block Network` 和 `Unblock` 需要管理员权限。

## 推荐启动方式

正式软件版位于：

```text
dist\NetFence-win-x64\NetFence.exe
```

这是自包含 Windows x64 WPF 程序，不需要用户手动安装 .NET Runtime。程序 manifest 会请求管理员权限，因为禁止/解除联网需要修改 Windows Defender 防火墙规则。

GitHub Release 可以直接上传：

```text
dist\NetFence-win-x64.zip
```

## 程序图标

可执行程序图标使用 Windows 标准 `.ico` 格式，当前文件位置：

```text
dotnet\NetFence.App\Assets\NetFence.ico
```

建议 `.ico` 内包含 16、32、48、64、128、256 像素等多尺寸图标，这样任务栏、资源管理器和高 DPI 屏幕显示会更清晰。以后要替换图标时，直接用同名 `NetFence.ico` 覆盖该文件，然后重新运行打包脚本即可。

## 脚本版启动方式

脚本版仍然保留，主要用于开发、调试和对照验证。

双击运行：

```text
NetFence.vbs
```

这个入口会隐藏 PowerShell 终端，只显示 NetFence 图形界面。`NetFence.cmd` 会显示终端窗口，主要用于调试。

也可以在当前目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -STA -File .\Start-NetFence.ps1
```

如果窗口提示当前不是管理员，点击界面里的 `Restart Admin`，用管理员权限重新打开后再执行阻断或解除。

首次启动会显示安全提示，说明 NetFence 会修改 Windows Defender 防火墙规则，规则会持久保留。确认后会在本机用户数据目录写入一次性确认标记，之后不再重复提示。

## 语言切换

NetFence 支持中文和英文界面。启动时会根据 Windows 当前界面语言自动选择：

- 中文系统默认显示中文。
- 其他语言系统默认显示英文。

也可以在窗口右上角的 `语言 / Language` 下拉框里手动切换。界面按钮、标签、表格列名和状态提示会立即刷新。

## 使用方法

1. 点击 `选择 EXE / Select EXE` 选择单个程序，或点击 `选择文件夹 / Select Folder` 选择一个软件目录。
2. 保留或修改 `规则名称 / Rule name`，后续解除时需要使用同一个目标和规则名称。
3. 如果目标程序已经在运行，并且可能启动了辅助进程，保持“包含当前运行中的子进程可执行文件”勾选。
4. 点击 `扫描关联程序 / Scan Related` 扫描关联程序。
5. 在“关联候选程序 / Related candidates”列表里检查候选程序。默认会勾选发现到的非系统程序，可以取消不想阻断的项。
6. 点击 `禁止联网 / Block Network` 禁止联网。
7. 以后需要恢复联网时，重新选择同一个目标和规则名称，点击 `解除禁止 / Unblock`。

## 解除和紧急恢复

- `解除禁止 / Unblock`：删除当前规则名称对应的 NetFence 防火墙规则。
- `解除选中 / Unblock Selected`：在“防火墙规则 / Firewall rules”列表中选中一条或多条规则后，删除这些规则对应程序路径的全部 NetFence 入站和出站规则。适合只恢复某个辅助程序或某个 `.exe` 的联网。
- `解除全部 / Unblock All`：删除所有由 NetFence 管理的防火墙规则，适合忘记原目标路径或需要紧急恢复联网时使用。

`解除选中 / Unblock Selected` 和 `解除全部 / Unblock All` 都会先弹出确认框，避免误删。

## 导出和日志

- `导出快照 / Export Snapshot`：把当前防火墙规则和关联候选列表导出为 CSV，便于排查和提交 issue。
- `打开日志 / Open Log`：打开本机操作日志。

日志默认写入当前用户的本地数据目录：

```text
%LOCALAPPDATA%\NetFence\NetFence.log
```

日志会记录阻断、解除、解除全部、导出等操作，以及涉及的程序路径。

## 执行期间的界面反馈

扫描、禁止联网、解除禁止、刷新防火墙状态都在后台执行。执行期间：

- 窗口不会假死，可以移动窗口、切换标签页、切换语言。
- 底部会显示进度条和当前状态，例如正在扫描、正在写入规则、正在删除规则。
- 为避免重复操作，路径输入、选择文件、扫描、阻断、解除和刷新按钮会暂时禁用。
- 后台任务完成后，按钮会自动恢复，并显示成功或失败原因。

## 关联程序扫描

`扫描关联程序 / Scan Related` 会尝试发现任务管理器里能看到、但不一定在目标目录里的相关进程：

- 目标程序启动的子进程、孙进程。
- 与目标程序在同一安装目录或子目录下运行的 `.exe`。
- 命令行参数里引用目标路径或安装目录的 helper 程序。
- 已经有网络连接、并且同时满足上述关联条件的进程。

扫描结果会显示在“关联候选程序 / Related candidates”标签页中，包含程序路径、进程 ID 和发现原因。NetFence 不会自动阻断 Windows 系统目录下的进程。

对常见公共运行库或共享依赖，例如 `dotnet.exe`、`java.exe`、`node.exe`、`python.exe`、`msedgewebview2.exe`、`crashpad_handler.exe`，如果它们不在目标软件安装目录下，NetFence 会显示为候选项但默认不勾选。确实需要一起阻断时，可以手动勾选。

## 重要说明

- Windows 防火墙规则按程序路径生效。如果软件以后在新路径生成新的辅助 `.exe`，需要再次点击 `Block Network` 把新程序加入规则。
- Microsoft Store/UWP 应用、驱动、内核组件不一定像普通 `.exe` 一样工作，可能需要单独处理。
- NetFence 会拒绝直接阻断 Windows 系统目录下的目标，也会跳过系统目录下的联动进程。它适合阻断用户选择的软件，不适合阻断 Windows 核心组件。
- 规则是持久化的。关闭 NetFence 不会解除阻断，必须使用 `Unblock` 删除规则。
- 关联程序扫描是辅助判断，不是绝对准确。阻断前建议检查 `Related candidates` 里的路径，避免误封浏览器、输入法、显卡工具或其他共享组件。

## 文案资源

界面文案放在独立资源文件里，PowerShell 主脚本保持 ASCII，避免 Windows PowerShell 5.1 对中文脚本编码处理不稳定：

```text
i18n/en-US.json
i18n/zh-CN.json
```

## 测试

运行 PowerShell 版非破坏性测试：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\NetFence.Tests.ps1
```

运行 .NET 版非破坏性测试：

```powershell
dotnet run --project .\dotnet\NetFence.Core.Tests\NetFence.Core.Tests.csproj -c Release
```

测试会验证目标发现、规则命名、系统路径保护、联动进程遍历、关联程序扫描、额外阻断目标合并、导出、日志和首次启动确认。测试不会创建或删除真实防火墙规则。

## 打包发布

生成自包含 Windows x64 发布包：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-release.ps1
```

输出：

```text
dist\NetFence-win-x64\
dist\NetFence-win-x64.zip
```
