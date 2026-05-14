$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $repoRoot 'src\NetFence.Core.ps1')
. (Join-Path $repoRoot 'src\NetFence.I18n.ps1')

$script:Failures = 0

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)] $Actual,
        [Parameter(Mandatory = $true)] $Expected,
        [Parameter(Mandatory = $true)] [string] $Message
    )

    if ($Actual -ne $Expected) {
        $script:Failures++
        Write-Host "FAIL: $Message" -ForegroundColor Red
        Write-Host "  Expected: $Expected"
        Write-Host "  Actual:   $Actual"
    }
}

function Assert-True {
    param(
        [Parameter(Mandatory = $true)] [bool] $Condition,
        [Parameter(Mandatory = $true)] [string] $Message
    )

    if (-not $Condition) {
        $script:Failures++
        Write-Host "FAIL: $Message" -ForegroundColor Red
    }
}

function Invoke-Test {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [scriptblock] $Body
    )

    try {
        & $Body
        Write-Host "PASS: $Name" -ForegroundColor Green
    }
    catch {
        $script:Failures++
        Write-Host "FAIL: $Name" -ForegroundColor Red
        Write-Host "  $($_.Exception.Message)"
    }
}

Invoke-Test 'profile names are stable and filesystem-safe' {
    $name = Get-NetFenceProfileName -Path 'C:\Program Files\Example App\app.exe' -Name 'Example App!'

    Assert-Equal $name 'Example_App' 'explicit profile name should be sanitized'
}

Invoke-Test 'folder targets include recursive exe files only' {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("netfence-test-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $root 'sub') | Out-Null
    New-Item -ItemType File -Path (Join-Path $root 'app.exe') | Out-Null
    New-Item -ItemType File -Path (Join-Path $root 'readme.txt') | Out-Null
    New-Item -ItemType File -Path (Join-Path $root 'sub\helper.EXE') | Out-Null

    try {
        $targets = @(Get-NetFenceExecutableTargets -Path $root)

        Assert-Equal $targets.Count 2 'only exe files should be returned'
        Assert-True ($targets -contains (Join-Path $root 'app.exe')) 'root exe should be included'
        Assert-True ($targets -contains (Join-Path $root 'sub\helper.EXE')) 'nested exe should be included'
    }
    finally {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}

Invoke-Test 'file target returns the executable itself' {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("netfence-test-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root | Out-Null
    $exe = Join-Path $root 'single.exe'
    New-Item -ItemType File -Path $exe | Out-Null

    try {
        $targets = @(Get-NetFenceExecutableTargets -Path $exe)

        Assert-Equal $targets.Count 1 'single exe path should produce one target'
        Assert-Equal $targets[0] $exe 'single exe target should be returned unchanged'
    }
    finally {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}

Invoke-Test 'non-executable file targets are rejected' {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("netfence-test-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root | Out-Null
    $txt = Join-Path $root 'notes.txt'
    New-Item -ItemType File -Path $txt | Out-Null

    try {
        $failed = $false
        try {
            Get-NetFenceExecutableTargets -Path $txt | Out-Null
        }
        catch {
            $failed = $_.Exception.Message -like '*not an executable*'
        }

        Assert-True $failed 'non-exe file should throw a clear error'
    }
    finally {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}

Invoke-Test 'windows system paths are protected from direct blocking' {
    $systemExe = Join-Path $env:windir 'System32\svchost.exe'
    $normalExe = 'C:\Program Files\Example\app.exe'

    Assert-True (Test-NetFenceProtectedSystemPath -Path $systemExe) 'Windows system executable should be protected'
    Assert-True (-not (Test-NetFenceProtectedSystemPath -Path $normalExe)) 'normal Program Files executable should not be treated as protected system path'
}

Invoke-Test 'firewall rule names are stable and direction-specific' {
    $outRule = Get-NetFenceRuleName -ProfileName 'Example_App' -ProgramPath 'C:\Program Files\Example App\app.exe' -Direction 'Outbound'
    $inRule = Get-NetFenceRuleName -ProfileName 'Example_App' -ProgramPath 'C:\Program Files\Example App\app.exe' -Direction 'Inbound'

    Assert-True ($outRule -like 'NetFence Example_App Outbound *') 'outbound rule should include profile and direction'
    Assert-True ($inRule -like 'NetFence Example_App Inbound *') 'inbound rule should include profile and direction'
    Assert-True ($outRule -ne $inRule) 'different directions should create different rule names'
    Assert-True ($outRule.Length -le 90) 'rule name should stay short enough for firewall UI'
}

Invoke-Test 'linked process discovery returns transitive child executable paths' {
    $rows = @(
        [pscustomobject]@{ ProcessId = 10; ParentProcessId = 1; ExecutablePath = 'C:\apps\root.exe' },
        [pscustomobject]@{ ProcessId = 11; ParentProcessId = 10; ExecutablePath = 'C:\apps\child.exe' },
        [pscustomobject]@{ ProcessId = 12; ParentProcessId = 11; ExecutablePath = 'C:\apps\grandchild.exe' },
        [pscustomobject]@{ ProcessId = 13; ParentProcessId = 10; ExecutablePath = $null },
        [pscustomobject]@{ ProcessId = 14; ParentProcessId = 99; ExecutablePath = 'C:\apps\unrelated.exe' }
    )

    $paths = @(Get-NetFenceLinkedProcessPaths -RootProcessId 10 -ProcessRows $rows)

    Assert-Equal $paths.Count 2 'only descendants with executable paths should be returned'
    Assert-True ($paths -contains 'C:\apps\child.exe') 'direct child should be included'
    Assert-True ($paths -contains 'C:\apps\grandchild.exe') 'transitive child should be included'
    Assert-True (-not ($paths -contains 'C:\apps\unrelated.exe')) 'unrelated process should not be included'
}

Invoke-Test 'related process candidates include child same-folder command-line and network reasons' {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("netfence-test-" + [Guid]::NewGuid().ToString('N'))
    $appDir = Join-Path $root 'Vendor\App'
    $otherDir = Join-Path $root 'Vendor\Shared'
    New-Item -ItemType Directory -Path $appDir -Force | Out-Null
    New-Item -ItemType Directory -Path $otherDir -Force | Out-Null
    $mainExe = Join-Path $appDir 'main.exe'
    $childExe = Join-Path $otherDir 'child.exe'
    $helperExe = Join-Path $appDir 'helper.exe'
    $launcherExe = Join-Path $otherDir 'launcher.exe'
    $networkExe = Join-Path $appDir 'nethelper.exe'
    $systemExe = Join-Path $env:windir 'System32\svchost.exe'
    foreach ($path in @($mainExe, $childExe, $helperExe, $launcherExe, $networkExe)) {
        New-Item -ItemType File -Path $path -Force | Out-Null
    }

    try {
        $rows = @(
            [pscustomobject]@{ ProcessId = 100; ParentProcessId = 1; Name = 'main'; ExecutablePath = $mainExe; CommandLine = "`"$mainExe`"" },
            [pscustomobject]@{ ProcessId = 101; ParentProcessId = 100; Name = 'child'; ExecutablePath = $childExe; CommandLine = "`"$childExe`"" },
            [pscustomobject]@{ ProcessId = 102; ParentProcessId = 1; Name = 'helper'; ExecutablePath = $helperExe; CommandLine = "`"$helperExe`"" },
            [pscustomobject]@{ ProcessId = 103; ParentProcessId = 1; Name = 'launcher'; ExecutablePath = $launcherExe; CommandLine = "`"$launcherExe`" --app-dir `"$appDir`"" },
            [pscustomobject]@{ ProcessId = 104; ParentProcessId = 1; Name = 'nethelper'; ExecutablePath = $networkExe; CommandLine = "`"$networkExe`"" },
            [pscustomobject]@{ ProcessId = 105; ParentProcessId = 100; Name = 'system'; ExecutablePath = $systemExe; CommandLine = "`"$systemExe`"" }
        )

        $candidates = @(Get-NetFenceRelatedProcessCandidates -Path $mainExe -ProcessRows $rows -NetworkProcessIds @(104))
        $programs = @($candidates | ForEach-Object { $_.Program })

        Assert-True ($programs -contains $mainExe) 'selected target should be included'
        Assert-True ($programs -contains $childExe) 'child process outside install folder should be included'
        Assert-True ($programs -contains $helperExe) 'same install folder process should be included'
        Assert-True ($programs -contains $launcherExe) 'command line reference should be included'
        Assert-True ($programs -contains $networkExe) 'network process in install folder should be included'
        Assert-True (-not ($programs -contains $systemExe)) 'system child process should be excluded'
        Assert-Equal (@($candidates | Where-Object { $_.Program -eq $networkExe }).Count) 1 'duplicate related reasons should still produce one candidate'

        $networkCandidate = $candidates | Where-Object { $_.Program -eq $networkExe } | Select-Object -First 1
        Assert-True ($networkCandidate.Reason -like '*same install folder*') 'network candidate should preserve same-folder reason'
        Assert-True ($networkCandidate.Reason -like '*active network connection*') 'network candidate should include network reason'
        Assert-True ([bool] $networkCandidate.Selected) 'candidate should be selected by default'
    }
    finally {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}

Invoke-Test 'custom block targets are merged into firewall target planning' {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("netfence-test-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root | Out-Null
    $mainExe = Join-Path $root 'main.exe'
    $helperExe = Join-Path $root 'helper.exe'
    New-Item -ItemType File -Path $mainExe | Out-Null
    New-Item -ItemType File -Path $helperExe | Out-Null

    try {
        $targets = @(Get-NetFencePlannedBlockTargets -Path $mainExe -AdditionalTargets @($helperExe, $mainExe))

        Assert-Equal $targets.Count 2 'additional targets should be deduplicated with the main target'
        Assert-True ($targets -contains $mainExe) 'main target should be planned'
        Assert-True ($targets -contains $helperExe) 'additional target should be planned'
    }
    finally {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}

Invoke-Test 'managed firewall rule group detection is scoped to NetFence groups' {
    Assert-True (Test-NetFenceManagedRuleGroup -Group 'NetFence:Example') 'NetFence group should be managed'
    Assert-True (-not (Test-NetFenceManagedRuleGroup -Group 'Other:Example')) 'non-NetFence group should not be managed'
    Assert-True (-not (Test-NetFenceManagedRuleGroup -Group $null)) 'null group should not be managed'
}

Invoke-Test 'export snapshot writes firewall rules and candidates to csv' {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("netfence-test-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root | Out-Null
    $exportPath = Join-Path $root 'snapshot.csv'

    try {
        $rules = @(
            [pscustomobject]@{
                ProfileName = 'Demo'
                DisplayName = 'NetFence Demo Outbound abc'
                Direction = 'Outbound'
                Enabled = 'True'
                Action = 'Block'
                Program = 'C:\Apps\Demo\demo.exe'
            }
        )
        $candidates = @(
            [pscustomobject]@{
                Selected = $true
                Program = 'C:\Apps\Demo\helper.exe'
                Reason = 'same install folder'
                ProcessId = 123
                ProcessName = 'helper'
            }
        )

        Export-NetFenceSnapshot -Path $exportPath -Rules $rules -Candidates $candidates | Out-Null
        $rows = @(Import-Csv -LiteralPath $exportPath)

        Assert-Equal $rows.Count 2 'snapshot should include one rule and one candidate'
        Assert-True (@($rows | Where-Object { $_.Type -eq 'FirewallRule' -and $_.Program -eq 'C:\Apps\Demo\demo.exe' }).Count -eq 1) 'firewall rule row should be exported'
        Assert-True (@($rows | Where-Object { $_.Type -eq 'Candidate' -and $_.Reason -eq 'same install folder' }).Count -eq 1) 'candidate row should be exported'
    }
    finally {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}

Invoke-Test 'operation log appends timestamped action and items' {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("netfence-test-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root | Out-Null
    $logPath = Join-Path $root 'NetFence.log'

    try {
        Write-NetFenceOperationLog -LogPath $logPath -Action 'Block' -Message 'Blocked Demo' -Items @('C:\Apps\Demo\demo.exe')
        $content = Get-Content -LiteralPath $logPath -Encoding UTF8 -Raw

        Assert-True ($content -like '*Block*') 'log should contain action'
        Assert-True ($content -like '*Blocked Demo*') 'log should contain message'
        Assert-True ($content -like '*C:\Apps\Demo\demo.exe*') 'log should contain affected item'
    }
    finally {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}

Invoke-Test 'first run acknowledgement marker can be stored in custom state directory' {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("netfence-test-" + [Guid]::NewGuid().ToString('N'))

    try {
        Assert-True (-not (Test-NetFenceFirstRunAcknowledged -StateDirectory $root)) 'new state directory should not be acknowledged'
        Set-NetFenceFirstRunAcknowledged -StateDirectory $root
        Assert-True (Test-NetFenceFirstRunAcknowledged -StateDirectory $root) 'acknowledgement should be persisted'
    }
    finally {
        if (Test-Path -LiteralPath $root) {
            Remove-Item -LiteralPath $root -Recurse -Force
        }
    }
}

Invoke-Test 'translations load Chinese and English UI text' {
    $translations = Import-NetFenceTranslations -RootPath $repoRoot
    $chineseBlockNetwork = -join @([char]0x7981, [char]0x6B62, [char]0x8054, [char]0x7F51)

    Assert-Equal (Get-NetFenceText -Translations $translations -Language 'en-US' -Key 'blockNetwork') 'Block Network' 'English block button text should load'
    Assert-Equal (Get-NetFenceText -Translations $translations -Language 'zh-CN' -Key 'blockNetwork') $chineseBlockNetwork 'Chinese block button text should load'
}

Invoke-Test 'translation lookup falls back to English and key name' {
    $translations = Import-NetFenceTranslations -RootPath $repoRoot

    Assert-Equal (Get-NetFenceText -Translations $translations -Language 'fr-FR' -Key 'refresh') 'Refresh' 'unsupported language should fall back to English'
    Assert-Equal (Get-NetFenceText -Translations $translations -Language 'zh-CN' -Key 'missing.translation.key') 'missing.translation.key' 'missing key should return key name'
}

Invoke-Test 'translations include async operation status text' {
    $translations = Import-NetFenceTranslations -RootPath $repoRoot

    foreach ($language in @('en-US', 'zh-CN')) {
        foreach ($key in @('scanRunning', 'blockRunning', 'unblockRunning', 'refreshRunning')) {
            $value = Get-NetFenceText -Translations $translations -Language $language -Key $key
            Assert-True ($value -ne $key) "$language should include $key"
            Assert-True (-not [string]::IsNullOrWhiteSpace($value)) "$language $key should not be empty"
        }
    }
}

Invoke-Test 'default language maps Chinese cultures to zh-CN' {
    Assert-Equal (Get-NetFenceDefaultLanguage -CultureName 'zh-CN') 'zh-CN' 'zh-CN culture should select Chinese'
    Assert-Equal (Get-NetFenceDefaultLanguage -CultureName 'zh-Hans-CN') 'zh-CN' 'zh-Hans culture should select Chinese'
    Assert-Equal (Get-NetFenceDefaultLanguage -CultureName 'en-US') 'en-US' 'non-Chinese culture should select English'
}

if ($script:Failures -gt 0) {
    throw "$script:Failures test assertion(s) failed"
}

Write-Host 'All NetFence tests passed.' -ForegroundColor Green
