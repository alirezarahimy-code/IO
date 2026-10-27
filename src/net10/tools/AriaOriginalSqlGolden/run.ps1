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
$rolesExe = Join-Path $RunnerTemp 'AriaOriginalRolesGolden.exe'
& $csc /nologo /platform:x86 /target:exe "/out:$rolesExe" (Join-Path $tool 'RolesProgram.cs')
if ($LASTEXITCODE -ne 0) { throw 'Failed to compile original x86 manager harness' }
$actionExe = Join-Path $RunnerTemp 'AriaOriginalActionGolden.exe'
& $csc /nologo /platform:x86 /target:exe /reference:System.Configuration.dll "/out:$actionExe" `
    (Join-Path $tool 'ActionProgram.cs')
if ($LASTEXITCODE -ne 0) { throw 'Failed to compile original x86 action harness' }
$sql = Join-Path $RunnerTemp 'aria-golden-only.sql'
$phase = 'generating original-model synthetic SQL only'
& python (Join-Path $tool 'make_fixture.py') (Join-Path $OriginalBin 'Personnel.dll') $database $sql
if ($LASTEXITCODE -ne 0) { throw 'Refused unpinned or incomplete original user fixture' }
$rolesDb = "AriaGoldenT5_$RunId"
$rolesSql = Join-Path $RunnerTemp 'aria-golden-t5-roles-only.sql'
$phase = 'generating original-model synthetic T5 permissions'
& python (Join-Path $tool 'make_roles.py') (Join-Path $OriginalBin 'T5V3.DB.dll') $rolesDb $rolesSql
if ($LASTEXITCODE -ne 0) { throw 'Refused unpinned or incomplete original T5 fixture' }
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
$connection = $null
try {
    # LocalDB can finish starting its private process AFTER `sqllocaldb start`
    # returns. Retry ONLY Open; never repeat a non-idempotent fixture batch.
    for ($attempt = 0; $attempt -lt 3; $attempt++) {
        if ($attempt -gt 0) { Start-Sleep -Seconds 3 }
        $connection = New-Object System.Data.SqlClient.SqlConnection(
            "Data Source=$server;Initial Catalog=master;Integrated Security=True;Connect Timeout=10")
        try {
            $connection.Open()
            break
        } catch {
            $errorCause = $_.Exception
            while ($errorCause.InnerException) { $errorCause = $errorCause.InnerException }
            $connection.Dispose()
            $connection = $null
            if ($attempt -eq 2 -or $errorCause -isnot [System.Data.SqlClient.SqlException]) { throw }
        }
    }
    foreach ($fixture in @($sql, $rolesSql)) {
        $text = [System.IO.File]::ReadAllText($fixture)
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
    }
} catch {
    # Don't emit raw exception text (may expose SQL, a connection string or rows).
    $cause = $_.Exception
    while ($cause.InnerException) { $cause = $cause.InnerException }
    Write-Host ("::error title=Aria private LocalDB fixture::FAIL at " + $phase +
                "; exception type: " + $cause.GetType().FullName)
    if ($cause -is [System.Data.SqlClient.SqlException]) { Write-Host ("::error::SQL number: " + $cause.Number) }
    exit 1
} finally {
    if ($null -ne $connection) { $connection.Dispose() }
}
$phase = 'executing original x86 CheckUserPass on synthetic data'
& $exe $OriginalBin $instance $database
if ($LASTEXITCODE -ne 0) { throw 'Original CheckUserPass runtime golden FAILED (see sanitized stage/type above)' }
$phase = 'executing original Aria T5 main-manager decision on synthetic data'
& $rolesExe $OriginalBin $instance $rolesDb
if ($LASTEXITCODE -ne 0) { throw 'Original IsAnyMainManager runtime golden FAILED (see sanitized stage/type above)' }
$phase = 'creating OWNED synthetic executable config for original read-only MVC action'
$providerConnection = "Data Source=$server;Initial Catalog=$database;Integrated Security=True;Connect Timeout=10;MultipleActiveResultSets=True"
$entityConnection = 'metadata=res://*/DB.PersonnelMdl.csdl|res://*/DB.PersonnelMdl.ssdl|' + `
    'res://*/DB.PersonnelMdl.msl;provider=System.Data.SqlClient;provider connection string="' + `
    $providerConnection + '"'
$ownedConfig = [xml]'<configuration><appSettings><add key="PersonnelCnnStr" value=""/></appSettings></configuration>'
$ownedConfig.configuration.appSettings.add.SetAttribute('value', $entityConnection)
$ownedConfig.Save("$actionExe.config")
$phase = 'executing original TestUserPass MVC action METHOD on synthetic SQL'
& $actionExe $OriginalBin $instance $database
if ($LASTEXITCODE -ne 0) { throw 'Original TestUserPass in-process action method golden FAILED' }
$phase = 'comparing pinned original role pair with compiled net10 Aria adapter (fake reader only)'
$sdk10 = @(& dotnet --list-sdks | Where-Object { $_ -match '^10\.0\.[0-9]+ ' })
if ($sdk10.Count -eq 0) { throw 'Preinstalled .NET 10 SDK missing; offline net10 comparison unavailable' }
$roleFixture = Join-Path $RunnerTemp 'aria-original-role-golden.json'
$roleJson = '{"source_sha256":"d8f0570b39635e21b883257cf4d6bba1d6011002921ba5227dcd9cdf567b1faf","table":"dbo.UserPermission","access_type":0,"access_level":1,"scope":"SYNTHETIC_LOCALDB_2025_ONLY"}'
[System.IO.File]::WriteAllText($roleFixture, $roleJson, [System.Text.Encoding]::UTF8)
$root = (Resolve-Path (Join-Path $tool '..\..')).Path
$project = Join-Path $root 'Aria\Profile.Tests\Aria.Profile.Tests.csproj'
$web = Join-Path $root 'Aria\Web'
$config = Join-Path $root 'nuget.offline.config'
& dotnet restore $project --configfile $config -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Offline net10 restore failed' }
& dotnet build $project -c Release --no-restore -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Net10 role adapter build failed' }
& dotnet run --project $project -c Release --no-build --no-restore -- $web --original-aria-manager $roleFixture
if ($LASTEXITCODE -ne 0) { throw 'Same-run original-role-to-net10 adapter comparison failed' }
Write-Host 'Both fixtures remained private to this ephemeral CI runner; no installed/shared SQL service was stopped.'
