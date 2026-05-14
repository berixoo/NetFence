param()

$ErrorActionPreference = 'Stop'

if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    Start-Process -FilePath 'powershell.exe' -WindowStyle Hidden -ArgumentList @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-STA',
        '-WindowStyle', 'Hidden',
        '-File', "`"$PSCommandPath`""
    )
    exit
}

. (Join-Path $PSScriptRoot 'src\NetFence.Core.ps1')
. (Join-Path $PSScriptRoot 'src\NetFence.I18n.ps1')

$script:NfTranslations = Import-NetFenceTranslations -RootPath $PSScriptRoot
$script:NfLanguage = Get-NetFenceDefaultLanguage

function Get-NfUiText {
    param([Parameter(Mandatory = $true)] [string] $Key)
    return Get-NetFenceText -Translations $script:NfTranslations -Language $script:NfLanguage -Key $Key
}

function Format-NfUiText {
    param(
        [Parameter(Mandatory = $true)] [string] $Key,
        [object[]] $Arguments = @()
    )
    return Format-NetFenceText -Translations $script:NfTranslations -Language $script:NfLanguage -Key $Key -Arguments $Arguments
}

Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase
Add-Type -AssemblyName System.Windows.Forms

function New-NfTextBlock {
    param(
        [string] $Text,
        [int] $Row,
        [int] $Column = 0
    )

    $block = New-Object Windows.Controls.TextBlock
    $block.Text = $Text
    $block.VerticalAlignment = 'Center'
    $block.Margin = '0,6,10,6'
    [Windows.Controls.Grid]::SetRow($block, $Row)
    [Windows.Controls.Grid]::SetColumn($block, $Column)
    return $block
}

function New-NfButton {
    param(
        [string] $Text,
        [int] $Width = 96
    )

    $button = New-Object Windows.Controls.Button
    $button.Content = $Text
    $button.MinWidth = $Width
    $button.Height = 32
    $button.Margin = '4'
    return $button
}

$window = New-Object Windows.Window
$window.Title = 'NetFence'
$window.Width = 980
$window.Height = 620
$window.MinWidth = 860
$window.MinHeight = 520
$window.WindowStartupLocation = 'CenterScreen'

$root = New-Object Windows.Controls.Grid
$root.Margin = '16'
$window.Content = $root

@('Auto', 'Auto', 'Auto', 'Auto', '*', 'Auto') | ForEach-Object {
    $row = New-Object Windows.Controls.RowDefinition
    $row.Height = $_
    $root.RowDefinitions.Add($row)
}

$header = New-Object Windows.Controls.Grid
$header.Margin = '0,0,0,4'
$headerMainCol = New-Object Windows.Controls.ColumnDefinition
$headerMainCol.Width = '*'
$header.ColumnDefinitions.Add($headerMainCol)
$headerLabelCol = New-Object Windows.Controls.ColumnDefinition
$headerLabelCol.Width = 'Auto'
$header.ColumnDefinitions.Add($headerLabelCol)
$headerComboCol = New-Object Windows.Controls.ColumnDefinition
$headerComboCol.Width = 'Auto'
$header.ColumnDefinitions.Add($headerComboCol)
[Windows.Controls.Grid]::SetRow($header, 0)
$root.Children.Add($header) | Out-Null

$title = New-Object Windows.Controls.TextBlock
$title.Text = 'NetFence'
$title.FontSize = 26
$title.FontWeight = 'SemiBold'
[Windows.Controls.Grid]::SetColumn($title, 0)
$header.Children.Add($title) | Out-Null

$languageLabel = New-Object Windows.Controls.TextBlock
$languageLabel.VerticalAlignment = 'Center'
$languageLabel.Margin = '0,0,8,0'
[Windows.Controls.Grid]::SetColumn($languageLabel, 1)
$header.Children.Add($languageLabel) | Out-Null

$languageBox = New-Object Windows.Controls.ComboBox
$languageBox.Width = 130
$languageBox.Height = 28
$languageBox.VerticalContentAlignment = 'Center'
$languageEnglishItem = New-Object Windows.Controls.ComboBoxItem
$languageEnglishItem.Tag = 'en-US'
$languageChineseItem = New-Object Windows.Controls.ComboBoxItem
$languageChineseItem.Tag = 'zh-CN'
$languageBox.Items.Add($languageEnglishItem) | Out-Null
$languageBox.Items.Add($languageChineseItem) | Out-Null
[Windows.Controls.Grid]::SetColumn($languageBox, 2)
$header.Children.Add($languageBox) | Out-Null

$adminText = New-Object Windows.Controls.TextBlock
$adminText.Margin = '0,0,0,12'
$adminText.Foreground = if (Test-NetFenceIsAdministrator) { 'DarkGreen' } else { 'DarkRed' }
[Windows.Controls.Grid]::SetRow($adminText, 1)
$root.Children.Add($adminText) | Out-Null

$form = New-Object Windows.Controls.Grid
@('Auto', 'Auto', 'Auto') | ForEach-Object {
    $row = New-Object Windows.Controls.RowDefinition
    $row.Height = $_
    $form.RowDefinitions.Add($row)
}
$labelCol = New-Object Windows.Controls.ColumnDefinition
$labelCol.Width = '90'
$form.ColumnDefinitions.Add($labelCol)
$mainCol = New-Object Windows.Controls.ColumnDefinition
$mainCol.Width = '*'
$form.ColumnDefinitions.Add($mainCol)
$buttonCol = New-Object Windows.Controls.ColumnDefinition
$buttonCol.Width = 'Auto'
$form.ColumnDefinitions.Add($buttonCol)
[Windows.Controls.Grid]::SetRow($form, 2)
$root.Children.Add($form) | Out-Null

$pathLabel = New-NfTextBlock -Text '' -Row 0
$form.Children.Add($pathLabel) | Out-Null

$pathBox = New-Object Windows.Controls.TextBox
$pathBox.Height = 30
$pathBox.Margin = '0,4,8,4'
$pathBox.VerticalContentAlignment = 'Center'
[Windows.Controls.Grid]::SetRow($pathBox, 0)
[Windows.Controls.Grid]::SetColumn($pathBox, 1)
$form.Children.Add($pathBox) | Out-Null

$browsePanel = New-Object Windows.Controls.StackPanel
$browsePanel.Orientation = 'Horizontal'
$browseExeButton = New-NfButton -Text '' -Width 96
$browseFolderButton = New-NfButton -Text '' -Width 110
$browsePanel.Children.Add($browseExeButton) | Out-Null
$browsePanel.Children.Add($browseFolderButton) | Out-Null
[Windows.Controls.Grid]::SetRow($browsePanel, 0)
[Windows.Controls.Grid]::SetColumn($browsePanel, 2)
$form.Children.Add($browsePanel) | Out-Null

$nameLabel = New-NfTextBlock -Text '' -Row 1
$form.Children.Add($nameLabel) | Out-Null

$nameBox = New-Object Windows.Controls.TextBox
$nameBox.Height = 30
$nameBox.Margin = '0,4,8,4'
$nameBox.VerticalContentAlignment = 'Center'
[Windows.Controls.Grid]::SetRow($nameBox, 1)
[Windows.Controls.Grid]::SetColumn($nameBox, 1)
[Windows.Controls.Grid]::SetColumnSpan($nameBox, 2)
$form.Children.Add($nameBox) | Out-Null

$linkedCheck = New-Object Windows.Controls.CheckBox
$linkedCheck.Margin = '0,8,0,8'
$linkedCheck.IsChecked = $true
[Windows.Controls.Grid]::SetRow($linkedCheck, 2)
[Windows.Controls.Grid]::SetColumn($linkedCheck, 1)
[Windows.Controls.Grid]::SetColumnSpan($linkedCheck, 2)
$form.Children.Add($linkedCheck) | Out-Null

$actionPanel = New-Object Windows.Controls.StackPanel
$actionPanel.Orientation = 'Horizontal'
$actionPanel.Margin = '0,8,0,10'
$blockButton = New-NfButton -Text '' -Width 116
$unblockButton = New-NfButton -Text '' -Width 104
$unblockAllButton = New-NfButton -Text '' -Width 112
$scanButton = New-NfButton -Text '' -Width 120
$exportButton = New-NfButton -Text '' -Width 124
$openLogButton = New-NfButton -Text '' -Width 104
$refreshButton = New-NfButton -Text '' -Width 104
$adminButton = New-NfButton -Text '' -Width 116
$actionPanel.Children.Add($scanButton) | Out-Null
$actionPanel.Children.Add($blockButton) | Out-Null
$actionPanel.Children.Add($unblockButton) | Out-Null
$actionPanel.Children.Add($unblockAllButton) | Out-Null
$actionPanel.Children.Add($exportButton) | Out-Null
$actionPanel.Children.Add($openLogButton) | Out-Null
$actionPanel.Children.Add($refreshButton) | Out-Null
if (-not (Test-NetFenceIsAdministrator)) {
    $actionPanel.Children.Add($adminButton) | Out-Null
}
[Windows.Controls.Grid]::SetRow($actionPanel, 3)
$root.Children.Add($actionPanel) | Out-Null

$tabs = New-Object Windows.Controls.TabControl
$tabs.Margin = '0,0,0,10'

$candidateTab = New-Object Windows.Controls.TabItem
$candidateGrid = New-Object Windows.Controls.DataGrid
$candidateGrid.AutoGenerateColumns = $false
$candidateGrid.IsReadOnly = $false
$candidateGrid.CanUserAddRows = $false
$candidateGrid.Margin = '0,0,0,0'
$candidateGrid.HeadersVisibility = 'Column'

$selectedColumn = New-Object Windows.Controls.DataGridCheckBoxColumn
$selectedColumn.Binding = New-Object Windows.Data.Binding -ArgumentList 'Selected'
$selectedColumn.Width = 60
$candidateGrid.Columns.Add($selectedColumn) | Out-Null

$candidateColumns = @(
    @{ Key = 'columnReason'; Binding = 'Reason'; Width = 210 },
    @{ Key = 'columnPid'; Binding = 'ProcessId'; Width = 70 },
    @{ Key = 'columnProcess'; Binding = 'ProcessName'; Width = 120 },
    @{ Key = 'columnProgramPath'; Binding = 'Program'; Width = 470 }
)

$candidateTextColumns = @{}
foreach ($column in $candidateColumns) {
    $dataColumn = New-Object Windows.Controls.DataGridTextColumn
    $dataColumn.Binding = New-Object Windows.Data.Binding -ArgumentList $column.Binding
    $dataColumn.Width = $column.Width
    $dataColumn.IsReadOnly = $true
    $candidateGrid.Columns.Add($dataColumn) | Out-Null
    $candidateTextColumns[$column.Key] = $dataColumn
}

$candidateTab.Content = $candidateGrid
$tabs.Items.Add($candidateTab) | Out-Null

$statusTab = New-Object Windows.Controls.TabItem
$statusGrid = New-Object Windows.Controls.DataGrid
$statusGrid.AutoGenerateColumns = $false
$statusGrid.IsReadOnly = $true
$statusGrid.CanUserAddRows = $false
$statusGrid.Margin = '0,0,0,0'
$statusGrid.HeadersVisibility = 'Column'

$statusColumns = @(
    @{ Key = 'columnRule'; Binding = 'ProfileName'; Width = 130 },
    @{ Key = 'columnDirection'; Binding = 'Direction'; Width = 80 },
    @{ Key = 'columnEnabled'; Binding = 'Enabled'; Width = 70 },
    @{ Key = 'columnAction'; Binding = 'Action'; Width = 70 },
    @{ Key = 'columnProgramPath'; Binding = 'Program'; Width = 420 }
)

$statusTextColumns = @{}
foreach ($column in $statusColumns) {
    $dataColumn = New-Object Windows.Controls.DataGridTextColumn
    $dataColumn.Binding = New-Object Windows.Data.Binding -ArgumentList $column.Binding
    $dataColumn.Width = $column.Width
    $statusGrid.Columns.Add($dataColumn) | Out-Null
    $statusTextColumns[$column.Key] = $dataColumn
}

$statusTab.Content = $statusGrid
$tabs.Items.Add($statusTab) | Out-Null

[Windows.Controls.Grid]::SetRow($tabs, 4)
$root.Children.Add($tabs) | Out-Null

$statusPanel = New-Object Windows.Controls.StackPanel
$statusPanel.Orientation = 'Vertical'
[Windows.Controls.Grid]::SetRow($statusPanel, 5)
$root.Children.Add($statusPanel) | Out-Null

$progressBar = New-Object Windows.Controls.ProgressBar
$progressBar.Height = 4
$progressBar.Margin = '0,0,0,6'
$progressBar.IsIndeterminate = $true
$progressBar.Visibility = 'Collapsed'
$statusPanel.Children.Add($progressBar) | Out-Null

$statusText = New-Object Windows.Controls.TextBlock
$statusText.TextWrapping = 'Wrap'
$statusPanel.Children.Add($statusText) | Out-Null

$script:NfLastStatusKey = 'ready'
$script:NfLastStatusArgs = @()
$script:NfLastStatusIsError = $false
$script:NfActiveJob = $null
$script:NfActiveJobOnSuccess = $null
$script:NfActiveJobFailureKey = 'operationRunning'
$script:NfCorePath = Join-Path $PSScriptRoot 'src\NetFence.Core.ps1'
$script:NfLogPath = Get-NetFenceLogPath

function Set-NfStatus {
    param(
        [string] $Message,
        [bool] $IsError = $false
    )

    $statusText.Text = $Message
    $statusText.Foreground = if ($IsError) { 'DarkRed' } else { 'Black' }
}

function Set-NfStatusMessage {
    param(
        [Parameter(Mandatory = $true)] [string] $Key,
        [object[]] $Arguments = @(),
        [bool] $IsError = $false
    )

    $script:NfLastStatusKey = $Key
    $script:NfLastStatusArgs = @($Arguments)
    $script:NfLastStatusIsError = $IsError
    Set-NfStatus (Format-NfUiText -Key $Key -Arguments $Arguments) $IsError
}

function Apply-NfLanguage {
    $languageLabel.Text = Get-NfUiText 'languageLabel'
    $languageEnglishItem.Content = Get-NfUiText 'languageEnglish'
    $languageChineseItem.Content = Get-NfUiText 'languageChinese'

    $adminText.Text = if (Test-NetFenceIsAdministrator) { Get-NfUiText 'adminEnabled' } else { Get-NfUiText 'adminReadonly' }
    $pathLabel.Text = Get-NfUiText 'targetPath'
    $browseExeButton.Content = Get-NfUiText 'selectExe'
    $browseFolderButton.Content = Get-NfUiText 'selectFolder'
    $nameLabel.Text = Get-NfUiText 'ruleName'
    $linkedCheck.Content = Get-NfUiText 'includeChildProcesses'

    $scanButton.Content = Get-NfUiText 'scanRelated'
    $blockButton.Content = Get-NfUiText 'blockNetwork'
    $unblockButton.Content = Get-NfUiText 'unblock'
    $unblockAllButton.Content = Get-NfUiText 'unblockAll'
    $exportButton.Content = Get-NfUiText 'exportSnapshot'
    $openLogButton.Content = Get-NfUiText 'openLog'
    $refreshButton.Content = Get-NfUiText 'refresh'
    $adminButton.Content = Get-NfUiText 'restartAdmin'

    $candidateTab.Header = Get-NfUiText 'relatedCandidates'
    $statusTab.Header = Get-NfUiText 'firewallRules'
    $selectedColumn.Header = Get-NfUiText 'columnBlock'
    foreach ($key in $candidateTextColumns.Keys) {
        $candidateTextColumns[$key].Header = Get-NfUiText $key
    }
    foreach ($key in $statusTextColumns.Keys) {
        $statusTextColumns[$key].Header = Get-NfUiText $key
    }

    Set-NfStatus (Format-NfUiText -Key $script:NfLastStatusKey -Arguments $script:NfLastStatusArgs) $script:NfLastStatusIsError
}

function Set-NfBusy {
    param([bool] $IsBusy)

    $progressBar.Visibility = if ($IsBusy) { 'Visible' } else { 'Collapsed' }
    $window.Cursor = if ($IsBusy) { [Windows.Input.Cursors]::Wait } else { $null }

    foreach ($control in @($browseExeButton, $browseFolderButton, $pathBox, $nameBox, $linkedCheck, $scanButton, $blockButton, $unblockButton, $unblockAllButton, $exportButton, $refreshButton)) {
        $control.IsEnabled = -not $IsBusy
    }
}

function Start-NfBackgroundOperation {
    param(
        [Parameter(Mandatory = $true)] [string] $StatusKey,
        [Parameter(Mandatory = $true)] [string] $FailureKey,
        [Parameter(Mandatory = $true)] [scriptblock] $JobScript,
        [object[]] $ArgumentList = @(),
        [Parameter(Mandatory = $true)] [scriptblock] $OnSuccess
    )

    if ($null -ne $script:NfActiveJob) {
        Set-NfStatusMessage -Key 'operationRunning'
        return
    }

    Set-NfStatusMessage -Key $StatusKey
    Set-NfBusy $true
    $script:NfActiveJobOnSuccess = $OnSuccess
    $script:NfActiveJobFailureKey = $FailureKey
    $script:NfActiveJob = Start-Job -ScriptBlock $JobScript -ArgumentList $ArgumentList
    $script:NfJobTimer.Start()
}

$script:NfJobTimer = New-Object Windows.Threading.DispatcherTimer
$script:NfJobTimer.Interval = [TimeSpan]::FromMilliseconds(250)
$script:NfJobTimer.Add_Tick({
    if ($null -eq $script:NfActiveJob) {
        $script:NfJobTimer.Stop()
        return
    }

    $job = $script:NfActiveJob
    if ($job.State -eq 'Running' -or $job.State -eq 'NotStarted') {
        return
    }

    $script:NfJobTimer.Stop()
    $script:NfActiveJob = $null
    Set-NfBusy $false

    if ($job.State -eq 'Completed') {
        try {
            $output = @(Receive-Job -Job $job -ErrorAction Stop)
            Remove-Job -Job $job -Force
            & $script:NfActiveJobOnSuccess $output
        }
        catch {
            Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
            Set-NfStatusMessage -Key $script:NfActiveJobFailureKey -Arguments @($_.Exception.Message) -IsError $true
        }
        finally {
            $script:NfActiveJobOnSuccess = $null
            $script:NfActiveJobFailureKey = 'operationRunning'
        }
        return
    }

    $reason = $null
    if ($job.ChildJobs.Count -gt 0 -and $null -ne $job.ChildJobs[0].JobStateInfo.Reason) {
        $reason = $job.ChildJobs[0].JobStateInfo.Reason.Message
    }
    if ([string]::IsNullOrWhiteSpace($reason)) {
        $reason = "Background operation ended with state: $($job.State)"
    }
    Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
    $script:NfActiveJobOnSuccess = $null
    Set-NfStatusMessage -Key $script:NfActiveJobFailureKey -Arguments @($reason) -IsError $true
    $script:NfActiveJobFailureKey = 'operationRunning'
})

function Update-NfStatusGrid {
    try {
        Start-NfBackgroundOperation `
            -StatusKey 'refreshRunning' `
            -FailureKey 'readStatusFailed' `
            -ArgumentList @($script:NfCorePath) `
            -JobScript {
                param([string] $CorePath)
                . $CorePath
                @(Get-NetFenceStatus)
            } `
            -OnSuccess {
                param($Output)
                $statusGrid.ItemsSource = @($Output)
                Set-NfStatusMessage -Key 'loadedRules' -Arguments @($Output.Count)
            }
    }
    catch {
        Set-NfStatusMessage -Key 'readStatusFailed' -Arguments @($_.Exception.Message) -IsError $true
    }
}

function Update-NfCandidateGrid {
    try {
        $path = Get-NfSelectedPath
        Start-NfBackgroundOperation `
            -StatusKey 'scanRunning' `
            -FailureKey 'scanFailed' `
            -ArgumentList @($script:NfCorePath, $path) `
            -JobScript {
                param([string] $CorePath, [string] $Path)
                . $CorePath
                @(Get-NetFenceRelatedProcessCandidates -Path $Path)
            } `
            -OnSuccess {
                param($Output)
                $candidateGrid.ItemsSource = @($Output)
                $tabs.SelectedItem = $candidateTab
                Set-NfStatusMessage -Key 'foundCandidates' -Arguments @($Output.Count)
            }
    }
    catch {
        Set-NfStatusMessage -Key 'scanFailed' -Arguments @($_.Exception.Message) -IsError $true
    }
}

function Get-NfSelectedCandidateTargets {
    $targets = New-Object System.Collections.Generic.List[string]
    foreach ($item in @($candidateGrid.ItemsSource)) {
        if ($null -ne $item -and [bool] $item.Selected -and -not [string]::IsNullOrWhiteSpace($item.Program)) {
            $targets.Add($item.Program)
        }
    }
    return @($targets)
}

function Get-NfSelectedPath {
    $path = $pathBox.Text.Trim()
    if ([string]::IsNullOrWhiteSpace($path)) {
        throw (Get-NfUiText 'selectTargetFirst')
    }
    if (-not (Test-Path -LiteralPath $path)) {
        throw (Format-NfUiText -Key 'pathDoesNotExist' -Arguments @($path))
    }
    return $path
}

function Show-NfFirstRunWarning {
    if (Test-NetFenceFirstRunAcknowledged) {
        return $true
    }

    $result = [Windows.MessageBox]::Show(
        (Get-NfUiText 'firstRunMessage'),
        (Get-NfUiText 'firstRunTitle'),
        [Windows.MessageBoxButton]::OKCancel,
        [Windows.MessageBoxImage]::Warning
    )

    if ($result -ne [Windows.MessageBoxResult]::OK) {
        return $false
    }

    Set-NetFenceFirstRunAcknowledged
    return $true
}

$browseExeButton.Add_Click({
    $dialog = New-Object System.Windows.Forms.OpenFileDialog
    $dialog.Filter = 'Executable files (*.exe)|*.exe'
    $dialog.CheckFileExists = $true
    if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        $pathBox.Text = $dialog.FileName
        if ([string]::IsNullOrWhiteSpace($nameBox.Text)) {
            $nameBox.Text = Get-NetFenceProfileName -Path $dialog.FileName
        }
    }
})

$browseFolderButton.Add_Click({
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = Get-NfUiText 'folderDialogDescription'
    if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        $pathBox.Text = $dialog.SelectedPath
        if ([string]::IsNullOrWhiteSpace($nameBox.Text)) {
            $nameBox.Text = Get-NetFenceProfileName -Path $dialog.SelectedPath
        }
    }
})

$blockButton.Add_Click({
    try {
        $path = Get-NfSelectedPath
        $name = $nameBox.Text.Trim()
        $includeLinked = [bool] $linkedCheck.IsChecked
        $additionalTargets = @(Get-NfSelectedCandidateTargets)
        Start-NfBackgroundOperation `
            -StatusKey 'blockRunning' `
            -FailureKey 'blockFailed' `
            -ArgumentList @($script:NfCorePath, $path, $name, $includeLinked, $additionalTargets, $script:NfLogPath) `
            -JobScript {
                param([string] $CorePath, [string] $Path, [string] $Name, [bool] $IncludeLinked, [string[]] $AdditionalTargets, [string] $LogPath)
                . $CorePath
                $result = Enable-NetFenceBlock -Path $Path -Name $Name -IncludeLinkedProcesses:$IncludeLinked -AdditionalTargets $AdditionalTargets -LogPath $LogPath
                $rules = @(Get-NetFenceStatus)
                [pscustomobject]@{
                    ProfileName = $result.ProfileName
                    TargetCount = $result.TargetCount
                    Rules = $rules
                }
            } `
            -OnSuccess {
                param($Output)
                $result = $Output | Select-Object -First 1
                $statusGrid.ItemsSource = @($result.Rules)
                Set-NfStatusMessage -Key 'blockedTargets' -Arguments @($result.ProfileName, $result.TargetCount)
            }
    }
    catch {
        Set-NfStatusMessage -Key 'blockFailed' -Arguments @($_.Exception.Message) -IsError $true
    }
})

$unblockButton.Add_Click({
    try {
        $path = Get-NfSelectedPath
        $name = $nameBox.Text.Trim()
        Start-NfBackgroundOperation `
            -StatusKey 'unblockRunning' `
            -FailureKey 'unblockFailed' `
            -ArgumentList @($script:NfCorePath, $path, $name, $script:NfLogPath) `
            -JobScript {
                param([string] $CorePath, [string] $Path, [string] $Name, [string] $LogPath)
                . $CorePath
                $result = Disable-NetFenceBlock -Path $Path -Name $Name -LogPath $LogPath
                $rules = @(Get-NetFenceStatus)
                [pscustomobject]@{
                    ProfileName = $result.ProfileName
                    RemovedRuleCount = $result.RemovedRuleCount
                    Rules = $rules
                }
            } `
            -OnSuccess {
                param($Output)
                $result = $Output | Select-Object -First 1
                $statusGrid.ItemsSource = @($result.Rules)
                Set-NfStatusMessage -Key 'unblockedRules' -Arguments @($result.ProfileName, $result.RemovedRuleCount)
            }
    }
    catch {
        Set-NfStatusMessage -Key 'unblockFailed' -Arguments @($_.Exception.Message) -IsError $true
    }
})

$unblockAllButton.Add_Click({
    try {
        $confirm = [Windows.MessageBox]::Show(
            (Get-NfUiText 'unblockAllConfirmMessage'),
            (Get-NfUiText 'unblockAllConfirmTitle'),
            [Windows.MessageBoxButton]::YesNo,
            [Windows.MessageBoxImage]::Warning
        )
        if ($confirm -ne [Windows.MessageBoxResult]::Yes) {
            return
        }

        Start-NfBackgroundOperation `
            -StatusKey 'unblockAllRunning' `
            -FailureKey 'unblockAllFailed' `
            -ArgumentList @($script:NfCorePath, $script:NfLogPath) `
            -JobScript {
                param([string] $CorePath, [string] $LogPath)
                . $CorePath
                $result = Disable-NetFenceAllBlocks -LogPath $LogPath
                $rules = @(Get-NetFenceStatus)
                [pscustomobject]@{
                    RemovedRuleCount = $result.RemovedRuleCount
                    Rules = $rules
                }
            } `
            -OnSuccess {
                param($Output)
                $result = $Output | Select-Object -First 1
                $statusGrid.ItemsSource = @($result.Rules)
                Set-NfStatusMessage -Key 'unblockedAllRules' -Arguments @($result.RemovedRuleCount)
            }
    }
    catch {
        Set-NfStatusMessage -Key 'unblockAllFailed' -Arguments @($_.Exception.Message) -IsError $true
    }
})

$exportButton.Add_Click({
    try {
        $dialog = New-Object System.Windows.Forms.SaveFileDialog
        $dialog.Filter = 'CSV files (*.csv)|*.csv'
        $dialog.FileName = "NetFence-snapshot-$((Get-Date).ToString('yyyyMMdd-HHmmss')).csv"
        if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
            return
        }

        $rules = @($statusGrid.ItemsSource)
        $candidates = @($candidateGrid.ItemsSource)
        Start-NfBackgroundOperation `
            -StatusKey 'exportRunning' `
            -FailureKey 'exportFailed' `
            -ArgumentList @($script:NfCorePath, $dialog.FileName, $rules, $candidates, $script:NfLogPath) `
            -JobScript {
                param([string] $CorePath, [string] $ExportPath, [object[]] $Rules, [object[]] $Candidates, [string] $LogPath)
                . $CorePath
                $result = Export-NetFenceSnapshot -Path $ExportPath -Rules $Rules -Candidates $Candidates
                Write-NetFenceOperationLog -LogPath $LogPath -Action 'Export' -Message "Exported $($result.RowCount) row(s) to '$($result.Path)'." -Items @($result.Path)
                $result
            } `
            -OnSuccess {
                param($Output)
                $result = $Output | Select-Object -First 1
                Set-NfStatusMessage -Key 'exportComplete' -Arguments @($result.RowCount, $result.Path)
            }
    }
    catch {
        Set-NfStatusMessage -Key 'exportFailed' -Arguments @($_.Exception.Message) -IsError $true
    }
})

$openLogButton.Add_Click({
    try {
        if (-not (Test-Path -LiteralPath $script:NfLogPath -PathType Leaf)) {
            Write-NetFenceOperationLog -LogPath $script:NfLogPath -Action 'OpenLog' -Message 'Created log file.' -Items @()
        }
        Start-Process -FilePath $script:NfLogPath
    }
    catch {
        Set-NfStatusMessage -Key 'openLogFailed' -Arguments @($_.Exception.Message) -IsError $true
    }
})

$scanButton.Add_Click({ Update-NfCandidateGrid })

$refreshButton.Add_Click({ Update-NfStatusGrid })

$adminButton.Add_Click({
    try {
        Start-Process -FilePath 'powershell.exe' -Verb RunAs -WindowStyle Hidden -ArgumentList @(
            '-NoProfile',
            '-ExecutionPolicy', 'Bypass',
            '-STA',
            '-WindowStyle', 'Hidden',
            '-File', "`"$PSCommandPath`""
        )
        $window.Close()
    }
    catch {
        Set-NfStatusMessage -Key 'adminRestartFailed' -Arguments @($_.Exception.Message) -IsError $true
    }
})

$languageBox.Add_SelectionChanged({
    if ($null -ne $languageBox.SelectedItem -and -not [string]::IsNullOrWhiteSpace($languageBox.SelectedItem.Tag)) {
        $script:NfLanguage = [string] $languageBox.SelectedItem.Tag
        Apply-NfLanguage
    }
})

if ($script:NfLanguage -eq 'zh-CN') {
    $languageBox.SelectedItem = $languageChineseItem
}
else {
    $languageBox.SelectedItem = $languageEnglishItem
}

Apply-NfLanguage
if (Show-NfFirstRunWarning) {
    Update-NfStatusGrid
    $window.ShowDialog() | Out-Null
}
