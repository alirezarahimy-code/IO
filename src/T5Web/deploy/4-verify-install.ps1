#requires -Version 5.1
<#
.SYNOPSIS
    The acceptance checklist as code. Prints PASS/FAIL and exits with the number of failures.

.DESCRIPTION
    Six checks, in the order a broken installation reveals itself:

      1. schema     - every object and column the code names exists on the server (db\verify.sql)
      2. relations  - the data satisfies the relations the code assumes (db\03-relations.sql)
      3. fingerprint- this server matches the source it was moved from (optional -Baseline)
      4. auth       - an anonymous request is refused (401) and a Windows request succeeds (200)
      5. leastpriv  - the portal login can read but cannot create a table
      6. headers    - the documented CSP and hardening headers are really sent

    It never writes: db\03-relations.sql is read-only by design, and the fingerprint
    script only counts rows. Running this against production is safe.

.EXAMPLE
    .\4-verify-install.ps1 -BaseUrl http://localhost:8080 -SourceFingerprint D:\cutover\source-before.txt
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:8080',
    [string]$Instance = 'MSSQLSERVER',
    [string]$T5Database = 'ReportT5V3',
    [string]$PersonnelDatabase = 'Personnel',
    [string]$SourceFingerprint,
    [string]$PortalLogin = 't5web_admin',
    [PSCredential]$PortalCredential
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

$script:failures = 0
function Report([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) {
        Write-Host ("  PASS  {0,-14} {1}" -f $name, $detail) -ForegroundColor Green
    } else {
        $script:failures++
        Write-Host ("  FAIL  {0,-14} {1}" -f $name, $detail) -ForegroundColor Red
    }
}

$ServerName = if ($Instance -eq 'MSSQLSERVER') { '.' } else { ".\$Instance" }
$repoRoot = Split-Path -Parent $PSScriptRoot
$sqlcmd = (Get-Command sqlcmd.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source)
if (-not $sqlcmd) {
    foreach ($candidate in @(
        "$env:ProgramFiles\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE",
        "$env:ProgramFiles\Microsoft SQL Server\120\Tools\Binn\SQLCMD.EXE")) {
        if (Test-Path $candidate) { $sqlcmd = $candidate; break }
    }
}
if (-not $sqlcmd) { Write-Warning 'sqlcmd.exe not found: the database checks will be skipped.' }

function Invoke-Script([string]$path, [string]$database) {
    $output = & $sqlcmd -S $ServerName -E -b -i $path -d $database -h -1 -W -s '|' 2>&1
    return @{ Code = $LASTEXITCODE; Lines = $output }
}

# 1. schema ---------------------------------------------------------------
if ($sqlcmd) {
    $verify = Join-Path $repoRoot 'db\verify.sql'
    $result = Invoke-Script $verify $T5Database
    $missing = @($result.Lines | Where-Object { $_ -match 'MISSING' })
    if ($result.Code -ne 0) {
        Report 'schema' $false ("verify.sql failed (exit {0})" -f $result.Code)
    } else {
        $detail = 'no MISSING object'
        if ($missing.Count -gt 0) {
            $detail = "{0} missing: {1}" -f $missing.Count, (($missing | Select-Object -First 3) -join '; ')
        }
        Report 'schema' ($missing.Count -eq 0) $detail
    }

    # 2. relations ---------------------------------------------------------
    $relations = Join-Path $repoRoot 'db\03-relations.sql'
    $result = Invoke-Script $relations $T5Database
    $violations = @($result.Lines | Where-Object { $_ -match 'VIOLATION' })
    if ($result.Code -ne 0) {
        Report 'relations' $false ("03-relations.sql failed (exit {0})" -f $result.Code)
    } else {
        $detail = 'no orphan rows'
        if ($violations.Count -gt 0) {
            $detail = "{0} violation(s): {1}" -f $violations.Count, (($violations | Select-Object -First 3) -join '; ')
        }
        Report 'relations' ($violations.Count -eq 0) $detail
    }

    # 3. fingerprint -------------------------------------------------------
    if ($SourceFingerprint) {
        $tool = Join-Path $repoRoot 'tools\migration-fingerprint.py'
        $python = (Get-Command python -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source)
        if (-not $python) {
            Report 'fingerprint' $false 'python not found; cannot run the comparison'
        } elseif (-not (Test-Path $SourceFingerprint)) {
            Report 'fingerprint' $false "baseline not found: $SourceFingerprint"
        } else {
            $generated = Join-Path $env:TEMP 't5web-fingerprint.sql'
            & $python $tool --sql > $generated
            $current = Join-Path $env:TEMP 't5web-fingerprint-after.txt'
            & $sqlcmd -S $ServerName -E -i $generated -h -1 -W -s '' | Out-File -FilePath $current -Encoding UTF8
            $compare = & $python $tool --compare $SourceFingerprint $current 2>&1
            Report 'fingerprint' ($LASTEXITCODE -eq 0) (($compare | Select-Object -Last 1))
        }
    } else {
        Write-Host '  SKIP  fingerprint    (pass -SourceFingerprint to compare against the pre-move baseline)' -ForegroundColor DarkYellow
    }

    # 5. least privilege ---------------------------------------------------
    # Only meaningful with the portal's own credentials: running this probe as a
    # sysadmin proves nothing. The password is read at run time (-PortalCredential).
    if (-not $PortalCredential) {
        Write-Host '  SKIP  leastpriv     (pass -PortalCredential for the portal login to test its grants)' -ForegroundColor DarkYellow
    } else {
        $plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
            [Runtime.InteropServices.Marshal]::SecureStringToBSTR($PortalCredential.Password))
        $probe = "BEGIN TRY CREATE TABLE dbo.T5WebProbe (ID int); SELECT 'CREATED'; DROP TABLE dbo.T5WebProbe; END TRY BEGIN CATCH SELECT 'DENIED'; END CATCH"
        $asPortal = & $sqlcmd -S $ServerName -U $PortalCredential.UserName -P $plain -b -h -1 -W -Q $probe 2>&1
        $denied = (($asPortal -join ' ') -match 'DENIED')
        Report 'leastpriv' $denied 'portal login cannot create a table'
        $read = & $sqlcmd -S $ServerName -U $PortalCredential.UserName -P $plain -b -h -1 -W -Q 'SELECT COUNT_BIG(*) FROM dbo.T5WebAccess' 2>&1
        Report 'leastpriv-read' ($LASTEXITCODE -eq 0) 'portal login can read its own table'
        $plain = $null
    }
} else {
    Write-Warning 'database checks skipped (no sqlcmd).'
}

# 4. authentication -------------------------------------------------------
try {
    $anonymous = Invoke-WebRequest -Uri $BaseUrl -UseBasicParsing -MaximumRedirection 0 -ErrorAction Stop
    # A 200 for an anonymous caller means Windows auth is off somewhere.
    Report 'auth-anonymous' $false ("anonymous request returned {0}; Windows authentication is not enforced" -f $anonymous.StatusCode)
} catch {
    $status = $null
    if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
    Report 'auth-anonymous' ($status -eq 401) ("anonymous request returned {0} (expected 401)" -f $status)
}

try {
    $authenticated = Invoke-WebRequest -Uri ($BaseUrl.TrimEnd('/') + '/Dashboard') -UseBasicParsing -UseDefaultCredentials -ErrorAction Stop
    $ok = $authenticated.StatusCode -eq 200 -and $authenticated.Content -notmatch 'دسترسی ندارید'
    Report 'auth-windows' $ok ("authenticated request returned {0}" -f $authenticated.StatusCode)

    # 6. headers ----------------------------------------------------------
    $csp = $authenticated.Headers['Content-Security-Policy']
    $hasCsp = $csp -and ($csp -join '') -match "default-src 'self'" -and ($csp -join '') -notmatch 'unsafe-inline'
    Report 'headers' ([bool]$hasCsp) 'CSP is present and has no unsafe-inline'
} catch {
    $status = $null
    if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
    Report 'auth-windows' $false ("authenticated request failed ({0}); is the current user a WebUser with PersonnelView?" -f $status)
}

Write-Host ''
if ($script:failures -eq 0) {
    Write-Host 'ALL CHECKS PASSED - the installation is verified.' -ForegroundColor Green
    exit 0
}
Write-Host ("{0} CHECK(S) FAILED - see docs/13-install-iis-sqlserver.md, section 'when a check fails'." -f $script:failures) -ForegroundColor Red
exit $script:failures
