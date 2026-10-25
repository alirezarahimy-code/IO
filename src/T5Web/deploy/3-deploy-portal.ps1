#requires -Version 5.1
#requires -RunAsAdministrator
<#
.SYNOPSIS
    Publish the T5Web admin console to IIS: app pool, site, authentication, ACLs, Web.config.

.DESCRIPTION
    Idempotent: an existing pool/site is reconfigured rather than recreated, and the
    Web.config keys are replaced in place.

    Four decisions are deliberate:
      * the pool runs as the default ApplicationPoolIdentity - no domain account, no
        stored credential, and the SQL login can be mapped to it;
      * the pool is AlwaysRunning with no idle timeout, because the first request after
        a recycle pays the ASP.NET start-up cost and an admin console is opened in a
        hurry;
      * Windows Authentication on, Anonymous off, and <deny users="?" /> already in
        Web.config - two independent layers, so a misconfigured site still refuses
        anonymous requests;
      * appSettings are patched, never appended: a second run with a different
        -DataSource replaces the value instead of adding a duplicate key that
        ConfigurationManager would reject at start-up.

.EXAMPLE
    .\3-deploy-portal.ps1 -DataSource 'SRV-SQL01\T5' -Mode coexist -Port 8080 -Build
#>
[CmdletBinding()]
param(
    [string]$DataSource = '.',
    [string]$T5Database = 'ReportT5V3',
    [string]$PersonnelDatabase = 'Personnel',
    [ValidateSet('coexist', 'portal', 'legacy')]
    [string]$Mode = 'coexist',
    [string]$LegacyUrl = '',
    [string]$SiteName = 'T5WebAdmin',
    [string]$PoolName = 'T5WebAdmin',
    [int]$Port = 8080,
    [string]$SitePath,
    [switch]$Build
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module WebAdministration -ErrorAction Stop

# The IIS: drive is created by the WebAdministration provider, which only exists in
# Windows PowerShell 5.1. Running this script from PowerShell 7 gives a much less
# helpful "Cannot find drive. A drive with the name 'IIS' does not exist." further
# down, so say it here instead.
if (-not (Get-PSDrive -Name IIS -ErrorAction SilentlyContinue)) {
    throw 'The IIS: drive is missing. Run this script in Windows PowerShell 5.1 (powershell.exe), not PowerShell 7 (pwsh).'
}

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $SitePath) { $SitePath = Join-Path $repoRoot 'T5Web.Web' }
if (-not (Test-Path (Join-Path $SitePath 'Web.config'))) {
    throw "Web.config not found under $SitePath - pass -SitePath with the deployed folder."
}

# --------------------------------------------------------------- build (optional)
if ($Build) {
    # Same discovery as 0-publish.ps1: vswhere first, because the .NET Framework
    # MSBuild's csc.exe rejects <LangVersion>7.3</LangVersion> (CS1617).
    $msbuild = $null
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
                    -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null | Select-Object -First 1
        if ($found -and (Test-Path $found)) { $msbuild = $found }
    }
    if (-not $msbuild) {
        $onPath = Get-Command msbuild -ErrorAction SilentlyContinue
        if ($onPath) { $msbuild = $onPath.Source }
    }
    if (-not $msbuild) { throw 'MSBuild not found. Install the .NET Framework 4.7.2 targeting pack / Build Tools, or build the solution first and omit -Build.' }
    Write-Host ("  building {0}" -f $solution)
    & $msbuild $solution /p:Configuration=Release /m /v:minimal
    if ($LASTEXITCODE -ne 0) { throw "MSBuild failed with exit code $LASTEXITCODE." }
}

# --------------------------------------------------------------- app pool
if (-not (Test-Path "IIS:\AppPools\$PoolName")) {
    New-WebAppPool -Name $PoolName | Out-Null
    Write-Host ("  app pool {0} created" -f $PoolName) -ForegroundColor Green
} else {
    Write-Host ("  app pool {0} present" -f $PoolName) -ForegroundColor DarkGray
}
Set-ItemProperty "IIS:\AppPools\$PoolName" managedRuntimeVersion 'v4.0'
Set-ItemProperty "IIS:\AppPools\$PoolName" managedPipelineMode 'Integrated'
Set-ItemProperty "IIS:\AppPools\$PoolName" startMode 'AlwaysRunning'
Set-ItemProperty "IIS:\AppPools\$PoolName" processModel.idleTimeout ([TimeSpan]::Zero)
Set-ItemProperty "IIS:\AppPools\$PoolName" processModel.loadUserProfile $true
Set-ItemProperty "IIS:\AppPools\$PoolName" recycling.periodicRestart.time ([TimeSpan]::Zero)

# --------------------------------------------------------------- site
$binding = "*:${Port}:"
$site = Get-Website -Name $SiteName -ErrorAction SilentlyContinue
if ($null -eq $site) {
    New-Website -Name $SiteName -PhysicalPath $SitePath -Port $Port -ApplicationPool $PoolName | Out-Null
    Write-Host ("  site {0} on port {1}" -f $SiteName, $Port) -ForegroundColor Green
} else {
    Set-ItemProperty "IIS:\Sites\$SiteName" physicalPath $SitePath
    Set-ItemProperty "IIS:\Sites\$SiteName" applicationPool $PoolName
    if (-not ($site.bindings.Collection | Where-Object { $_.bindingInformation -eq $binding })) {
        New-WebBinding -Name $SiteName -Protocol http -Port $Port | Out-Null
        Write-Host ("  binding added: port {0}" -f $Port)
    } else {
        Write-Host ("  site {0} present on port {1}" -f $SiteName, $Port) -ForegroundColor DarkGray
    }
}

# Windows authentication on, anonymous off, at the site level too.
Set-WebConfigurationProperty -Filter '/system.webServer/security/authentication/anonymousAuthentication' `
    -Name enabled -Value $false -PSPath "IIS:\Sites\$SiteName"
Set-WebConfigurationProperty -Filter '/system.webServer/security/authentication/windowsAuthentication' `
    -Name enabled -Value $true -PSPath "IIS:\Sites\$SiteName"
Set-WebConfigurationProperty -Filter '/system.webServer/security/authentication/windowsAuthentication/providers' `
    -Name '.' -Value @('Negotiate', 'NTLM') -PSPath "IIS:\Sites\$SiteName"

# --------------------------------------------------------------- ACLs
# The pool identity needs to read the app and to compile views into the ASP.NET
# temporary folder; it must not be able to write next to Web.config.
$identity = "IIS AppPool\$PoolName"
& icacls.exe $SitePath /grant "${identity}:(OI)(CI)(RX)" /T /C /Q | Out-Null
if ($LASTEXITCODE -ne 0) { throw "icacls failed to grant read access to $SitePath." }
Write-Host ("  {0} granted read on {1}" -f $identity, $SitePath) -ForegroundColor DarkGray

# --------------------------------------------------------------- Web.config
$webConfig = Join-Path $SitePath 'Web.config'
[xml]$config = Get-Content -Path $webConfig -Encoding UTF8

function Set-AppSetting([string]$key, [string]$value) {
    $node = $config.configuration.appSettings.add | Where-Object { $_.key -eq $key }
    if ($null -eq $node) {
        $new = $config.CreateElement('add')
        $new.SetAttribute('key', $key)
        $new.SetAttribute('value', $value)
        [void]$config.configuration.appSettings.AppendChild($new)
        return
    }
    if ($node -is [array]) {
        # A duplicated key makes ConfigurationManager throw at start-up; keep the first.
        for ($i = $node.Count - 1; $i -ge 1; $i--) { [void]$node[$i].ParentNode.RemoveChild($node[$i]) }
        $node = $node[0]
    }
    $node.SetAttribute('value', $value)
}

# Integrated Security only: a password in Web.config is a password in every backup,
# every copy of the site folder and every screenshot of it. Map "IIS AppPool\<pool>"
# to the least-privilege login on the SQL side instead (db\02-t5web-sql-login.sql
# documents the grants; docs/13 section 6 shows the CREATE LOGIN ... FROM WINDOWS line).
Set-AppSetting 'T5CnnStr'         ("data source={0};initial catalog={1};integrated security=True;MultipleActiveResultSets=True" -f $DataSource, $T5Database)
Set-AppSetting 'PersonnelCnnStr'  ("data source={0};initial catalog={1};integrated security=True;MultipleActiveResultSets=True" -f $DataSource, $PersonnelDatabase)
Set-AppSetting 'T5WebMode'        $Mode
Set-AppSetting 'T5WebLegacyUrl'   $LegacyUrl
$config.Save($webConfig)
Write-Host ("  Web.config patched: source={0}, mode={1}" -f $DataSource, $Mode) -ForegroundColor Green

Restart-WebAppPool -Name $PoolName
& "$env:WINDIR\system32\inetsrv\appcmd.exe" start site /site.name:$SiteName | Out-Null
Start-Sleep -Seconds 2

Write-Host ''
Write-Host ("Console is at http://localhost:{0}/ (Windows credentials of the current user)." -f $Port) -ForegroundColor Green
Write-Host 'Next: .\4-verify-install.ps1 -BaseUrl ' -NoNewline
Write-Host ("http://localhost:{0}" -f $Port) -ForegroundColor Green
exit 0
