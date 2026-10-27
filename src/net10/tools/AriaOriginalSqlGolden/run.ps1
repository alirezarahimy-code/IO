# Windows runner ONLY. Create a private LocalDB instance + entirely synthetic rows.
# Never read original Web.config; never stop/restart any installed shared instance.
param(
    [Parameter(Mandatory=$true)][string]$OriginalBin,
    [Parameter(Mandatory=$true)][string]$RunnerTemp,
    [Parameter(Mandatory=$true)][string]$RunId
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$phase = 'CI-owned fixture name validation'
trap {
    # GitHub check-run annotation readable without ever exposing exception text,
    # a connection string, SQL text, original config or a synthetic password.
    Write-Host ("::error title=Aria original SQL golden setup::FAIL at " + $phase +
                "; exception type: " + $_.Exception.GetType().FullName)
    exit 1
}
if ($RunId -cnotmatch '^[0-9]{6,20}$') { throw 'Invalid CI run id for isolated fixture' }
$instance = "AriaGolden_$RunId"
$database = "AriaGoldenDb_$RunId"
$tool = $PSScriptRoot
$phase = 'locating locally installed LocalDB and x86 Framework compiler'
$localdb = Get-Command SqlLocalDB.exe -ErrorAction SilentlyContinue
if ($null -eq $localdb) {
    $sqlDir = Join-Path $env:ProgramFiles 'Microsoft SQL Server'
    if (Test-Path $sqlDir) {
        $localdb = Get-ChildItem $sqlDir -Filter SqlLocalDB.exe -File -Recurse |
            Sort-Object FullName -Descending | Select-Object -First 1
    }
}
if ($null -eq $localdb) { throw 'SqlLocalDB.exe unavailable; cannot run isolated ORIGINAL SQL golden' }
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if (!(Test-Path $csc)) { throw 'Framework x86 compiler unavailable' }
$exe = Join-Path $RunnerTemp 'AriaOriginalSqlGolden.exe'
$phase = 'compiling x86 original golden harness'
& $csc /nologo /platform:x86 /target:exe "/out:$exe" (Join-Path $tool 'Program.cs')
if ($LASTEXITCODE -ne 0) { throw 'Failed to compile original x86 golden harness' }
$sql = Join-Path $RunnerTemp 'aria-golden-only.sql'
$phase = 'generating original-model synthetic SQL only'
& python (Join-Path $tool 'make_fixture.py') (Join-Path $OriginalBin 'Personnel.dll') $database $sql
if ($LASTEXITCODE -ne 0) { throw 'Refused unpinned or incomplete original user fixture' }
$localdbExe = if ($localdb -is [System.Management.Automation.CommandInfo]) { $localdb.Source } else { $localdb.FullName }
# We create and start our own uniquely named instance. We do not touch an existing one.
$phase = 'creating private CI-owned LocalDB instance'
& $localdbExe create $instance
if ($LASTEXITCODE -ne 0) { throw 'Failed to create private LocalDB instance' }
$phase = 'starting private CI-owned LocalDB instance'
& $localdbExe start $instance
if ($LASTEXITCODE -ne 0) { throw 'Failed to start private LocalDB instance' }
Write-Host 'Running original Personnel password method on CI-owned synthetic rows (NOT SQL2016).'
# Windows PowerShell 5.1 runs .NET Framework; SqlClient here is ONLY for creating
# the fixture, NOT evidence of a net10 SQL provider.
$server = '(localdb)\' + $instance
$phase = 'opening private LocalDB for synthetic user fixture'
$connection = New-Object System.Data.SqlClient.SqlConnection(
    "Data Source=$server;Initial Catalog=master;Integrated Security=True;Connect Timeout=10")
try {
    $connection.Open()
    $text = [System.IO.File]::ReadAllText($sql)
    $batches = [System.Text.RegularExpressions.Regex]::Split($text, '(?im)^\s*GO\s*$')
    $phase = 'executing generated synthetic SQL batches'
    foreach ($batch in $batches) {
        if ([string]::IsNullOrWhiteSpace($batch)) { continue }
        $command = $connection.CreateCommand()
        try {
            $command.CommandText = $batch
            $command.CommandTimeout = 30
            $null = $command.ExecuteNonQuery()
        } finally {
            $command.Dispose()
        }
    }
} catch {
    # Don't emit raw exception text (may expose SQL, a connection string or rows).
    $cause = $_.Exception
    Write-Host ("::error title=Aria private LocalDB fixture::FAIL at " + $phase +
                "; exception type: " + $cause.GetType().FullName)
    if ($cause -is [System.Data.SqlClient.SqlException]) { Write-Host ("::error::SQL number: " + $cause.Number) }
    exit 1
} finally {
    $connection.Dispose()
}
$phase = 'executing original x86 CheckUserPass on synthetic data'
& $exe $OriginalBin $instance $database
if ($LASTEXITCODE -ne 0) { throw 'Original CheckUserPass runtime golden FAILED (see sanitized stage/type above)' }
Write-Host 'Fixture remained private to this ephemeral CI runner; no installed/shared SQL service was stopped.'
