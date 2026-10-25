#requires -Version 5.1
#requires -RunAsAdministrator
<#
.SYNOPSIS
    Enable IIS and the features the T5Web admin console actually needs.

.DESCRIPTION
    Idempotent: every feature is checked before it is enabled, so a second run
    changes nothing. Deliberately *not* enabled:
      * FTP, WebDAV, SMTP - not used by the portal and they widen the attack surface;
      * Anonymous Authentication - the console authenticates Windows users only;
      * Directory Browsing - the app has no static file gallery.

    Windows Authentication *is* enabled: Web.config says <authentication mode="Windows" />
    and <deny users="?" />, so anonymous requests must be refused by IIS before MVC runs.

.EXAMPLE
    .\1-install-iis.ps1
#>
[CmdletBinding()]
param(
    [switch]$NoRebootPrompt
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$features = @(
    'IIS-WebServerRole',
    'IIS-WebServer',
    'IIS-CommonHttpFeatures',
    'IIS-StaticContent',
    'IIS-DefaultDocument',
    'IIS-HttpErrors',
    'IIS-RequestFiltering',
    'IIS-HttpLogging',
    'IIS-ApplicationDevelopment',
    'IIS-NetFxExtensibility45',
    'IIS-ASPNET45',
    'IIS-ISAPIExtensions',
    'IIS-ISAPIFilter',
    'IIS-WebServerManagementTools',
    'IIS-ManagementConsole',
    'IIS-WindowsAuthentication'
)

$enabled = @()
foreach ($name in $features) {
    $feature = Get-WindowsOptionalFeature -Online -FeatureName $name -ErrorAction SilentlyContinue
    if ($null -eq $feature) {
        Write-Host ("  {0,-38} not available on this SKU (skipped)" -f $name) -ForegroundColor DarkYellow
        continue
    }
    if ($feature.State -eq 'Enabled') {
        Write-Host ("  {0,-38} already enabled" -f $name) -ForegroundColor DarkGray
        continue
    }
    Write-Host ("  {0,-38} enabling..." -f $name)
    $result = Enable-WindowsOptionalFeature -Online -FeatureName $name -All -NoRestart
    if ($result.RestartNeeded) { $enabled += $name }
}

Import-Module WebAdministration -ErrorAction Stop

# The IIS: drive is created by the WebAdministration provider, which only exists in
# Windows PowerShell 5.1. Running this script from PowerShell 7 gives a much less
# helpful "Cannot find drive. A drive with the name 'IIS' does not exist." further
# down, so say it here instead.
if (-not (Get-PSDrive -Name IIS -ErrorAction SilentlyContinue)) {
    throw 'The IIS: drive is missing. Run this script in Windows PowerShell 5.1 (powershell.exe), not PowerShell 7 (pwsh).'
}

# Anonymous out, Windows in - at the server level, so a new site cannot forget it.
Set-WebConfigurationProperty -Filter '/system.webServer/security/authentication/anonymousAuthentication' `
    -Name enabled -Value $false -PSPath 'IIS:\' -ErrorAction SilentlyContinue
Set-WebConfigurationProperty -Filter '/system.webServer/security/authentication/windowsAuthentication' `
    -Name enabled -Value $true -PSPath 'IIS:\' -ErrorAction SilentlyContinue

# The console is an administration tool; directory listing it is a data leak, not a feature.
Set-WebConfigurationProperty -Filter '/system.webServer/directoryBrowse' `
    -Name enabled -Value $false -PSPath 'IIS:\' -ErrorAction SilentlyContinue

$iis = Get-Service W3SVC -ErrorAction SilentlyContinue
if ($null -ne $iis) {
    if ($iis.Status -ne 'Running') { Start-Service W3SVC }
    Write-Host ("  W3SVC                               {0}" -f (Get-Service W3SVC).Status) -ForegroundColor Green
}

Write-Host ''
if ($enabled.Count -gt 0 -and -not $NoRebootPrompt) {
    Write-Warning ("A reboot is pending for: {0}. Reboot before deploying the site." -f ($enabled -join ', '))
    exit 3010
}

Write-Host 'IIS is ready. Next: .\2-install-sqlserver.ps1' -ForegroundColor Green
exit 0
