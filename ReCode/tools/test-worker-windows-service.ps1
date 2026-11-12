# Real SCM lifecycle smoke in an expendable GitHub Windows runner only.
# No SDK, device traffic, production configuration, real DB credential or grants.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$DotnetPath,
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [switch]$AllowIsolatedCiInstallation
)
$ErrorActionPreference = 'Stop'
if (!$AllowIsolatedCiInstallation -or !$IsWindows -or $env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows') {
    throw 'Actual service smoke requires explicit opt-in on an isolated GitHub Windows runner.'
}
if (Get-Service -Name IODeviceWorker -ErrorAction SilentlyContinue) { throw 'Refusing to touch an existing IODeviceWorker service.' }
foreach ($path in @($DotnetPath, $PublishDirectory)) {
    if (![IO.Path]::IsPathFullyQualified($path) -or !(Test-Path -LiteralPath $path)) { throw 'Absolute existing trusted publish and runtime paths required.' }
}
$directory = Join-Path $env:SystemDrive ('io-worker-ci-' + [guid]::NewGuid().ToString('N'))
$created = $false
$sc = Join-Path $env:SystemRoot 'System32/sc.exe'
try {
    $null = New-Item -Path $directory -ItemType Directory
    Copy-Item -Path (Join-Path $PublishDirectory '*') -Destination $directory -Recurse
    # A deliberately unavailable loopback DB exercises cancellation during DB
    # failure. No native pin/endpoint or execute flag exists in this fixture.
    $configuration = @{
        ConnectionStrings = @{ IO = 'Server=127.0.0.1,14331;Database=IO_CI_UNAVAILABLE;User Id=io_ci_nonexistent;Password=not-a-real-credential;Connect Timeout=1;Encrypt=True;TrustServerCertificate=False' }
        IO = @{ DeviceWorker = @{ ServiceUserId = [guid]::NewGuid().ToString(); AllowedEndpoints = @(); NativeEndpoints = @(); EnableHealthReports = $false } }
    }
    $configuration | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $directory 'appsettings.json') -Encoding utf8NoBOM
    $installer = Join-Path $PSScriptRoot '../deploy/device-worker/install-windows-service.ps1'
    $registration = & $installer -DotnetPath $DotnetPath -WorkerAssemblyPath (Join-Path $directory 'IO.DeviceWorker.dll') -Confirm:$false
    $created = $true
    if (!($registration | Where-Object { $_.installed -eq $true })) { throw 'Installer did not report real SCM registration.' }
    & (Join-Path $env:SystemRoot 'System32/icacls.exe') $directory /grant 'NT SERVICE\IODeviceWorker:(OI)(CI)RX' /T /Q
    if ($LASTEXITCODE -ne 0) { throw 'Could not grant service read/execute on isolated publish directory.' }
    $service = Get-CimInstance Win32_Service -Filter "Name='IODeviceWorker'"
    if ($service.StartName -ne 'NT SERVICE\IODeviceWorker' -or $service.StartMode -ne 'Manual') { throw 'Installer changed the dedicated identity or manual-start boundary.' }
    if ($service.PathName -ne ('"' + $DotnetPath + '" "' + (Join-Path $directory 'IO.DeviceWorker.dll') + '"')) { throw 'SCM executable path differs from the explicit trusted publish.' }
    Start-Service IODeviceWorker
    (Get-Service IODeviceWorker).WaitForStatus([ServiceProcess.ServiceControllerStatus]::Running, [TimeSpan]::FromSeconds(30))
    Start-Sleep -Seconds 3
    if ((Get-Service IODeviceWorker).Status -ne 'Running') { throw 'Published worker did not remain hosted by SCM during unavailable-DB isolation.' }
    Stop-Service IODeviceWorker
    (Get-Service IODeviceWorker).WaitForStatus([ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(40))
    Write-Host 'PASS actual Windows SCM install/start/stop of published worker under virtual service identity with unavailable synthetic DB and no native endpoints; not SDK, device, Linux or deployment acceptance'
} finally {
    if ($created) {
        $service = Get-Service IODeviceWorker -ErrorAction SilentlyContinue
        if ($service -and $service.Status -ne 'Stopped') {
            Stop-Service IODeviceWorker -ErrorAction Stop
            (Get-Service IODeviceWorker).WaitForStatus([ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(40))
        }
        & $sc delete IODeviceWorker
        if ($LASTEXITCODE -ne 0) { throw 'Isolated CI service cleanup failed.' }
        if ($service) { $service.Dispose() }
    }
    if (Test-Path -LiteralPath $directory) { Remove-Item -LiteralPath $directory -Recurse -Force }
}
