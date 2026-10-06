# Acceptance checks for the port: restore, a solution build with no errors and no warnings, the
# structural repository checks, and the whole xUnit suite. The build environment is spelled out here
# on purpose (docs/BUILD.md) so the script works from any shell.

param(
    [string]$Configuration = "Debug"
)

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not (Test-Path (Join-Path $root "NaraDreamPainter.sln"))) {
    Write-Host "FAIL: $root is not the repository root."
    exit 1
}

$env:APPDATA = "$root\.tools\appdata"
$env:DOTNET_ROOT = "$root\.tools\dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:DOTNET_CLI_HOME = "$root\.tools\cli-home"
$env:NUGET_PACKAGES = "$root\.tools\nuget-packages"
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = "$root\.tools\bundle-extract"
# MSBuild creates %TEMP%\MSBuildTemp for its XAML and PRI work, and the real temp folder is outside
# the writable workspace here, so it fails with MSB1025 before doing anything useful.
$env:TEMP = "$root\.tools\temp"
$env:TMP = $env:TEMP
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
# Keeps the parsing below working on machines whose dotnet prints in another language.
$env:DOTNET_CLI_UI_LANGUAGE = "en"
foreach ($directory in @($env:APPDATA, $env:TEMP)) {
    if (-not (Test-Path $directory)) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
}
Set-Location $root

$TestsProject = "tests\NaraDreamPainter.Tests\NaraDreamPainter.Tests.csproj"
$TestAssembly = "tests\NaraDreamPainter.Tests\bin\$Configuration\net8.0-windows10.0.19041.0\win-x64\NaraDreamPainter.Tests.dll"
$RunnerProject = "tools\verify\NaraDreamPainter.TestRunner\NaraDreamPainter.TestRunner.csproj"
# The runner is self-contained and pinned to x64, so its output sits one level deeper than a plain
# framework-dependent build.
$RunnerAssembly = "tools\verify\NaraDreamPainter.TestRunner\bin\x64\$Configuration\net8.0-windows10.0.19041.0\win-x64\NaraDreamPainter.TestRunner.dll"
if (-not (Test-Path (Join-Path $root $RunnerAssembly))) {
    $RunnerAssembly = "tools\verify\NaraDreamPainter.TestRunner\bin\$Configuration\net8.0-windows10.0.19041.0\NaraDreamPainter.TestRunner.dll"
}
$FallbackNote = "tools/verify/NaraDreamPainter.TestRunner, because vstest cannot keep its test host alive here (the host opens a handle to the vstest process and the sandbox denies it)"
$script:VstestBlocked = $false

function Invoke-Dotnet {
    param([string[]]$Arguments)
    # -m:1 -nodeReuse:false keep MSBuild in this process: the sandbox blocks the named pipes a worker
    # node needs, and that failure shows up as "0 Error(s)" plus a non-zero exit code.
    $output = & dotnet @Arguments 2>&1 | Out-String
    $code = $LASTEXITCODE
    Write-Host $output.Trim()
    return [pscustomobject]@{ ExitCode = $code; Output = $output }
}

function Count-Diagnostics {
    param([string]$Text, [string]$Severity)
    # Matches both "error CS1234" and the coded-less form the XAML compiler emits, "error : message".
    return ([regex]::Matches($Text, "(?m):\s+$Severity\b")).Count
}

function Get-TestSummary {
    param([string]$Text)
    $hits = [regex]::Matches($Text, "Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+),\s+Total:\s+(\d+)")
    if ($hits.Count -eq 0) {
        return $null
    }
    $last = $hits[$hits.Count - 1]
    return [pscustomobject]@{
        Failed  = [int]$last.Groups[1].Value
        Passed  = [int]$last.Groups[2].Value
        Skipped = [int]$last.Groups[3].Value
        Total   = [int]$last.Groups[4].Value
    }
}

function Show-FailureLines {
    param([string]$Text)
    $lines = $Text -split "`r?`n" | Where-Object { $_ -match "\berror\b|\bFAILED\b" }
    $lines | Select-Object -First 15 | ForEach-Object { Write-Host "    $($_.Trim())" }
}

function Normalize-Text {
    param([string]$Text)
    # Flattens line breaks and drops Markdown code ticks, so a sentence can be matched regardless of
    # how the surrounding prose is formatted.
    $flat = $Text.Replace("`r", " ").Replace("`n", " ").Replace([string][char]96, "")
    while ($flat.Contains("  ")) { $flat = $flat.Replace("  ", " ") }
    return $flat.Trim()
}

# Resolved after the builds, not before: the test project is written under bin\<cfg>\ when the solution
# builds it and under bin\x64\<cfg>\ when the x64 runner drives it, so take whichever was built last.
function Select-NewestAssembly {
    param([string[]]$Candidates)

    $existing = @($Candidates |
        ForEach-Object { Join-Path $root $_ } |
        Where-Object { Test-Path $_ } |
        ForEach-Object { Get-Item $_ })
    if ($existing.Count -eq 0) { return $Candidates[0] }

    return ($existing | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName.Substring($root.Length + 1)
}

# Runs the xUnit suite through "dotnet test" and falls back to the bundled runner when vstest aborts
# before a single test runs.
function Invoke-Tests {
    param([string]$DotnetFilter, [string]$RunnerFilter)

    if (-not $script:VstestBlocked) {
        $arguments = @("test", $TestsProject, "-c", $Configuration, "--no-restore", "-m:1", "-nodeReuse:false")
        if ($DotnetFilter) { $arguments += @("--filter", $DotnetFilter) }
        $result = Invoke-Dotnet $arguments
        $summary = Get-TestSummary $result.Output

        if (($null -ne $summary) -and ($result.Output -notmatch "Testhost process for source")) {
            return [pscustomobject]@{ ExitCode = $result.ExitCode; Output = $result.Output; Summary = $summary; Mode = "dotnet test" }
        }

        $script:VstestBlocked = $true
        Write-Host ""
        Write-Host "note: dotnet test aborted before running a test. Falling back to $FallbackNote."
        Write-Host ""
    }

    $runnerArguments = @("exec", (Select-NewestAssembly $RunnerAssemblyCandidates), (Select-NewestAssembly $TestAssemblyCandidates))
    if ($RunnerFilter) { $runnerArguments += $RunnerFilter }
    $fallback = Invoke-Dotnet $runnerArguments
    return [pscustomobject]@{ ExitCode = $fallback.ExitCode; Output = $fallback.Output; Summary = (Get-TestSummary $fallback.Output); Mode = "fallback runner" }
}

Write-Host "=== restore ==="
$restore = Invoke-Dotnet @("restore", "NaraDreamPainter.sln", "-m:1", "-nodeReuse:false")
if ($restore.ExitCode -ne 0) {
    Write-Host "restore failed:"
    Show-FailureLines $restore.Output
}

$restoreRunner = Invoke-Dotnet @("restore", $RunnerProject, "-m:1", "-nodeReuse:false")

Write-Host ""
Write-Host "=== build ($Configuration) ==="
$build = Invoke-Dotnet @("build", "NaraDreamPainter.sln", "-c", $Configuration, "--no-restore", "-m:1", "-nodeReuse:false")
$errors = Count-Diagnostics $build.Output "error"
$warnings = Count-Diagnostics $build.Output "warning"
if ($build.ExitCode -ne 0 -or $errors -gt 0) {
    Show-FailureLines $build.Output
}
elseif ($warnings -gt 0) {
    ($build.Output -split "`r?`n" | Where-Object { $_ -match "\bwarning\b" }) |
        Select-Object -First 15 | ForEach-Object { Write-Host "    $($_.Trim())" }
}

Write-Host ""
Write-Host "=== runner build ==="
$runnerBuild = Invoke-Dotnet @("build", $RunnerProject, "-c", $Configuration, "--no-restore", "-m:1", "-nodeReuse:false")

Write-Host ""
Write-Host "=== structure ==="
$structure = Invoke-Tests -DotnetFilter "FullyQualifiedName~RepositoryStructureTests" -RunnerFilter "RepositoryStructureTests"
if ($structure.ExitCode -ne 0) {
    Show-FailureLines $structure.Output
}

Write-Host ""
Write-Host "=== tests ==="
$tests = Invoke-Tests -DotnetFilter "" -RunnerFilter ""
if ($tests.ExitCode -ne 0) {
    Show-FailureLines $tests.Output
}

Write-Host ""
Write-Host "=== package layout ==="
$packageZip = @(Get-ChildItem -Path (Join-Path $root "dist") -Filter "NaraDreamPainter-*-win-x64.zip" -File -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending) | Select-Object -First 1

$packageStatus = "SKIP"
$packageEntries = 0
$packageMegabytes = 0
$packageExeAtRoot = $false

if ($null -eq $packageZip) {
    Write-Host "note: package not built; run packaging/pack.ps1 to check the layout"
}
else {
    $packageStatus = "PASS"
    $packageMegabytes = [Math]::Round($packageZip.Length / 1MB, 2)
    try {
        Add-Type -AssemblyName System.IO.Compression | Out-Null
        Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null
        # The required sentence lives in its own UTF-8 file: Windows PowerShell reads a script without
        # a byte order mark as ANSI, which would mangle an inline copy of it.
        $requiredLine = Normalize-Text ([System.IO.File]::ReadAllText((Join-Path $PSScriptRoot "required-readme-line.txt"), [System.Text.Encoding]::UTF8))
        $archive = [System.IO.Compression.ZipFile]::OpenRead($packageZip.FullName)
        try {
            $names = @($archive.Entries | ForEach-Object { $_.FullName })
            $packageEntries = $names.Count
            $atRoot = @($names | Where-Object { -not $_.Contains("/") })
            $nested = @($names | Where-Object { $_.Contains("/") })
            $firstSegments = @($nested | ForEach-Object { ($_ -split "/")[0] } | Sort-Object -Unique)
            $packageExeAtRoot = $atRoot -contains "NaraDreamPainter.exe"

            $problems = @()
            foreach ($name in @("NaraDreamPainter.exe", "NaraDreamPainter.dll", "NaraDreamPainter.deps.json", "NaraDreamPainter.runtimeconfig.json", "LICENSE", "README.md", "RUNNING.txt", "Run.bat")) {
                if ($atRoot -notcontains $name) { $problems += "missing from the archive root: $name" }
            }
            if ($atRoot.Count -eq 0 -and $firstSegments.Count -eq 1) {
                $problems += "every entry sits inside the wrapper folder '$($firstSegments[0])'"
            }

            $readmeEntry = $archive.Entries | Where-Object { $_.FullName -eq "README.md" } | Select-Object -First 1
            if ($null -ne $readmeEntry) {
                $stream = $readmeEntry.Open()
                try {
                    $reader = New-Object System.IO.StreamReader -ArgumentList $stream, ([System.Text.Encoding]::UTF8)
                    try { $readmeText = $reader.ReadToEnd() } finally { $reader.Dispose() }
                }
                finally { $stream.Dispose() }

                if (-not (Normalize-Text $readmeText).Contains($requiredLine)) {
                    $problems += "README.md does not carry the required run instruction"
                }
            }

            if ($problems.Count -gt 0) {
                $packageStatus = "FAIL"
                foreach ($problem in $problems) { Write-Host "    $problem" }
            }
        }
        finally { $archive.Dispose() }
    }
    catch {
        $packageStatus = "FAIL"
        Write-Host "    could not read $($packageZip.Name): $($_.Exception.Message)"
    }
}

Write-Host ""
Write-Host "=== localization ==="
# Own process: the checker is a script with its own exit code, and calling it in-process would end this
# one instead of just failing a section. Its output is pure ASCII by design, so it needs no decoding.
$localizationScript = Join-Path $PSScriptRoot "check-localization.ps1"
$localizationText = (& powershell -ExecutionPolicy Bypass -File $localizationScript -Root $root 2>&1 | Out-String).Trim()
$localizationCode = $LASTEXITCODE
Write-Host $localizationText

$buildPass = $restore.ExitCode -eq 0 -and $build.ExitCode -eq 0 -and $errors -eq 0 -and $warnings -eq 0
$runnerPass = $restoreRunner.ExitCode -eq 0 -and $runnerBuild.ExitCode -eq 0
$structurePass = $structure.ExitCode -eq 0 -and $null -ne $structure.Summary -and $structure.Summary.Failed -eq 0 -and $structure.Summary.Passed -ge 4
$testsPass = $tests.ExitCode -eq 0 -and $null -ne $tests.Summary -and $tests.Summary.Failed -eq 0 -and $tests.Summary.Passed -gt 0
$packagePass = $packageStatus -ne "FAIL"
$localizationPass = $localizationCode -eq 0

Write-Host ""
Write-Host "=== summary ==="
Write-Host ("RESTORE   : {0}" -f $(if ($restore.ExitCode -eq 0) { "PASS" } else { "FAIL" }))
Write-Host ("BUILD     : {0} ({1} error(s), {2} warning(s))" -f $(if ($buildPass) { "PASS" } else { "FAIL" }), $errors, $warnings)
Write-Host ("RUNNER    : {0}" -f $(if ($runnerPass) { "PASS" } else { "FAIL" }))
if ($null -eq $structure.Summary) {
    Write-Host ("STRUCTURE : {0} (no test summary)" -f $(if ($structurePass) { "PASS" } else { "FAIL" }))
}
else {
    Write-Host ("STRUCTURE : {0} ({1} passed, {2} failed) via {3}" -f $(if ($structurePass) { "PASS" } else { "FAIL" }), $structure.Summary.Passed, $structure.Summary.Failed, $structure.Mode)
}
if ($null -eq $tests.Summary) {
    Write-Host ("TESTS     : {0} (no test summary)" -f $(if ($testsPass) { "PASS" } else { "FAIL" }))
}
else {
    Write-Host ("TESTS     : {0} ({1} passed, {2} failed, {3} skipped, {4} total) via {5}" -f $(if ($testsPass) { "PASS" } else { "FAIL" }), $tests.Summary.Passed, $tests.Summary.Failed, $tests.Summary.Skipped, $tests.Summary.Total, $tests.Mode)
}

if ($packageStatus -eq "SKIP") {
    Write-Host "PACKAGE   : SKIP (package not built; run packaging/pack.ps1 to check the layout)"
}
else {
    Write-Host ("PACKAGE   : {0} ({1} entries, {2} MB, {3})" -f $packageStatus, $packageEntries, $packageMegabytes, $(if ($packageExeAtRoot) { "exe at root" } else { "exe missing from root" }))
}
Write-Host ("LOCALIZATION: {0}" -f $(if ($localizationPass) { "PASS" } else { "FAIL" }))

if ($buildPass -and $runnerPass -and $structurePass -and $testsPass -and $packagePass -and $localizationPass) {
    Write-Host "RESULT: PASS"
    exit 0
}

Write-Host "RESULT: FAIL"
exit 1
