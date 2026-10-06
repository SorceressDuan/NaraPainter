# Builds the portable package. Run from the repository root.
#
#   powershell -ExecutionPolicy Bypass -File packaging/pack.ps1
#
# Produces:
#   dist/Compositor-<version>-win-x64.zip   self-contained, unpack and run
# The signed installer is a separate artifact: see installer/build-msix.ps1.
#
# The zip is the supported way to hand this to someone. The app is self-contained, so the
# recipient needs neither the .NET runtime nor the Windows App SDK.
#
# Layout contract, asserted at the end of this script:
#   - the archive has no wrapper folder: unpacking drops the payload straight into the folder
#     the user picks
#   - NaraDreamPainter.exe sits at the top level, with every DLL and resource it loads next to it,
#     because that is where the host resolves them from
#   - LICENSE and README.md sit at the top level as well
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

$publishDir = Join-Path $root "dist\publish\$RuntimeIdentifier"


Write-Host "Publishing $version ($Configuration / $RuntimeIdentifier)" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

& dotnet publish "src\NaraDreamPainter.App\NaraDreamPainter.App.csproj" `
    -c $Configuration `
    -r $RuntimeIdentifier `
    -p:Platform=x64 `
    -m:1 `
    -nodeReuse:false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with $LASTEXITCODE" }

# Do not pass -p:SelfContained or -p:WindowsAppSDKSelfContained here. A global property override
# reaches every project in the graph, including NaraDreamPainter.Compositing, which then stages its own
# copy of the Windows App SDK payload and collides with the app's: NETSDK1152, duplicate
# CoreMessagingXP.dll and friends. The app project already declares both, so they are not needed.

# The executable is named NaraDreamPainter by AssemblyName in the app project. Nothing in the code
# derives paths from the process name, so a rename would need no code changes - but confirm the file
# is where the layout contract says rather than assume it.
$exePath = Join-Path $publishDir "NaraDreamPainter.exe"
if (-not (Test-Path $exePath)) { throw "NaraDreamPainter.exe is missing from $publishDir - the layout contract requires it at the top level." }

# A one-line launcher at the root, because the folder holds several hundred runtime files and finding
# the executable among them is exactly the problem this is here to solve.
Copy-Item "packaging\Run.bat" (Join-Path $publishDir "Run.bat") -Force

# Drop the language folders the app will never load and the debug symbols nobody unpacks. The AI and
# ML payload is left alone on purpose: removing it makes WinUI fail to create its own controls
# (TextBox comes back as a XamlParseException), so it is not dead weight this build can shed.
$keepCultures = @("zh-CN", "zh-TW", "en-us")
foreach ($folder in Get-ChildItem $publishDir -Directory) {
    if ($folder.Name -match '^[a-z]{2}(-[A-Za-z]+)?$' -and $folder.Name -notin $keepCultures) {
        Remove-Item $folder.FullName -Recurse -Force
    }
}
Get-ChildItem $publishDir -Recurse -File -Filter *.pdb | Remove-Item -Force

# The MIT license and the provenance notice travel with the binaries; both are required by the
# licence terms and by the project's own compliance rules.
Copy-Item "LICENSE" (Join-Path $publishDir "LICENSE") -Force
Copy-Item "README.md" (Join-Path $publishDir "README.md") -Force

# The Windows App SDK payload is several hundred files deep in the folder. A tiny plain-text note
# next to the executable is what a user actually reads, so it ships with the payload.
#
# Neither the text nor the file name may be a literal in this script. Windows PowerShell decodes a
# BOM-less .ps1 as ANSI, so CJK literals here are already mojibake by the time they are used - an
# earlier revision shipped a note whose own name was garbled for exactly that reason. The content
# comes from packaging/RUNNING.txt and is written back out as explicit UTF-8.
$noteText = [System.IO.File]::ReadAllText((Join-Path $root "packaging\RUNNING.txt"), [System.Text.Encoding]::UTF8)
[System.IO.File]::WriteAllText((Join-Path $publishDir "RUNNING.txt"), $noteText, (New-Object System.Text.UTF8Encoding($false)))

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
        $prefix = $publishDir.TrimEnd('\') + '\'
        foreach ($file in Get-ChildItem -LiteralPath $publishDir -Recurse -File) {
            $entryName = $file.FullName.Substring($prefix.Length).Replace('\', '/')
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive, $file.FullName, $entryName, [System.IO.Compression.CompressionLevel]::Optimal)
        }
    }
    finally {
        $archive.Dispose()
    }
}

# Assert the layout contract on the artifact that actually ships, not on the staging folder.
if (-not $SkipZip) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $probe = [System.IO.Compression.ZipFile]::OpenRead($zip)
    try {
        $names = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($entry in $probe.Entries) { [void]$names.Add($entry.FullName) }

        $required = @("NaraDreamPainter.exe", "NaraDreamPainter.dll", "NaraDreamPainter.deps.json", "LICENSE", "README.md", "RUNNING.txt", "Run.bat")
        $missing = @($required | Where-Object { -not $names.Contains($_) })
        if ($missing.Count -gt 0) { throw "The zip is missing top-level entries: $($missing -join ', ')" }

        # A wrapper folder would show up as every entry sharing the same first path segment.
        $topLevel = @($probe.Entries | ForEach-Object { $_.FullName.Split('/')[0] } | Sort-Object -Unique)
        $sharedPrefix = $topLevel[0] + '/'
        $wrapped = ($topLevel.Count -eq 1) -and (@($probe.Entries | Where-Object { $_.FullName.StartsWith($sharedPrefix) }).Count -eq $probe.Entries.Count)
        if ($wrapped) {
            throw "The zip wraps everything in '$($topLevel[0])/'; unpacking must land the payload at the root."
        }

        $sizeMb = [Math]::Round((Get-Item $zip).Length / 1MB, 2)
        Write-Host ("  layout  : {0} entries, {1} MB, no wrapper folder" -f $probe.Entries.Count, $sizeMb) -ForegroundColor Green
    }
    finally {
        $probe.Dispose()
    }
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  payload : $publishDir"
Write-Host "  run     : $exePath"
if (-not $SkipZip) { Write-Host "  zip     : dist\NaraDreamPainter-$version-$RuntimeIdentifier.zip" }
Write-Host "  installer: run installer\build-msix.ps1 for the signed setup package"
