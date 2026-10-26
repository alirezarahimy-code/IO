# Real Windows source build/test probe. Without -SolutionRoot this is the RAW
# TWF.sln build (known to fail from missing project references). With it, build
# an explicitly normalized metadata shadow; never confuse the two outcomes.
param([string]$SolutionRoot = '')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
if (-not $SolutionRoot) { $SolutionRoot = Join-Path $root 'src/TWF' }
$msbuild = (Get-Command msbuild -ErrorAction SilentlyContinue).Source
if (-not $msbuild) { Write-Host '::error::MSBuild is not installed'; exit 2 }
$pinned = Join-Path $root 'T5Web [3.5.7]/App/bin'
if (-not (Test-Path -LiteralPath (Join-Path $pinned 'Personnel.dll'))) {
    Write-Host "::error::TWF pinned engine is missing: $pinned"
    exit 2
}
$output = Join-Path $root 'src/TWF/build'
New-Item -ItemType Directory -Force -Path $output | Out-Null
Write-Host "TWF source probe: solution=$SolutionRoot | MSBuild=$msbuild | pinned=$pinned | output=$output"
Write-Host 'TWF baseline: 47 registered TEST-01...TEST-47, no test or warning is skipped.'
$argsList = @(
    (Join-Path $SolutionRoot 'TWF.sln'),
    '/p:Configuration=Release',
    "/p:ReferencePath=$pinned",
    "/p:OutDir=$output/",
    '/m',
    '/v:minimal',
    '/nologo'
)
$log = & $msbuild @argsList 2>&1 | ForEach-Object { "$_" }
$code = $LASTEXITCODE
$log | ForEach-Object { Write-Host $_ }
if ($code -ne 0) {
    # MSB3270 is a *warning* about x86 vendor references. Do not annotate the
    # first 18 warnings as errors and hide the real compiler diagnostics.
    $errors = @($log | Select-String -Pattern '(^|\s)error\s+[A-Z]+[0-9]+|:\s*error\s+')
    if ($errors.Count -eq 0) { $errors = @($log | Select-Object -Last 30) }
    ($errors | Select-Object -First 35) |
        ForEach-Object { Write-Host "::error::TWF baseline: $_" }
    Write-Host "::error::TWF Framework build failed (code $code); no test or port-readiness claim."
    exit 1
}
$exe = Join-Path $output 'TWF.Tests.exe'
if (-not (Test-Path $exe)) {
    Write-Host "::error::TWF baseline build produced no test executable at $exe"
    exit 1
}
$testOutput = & $exe 2>&1 | ForEach-Object { "$_" }
$testCode = $LASTEXITCODE
$testOutput | ForEach-Object { Write-Host $_ }
if ($testCode -ne 0 -or !(($testOutput -join "`n") -match 'Summary:\s+47 Passed,\s+0 Failed')) {
    ($testOutput | Select-String -Pattern '\[FAIL\]|Summary:|Exception|Error:' | Select-Object -First 25) |
        ForEach-Object { Write-Host "::error::TWF baseline: $_" }
    Write-Host "::error::TWF Framework tests failed (code $testCode); no baseline equivalence claim."
    exit 1
}
Write-Host "::notice::TWF FRAMEWORK source probe | solution=$SolutionRoot | Release MSBuild succeeded | 47 Passed, 0 Failed | real Windows runner"
