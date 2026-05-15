using System.Collections.Generic;

namespace NetFence.App.Services;

public static class LocaleService
{
    public static event Action? LanguageChanged;

    private static string _currentLanguage = "";

    public static string CurrentLanguage
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_currentLanguage))
            {
                _currentLanguage = SettingsService.Language;
                if (string.IsNullOrWhiteSpace(_currentLanguage))
                {
                    _currentLanguage = Thread.CurrentThread.CurrentUICulture.Name
                        .StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                        ? "zh-CN" : "en-US";
                }
            }
            return _currentLanguage;
        }
        set
        {
            if (!string.Equals(_currentLanguage, value, StringComparison.OrdinalIgnoreCase))
            {
                _currentLanguage = value;
                SettingsService.Language = value;
                LanguageChanged?.Invoke();
            }
        }
    }

    public static string T(string key)
    {
        var map = Translations.TryGetValue(CurrentLanguage, out var selected)
            ? selected : Translations["en-US"];
        return map.TryGetValue(key, out var value) ? value
            : Translations["en-US"].GetValueOrDefault(key, key);
    }

    public static string T(string key, params object[] args)
    {
        try { return string.Format(T(key), args); }
        catch (FormatException ex)
        {
            System.Diagnostics.Debug.WriteLine($"LocaleService format error for key '{key}': {ex.Message}");
            return T(key) + " " + string.Join(" ", args);
        }
    }

    private static readonly Dictionary<string, Dictionary<string, string>> Translations = new()
    {
        ["en-US"] = new()
        {
            ["navScanBlock"] = "Scan & Block",
            ["navNetworkMonitor"] = "Network Monitor",
            ["navServicesTasks"] = "Services & Tasks",
            ["navRuleProfiles"] = "Rule Profiles",
            ["navSettings"] = "Settings",
            ["ready"] = "Ready",
            ["adminEnabled"] = "Administrator: firewall changes enabled",
            ["targetPath"] = "Target path",
            ["selectExe"] = "Select EXE",
            ["selectFolder"] = "Select Folder",
            ["ruleName"] = "Rule name",
            ["includeChildProcesses"] = "Include executable files from currently running child processes",
            ["scanRelated"] = "Scan Related",
            ["blockNetwork"] = "Block Network",
            ["unblock"] = "Unblock",
            ["unblockSelected"] = "Unblock Selected",
            ["unblockAll"] = "Unblock All",
            ["exportSnapshot"] = "Export Snapshot",
            ["openLog"] = "Open Log",
            ["refresh"] = "Refresh",
            ["relatedCandidates"] = "Related candidates",
            ["firewallRules"] = "Firewall rules",
            ["columnBlock"] = "Block",
            ["columnReason"] = "Reason",
            ["columnPid"] = "PID",
            ["columnProcess"] = "Process",
            ["columnProgramPath"] = "Program path",
            ["columnRule"] = "Rule",
            ["columnDirection"] = "Direction",
            ["columnEnabled"] = "Enabled",
            ["columnAction"] = "Action",
            ["scanRunning"] = "Scanning related executable files...",
            ["blockRunning"] = "Applying persistent firewall block rules...",
            ["unblockRunning"] = "Removing NetFence firewall rules...",
            ["unblockAllRunning"] = "Removing all NetFence firewall rules...",
            ["exportRunning"] = "Exporting current rules and candidates...",
            ["refreshRunning"] = "Refreshing firewall rule status...",
            ["loadedRules"] = "Loaded {0} NetFence firewall rule(s).",
            ["readStatusFailed"] = "Failed to read firewall status: {0}",
            ["foundCandidates"] = "Found {0} related candidate executable(s). Review checkboxes before blocking.",
            ["scanFailed"] = "Scan failed: {0}",
            ["selectTargetFirst"] = "Select an .exe file or a folder first.",
            ["pathDoesNotExist"] = "Path does not exist: {0}",
            ["folderDialogDescription"] = "Select a folder containing executable files",
            ["blockedTargets"] = "Blocked '{0}' for {1} executable file(s).",
            ["blockFailed"] = "Block failed: {0}",
            ["unblockedRules"] = "Unblocked '{0}' and removed {1} rule(s).",
            ["unblockFailed"] = "Unblock failed: {0}",
            ["selectRuleFirst"] = "Select one or more firewall rules first.",
            ["unblockSelectedRunning"] = "Removing firewall rules for selected executable file(s)...",
            ["unblockedSelectedRules"] = "Removed {0} rule(s) for {1} selected executable file(s).",
            ["unblockSelectedFailed"] = "Unblock selected failed: {0}",
            ["unblockSelectedConfirmTitle"] = "Unblock selected executable files",
            ["unblockSelectedConfirmMessage"] = "This removes all NetFence rules for {0} selected executable file(s). Continue?",
            ["unblockedAllRules"] = "Removed all NetFence rules: {0} rule(s).",
            ["unblockAllFailed"] = "Unblock all failed: {0}",
            ["unblockAllConfirmTitle"] = "Remove all NetFence rules",
            ["unblockAllConfirmMessage"] = "This removes every firewall rule managed by NetFence. Continue?",
            ["exportComplete"] = "Exported {0} row(s) to {1}",
            ["exportFailed"] = "Export failed: {0}",
            ["openLogFailed"] = "Open log failed: {0}",
            ["languageSwitchFailed"] = "Language switch failed: {0}",
            ["firstRunTitle"] = "NetFence safety notice",
            ["firstRunMessage"] = "NetFence changes Windows Defender Firewall rules and requires administrator permission for block/unblock actions. Rules persist after closing this tool. Use Unblock or Unblock All to restore networking. Continue?",
            ["networkMonitorComing"] = "Network monitoring will be available in a future update.",
            ["servicesTasksComing"] = "Service and scheduled task scanning will be available in a future update.",
            ["ruleProfilesSection"] = "Rule Profiles",
            ["saveProfile"] = "Save Profile",
            ["loadProfile"] = "Load & Block",
            ["deleteProfile"] = "Delete",
            ["exportProfile"] = "Export JSON",
            ["importProfile"] = "Import JSON",
            ["snapshotsSection"] = "Snapshots",
            ["rollback"] = "Rollback",
            ["columnProfileName"] = "Name",
            ["columnPaths"] = "Paths",
            ["columnPrograms"] = "Programs",
            ["noRulesToSave"] = "No NetFence firewall rules exist to save as a profile.",
            ["selectProfileFirst"] = "Select a profile first.",
            ["loadProfileConfirm"] = "Load profile '{0}' and apply its block rules?",
            ["profileLoaded"] = "Profile loaded and rules applied.",
            ["profileImported"] = "Profile '{0}' imported successfully.",
            ["selectSnapshotFirst"] = "Select a snapshot first.",
            ["rollbackConfirm"] = "This will remove ALL current NetFence rules and restore the snapshot state. Continue?",
            ["rollbackConfirmTitle"] = "Rollback to snapshot",
            ["rollbackComplete"] = "Rollback complete. Rules restored from snapshot.",
            ["rollbackPartial"] = "Rollback partially complete: {0} rules restored, {1} rule(s) failed.",
            ["columnUpdated"] = "Updated",
            ["columnStatus"] = "Status",
            ["languageLabel"] = "Language",
            ["languageEnglish"] = "English",
            ["languageChinese"] = "Chinese",
            ["themeLabel"] = "Theme",
            ["themeSystem"] = "Follow System",
            ["themeDark"] = "Dark",
            ["themeLight"] = "Light",
            ["aboutLabel"] = "About",
            ["aboutText"] = "NetFence — Manage Windows Firewall outbound and inbound block rules for selected applications.",
            ["openLogLabel"] = "Open Log",
        },
        ["zh-CN"] = new()
        {
            ["navScanBlock"] = "扫描封禁",
            ["navNetworkMonitor"] = "联网监控",
            ["navServicesTasks"] = "服务/计划任务",
            ["navRuleProfiles"] = "规则档案",
            ["navSettings"] = "设置",
            ["ready"] = "就绪",
            ["adminEnabled"] = "管理员权限：可以修改防火墙规则",
            ["targetPath"] = "目标路径",
            ["selectExe"] = "选择 EXE",
            ["selectFolder"] = "选择文件夹",
            ["ruleName"] = "规则名称",
            ["includeChildProcesses"] = "包含当前运行中的子进程可执行文件",
            ["scanRelated"] = "扫描关联程序",
            ["blockNetwork"] = "禁止联网",
            ["unblock"] = "解除禁止",
            ["unblockSelected"] = "解除选中",
            ["unblockAll"] = "解除全部",
            ["exportSnapshot"] = "导出快照",
            ["openLog"] = "打开日志",
            ["refresh"] = "刷新",
            ["relatedCandidates"] = "关联候选程序",
            ["firewallRules"] = "防火墙规则",
            ["columnBlock"] = "阻断",
            ["columnReason"] = "原因",
            ["columnPid"] = "PID",
            ["columnProcess"] = "进程",
            ["columnProgramPath"] = "程序路径",
            ["columnRule"] = "规则",
            ["columnDirection"] = "方向",
            ["columnEnabled"] = "启用",
            ["columnAction"] = "动作",
            ["scanRunning"] = "正在扫描关联可执行文件...",
            ["blockRunning"] = "正在写入持久化防火墙阻断规则...",
            ["unblockRunning"] = "正在删除 NetFence 防火墙规则...",
            ["unblockAllRunning"] = "正在删除全部 NetFence 防火墙规则...",
            ["exportRunning"] = "正在导出当前规则和候选列表...",
            ["refreshRunning"] = "正在刷新防火墙规则状态...",
            ["loadedRules"] = "已加载 {0} 条 NetFence 防火墙规则。",
            ["readStatusFailed"] = "读取防火墙状态失败：{0}",
            ["foundCandidates"] = "发现 {0} 个关联候选可执行文件。阻断前请检查勾选项。",
            ["scanFailed"] = "扫描失败：{0}",
            ["selectTargetFirst"] = "请先选择一个 .exe 文件或文件夹。",
            ["pathDoesNotExist"] = "路径不存在：{0}",
            ["folderDialogDescription"] = "选择包含可执行程序的文件夹",
            ["blockedTargets"] = "已禁止 '{0}' 联网，覆盖 {1} 个可执行文件。",
            ["blockFailed"] = "禁止联网失败：{0}",
            ["unblockedRules"] = "已解除 '{0}'，删除 {1} 条规则。",
            ["unblockFailed"] = "解除失败：{0}",
            ["selectRuleFirst"] = "请先选择一条或多条防火墙规则。",
            ["unblockSelectedRunning"] = "正在删除选中可执行文件的防火墙规则...",
            ["unblockedSelectedRules"] = "已删除 {0} 条规则，涉及 {1} 个选中可执行文件。",
            ["unblockSelectedFailed"] = "解除选中失败：{0}",
            ["unblockSelectedConfirmTitle"] = "解除选中可执行文件",
            ["unblockSelectedConfirmMessage"] = "这会删除 {0} 个选中可执行文件的全部 NetFence 规则。是否继续？",
            ["unblockedAllRules"] = "已删除全部 NetFence 规则：{0} 条。",
            ["unblockAllFailed"] = "解除全部失败：{0}",
            ["unblockAllConfirmTitle"] = "删除全部 NetFence 规则",
            ["unblockAllConfirmMessage"] = "这会删除所有由 NetFence 管理的防火墙规则。是否继续？",
            ["exportComplete"] = "已导出 {0} 行到 {1}",
            ["exportFailed"] = "导出失败：{0}",
            ["openLogFailed"] = "打开日志失败：{0}",
            ["languageSwitchFailed"] = "语言切换失败：{0}",
            ["firstRunTitle"] = "NetFence 安全提示",
            ["firstRunMessage"] = "NetFence 会修改 Windows Defender 防火墙规则，禁止/解除操作需要管理员权限。规则在关闭工具后仍会保留。需要恢复联网时请使用“解除禁止”或“解除全部”。是否继续？",
            ["networkMonitorComing"] = "联网监控功能将在后续版本中推出。",
            ["servicesTasksComing"] = "服务与计划任务扫描将在后续版本中推出。",
            ["ruleProfilesSection"] = "规则档案",
            ["saveProfile"] = "保存档案",
            ["loadProfile"] = "加载并阻断",
            ["deleteProfile"] = "删除",
            ["exportProfile"] = "导出 JSON",
            ["importProfile"] = "导入 JSON",
            ["snapshotsSection"] = "操作快照",
            ["rollback"] = "回滚",
            ["columnProfileName"] = "名称",
            ["columnPaths"] = "路径",
            ["columnPrograms"] = "程序",
            ["noRulesToSave"] = "没有可保存的 NetFence 防火墙规则。",
            ["selectProfileFirst"] = "请先选择一个档案。",
            ["loadProfileConfirm"] = "加载档案 '{0}' 并应用阻断规则？",
            ["profileLoaded"] = "档案已加载，规则已应用。",
            ["profileImported"] = "档案 '{0}' 已导入。",
            ["selectSnapshotFirst"] = "请先选择一个快照。",
            ["rollbackConfirm"] = "此操作将删除当前所有 NetFence 规则并恢复到快照状态。是否继续？",
            ["rollbackConfirmTitle"] = "回滚到快照",
            ["rollbackComplete"] = "回滚完成。规则已从快照恢复。",
            ["rollbackPartial"] = "回滚部分完成：已恢复 {0} 条规则，{1} 条失败。",
            ["columnUpdated"] = "更新时间",
            ["columnStatus"] = "状态",
            ["languageLabel"] = "语言",
            ["languageEnglish"] = "英文",
            ["languageChinese"] = "中文",
            ["themeLabel"] = "主题",
            ["themeSystem"] = "跟随系统",
            ["themeDark"] = "深色",
            ["themeLight"] = "浅色",
            ["aboutLabel"] = "关于",
            ["aboutText"] = "NetFence — 管理 Windows Defender 防火墙出入站规则，控制指定软件的网络访问。",
            ["openLogLabel"] = "打开日志",
        }
    };
}
