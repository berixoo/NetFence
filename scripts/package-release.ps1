param(
    [string] $Configuration = 'Release',
    [string] $Runtime = 'win-x64',
    [string] $OutputRoot = 'dist'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot "$OutputRoot\NetFence-$Runtime"
$repoRootFull = [System.IO.Path]::GetFullPath($repoRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
$publishDirFull = [System.IO.Path]::GetFullPath($publishDir).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
if (-not $publishDirFull.StartsWith($repoRootFull + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Publish directory must stay inside the repository: $publishDirFull"
}
if ($publishDirFull -eq $repoRootFull) {
    throw 'Publish directory must not be the repository root.'
}

dotnet run --project (Join-Path $repoRoot 'dotnet\NetFence.Core.Tests\NetFence.Core.Tests.csproj') -c $Configuration

if (Test-Path -LiteralPath $publishDirFull) {
    Remove-Item -LiteralPath $publishDirFull -Recurse -Force
}

dotnet publish (Join-Path $repoRoot 'dotnet\NetFence.App\NetFence.App.csproj') `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:NuGetAudit=false `
    -o $publishDirFull

Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination $publishDirFull -Force

$zipPath = Join-Path $repoRoot "$OutputRoot\NetFence-$Runtime.zip"
$zipPathFull = [System.IO.Path]::GetFullPath($zipPath)
if (-not $zipPathFull.StartsWith($repoRootFull + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Zip path must stay inside the repository: $zipPathFull"
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPathFull -Force
}
Compress-Archive -Path (Join-Path $publishDirFull '*') -DestinationPath $zipPathFull

[pscustomobject]@{
    PublishDirectory = $publishDirFull
    ZipPath = $zipPathFull
}
