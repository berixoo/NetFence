$ErrorActionPreference = 'Stop'

function Get-NetFenceProfileName {
    param(
        [Parameter(Mandatory = $true)] [string] $Path,
        [string] $Name
    )

    $source = $Name
    if ([string]::IsNullOrWhiteSpace($source)) {
        $source = [System.IO.Path]::GetFileNameWithoutExtension($Path)
    }
    if ([string]::IsNullOrWhiteSpace($source)) {
        $source = 'Profile'
    }

    $sanitized = [regex]::Replace($source.Trim(), '[^A-Za-z0-9._-]+', '_').Trim('_')
    if ([string]::IsNullOrWhiteSpace($sanitized)) {
        $sanitized = 'Profile'
    }

    if ($sanitized.Length -gt 40) {
        return $sanitized.Substring(0, 40)
    }

    return $sanitized
}

function Get-NetFenceShortHash {
    param([Parameter(Mandatory = $true)] [string] $Value)

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value.ToLowerInvariant())
    $hash = [System.Security.Cryptography.SHA256]::Create().ComputeHash($bytes)
    return -join ($hash[0..5] | ForEach-Object { $_.ToString('x2') })
}

function Get-NetFenceRuleName {
    param(
        [Parameter(Mandatory = $true)] [string] $ProfileName,
        [Parameter(Mandatory = $true)] [string] $ProgramPath,
        [Parameter(Mandatory = $true)] [ValidateSet('Inbound', 'Outbound')] [string] $Direction
    )

    $hash = Get-NetFenceShortHash -Value "$ProfileName|$Direction|$ProgramPath"
    return "NetFence $ProfileName $Direction $hash"
}

function Get-NetFenceDataDirectory {
    if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        return (Join-Path ([System.IO.Path]::GetTempPath()) 'NetFence')
    }

    return (Join-Path $env:LOCALAPPDATA 'NetFence')
}

function Get-NetFenceLogPath {
    param([string] $StateDirectory = (Get-NetFenceDataDirectory))

    return (Join-Path $StateDirectory 'NetFence.log')
}

function Test-NetFenceManagedRuleGroup {
    param([AllowNull()] [string] $Group)

    return (-not [string]::IsNullOrWhiteSpace($Group)) -and $Group.StartsWith('NetFence:', [System.StringComparison]::OrdinalIgnoreCase)
}

function Write-NetFenceOperationLog {
    param(
        [string] $LogPath = (Get-NetFenceLogPath),
        [Parameter(Mandatory = $true)] [string] $Action,
        [Parameter(Mandatory = $true)] [string] $Message,
        [string[]] $Items = @()
    )

    $directory = Split-Path -Parent $LogPath
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $timestamp = [DateTimeOffset]::Now.ToString('yyyy-MM-dd HH:mm:ss zzz')
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("[$timestamp] $Action - $Message")
    foreach ($item in @($Items)) {
        if (-not [string]::IsNullOrWhiteSpace($item)) {
            $lines.Add("  - $item")
        }
    }

    Add-Content -LiteralPath $LogPath -Encoding UTF8 -Value $lines
}

function Get-NetFenceFirstRunMarkerPath {
    param([string] $StateDirectory = (Get-NetFenceDataDirectory))

    return (Join-Path $StateDirectory 'first-run.ack')
}

function Test-NetFenceFirstRunAcknowledged {
    param([string] $StateDirectory = (Get-NetFenceDataDirectory))

    return (Test-Path -LiteralPath (Get-NetFenceFirstRunMarkerPath -StateDirectory $StateDirectory) -PathType Leaf)
}

function Set-NetFenceFirstRunAcknowledged {
    param([string] $StateDirectory = (Get-NetFenceDataDirectory))

    if (-not (Test-Path -LiteralPath $StateDirectory -PathType Container)) {
        New-Item -ItemType Directory -Path $StateDirectory -Force | Out-Null
    }

    Set-Content -LiteralPath (Get-NetFenceFirstRunMarkerPath -StateDirectory $StateDirectory) -Encoding ASCII -Value 'acknowledged'
}

function Test-NetFenceProtectedSystemPath {
    param([Parameter(Mandatory = $true)] [string] $Path)

    $windowsRoot = [System.IO.Path]::GetFullPath($env:windir).TrimEnd('\') + '\'
    $candidate = [System.IO.Path]::GetFullPath($Path)
    return $candidate.StartsWith($windowsRoot, [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-NetFenceAllowedTargetPath {
    param([Parameter(Mandatory = $true)] [string] $Path)

    if (Test-NetFenceProtectedSystemPath -Path $Path) {
        throw "Target '$Path' is under the Windows system directory. NetFence will not block system-required executables."
    }
}

function Test-NetFencePathUnderDirectory {
    param(
        [Parameter(Mandatory = $true)] [string] $Path,
        [Parameter(Mandatory = $true)] [string] $Directory
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullDirectory = [System.IO.Path]::GetFullPath($Directory).TrimEnd('\') + '\'
    return $fullPath.StartsWith($fullDirectory, [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-NetFenceExecutableTargets {
    param([Parameter(Mandatory = $true)] [string] $Path)

    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction Stop
    $item = Get-Item -LiteralPath $resolved.ProviderPath -Force
    Assert-NetFenceAllowedTargetPath -Path $item.FullName

    if ($item.PSIsContainer) {
        return @(Get-ChildItem -LiteralPath $item.FullName -Recurse -File -Force |
            Where-Object { $_.Extension -ieq '.exe' } |
            Where-Object { -not (Test-NetFenceProtectedSystemPath -Path $_.FullName) } |
            Sort-Object -Property FullName |
            ForEach-Object { $_.FullName })
    }

    if ($item.Extension -ine '.exe') {
        throw "Target '$($item.FullName)' is not an executable. Choose an .exe file or a folder containing .exe files."
    }

    return @($item.FullName)
}

function Get-NetFenceProcessRows {
    Get-CimInstance Win32_Process |
        Select-Object ProcessId, ParentProcessId, Name, ExecutablePath, CommandLine
}

function Get-NetFenceLinkedProcessPaths {
    param(
        [Parameter(Mandatory = $true)] [int] $RootProcessId,
        [object[]] $ProcessRows = $null
    )

    if ($null -eq $ProcessRows) {
        $ProcessRows = @(Get-NetFenceProcessRows)
    }

    $byParent = @{}
    foreach ($row in $ProcessRows) {
        if (-not $byParent.ContainsKey($row.ParentProcessId)) {
            $byParent[$row.ParentProcessId] = New-Object System.Collections.Generic.List[object]
        }
        $byParent[$row.ParentProcessId].Add($row)
    }

    $seen = New-Object 'System.Collections.Generic.HashSet[int]'
    $queue = New-Object System.Collections.Generic.Queue[int]
    $queue.Enqueue($RootProcessId)
    [void] $seen.Add($RootProcessId)

    $paths = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)

    while ($queue.Count -gt 0) {
        $parentId = $queue.Dequeue()
        if (-not $byParent.ContainsKey($parentId)) {
            continue
        }

        foreach ($child in $byParent[$parentId]) {
            if ($seen.Add([int] $child.ProcessId)) {
                $queue.Enqueue([int] $child.ProcessId)
                if (-not [string]::IsNullOrWhiteSpace($child.ExecutablePath)) {
                    [void] $paths.Add($child.ExecutablePath)
                }
            }
        }
    }

    return @($paths | Sort-Object)
}

function Get-NetFenceLinkedTargetsForPath {
    param([Parameter(Mandatory = $true)] [string] $Path)

    $resolved = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).ProviderPath
    $item = Get-Item -LiteralPath $resolved -Force
    $rows = @(Get-NetFenceProcessRows)
    $matchedRoots = @()

    foreach ($row in $rows) {
        if ([string]::IsNullOrWhiteSpace($row.ExecutablePath)) {
            continue
        }

        if ($item.PSIsContainer) {
            $prefix = $item.FullName.TrimEnd('\') + '\'
            if ($row.ExecutablePath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                $matchedRoots += $row
            }
        }
        elseif ($row.ExecutablePath -ieq $item.FullName) {
            $matchedRoots += $row
        }
    }

    $linked = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($root in $matchedRoots) {
        foreach ($path in @(Get-NetFenceLinkedProcessPaths -RootProcessId $root.ProcessId -ProcessRows $rows)) {
            if ((Test-Path -LiteralPath $path -PathType Leaf) -and
                ([System.IO.Path]::GetExtension($path) -ieq '.exe') -and
                (-not (Test-NetFenceProtectedSystemPath -Path $path))) {
                [void] $linked.Add($path)
            }
        }
    }

    return @($linked | Sort-Object)
}

function Get-NetFenceNetworkProcessIds {
    $ids = New-Object 'System.Collections.Generic.HashSet[int]'

    try {
        foreach ($connection in @(Get-NetTCPConnection -ErrorAction SilentlyContinue)) {
            if ($connection.OwningProcess -gt 0) {
                [void] $ids.Add([int] $connection.OwningProcess)
            }
        }
    }
    catch {
        return @()
    }

    return @($ids | Sort-Object)
}

function Add-NetFenceRelatedCandidate {
    param(
        [Parameter(Mandatory = $true)] [hashtable] $CandidateMap,
        [Parameter(Mandatory = $true)] [string] $Program,
        [Parameter(Mandatory = $true)] [string] $Reason,
        [int] $ProcessId = 0,
        [string] $ProcessName = ''
    )

    if ([string]::IsNullOrWhiteSpace($Program)) {
        return
    }
    if ([System.IO.Path]::GetExtension($Program) -ine '.exe') {
        return
    }
    if (Test-NetFenceProtectedSystemPath -Path $Program) {
        return
    }
    if (-not (Test-Path -LiteralPath $Program -PathType Leaf)) {
        return
    }

    $key = [System.IO.Path]::GetFullPath($Program).ToLowerInvariant()
    if (-not $CandidateMap.ContainsKey($key)) {
        $CandidateMap[$key] = [pscustomobject]@{
            Selected = $true
            Program = [System.IO.Path]::GetFullPath($Program)
            Reason = $Reason
            ProcessId = $ProcessId
            ProcessName = $ProcessName
        }
        return
    }

    $candidate = $CandidateMap[$key]
    $reasons = @($candidate.Reason -split '; ' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($reasons -notcontains $Reason) {
        $candidate.Reason = (@($reasons + $Reason) -join '; ')
    }
    if ($candidate.ProcessId -eq 0 -and $ProcessId -gt 0) {
        $candidate.ProcessId = $ProcessId
    }
    if ([string]::IsNullOrWhiteSpace($candidate.ProcessName) -and -not [string]::IsNullOrWhiteSpace($ProcessName)) {
        $candidate.ProcessName = $ProcessName
    }
}

function Get-NetFenceRelatedProcessCandidates {
    param(
        [Parameter(Mandatory = $true)] [string] $Path,
        [object[]] $ProcessRows = $null,
        [int[]] $NetworkProcessIds = $null
    )

    $resolved = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).ProviderPath
    $item = Get-Item -LiteralPath $resolved -Force
    Assert-NetFenceAllowedTargetPath -Path $item.FullName

    if ($null -eq $ProcessRows) {
        $ProcessRows = @(Get-NetFenceProcessRows)
    }
    if ($null -eq $NetworkProcessIds) {
        $NetworkProcessIds = @(Get-NetFenceNetworkProcessIds)
    }

    $networkSet = New-Object 'System.Collections.Generic.HashSet[int]'
    foreach ($id in @($NetworkProcessIds)) {
        if ($id -gt 0) {
            [void] $networkSet.Add([int] $id)
        }
    }

    $installDirectory = if ($item.PSIsContainer) { $item.FullName } else { $item.DirectoryName }
    $targetExecutableSet = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($target in @(Get-NetFenceExecutableTargets -Path $item.FullName)) {
        [void] $targetExecutableSet.Add([System.IO.Path]::GetFullPath($target))
    }

    $candidates = @{}
    foreach ($target in @($targetExecutableSet | Sort-Object)) {
        Add-NetFenceRelatedCandidate -CandidateMap $candidates -Program $target -Reason 'selected target'
    }

    $rootProcessIds = New-Object 'System.Collections.Generic.HashSet[int]'

    foreach ($row in @($ProcessRows)) {
        if ([string]::IsNullOrWhiteSpace($row.ExecutablePath)) {
            continue
        }
        if (Test-NetFenceProtectedSystemPath -Path $row.ExecutablePath) {
            continue
        }

        $isTargetProcess = $targetExecutableSet.Contains([System.IO.Path]::GetFullPath($row.ExecutablePath))
        if ($isTargetProcess) {
            [void] $rootProcessIds.Add([int] $row.ProcessId)
            Add-NetFenceRelatedCandidate -CandidateMap $candidates -Program $row.ExecutablePath -Reason 'running target process' -ProcessId $row.ProcessId -ProcessName $row.Name
        }

        if (Test-NetFencePathUnderDirectory -Path $row.ExecutablePath -Directory $installDirectory) {
            Add-NetFenceRelatedCandidate -CandidateMap $candidates -Program $row.ExecutablePath -Reason 'same install folder' -ProcessId $row.ProcessId -ProcessName $row.Name
        }

        if (-not [string]::IsNullOrWhiteSpace($row.CommandLine)) {
            $referencesSelectedPath = $row.CommandLine.IndexOf($item.FullName, [System.StringComparison]::OrdinalIgnoreCase) -ge 0
            $referencesInstallDirectory = $row.CommandLine.IndexOf($installDirectory, [System.StringComparison]::OrdinalIgnoreCase) -ge 0
            if ($referencesSelectedPath -or $referencesInstallDirectory) {
                Add-NetFenceRelatedCandidate -CandidateMap $candidates -Program $row.ExecutablePath -Reason 'command line references target' -ProcessId $row.ProcessId -ProcessName $row.Name
            }
        }
    }

    foreach ($rootId in @($rootProcessIds)) {
        foreach ($childPath in @(Get-NetFenceLinkedProcessPaths -RootProcessId $rootId -ProcessRows $ProcessRows)) {
            $matchingRow = $ProcessRows | Where-Object { $_.ExecutablePath -ieq $childPath } | Select-Object -First 1
            Add-NetFenceRelatedCandidate -CandidateMap $candidates -Program $childPath -Reason 'child process' -ProcessId $matchingRow.ProcessId -ProcessName $matchingRow.Name
        }
    }

    foreach ($row in @($ProcessRows)) {
        if ($networkSet.Contains([int] $row.ProcessId) -and -not [string]::IsNullOrWhiteSpace($row.ExecutablePath)) {
            $key = [System.IO.Path]::GetFullPath($row.ExecutablePath).ToLowerInvariant()
            if ($candidates.ContainsKey($key)) {
                Add-NetFenceRelatedCandidate -CandidateMap $candidates -Program $row.ExecutablePath -Reason 'active network connection' -ProcessId $row.ProcessId -ProcessName $row.Name
            }
        }
    }

    return @($candidates.Values | Sort-Object Program)
}

function Get-NetFencePlannedBlockTargets {
    param(
        [Parameter(Mandatory = $true)] [string] $Path,
        [string[]] $AdditionalTargets = @(),
        [switch] $IncludeLinkedProcesses
    )

    $targets = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($target in @(Get-NetFenceExecutableTargets -Path $Path)) {
        [void] $targets.Add($target)
    }
    if ($IncludeLinkedProcesses) {
        foreach ($target in @(Get-NetFenceLinkedTargetsForPath -Path $Path)) {
            [void] $targets.Add($target)
        }
    }
    foreach ($target in @($AdditionalTargets)) {
        if ([string]::IsNullOrWhiteSpace($target)) {
            continue
        }
        $resolved = (Resolve-Path -LiteralPath $target -ErrorAction Stop).ProviderPath
        $item = Get-Item -LiteralPath $resolved -Force
        if ($item.PSIsContainer -or $item.Extension -ine '.exe') {
            throw "Additional target '$($item.FullName)' is not an executable file."
        }
        if (Test-NetFenceProtectedSystemPath -Path $item.FullName) {
            continue
        }
        [void] $targets.Add($item.FullName)
    }

    return @($targets | Sort-Object)
}

function Test-NetFenceIsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-NetFenceAdministrator {
    if (-not (Test-NetFenceIsAdministrator)) {
        throw 'Administrator privileges are required to change Windows Defender Firewall rules.'
    }
}

function New-NetFenceRuleSpec {
    param(
        [Parameter(Mandatory = $true)] [string] $ProfileName,
        [Parameter(Mandatory = $true)] [string] $ProgramPath,
        [Parameter(Mandatory = $true)] [ValidateSet('Inbound', 'Outbound')] [string] $Direction
    )

    [pscustomobject]@{
        DisplayName = Get-NetFenceRuleName -ProfileName $ProfileName -ProgramPath $ProgramPath -Direction $Direction
        ProfileName = $ProfileName
        ProgramPath = $ProgramPath
        Direction = $Direction
        Group = "NetFence:$ProfileName"
    }
}

function Enable-NetFenceBlock {
    param(
        [Parameter(Mandatory = $true)] [string] $Path,
        [string] $Name,
        [switch] $IncludeLinkedProcesses,
        [string[]] $AdditionalTargets = @(),
        [string] $LogPath = (Get-NetFenceLogPath)
    )

    Assert-NetFenceAdministrator

    $profileName = Get-NetFenceProfileName -Path $Path -Name $Name
    $targets = @(Get-NetFencePlannedBlockTargets -Path $Path -IncludeLinkedProcesses:$IncludeLinkedProcesses -AdditionalTargets $AdditionalTargets)

    foreach ($program in $targets) {
        foreach ($direction in @('Outbound', 'Inbound')) {
            $spec = New-NetFenceRuleSpec -ProfileName $profileName -ProgramPath $program -Direction $direction
            $existing = Get-NetFirewallRule -DisplayName $spec.DisplayName -ErrorAction SilentlyContinue
            if ($existing) {
                Set-NetFirewallRule -DisplayName $spec.DisplayName -Enabled True -Action Block -Direction $direction -Profile Any | Out-Null
            }
            else {
                New-NetFirewallRule `
                    -DisplayName $spec.DisplayName `
                    -Group $spec.Group `
                    -Direction $direction `
                    -Action Block `
                    -Program $program `
                    -Profile Any `
                    -Enabled True `
                    -Description "Managed by NetFence for profile '$profileName'. Remove with NetFence to restore networking." |
                    Out-Null
            }
        }
    }

    $result = [pscustomobject]@{
        ProfileName = $profileName
        TargetCount = $targets.Count
        Targets = @($targets)
    }

    Write-NetFenceOperationLog -LogPath $LogPath -Action 'Block' -Message "Blocked profile '$profileName' for $($targets.Count) executable file(s)." -Items $targets
    return $result
}

function Disable-NetFenceBlock {
    param(
        [Parameter(Mandatory = $true)] [string] $Path,
        [string] $Name,
        [string] $LogPath = (Get-NetFenceLogPath)
    )

    Assert-NetFenceAdministrator

    $profileName = Get-NetFenceProfileName -Path $Path -Name $Name
    $rules = @(Get-NetFirewallRule -Group "NetFence:$profileName" -ErrorAction SilentlyContinue)

    if ($rules.Count -gt 0) {
        $rules | Remove-NetFirewallRule
    }

    $result = [pscustomobject]@{
        ProfileName = $profileName
        RemovedRuleCount = $rules.Count
    }

    Write-NetFenceOperationLog -LogPath $LogPath -Action 'Unblock' -Message "Removed $($rules.Count) rule(s) for profile '$profileName'." -Items @()
    return $result
}

function Disable-NetFenceAllBlocks {
    param([string] $LogPath = (Get-NetFenceLogPath))

    Assert-NetFenceAdministrator

    $rules = @(Get-NetFirewallRule -ErrorAction SilentlyContinue |
        Where-Object { Test-NetFenceManagedRuleGroup -Group $_.Group })

    if ($rules.Count -gt 0) {
        $rules | Remove-NetFirewallRule
    }

    Write-NetFenceOperationLog -LogPath $LogPath -Action 'UnblockAll' -Message "Removed $($rules.Count) NetFence rule(s)." -Items @($rules | ForEach-Object { $_.DisplayName })
    return [pscustomobject]@{
        RemovedRuleCount = $rules.Count
    }
}

function Get-NetFenceStatus {
    $rules = @(Get-NetFirewallRule -ErrorAction SilentlyContinue |
        Where-Object { Test-NetFenceManagedRuleGroup -Group $_.Group } |
        Sort-Object Group, DisplayName)

    foreach ($rule in $rules) {
        $appFilter = $rule | Get-NetFirewallApplicationFilter -ErrorAction SilentlyContinue
        [pscustomobject]@{
            ProfileName = $rule.Group -replace '^NetFence:', ''
            DisplayName = $rule.DisplayName
            Direction = $rule.Direction
            Enabled = $rule.Enabled
            Action = $rule.Action
            Program = $appFilter.Program
        }
    }
}

function ConvertTo-NetFenceExportRows {
    param(
        [object[]] $Rules = @(),
        [object[]] $Candidates = @()
    )

    foreach ($rule in @($Rules)) {
        [pscustomobject]@{
            Type = 'FirewallRule'
            Selected = ''
            ProfileName = $rule.ProfileName
            Direction = $rule.Direction
            Enabled = $rule.Enabled
            Action = $rule.Action
            Program = $rule.Program
            Reason = ''
            ProcessId = ''
            ProcessName = ''
            DisplayName = $rule.DisplayName
        }
    }

    foreach ($candidate in @($Candidates)) {
        [pscustomobject]@{
            Type = 'Candidate'
            Selected = $candidate.Selected
            ProfileName = ''
            Direction = ''
            Enabled = ''
            Action = ''
            Program = $candidate.Program
            Reason = $candidate.Reason
            ProcessId = $candidate.ProcessId
            ProcessName = $candidate.ProcessName
            DisplayName = ''
        }
    }
}

function Export-NetFenceSnapshot {
    param(
        [Parameter(Mandatory = $true)] [string] $Path,
        [object[]] $Rules = @(),
        [object[]] $Candidates = @()
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory) -and -not (Test-Path -LiteralPath $directory -PathType Container)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $rows = @(ConvertTo-NetFenceExportRows -Rules $Rules -Candidates $Candidates)
    $rows | Export-Csv -LiteralPath $Path -Encoding UTF8 -NoTypeInformation
    return [pscustomobject]@{
        Path = $Path
        RowCount = $rows.Count
    }
}
