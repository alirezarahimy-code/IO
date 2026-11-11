using System.Net;
using System.Net.Sockets;

namespace IO.Infrastructure.Devices;

/// <summary>A TCP handshake is NOT authenticated device connection, model verification or SDK support.</summary>
public static class TcpConnectionDiagnostic
{
    public sealed record AllowedEndpoint(Guid DeviceId, string Address, int Port);

    public static async Task<string> RunAsync(DeviceConnections.Claim claim, IReadOnlyList<AllowedEndpoint> allowed, CancellationToken stoppingToken = default)
    {
        if (claim.TimeoutSeconds is < 1 or > 60) throw new ArgumentException("Invalid timeout.");
        if (claim.Profile != "zk-standalone") return "unsupported";
        // No DNS rebinding, wildcard hosts, subnet scans or metadata-only arbitrary outbound destinations.
        if (!IPAddress.TryParse(claim.Host, out var address) || claim.Port is not (> 0 and <= 65535) ||
            !allowed.Any(x => x.DeviceId == claim.DeviceId && IPAddress.TryParse(x.Address, out var pinned) && pinned.Equals(address) && x.Port == claim.Port))
            return "endpoint-denied";
        using var socket = new TcpClient(address.AddressFamily);
        // Socket disposal aborts the actual portable transport even after the outer deadline returns.
        return await TcpProbeDeadline.RunAsync(token => socket.ConnectAsync(address, claim.Port.Value, token).AsTask(), claim.TimeoutSeconds, stoppingToken);
    }
}
