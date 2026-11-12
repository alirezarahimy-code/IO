$ErrorActionPreference = 'Stop'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
# Native commands do not automatically terminate PowerShell 7 scripts on failure.
function Run-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet command failed (exit $LASTEXITCODE)" }
}
Run-Dotnet --info
Run-Dotnet build IO.slnx --configuration Release
Run-Dotnet run --project tests/IO.Domain.Tests --configuration Release --no-build
Run-Dotnet run --project tests/IO.DeviceRuntime.Tests --configuration Release --no-build -- --verify
Run-Dotnet run --project tests/IO.Foundation.Tests --configuration Release --no-build
$publish = Join-Path ([System.IO.Path]::GetTempPath()) ('io-publish-' + [guid]::NewGuid().ToString('N'))
try {
    Run-Dotnet publish src/IO.Web --configuration Release --no-restore --output (Join-Path $publish 'web')
    Run-Dotnet publish src/IO.Bootstrap --configuration Release --no-restore --output (Join-Path $publish 'bootstrap')
    Run-Dotnet publish src/IO.DeviceWorker --configuration Release --no-restore --output (Join-Path $publish 'device-worker')
    Run-Dotnet publish src/IO.ZkBridge --configuration Release --no-restore --output (Join-Path $publish 'zk-bridge')
    # Smoke the actual published web executable on this OS, not an HTML mock.
    $env:IO__EnableExperimentalIdentity = 'false'
    $env:ASPNETCORE_URLS = 'http://127.0.0.1:5198'
    $start = [System.Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.ArgumentList.Add((Join-Path $publish 'web/IO.Web.dll'))
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    try {
        $alive = $false
        for ($i = 0; $i -lt 60; $i++) {
            if ($process.HasExited) { throw ('Published host exited: ' + $stderr.GetAwaiter().GetResult()) }
            try {
                $health = Invoke-RestMethod 'http://127.0.0.1:5198/health/live' -TimeoutSec 2
                if ($health.status -eq 'alive') { $alive = $true; break }
            } catch { Start-Sleep -Milliseconds 500 }
        }
        if (!$alive) { throw 'Published host did not become healthy' }
        Write-Host 'PASS published .NET 10 host starts on the current OS (foundation-only; no database test implied)'
    } finally {
        if (!$process.HasExited) { $process.Kill($true) }
        $process.WaitForExit()
        $process.Dispose()
    }
} finally {
    Remove-Item Env:IO__EnableExperimentalIdentity -ErrorAction SilentlyContinue
    Remove-Item Env:ASPNETCORE_URLS -ErrorAction SilentlyContinue
    if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
}
