# Builds the MSIX installer and signs it with a self-signed certificate.
#
#   powershell -ExecutionPolicy Bypass -File installer\build-msix.ps1
#
# Produces:
#   dist\NaraDreamPainter-Setup.msix   the installer
#   dist\NaraDreamPainter.cer          the certificate the machine has to trust first
#
# Why a certificate: Windows refuses to install an unsigned MSIX. A self-signed one is fine for a
# build that is handed over directly, but the recipient has to add the .cer to Trusted People once -
# see installer/README.md. Signing does not need administrator rights; trusting the certificate does.
param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$env:DOTNET_ROOT = Join-Path $root ".tools\dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:DOTNET_CLI_HOME = Join-Path $root ".tools\cli-home"
$env:NUGET_PACKAGES = Join-Path $root ".tools\nuget-packages"
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $root ".tools\bundle-extract"
$env:APPDATA = Join-Path $root ".tools\appdata"
$env:TEMP = Join-Path $root ".tools\temp"
$env:TMP = $env:TEMP
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
$env:DOTNET_CLI_UI_LANGUAGE = "en"
foreach ($directory in @($env:APPDATA, $env:TEMP)) {
    if (-not (Test-Path $directory)) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
}

$version = ([xml](Get-Content "Directory.Build.props")).Project.PropertyGroup.Version
if (-not $version) { $version = "0.2.0" }
$packageVersion = "$version.0"

$publishDir = Join-Path $root "dist\publish\$RuntimeIdentifier"
$stageDir = Join-Path $root "dist\msix"
$msixPath = Join-Path $root "dist\NaraDreamPainter-Setup.msix"
$cerPath = Join-Path $root "dist\NaraDreamPainter.cer"
$pfxPath = Join-Path $root ".tools\signing\NaraDreamPainter.pfx"
$pfxPassword = "naradreampainter-local"

# The publisher in the manifest has to match the certificate subject exactly or the install is
# rejected with a signature error, so both come from this one string.
$publisher = "CN=Nara Dream Painter"

$makeappx = Get-ChildItem (Join-Path $root ".tools\nuget-packages\microsoft.windows.sdk.buildtools") -Recurse -Filter "makeappx.exe" |
    Where-Object { $_.FullName -match '\\x64\\' } | Select-Object -First 1
$signtool = Get-ChildItem (Join-Path $root ".tools\nuget-packages\microsoft.windows.sdk.buildtools") -Recurse -Filter "signtool.exe" |
    Where-Object { $_.FullName -match '\\x64\\' } | Select-Object -First 1
if (-not $makeappx) { throw "makeappx.exe not found in the SDK build tools package." }
if (-not $signtool) { throw "signtool.exe not found in the SDK build tools package." }

if (-not $SkipPublish -or -not (Test-Path (Join-Path $publishDir "NaraDreamPainter.exe"))) {
    Write-Host "Publishing $version ($Configuration / $RuntimeIdentifier)" -ForegroundColor Cyan
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    & dotnet publish "src\NaraDreamPainter.App\NaraDreamPainter.App.csproj" `
        -c $Configuration -r $RuntimeIdentifier -p:Platform=x64 -m:1 -nodeReuse:false -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with $LASTEXITCODE" }
}

$exePath = Join-Path $publishDir "NaraDreamPainter.exe"
if (-not (Test-Path $exePath)) { throw "NaraDreamPainter.exe is missing from $publishDir" }

Write-Host "Staging the package" -ForegroundColor Cyan
if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
New-Item -ItemType Directory -Path $stageDir | Out-Null
Copy-Item (Join-Path $publishDir "*") $stageDir -Recurse -Force

# The licence and the provenance notice travel with the binaries, installer included.
Copy-Item "LICENSE" (Join-Path $stageDir "LICENSE") -Force
Copy-Item "README.md" (Join-Path $stageDir "README.md") -Force

# The manifest is filled in here rather than committed with a version baked into it.
$manifest = [System.IO.File]::ReadAllText((Join-Path $root "installer\Package.appxmanifest"), [System.Text.Encoding]::UTF8)
$manifest = $manifest.Replace("__VERSION__", $packageVersion).Replace("__PUBLISHER__", $publisher)
[System.IO.File]::WriteAllText((Join-Path $stageDir "AppxManifest.xml"), $manifest, (New-Object System.Text.UTF8Encoding($false)))

$assets = Join-Path $stageDir "Assets"
New-Item -ItemType Directory -Force -Path $assets | Out-Null
Copy-Item "assets\icon\compositor-256.png" (Join-Path $assets "Square150x150Logo.png") -Force
Copy-Item "assets\icon\compositor-64.png" (Join-Path $assets "Square44x44Logo.png") -Force
Copy-Item "assets\icon\compositor-256.png" (Join-Path $assets "Wide310x150Logo.png") -Force
Copy-Item "assets\icon\compositor-64.png" (Join-Path $assets "StoreLogo.png") -Force

Write-Host "Creating the certificate" -ForegroundColor Cyan
$signingDir = Split-Path -Parent $pfxPath
if (-not (Test-Path $signingDir)) { New-Item -ItemType Directory -Force -Path $signingDir | Out-Null }

$cert = Get-ChildItem "Cert:\CurrentUser\My" | Where-Object { $_.Subject -eq $publisher } | Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate `
        -Type Custom `
        -Subject $publisher `
        -KeyUsage DigitalSignature `
        -FriendlyName "Nara Dream Painter local signing" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
}
$securePassword = ConvertTo-SecureString -String $pfxPassword -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePassword | Out-Null
Export-Certificate -Cert $cert -FilePath $cerPath -Type CERT | Out-Null

Write-Host "Packing and signing" -ForegroundColor Cyan
if (Test-Path $msixPath) { Remove-Item $msixPath -Force }
& $makeappx.FullName pack /d $stageDir /p $msixPath /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw "makeappx failed with $LASTEXITCODE" }

& $signtool.FullName sign /fd SHA256 /f $pfxPath /p $pfxPassword $msixPath | Out-Null
if ($LASTEXITCODE -ne 0) { throw "signtool failed with $LASTEXITCODE" }

$sizeMb = [Math]::Round((Get-Item $msixPath).Length / 1MB, 1)
Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  installer : dist\NaraDreamPainter-Setup.msix ($sizeMb MB)"
Write-Host "  trust     : dist\NaraDreamPainter.cer  (add to Trusted People before installing)"
Write-Host "  install   : Add-AppxPackage -Path dist\NaraDreamPainter-Setup.msix"
