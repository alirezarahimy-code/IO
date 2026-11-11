using IO.Infrastructure.Devices;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// A separate process, never a COM/native library inside the web host.
var builder = Host.CreateApplicationBuilder(args);
var connection = builder.Configuration.GetConnectionString("IO");
if (string.IsNullOrWhiteSpace(connection) || !Guid.TryParse(builder.Configuration["IO:DeviceWorker:ServiceUserId"], out var service) || service == Guid.Empty)
    throw new InvalidOperationException("An explicit database connection and provisioned service identity are required.");
var targets = builder.Configuration.GetSection("IO:DeviceWorker:AllowedEndpoints").Get<TcpConnectionDiagnostic.AllowedEndpoint[]>() ?? [];
if (targets.Any(t => t.DeviceId == Guid.Empty || !System.Net.IPAddress.TryParse(t.Address, out _) || t.Port is < 1 or > 65535))
    throw new InvalidOperationException("Invalid pinned diagnostic endpoint.");
builder.Logging.ClearProviders(); // No EF/provider exception strings or configuration secrets in stdout.
builder.Services.AddIoIdentityStorage(connection);
builder.Services.AddSingleton(new WorkerConfiguration(service, targets));
builder.Services.AddHostedService<DiagnosticWorker>();
await builder.Build().RunAsync();

internal sealed record WorkerConfiguration(Guid ServiceUserId, TcpConnectionDiagnostic.AllowedEndpoint[] AllowedEndpoints);
internal sealed class DiagnosticWorker(IServiceScopeFactory scopes, WorkerConfiguration configuration) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Loop(stoppingToken)));

    private async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                DeviceConnections.Claim? claim;
                await using (var scope = scopes.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<IoDbContext>();
                    var cutoff = DateTimeOffset.UtcNow.AddMinutes(-2);
                    var available = await db.DeviceConnections.AnyAsync(x => x.State == "pending" || (x.State == "running" && x.ClaimedAtUtc < cutoff), ct);
                    claim = available ? await scope.ServiceProvider.GetRequiredService<DeviceConnections>().ClaimAsync(configuration.ServiceUserId, ct) : null;
                }
                if (claim is null) { await Task.Delay(TimeSpan.FromSeconds(3), ct); continue; }
                var result = await TcpConnectionDiagnostic.RunAsync(claim, configuration.AllowedEndpoints, ct);
                await using var finish = scopes.CreateAsyncScope();
                await finish.ServiceProvider.GetRequiredService<DeviceConnections>().CompleteAsync(configuration.ServiceUserId, claim.Id, claim.Token, result, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception)
            {
                // Failure of one test or revoked service grant must not terminate the other slots.
                // Claimed-but-uncommitted results become interrupted via the durable expiry rule.
                Console.Error.WriteLine("Device diagnostic unavailable; check database, service grant and configuration. No automatic command replay.");
                try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            }
        }
    }
}
