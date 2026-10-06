# Publishes the app and lays out the MSIX payload. Run from the repository root.
#
#   powershell -ExecutionPolicy Bypass -File packaging/pack.ps1
#
# Produces:
#   dist/Compositor-<version>-win-x64.zip   self-contained, unpack and run
#   dist/msix/                              MSIX payload ready for makeappx
#
# The zip is the supported way to hand this to someone. The app is self-contained, so the
# recipient needs neither the .NET runtime nor the Windows App SDK.
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

$version = ([xml](Get-Content "Directory.Build.props")).Project.PropertyGroup.Version
if (-not $version) { $version = "0.1.0" }

$publishDir = Join-Path $root "dist\publish\$RuntimeIdentifier"
$msixDir = Join-Path $root "dist\msix"

Write-Host "Publishing $version ($Configuration / $RuntimeIdentifier)" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

& dotnet publish "src\Compositor.App\Compositor.App.csproj" `
    -c $Configuration `
    -r $RuntimeIdentifier `
    -p:Platform=x64 `
    -m:1 `
    -nodeReuse:false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with $LASTEXITCODE" }

# Do not pass -p:SelfContained or -p:WindowsAppSDKSelfContained here. A global property override
# reaches every project in the graph, including Compositor.Compositing, which then stages its own
# copy of the Windows App SDK payload and collides with the app's: NETSDK1152, duplicate
# CoreMessagingXP.dll and friends. The app project already declares both, so they are not needed.

# The MIT license and the provenance notice travel with the binaries; both are required by the
# licence terms and by the project's own compliance rules.
Copy-Item "LICENSE" (Join-Path $publishDir "LICENSE") -Force
Copy-Item "README.md" (Join-Path $publishDir "README.md") -Force

if (-not $SkipZip) {
    $zip = Join-Path $root "dist\Compositor-$version-$RuntimeIdentifier.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Write-Host "Zipping to $zip" -ForegroundColor Cyan
    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zip -CompressionLevel Optimal
}

Write-Host "Laying out the MSIX payload in $msixDir" -ForegroundColor Cyan
if (Test-Path $msixDir) { Remove-Item $msixDir -Recurse -Force }
New-Item -ItemType Directory -Path $msixDir | Out-Null
Copy-Item (Join-Path $publishDir "*") $msixDir -Recurse -Force
Copy-Item "packaging\Package.appxmanifest" (Join-Path $msixDir "AppxManifest.xml") -Force

$assets = Join-Path $msixDir "Assets"
New-Item -ItemType Directory -Force -Path $assets | Out-Null
Copy-Item "assets\icon\compositor-256.png" (Join-Path $assets "Square150x150Logo.png") -Force
Copy-Item "assets\icon\compositor-64.png" (Join-Path $assets "Square44x44Logo.png") -Force
Copy-Item "assets\icon\compositor-256.png" (Join-Path $assets "Wide310x150Logo.png") -Force
Copy-Item "assets\icon\compositor-64.png" (Join-Path $assets "StoreLogo.png") -Force

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  payload : $publishDir"
if (-not $SkipZip) { Write-Host "  zip     : dist\Compositor-$version-$RuntimeIdentifier.zip" }
Write-Host "  msix    : $msixDir (run makeappx pack /d `"$msixDir`" /p dist\Compositor.msix)"
