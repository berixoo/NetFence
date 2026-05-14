$ErrorActionPreference = 'Stop'

function Get-NetFenceDefaultLanguage {
    param([string] $CultureName = [Globalization.CultureInfo]::CurrentUICulture.Name)

    if ($CultureName -like 'zh*') {
        return 'zh-CN'
    }

    return 'en-US'
}

function Import-NetFenceTranslations {
    param([Parameter(Mandatory = $true)] [string] $RootPath)

    $translations = @{}
    foreach ($language in @('en-US', 'zh-CN')) {
        $path = Join-Path $RootPath "i18n\$language.json"
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Translation file not found: $path"
        }

        $json = Get-Content -LiteralPath $path -Encoding UTF8 -Raw
        $object = $json | ConvertFrom-Json
        $table = @{}
        foreach ($property in $object.PSObject.Properties) {
            $table[$property.Name] = [string] $property.Value
        }
        $translations[$language] = $table
    }

    return $translations
}

function Get-NetFenceText {
    param(
        [Parameter(Mandatory = $true)] [hashtable] $Translations,
        [Parameter(Mandatory = $true)] [string] $Language,
        [Parameter(Mandatory = $true)] [string] $Key
    )

    if ($Translations.ContainsKey($Language) -and $Translations[$Language].ContainsKey($Key)) {
        return $Translations[$Language][$Key]
    }

    if ($Translations.ContainsKey('en-US') -and $Translations['en-US'].ContainsKey($Key)) {
        return $Translations['en-US'][$Key]
    }

    return $Key
}

function Format-NetFenceText {
    param(
        [Parameter(Mandatory = $true)] [hashtable] $Translations,
        [Parameter(Mandatory = $true)] [string] $Language,
        [Parameter(Mandatory = $true)] [string] $Key,
        [object[]] $Arguments = @()
    )

    $template = Get-NetFenceText -Translations $Translations -Language $Language -Key $Key
    return [string]::Format($template, $Arguments)
}
