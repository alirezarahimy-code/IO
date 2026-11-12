# No SDK registration, migration, credential input, grant, automatic start, or recovery replay.
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)][string]$DotnetPath,
    [Parameter(Mandatory = $true)][string]$WorkerAssemblyPath,
    [switch]$ValidateOnly
)
$ErrorActionPreference = 'Stop'
if (!$IsWindows) { throw 'Windows service installation requires PowerShell 7 on Windows.' }
foreach ($path in @($DotnetPath, $WorkerAssemblyPath)) {
    if (![System.IO.Path]::IsPathFullyQualified($path) -or $path.StartsWith('\\') -or $path -match '[\x00-\x1F"]' -or !(Test-Path -LiteralPath $path -PathType Leaf)) {
        throw 'Existing absolute local trusted deployment files are required.'
    }
}
if ([System.IO.Path]::GetFileName($DotnetPath) -ine 'dotnet.exe' -or [System.IO.Path]::GetFileName($WorkerAssemblyPath) -ine 'IO.DeviceWorker.dll') {
    throw 'Use the trusted dotnet.exe host and the published IO.DeviceWorker.dll.'
}
$directory = [System.IO.Path]::GetDirectoryName($WorkerAssemblyPath)
foreach ($file in @('IO.DeviceWorker.runtimeconfig.json', 'IO.DeviceWorker.deps.json')) {
    if (!(Test-Path -LiteralPath (Join-Path $directory $file) -PathType Leaf)) { throw 'A complete published worker directory is required.' }
}
if ($ValidateOnly) {
    [pscustomobject]@{ validated = $true; serviceName = 'IODeviceWorker'; installed = $false; started = $false; hardwareVerified = $false }
    return
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Elevated installation permission is required.' }
} finally { $identity.Dispose() }
if (Get-Service -Name 'IODeviceWorker' -ErrorAction SilentlyContinue) { throw 'IODeviceWorker already exists; this installer never overwrites its identity or settings.' }
if ($PSCmdlet.ShouldProcess('IODeviceWorker', 'Create manual-start service under its dedicated virtual service account')) {
    $sc = Join-Path $env:SystemRoot 'System32/sc.exe'
    $binaryPath = '"' + $DotnetPath + '" "' + $WorkerAssemblyPath + '"'
    & $sc create IODeviceWorker binPath= $binaryPath start= demand obj= 'NT SERVICE\IODeviceWorker' DisplayName= 'IO Device Worker'
    if ($LASTEXITCODE -ne 0) { throw "Service registration failed (exit $LASTEXITCODE)." }
    [pscustomobject]@{ validated = $true; serviceName = 'IODeviceWorker'; installed = $true; started = $false; hardwareVerified = $false }
}
