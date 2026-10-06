# Localization gates for the App project. Kept separate from verify.ps1 so it can also be run on its
# own while editing resources:
#
#   powershell -ExecutionPolicy Bypass -File tools/verify/check-localization.ps1
#
# Exits 0 when every gate passes, 1 otherwise. The script is deliberately pure ASCII: Windows
# PowerShell reads a BOM-less script as ANSI, so an inline Chinese or em-dash literal would already be
# mojibake before any check runs (docs/PORTING.md). Chinese belongs in the .resx files only, and they
# are read back with an explicit UTF-8 decoder below.

param(
    [string]$Root = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
)

$ErrorActionPreference = "Stop"

$appRoot = Join-Path $Root "src\NaraDreamPainter.App"
$neutralPath = Join-Path $appRoot "Resources\Strings.resx"
$chinesePath = Join-Path $appRoot "Resources\Strings.zh-CN.resx"
$stringsPath = Join-Path $appRoot "Strings.cs"
$localizedPath = Join-Path $appRoot "LocalizedStrings.cs"

$problems = New-Object System.Collections.Generic.List[string]

function Read-Text {
    param([string]$Path)
    return [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
}

function Get-ResourceKeys {
    param([string]$Path)
    [xml]$document = Read-Text $Path
    return @($document.root.data | ForEach-Object { $_.name })
}

function Get-TypedKeys {
    param([string]$Path)
    $text = Read-Text $Path
    $found = [regex]::Matches($text, 'public\s+static\s+string\s+(?<name>\w+)\s*=>\s*Localization\.Get\("(?<key>[^"]+)"\)')
    $pairs = @{}
    $order = New-Object System.Collections.Generic.List[string]
    foreach ($match in $found) {
        $name = $match.Groups["name"].Value
        $key = $match.Groups["key"].Value
        $order.Add($name)
        if ($pairs.ContainsKey($name)) {
            $problems.Add("Strings.cs declares $name twice")
            continue
        }
        $pairs[$name] = $key
    }
    return [pscustomobject]@{ Pairs = $pairs; Order = $order }
}

function Get-BoundNames {
    param([string]$Path)
    $text = Read-Text $Path
    $found = [regex]::Matches($text, 'public\s+string\s+(?<name>\w+)\s*=>\s*Strings\.(?<target>\w+);')
    $names = New-Object System.Collections.Generic.List[string]
    foreach ($match in $found) {
        $name = $match.Groups["name"].Value
        $names.Add($name)
        if ($name -ne $match.Groups["target"].Value) {
            $problems.Add("LocalizedStrings.$name delegates to Strings.$($match.Groups["target"].Value)")
        }
    }
    return $names
}

function Get-AppSourceFiles {
    param([string]$Extension)
    $skipped = @("bin", "obj")
    $pending = New-Object System.Collections.Generic.Stack[string]
    $pending.Push($appRoot)
    $files = New-Object System.Collections.Generic.List[string]
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        foreach ($file in [System.IO.Directory]::EnumerateFiles($directory)) {
            if ([System.IO.Path]::GetExtension($file).Equals($Extension, [System.StringComparison]::OrdinalIgnoreCase)) {
                $files.Add($file)
            }
        }
        foreach ($child in [System.IO.Directory]::EnumerateDirectories($directory)) {
            if ($skipped -notcontains [System.IO.Path]::GetFileName($child)) { $pending.Push($child) }
        }
    }
    return $files
}

foreach ($path in @($neutralPath, $chinesePath, $stringsPath, $localizedPath)) {
    if (-not (Test-Path $path)) {
        $problems.Add("missing file: $path")
    }
}
if ($problems.Count -gt 0) {
    foreach ($problem in $problems) { Write-Host "    $problem" }
    Write-Host "LOCALIZATION: FAIL"
    exit 1
}

# Gate 1: the two resource files carry the same keys.
$neutral = Get-ResourceKeys $neutralPath
$chinese = Get-ResourceKeys $chinesePath
foreach ($key in $neutral) {
    if ($chinese -notcontains $key) { $problems.Add("Strings.zh-CN.resx is missing: $key") }
}
foreach ($key in $chinese) {
    if ($neutral -notcontains $key) { $problems.Add("Strings.resx is missing: $key") }
}
foreach ($key in ($neutral | Group-Object | Where-Object { $_.Count -gt 1 })) {
    $problems.Add("Strings.resx repeats the key: $($key.Name)")
}

# Gate 2: Strings.cs covers the resource keys one for one, with the name derived from the key.
$typed = Get-TypedKeys $stringsPath
if ($typed.Pairs.Count -eq 0) { $problems.Add("Strings.cs declares no typed property at all") }
foreach ($name in $typed.Pairs.Keys) {
    $key = $typed.Pairs[$name]
    if ($neutral -notcontains $key) { $problems.Add("Strings.$name asks for '$key', which Strings.resx does not define") }
    if ($chinese -notcontains $key) { $problems.Add("Strings.$name asks for '$key', which Strings.zh-CN.resx does not define") }
    if ($name -ne $key.Replace("_", "")) { $problems.Add("Strings.$name does not match its key '$key' (name must be the key without underscores)") }
}
foreach ($key in $neutral) {
    $expected = $key.Replace("_", "")
    if (-not $typed.Pairs.ContainsKey($expected)) { $problems.Add("'$key' has no Strings.$expected accessor") }
}

# Gate 3: the bindable wrapper mirrors Strings.cs, because XAML binds through it.
$bound = Get-BoundNames $localizedPath
foreach ($name in $typed.Pairs.Keys) {
    if ($bound -notcontains $name) { $problems.Add("LocalizedStrings.$name is missing, so XAML cannot bind that key") }
}
foreach ($name in $bound) {
    if (-not $typed.Pairs.ContainsKey($name)) { $problems.Add("LocalizedStrings.$name has no matching property in Strings.cs") }
}

# Gate 4: nothing in the app sources holds interface text. The .resx files are the one place CJK and
# the one place prose belong, so they are not scanned.
$cjk = [regex]'[\u3000-\u303f\u3400-\u4dbf\u4e00-\u9fff\uf900-\ufaff\uff00-\uffef]'
$scannedSources = 0
foreach ($file in @((Get-AppSourceFiles ".cs") + (Get-AppSourceFiles ".xaml"))) {
    $scannedSources++
    $lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)
    for ($index = 0; $index -lt $lines.Length; $index++) {
        if ($cjk.IsMatch($lines[$index])) {
            $problems.Add("CJK text in source: $($file.Substring($Root.Length + 1)):$($index + 1)")
        }
    }
}

# Gate 5: XAML shows text only through a binding or an assigned property, never through a literal,
# and every bound name exists on the wrapper.
$textAttribute = [regex]'(?:Text|Label|Header|Content|PlaceholderText|Title|ToolTipService\.ToolTip)\s*=\s*"(?<value>[^"]*)"'
$boundReference = [regex]'\bText\.(?<name>[A-Za-z_]\w*)'
$xamlFiles = Get-AppSourceFiles ".xaml"
foreach ($file in $xamlFiles) {
    $relative = $file.Substring($Root.Length + 1)
    $text = Read-Text $file
    foreach ($match in $textAttribute.Matches($text)) {
        $value = $match.Groups["value"].Value
        if ($value.StartsWith("{") -or $value.Length -eq 0) { continue }
        $problems.Add("hard-coded XAML text: ${relative}: $($match.Value)")
    }
    foreach ($match in $boundReference.Matches($text)) {
        $name = $match.Groups["name"].Value
        if ($bound -notcontains $name) { $problems.Add("${relative}: Text.$name has no property on LocalizedStrings") }
    }
}

# Gate 6: every key the code asks for by name exists, since a missing one renders as '!Key!'.
foreach ($file in Get-AppSourceFiles ".cs") {
    $relative = $file.Substring($Root.Length + 1)
    $text = Read-Text $file
    foreach ($match in [regex]::Matches($text, 'Localization\.(?:Get|Format)\("(?<key>[^"]+)"')) {
        $key = $match.Groups["key"].Value
        if ($neutral -notcontains $key) { $problems.Add("${relative}: '$key' is not in Strings.resx") }
        if ($chinese -notcontains $key) { $problems.Add("${relative}: '$key' is not in Strings.zh-CN.resx") }
    }
}

if ($problems.Count -gt 0) {
    foreach ($problem in $problems) { Write-Host "    $problem" }
    Write-Host "LOCALIZATION: FAIL ($($problems.Count) problem(s))"
    exit 1
}

Write-Host "LOCALIZATION: PASS ($($neutral.Count) keys, $($typed.Pairs.Count) properties, $scannedSources sources, $($xamlFiles.Count) xaml)"
exit 0
