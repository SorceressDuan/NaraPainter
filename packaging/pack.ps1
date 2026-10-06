# Builds the portable package. Run from the repository root.
#
#   powershell -ExecutionPolicy Bypass -File packaging/pack.ps1
#
# Produces dist/NaraDreamPainter-<version>-win-x64.zip, which unpacks to:
#
#   Run.bat                 double-click this to start the application
#   <the Chinese-named batch file>   makes a shortcut beside itself, for the desktop or Start menu
#   README.md  LICENSE  RUNNING.txt
#   launcher\               a self-contained stub; it starts the application and shows no window of its own
#   app\                    the application and the several hundred runtime files it needs
#
# The split exists because a flat folder of roughly four hundred DLLs gives a first-time user nothing
# obvious to click. The application runs correctly from app\ because it resolves its runtime relative
# to its own executable, which was verified rather than assumed; see docs/BUILD.md.
#
# The launcher is self contained so it starts on a machine that has never had .NET installed. A
# framework-dependent launcher would put only three files in the folder, but it would need the .NET 8
# desktop runtime present, which is a worse trade for a portable build.
#
# Layout contract, asserted at the end of this script:
#   - the archive has no wrapper folder: unpacking drops the payload straight into the folder
#     the user picks
#   - the root holds only the two batch files, the documents, and the two folders
#   - app\NaraDreamPainter.exe and launcher\NaraDreamPainter.exe both exist
param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$SkipZip
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# The sandbox this repository was set up in cannot use the machine-wide dotnet install, so the
# toolchain lives in .tools/. Keep these in one place; everything below depends on them.
$env:DOTNET_ROOT = Join-Path $root ".tools\dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:DOTNET_CLI_HOME = Join-Path $root ".tools\cli-home"
$env:NUGET_PACKAGES = Join-Path $root ".tools\nuget-packages"
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $root ".tools\bundle-extract"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
# NuGet reads %APPDATA%\NuGet\NuGet.Config, which is outside the writable workspace here.
$env:APPDATA = Join-Path $root ".tools\appdata"
# MSBuild drops intermediate XAML and PRI work under %TEMP%\MSBuildTemp, so that has to live in the
# workspace too, for the same reason.
$env:TEMP = Join-Path $root ".tools\temp"
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force -Path $env:TEMP | Out-Null

$version = ([xml](Get-Content "Directory.Build.props")).Project.PropertyGroup.Version
if (-not $version) { $version = "0.1.0" }

$staging = Join-Path $root "dist\publish\$RuntimeIdentifier"
$appDir = Join-Path $staging "app"
$launcherDir = Join-Path $staging "launcher"
$appPublish = Join-Path $root "dist\app-publish"
$launcherPublish = Join-Path $root "dist\launcher-publish"

Write-Host "Publishing $version ($Configuration / $RuntimeIdentifier)" -ForegroundColor Cyan
foreach ($dir in @($staging, $appPublish, $launcherPublish)) {
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
}

& dotnet publish "src\NaraDreamPainter.App\NaraDreamPainter.App.csproj" `
    -c $Configuration `
    -r $RuntimeIdentifier `
    -p:Platform=x64 `
    -m:1 `
    -nodeReuse:false `
    -o $appPublish
if ($LASTEXITCODE -ne 0) { throw "publishing the application failed with $LASTEXITCODE" }

# Do not pass -p:SelfContained or -p:WindowsAppSDKSelfContained here. A global property override
# reaches every project in the graph, including NaraDreamPainter.Compositing, which then stages its own
# copy of the Windows App SDK payload and collides with the app's: NETSDK1152, duplicate
# CoreMessagingXP.dll and friends. The app project already declares both, so they are not needed.

& dotnet publish "launcher\NaraDreamPainter.Launcher.csproj" `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained true `
    -m:1 `
    -nodeReuse:false `
    -o $launcherPublish
if ($LASTEXITCODE -ne 0) { throw "publishing the launcher failed with $LASTEXITCODE" }
if (-not (Test-Path (Join-Path $launcherPublish "NaraDreamPainter.exe"))) {
    throw "the launcher did not produce NaraDreamPainter.exe"
}

Write-Host "Assembling the layout" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $appDir, $launcherDir | Out-Null
Copy-Item (Join-Path $appPublish "*") $appDir -Recurse -Force
Copy-Item (Join-Path $launcherPublish "*") $launcherDir -Recurse -Force

foreach ($exe in @("app\NaraDreamPainter.exe", "launcher\NaraDreamPainter.exe")) {
    if (-not (Test-Path (Join-Path $staging $exe))) { throw "$exe is missing from the staging folder." }
}

# Drop the language folders the app will never load and the debug symbols nobody unpacks. The AI and
# ML payload is left alone on purpose: removing it makes WinUI fail to create its own controls
# (TextBox comes back as a XamlParseException), so it is not dead weight this build can shed.
$keepCultures = @("zh-CN", "zh-TW", "en-us")
foreach ($dir in @($appDir, $launcherDir)) {
    foreach ($folder in Get-ChildItem $dir -Directory) {
        if ($folder.Name -match '^[a-z]{2}(-[A-Za-z]+)?$' -and $folder.Name -notin $keepCultures) {
            Remove-Item $folder.FullName -Recurse -Force
        }
    }
    Get-ChildItem $dir -Recurse -File -Filter *.pdb | Remove-Item -Force
}

# The MIT license and the provenance notice travel with the binaries; both are required by the
# licence terms and by the project's own compliance rules.
Copy-Item "LICENSE" (Join-Path $staging "LICENSE") -Force
Copy-Item "README.md" (Join-Path $staging "README.md") -Force
Copy-Item "packaging\Run.bat" (Join-Path $staging "Run.bat") -Force
$shortcutMakerName = [System.IO.File]::ReadAllText((Join-Path $root "packaging\shortcut-name.txt"), [System.Text.Encoding]::UTF8).Trim()
Copy-Item (Join-Path $root "packaging\$shortcutMakerName") (Join-Path $staging $shortcutMakerName) -Force

# The Windows App SDK payload is several hundred files deep in app\. A tiny plain-text note at the
# root is what a user actually reads, so it ships with the payload.
#
# Neither the text nor the file name may be a literal in this script. Windows PowerShell decodes a
# BOM-less .ps1 as ANSI, so CJK literals here are already mojibake by the time they are used - an
# earlier revision shipped a note whose own name was garbled for exactly that reason. The content
# comes from packaging/RUNNING.txt and is written back out as explicit UTF-8.
$noteText = [System.IO.File]::ReadAllText((Join-Path $root "packaging\RUNNING.txt"), [System.Text.Encoding]::UTF8)
[System.IO.File]::WriteAllText((Join-Path $staging "RUNNING.txt"), $noteText, (New-Object System.Text.UTF8Encoding($false)))

# A shortcut cannot be shipped ready-made: a .lnk stores an absolute target, so one built here would
# point into this build machine's folders. The batch file beside it builds the shortcut on the user's
# machine instead, where the path is final.
$iconPath = Join-Path $root "packaging\NaraDreamPainter.ico"
if (-not (Test-Path $iconPath)) { throw "packaging\NaraDreamPainter.ico is missing." }

if (-not $SkipZip) {
    $zip = Join-Path $root "dist\NaraDreamPainter-$version-$RuntimeIdentifier.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Write-Host "Zipping to $zip" -ForegroundColor Cyan

    # Created through System.IO.Compression rather than Compress-Archive: this writes each file at
    # the archive root with no wrapper folder, which is the contract, and it does not need the
    # directory enumerated twice. Both assemblies have to be loaded explicitly - ZipArchiveMode
    # lives in System.IO.Compression, not in the FileSystem facade.
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $prefix = $staging.TrimEnd('\') + '\'
        foreach ($file in Get-ChildItem -LiteralPath $staging -Recurse -File) {
            $entryName = $file.FullName.Substring($prefix.Length).Replace('\', '/')
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive, $file.FullName, $entryName, [System.IO.Compression.CompressionLevel]::Optimal)
        }
    }
    finally {
        $archive.Dispose()
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $probe = [System.IO.Compression.ZipFile]::OpenRead($zip)
    try {
        $names = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($entry in $probe.Entries) { [void]$names.Add($entry.FullName) }

        $required = @(
            "Run.bat", $shortcutMakerName, "README.md", "LICENSE", "RUNNING.txt",
            "app/NaraDreamPainter.exe", "launcher/NaraDreamPainter.exe"
        )
        $missing = @($required | Where-Object { -not $names.Contains($_) })
        if ($missing.Count -gt 0) { throw "The zip is missing expected entries: $($missing -join ', ')" }

        # Nothing but the documented items may sit at the root, which is the whole point of the split.
        $allowed = @(
            "Run.bat", $shortcutMakerName, "README.md", "LICENSE", "RUNNING.txt", "app", "launcher"
        )
        $topLevel = @($probe.Entries | ForEach-Object { $_.FullName.Split('/')[0] } | Sort-Object -Unique)
        $unexpected = @($topLevel | Where-Object { $_ -notin $allowed })
        if ($unexpected.Count -gt 0) { throw "Unexpected entries at the root of the zip: $($unexpected -join ', ')" }

        $sizeMb = [Math]::Round((Get-Item $zip).Length / 1MB, 2)
        Write-Host ("  layout  : {0} entries, {1} MB, {2} items at the root" -f $probe.Entries.Count, $sizeMb, $topLevel.Count) -ForegroundColor Green
    }
    finally {
        $probe.Dispose()
    }
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  staging : $staging"
Write-Host "  run     : launcher\NaraDreamPainter.exe (or Run.bat)"
if (-not $SkipZip) { Write-Host "  zip     : dist\NaraDreamPainter-$version-$RuntimeIdentifier.zip" }
Write-Host "  installer: run installer\build-msix.ps1 for the signed setup package"
