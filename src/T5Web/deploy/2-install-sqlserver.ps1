#requires -Version 5.1
#requires -RunAsAdministrator
<#
.SYNOPSIS
    Prepare SQL Server for the T5Web console: instance, the two catalogs, the least-privilege login.

.DESCRIPTION
    Three jobs, each one skipped when it is already done:

      1. Instance. If an instance answers, it is used. Otherwise, with -SetupExe
         pointing at the *offline* installer media, SQL Server Express is installed
         silently. Nothing is downloaded: the deployment network has no internet.
      2. Catalogs. ReportT5V3 and Personnel are the vendor's databases. The script
         never creates them from scratch - it either restores them from -BackupFolder
         or insists they already exist. A hand-made empty schema would look right and
         behave wrong.
      3. Portal objects + login: db\01-t5web-objects.sql and db\02-t5web-sql-login.sql,
         both idempotent.

    The SA password and the t5web_admin password are read at run time. No password is
    written to disk by this script, and revoking the SQL login afterwards is a
    documented step (docs/08-runbook.md section 3).

.EXAMPLE
    .\2-install-sqlserver.ps1 -SetupExe D:\media\SQLEXPR_x64_ENU.exe

.EXAMPLE
    # Databases restored from the vendor's backups, portal objects + login created
    .\2-install-sqlserver.ps1 -BackupFolder D:\backup -T5Database ReportT5V3 -PersonnelDatabase Personnel
#>
[CmdletBinding()]
param(
    [string]$Instance = 'MSSQLSERVER',
    [string]$SetupExe,
    [string]$BackupFolder,
    [string]$T5Database = 'ReportT5V3',
    [string]$PersonnelDatabase = 'Personnel',
    [string]$PortalLogin = 't5web_admin',
    [switch]$SkipPortalObjects
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Find-SqlCmd {
    $candidates = @(
        (Get-Command sqlcmd.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source),
        "$env:ProgramFiles\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE",
        "$env:ProgramFiles\Microsoft SQL Server\120\Tools\Binn\SQLCMD.EXE",
        "$env:ProgramFiles\Microsoft SQL Server\110\Tools\Binn\SQLCMD.EXE"
    ) | Where-Object { $_ -and (Test-Path $_) }
    if (-not $candidates) { throw 'sqlcmd.exe not found. Install the SQL Server command-line utilities, or run this script on the SQL host after installing the engine.' }
    return $candidates[0]
}

function Invoke-Sql([string]$Query, [string]$Database = 'master') {
    $args = @('-S', $ServerName, '-E', '-b', '-h', '-1', '-W', '-Q', $Query)
    if ($Database) { $args += @('-d', $Database) }
    $output = & $script:SqlCmd @args 2>&1
    if ($LASTEXITCODE -ne 0) { throw ("sqlcmd failed ({0}): {1}`n{2}" -f $LASTEXITCODE, $Query, ($output -join "`n")) }
    return $output
}

$ServerName = if ($Instance -eq 'MSSQLSERVER') { '.' } else { ".\$Instance" }
$script:SqlCmd = Find-SqlCmd
$haveInstance = $false
try {
    Invoke-Sql 'SELECT 1' | Out-Null
    $haveInstance = $true
} catch {
    Write-Host '  no SQL Server instance answered on this machine' -ForegroundColor Yellow
}

# ---------------------------------------------------------------- 1. instance
if (-not $haveInstance) {
    if (-not $SetupExe) {
        throw @'
No SQL Server instance answers and no installer media was given.

Offline installations cannot download SQL Server, so download the Express
installer once on a machine that has internet, copy it to the server, then:

    .\2-install-sqlserver.ps1 -SetupExe D:\media\SQLEXPR_x64_ENU.exe

Or point this script at an instance that already runs:
    .\2-install-sqlserver.ps1 -Instance 'MSSQLSERVER'
'@
    }
    if (-not (Test-Path $SetupExe)) { throw "installer media not found: $SetupExe" }
    $sa = Read-Host -AsSecureString 'SA password for the new instance'
    $saPlain = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sa))
    Write-Host '  installing SQL Server (silent, ~10 minutes)...'
    $setupArgs = @(
        '/Q', '/IACCEPTSQLSERVERLICENSETERMS', '/ACTION=install', '/FEATURES=SQLEngine',
        "/INSTANCENAME=$Instance", '/SQLSYSADMINACCOUNTS=BUILTIN\Administrators',
        '/SECURITYMODE=SQL', "/SAPWD=$saPlain", '/TCPENABLED=1', '/NPENABLED=1',
        '/SQLCOLLATION=Persian_100_CI_AS', '/UPDATEENABLED=0'
    )
    $proc = Start-Process -FilePath $SetupExe -ArgumentList $setupArgs -Wait -PassThru
    $saPlain = $null
    if ($proc.ExitCode -ne 0) { throw "SQL Server setup failed with exit code $($proc.ExitCode). See C:\Program Files\Microsoft SQL Server\*\Setup Bootstrap\Log." }
    Start-Sleep -Seconds 5
    Invoke-Sql 'SELECT 1' | Out-Null
    $haveInstance = $true
} else {
    Write-Host ("  instance {0} is running" -f $Instance) -ForegroundColor DarkGray
}

# ---------------------------------------------------------------- 2. catalogs
foreach ($db in @($T5Database, $PersonnelDatabase)) {
    $exists = (Invoke-Sql ("SELECT CASE WHEN DB_ID(N'{0}') IS NULL THEN 0 ELSE 1 END" -f $db)) -join ''
    if ($exists.Trim() -eq '1') {
        Write-Host ("  catalog {0,-14} present" -f $db) -ForegroundColor DarkGray
        continue
    }
    if (-not $BackupFolder) {
        throw ("catalog '{0}' does not exist and no -BackupFolder was given. The engine owns the schema: " -f $db) +
              'install T5V3 or restore the vendor backup; an empty database with a guessed schema is worse than none.'
    }
    $files = Get-ChildItem -Path $BackupFolder -Filter '*.bak' | Where-Object { $_.Name -like "*$db*" }
    if (-not $files) { throw "no .bak file for $db in $BackupFolder" }
    $file = $files | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    Write-Host ("  restoring {0} from {1}" -f $db, $file.Name)
    $logical = (Invoke-Sql ("RESTORE FILELISTONLY FROM DISK = N'{0}'" -f $file.FullName)) |
        ForEach-Object { ($_ -split '\s+')[0] } |
        Where-Object { $_ -and $_ -ne 'LogicalName' }
    $dataLogical = $logical[0]
    $logLogical = if ($logical.Count -gt 1) { $logical[1] } else { $null }
    $move = "MOVE N'$dataLogical' TO N'$(Join-Path $env:ProgramFiles "Microsoft SQL Server\MSSQL*.MSSQLSERVER\MSSQL\DATA\$db.mdf")'"
    if ($logLogical) { $move += ", MOVE N'$logLogical' TO N'$(Join-Path $env:ProgramFiles "Microsoft SQL Server\MSSQL*.MSSQLSERVER\MSSQL\DATA\$db.ldf")'" }
    Invoke-Sql ("RESTORE DATABASE [{0}] FROM DISK = N'{1}' WITH {2}, REPLACE, STATS = 10" -f $db, $file.FullName, $move) | Out-Null
    Write-Host ("  catalog {0,-14} restored" -f $db) -ForegroundColor Green
}

# ---------------------------------------------------- 3. portal objects + login
if (-not $SkipPortalObjects) {
    $dbFolder = Join-Path (Split-Path -Parent $PSScriptRoot) 'db'
    foreach ($script in @('01-t5web-objects.sql', '02-t5web-sql-login.sql')) {
        $path = Join-Path $dbFolder $script
        if (-not (Test-Path $path)) { throw "missing deployment script: $path" }
        Write-Host ("  running {0}" -f $script)
        $output = & $script:SqlCmd -S $ServerName -E -b -i $path -d $T5Database 2>&1
        if ($LASTEXITCODE -ne 0) { throw ("{0} failed ({1}):`n{2}" -f $script, $LASTEXITCODE, ($output -join "`n")) }
        $output | Where-Object { $_ -match '\S' } | ForEach-Object { Write-Host ("    {0}" -f $_) -ForegroundColor DarkGray }
    }

    $loginExists = (Invoke-Sql ("SELECT CASE WHEN SUSER_ID(N'{0}') IS NULL THEN 0 ELSE 1 END" -f $PortalLogin)) -join ''
    if ($loginExists.Trim() -eq '1') {
        Write-Host ("  login {0} present - change its password with:" -f $PortalLogin) -ForegroundColor Yellow
        Write-Host ("    ALTER LOGIN [{0}] WITH PASSWORD = N'<strong password>';" -f $PortalLogin) -ForegroundColor Yellow
    }
}

Write-Host ''
Write-Host 'SQL Server is ready. Next: .\3-deploy-portal.ps1 -DataSource <server\instance>' -ForegroundColor Green
exit 0
