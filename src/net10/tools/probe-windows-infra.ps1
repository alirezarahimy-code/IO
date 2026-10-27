# Read-only Windows .NET 10 / IIS / ANCM / SQL tooling inventory.
# NEVER installs a role, stops/restarts a service, touches a database, creates a
# site/pool, provisions a key, connects to a customer instance or downloads SDKs.
$ErrorActionPreference = 'Stop'
$missing = [System.Collections.Generic.List[string]]::new()

function Notice([string]$message) { Write-Host "::notice::NET10 read-only infra | $message" }
function Needs([string]$message) { Write-Host "::warning::NEEDS-INFRA | $message" }

$os = Get-CimInstance Win32_OperatingSystem
Notice ("OS={0} {1}" -f $os.Caption, $os.Version)

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    $missing.Add('preinstalled dotnet SDK/runtime')
} else {
    $sdks = @(& $dotnet.Source --list-sdks)
    $runtimes = @(& $dotnet.Source --list-runtimes)
    $sdk10 = @($sdks | Where-Object { $_ -match '^10\.0\.[0-9]+ ' })
    $web10 = @($runtimes | Where-Object { $_ -match '^Microsoft\.AspNetCore\.App 10\.0\.[0-9]+ ' })
    if ($sdk10.Count -eq 0) { $missing.Add('preinstalled .NET SDK 10 reference packs') }
    if ($web10.Count -eq 0) { $missing.Add('preinstalled ASP.NET Core 10 runtime') }
    Notice ("SDK10={0} | AspNetCore10={1}" -f ($sdk10 -join ' / '), ($web10 -join ' / '))
}

$iis = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\InetStp' -ErrorAction SilentlyContinue
if (-not $iis -or -not $iis.MajorVersion) {
    $missing.Add('IIS web-server role (registry metadata not found)')
    Notice 'IIS=not detected; no role installed by this probe'
} else {
    Notice ("IIS={0}.{1} (metadata only; no site started)" -f $iis.MajorVersion, $iis.MinorVersion)
}
foreach ($service in @('W3SVC', 'WAS')) {
    $entry = Get-Service -Name $service -ErrorAction SilentlyContinue
    Notice ("{0}={1} (observed only)" -f $service, $(if ($entry) { $entry.Status } else { 'not installed' }))
}

# ANCM V2 has multiple conventional file locations across Windows images.
# Search only documented local paths and *read* IIS module registration; an SDK
# runtime or a file alone is not an IIS Hosting Bundle/deployment proof.
$ancmCandidates = @(
    (Join-Path $env:ProgramFiles 'IIS\Asp.Net Core Module\V2\aspnetcorev2.dll'),
    (Join-Path $env:windir 'System32\inetsrv\aspnetcorev2.dll')
)
$ancm64 = @($ancmCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1)
$configPath = Join-Path $env:windir 'System32\inetsrv\config\applicationHost.config'
$ancmRegistered = (Test-Path -LiteralPath $configPath -PathType Leaf) -and
    [bool](Select-String -LiteralPath $configPath -Pattern 'AspNetCoreModuleV2' -Quiet -ErrorAction SilentlyContinue)
Notice ("ANCM_V2_registered={0} (configuration read-only)" -f $ancmRegistered)
if ($ancm64.Count -gt 0) {
    $version = (Get-Item -LiteralPath $ancm64[0]).VersionInfo.FileVersion
    Notice "ANCM_V2_64=$version at $($ancm64[0]) (file only; IIS integration NOT RUN)"
} else {
    $missing.Add('64-bit IIS ASP.NET Core Module V2 / Hosting Bundle (both conventional file paths absent)')
    Notice 'ANCM_V2_64=not found in standard 64-bit paths; no bundle downloaded or installed'
}
if (-not $ancmRegistered) { $missing.Add('AspNetCoreModuleV2 IIS module registration') }

# Read-only executable discovery is not a database probe or provider pin.
$sqlcmd = Get-Command sqlcmd -ErrorAction SilentlyContinue
$localdb = Get-Command sqllocaldb -ErrorAction SilentlyContinue
Notice ("sqlcmd={0} | sqllocaldb={1} (paths only; no SQL connection)" -f `
    $(if ($sqlcmd) { $sqlcmd.Source } else { 'not found' }),
    $(if ($localdb) { $localdb.Source } else { 'not found' }))
# `sqllocaldb versions` reads installed engine registrations only. It does NOT
# create/start/stop an instance or prove the database is SQL Server 2016.
if ($localdb) {
    $installedVersions = @(& $localdb.Source versions 2>&1)
    if ($LASTEXITCODE -eq 0) {
        Notice ("LocalDB installed versions={0} (registry inventory only; NO instance started)" -f
            ($installedVersions -join ' / '))
    } else {
        Needs 'Cannot enumerate installed LocalDB versions (no instance touched).'
    }
}
# Candidate managed provider files from *already installed* .NET 10 SDK and
# the runner's LOCAL cache only. A filename is not a verified net10 driver,
# package source/license, functional SQL connection or redistributable feed.
$dotnetRoot = if ($dotnet) { Split-Path -Parent $dotnet.Source } else { $null }
$sdkPath = if ($dotnetRoot) { Join-Path $dotnetRoot 'sdk' } else { $null }
$sdk10Dirs = if ($sdkPath -and (Test-Path -LiteralPath $sdkPath)) {
    @(Get-ChildItem -LiteralPath $sdkPath -Directory | Where-Object { $_.Name -like '10.0.*' })
} else { @() }
$providerCandidates = [System.Collections.Generic.List[string]]::new()
foreach ($name in @('Microsoft.Data.SqlClient.dll', 'System.Data.SqlClient.dll', 'System.Data.Odbc.dll')) {
    $found = @($sdk10Dirs | ForEach-Object {
        Get-ChildItem -LiteralPath $_.FullName -Filter $name -File -Recurse -ErrorAction SilentlyContinue
    } | Select-Object -First 3)
    $providerCandidates.Add(("{0}={1}" -f $name,
        $(if ($found.Count -gt 0) { ($found.FullName -join ' / ') } else { 'none' })))
}
$cache = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.data.sqlclient'
$providerCandidates.Add(("local NuGet cache={0}" -f
    $(if (Test-Path -LiteralPath $cache -PathType Container) { 'directory exists; pin and audit before any use' } else { 'none' })))
Notice ("installed SDK10/local provider candidates: {0} (file discovery ONLY)" -f
    ($providerCandidates -join ' | '))
Needs 'No SQL Server 2016 two-catalog synthetic integration, tested net10 SQL provider from an approved local feed, or real application-pool rights were demonstrated. SQL gate NOT RUN.'
Needs 'No original TWF 4.28/T5Web 3.5.7 routes, IIS app, Windows-auth principal, offline Hosting Bundle install, rollback or 100-user operational load was executed.'

if ($missing.Count -gt 0) {
    foreach ($item in $missing) { Write-Host "::error::NEEDS-INFRA | $item" }
    exit 2
}
Notice 'Prerequisite file/registry inventory passed ONLY; deploy and IIS/SQL/security/load gates remain blocked/not run.'
exit 0
