# Acceptance checks for the port: restore, a solution build with no errors and no warnings, the
# structural repository checks, and the whole xUnit suite. The build environment is spelled out here
# on purpose (docs/BUILD.md) so the script works from any shell.

param(
    [string]$Configuration = "Debug"
)

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not (Test-Path (Join-Path $root "Compositor.sln"))) {
    Write-Host "FAIL: $root is not the repository root."
    exit 1
}

$env:APPDATA = "$root\.tools\appdata"
$env:DOTNET_ROOT = "$root\.tools\dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:DOTNET_CLI_HOME = "$root\.tools\cli-home"
$env:NUGET_PACKAGES = "$root\.tools\nuget-packages"
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = "$root\.tools\bundle-extract"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
# Keeps the parsing below working on machines whose dotnet prints in another language.
$env:DOTNET_CLI_UI_LANGUAGE = "en"
Set-Location $root

$TestsProject = "tests\Compositor.Tests\Compositor.Tests.csproj"
$TestAssembly = "tests\Compositor.Tests\bin\$Configuration\net8.0-windows10.0.19041.0\win-x64\Compositor.Tests.dll"
$RunnerProject = "tools\verify\Compositor.TestRunner\Compositor.TestRunner.csproj"
$RunnerAssembly = "tools\verify\Compositor.TestRunner\bin\$Configuration\net8.0-windows10.0.19041.0\Compositor.TestRunner.dll"
$FallbackNote = "tools/verify/Compositor.TestRunner, because vstest cannot keep its test host alive here (the host opens a handle to the vstest process and the sandbox denies it)"
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
    return ([regex]::Matches($Text, "(?m):\s+$Severity\s+[A-Z]{2,}\d+")).Count
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

    $runnerArguments = @("exec", $RunnerAssembly, $TestAssembly)
    if ($RunnerFilter) { $runnerArguments += $RunnerFilter }
    $fallback = Invoke-Dotnet $runnerArguments
    return [pscustomobject]@{ ExitCode = $fallback.ExitCode; Output = $fallback.Output; Summary = (Get-TestSummary $fallback.Output); Mode = "fallback runner" }
}

Write-Host "=== restore ==="
$restore = Invoke-Dotnet @("restore", "Compositor.sln", "-m:1", "-nodeReuse:false")
if ($restore.ExitCode -ne 0) {
    Write-Host "restore failed:"
    Show-FailureLines $restore.Output
}

$restoreRunner = Invoke-Dotnet @("restore", $RunnerProject, "-m:1", "-nodeReuse:false")

Write-Host ""
Write-Host "=== build ($Configuration) ==="
$build = Invoke-Dotnet @("build", "Compositor.sln", "-c", $Configuration, "--no-restore", "-m:1", "-nodeReuse:false")
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

$buildPass = $restore.ExitCode -eq 0 -and $build.ExitCode -eq 0 -and $errors -eq 0 -and $warnings -eq 0
$runnerPass = $restoreRunner.ExitCode -eq 0 -and $runnerBuild.ExitCode -eq 0
$structurePass = $structure.ExitCode -eq 0 -and $null -ne $structure.Summary -and $structure.Summary.Failed -eq 0 -and $structure.Summary.Passed -ge 4
$testsPass = $tests.ExitCode -eq 0 -and $null -ne $tests.Summary -and $tests.Summary.Failed -eq 0 -and $tests.Summary.Passed -gt 0

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

if ($buildPass -and $runnerPass -and $structurePass -and $testsPass) {
    Write-Host "RESULT: PASS"
    exit 0
}

Write-Host "RESULT: FAIL"
exit 1
