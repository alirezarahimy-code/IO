using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using IO.DeviceContracts;

// TEST ONLY. This is a private JSON laboratory protocol, NOT ZK's wire protocol,
// not a replacement DLL and never selectable by the production worker/bridge.
internal static class VirtualDeviceLab
{
    public static async Task RelayAsync(string request, ZkProbeInput connection)
    {
        if (!IPAddress.IsLoopback(IPAddress.Parse(connection.Address))) throw new InvalidDataException();
        using var client = new TcpClient();
        await client.ConnectAsync(connection.Address, connection.Port);
        await using var stream = client.GetStream();
        var bytes = Encoding.UTF8.GetBytes(request + "\n");
        await stream.WriteAsync(bytes);
        // The production supervisors, not this test relay, validate and bound frames.
        await stream.CopyToAsync(Console.OpenStandardOutput());
    }

    public static async Task VerifyAsync()
    {
        var count = 0;
        void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException(name);
            Console.WriteLine("PASS virtual-lab " + name); count++;
        }
        var host = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        var assembly = typeof(VirtualDeviceLab).Assembly.Location;
        await using var a = new Device("virtual-lab-A");
        await using var b = new Device("virtual-lab-B");
        var pinA = a.Pin; var pinB = b.Pin;
        Task<string> Probe(ZkProbeInput pin, int seconds = 5) => NativeProcessSupervisor.RunAsync(host, assembly, pin, seconds);
        async Task<(NativeAttendanceReadSupervisor.Result Result, List<ZkAttendanceRaw> Rows)> Read(Device device, CancellationToken ct = default, int seconds = 5)
        {
            var rows = new List<ZkAttendanceRaw>();
            var result = await NativeAttendanceReadSupervisor.RunAsync(host, assembly, new(device.Pin, true, true), seconds,
                (batch, token) => { token.ThrowIfCancellationRequested(); rows.AddRange(batch); return Task.CompletedTask; }, ct);
            return (result, rows);
        }
        Task<ZkUserWriteOutput> Write(Device device, ZkUser? expected, ZkUser desired) => NativeProcessSupervisor.RunUserWriteAsync(host, assembly, new(device.Pin, true, true, expected, desired), 5);
        async Task<List<ZkUser>> Users(Device device)
        {
            var users = new List<ZkUser>();
            var result = await NativeUserReadSupervisor.RunAsync(host, assembly, new(device.Pin, true, true), 5,
                (batch, token) => { token.ThrowIfCancellationRequested(); users.AddRange(batch); return Task.CompletedTask; });
            if (result.State != "succeeded") throw new InvalidOperationException("Virtual user read failed");
            return users;
        }
        Check(await Probe(pinA) == "sdk-serial-matched" && await Probe(pinB) == "sdk-serial-matched" && pinA.Port != pinB.Port,
            "two independent loopback endpoints pass through actual child-process supervisors");
        Check(await Probe(pinA with { ExpectedSerial = "virtual-lab-other" }) == "sdk-identity-mismatch" &&
              await Probe(pinA with { CommKey = 7 }) == "sdk-identity-mismatch", "wrong serial or synthetic key never identifies a device");
        var first = await Read(a); var again = await Read(a);
        Check(first.Result.State == "succeeded" && first.Result.Count == 103 && first.Rows.Count == 103 && first.Rows.Count(x => x == first.Rows[0]) == 2 && first.Rows.SequenceEqual(again.Rows),
            "device state survives separate child processes and repeated extraction preserves exact tuples and multiplicity");
        a.Append(new("00001", 1, 4, 2026, 11, 8, 7, 1, 0, 999));
        var late = await Read(a); var separate = await Read(b);
        Check(late.Result.State == "succeeded" && late.Rows.Count == 104 && late.Rows.Take(103).SequenceEqual(first.Rows) && separate.Rows.Count == 103,
            "late-arriving historical data is appended without changing original tuples or the second device");
        a.Fault = "truncate";
        var partial = await Read(a);
        Check(partial.Result.State == "bridge-failed" && partial.Rows.Count == 50,
            "real TCP disconnect after a delivered batch preserves partial rows but cannot report a complete read");
        a.Fault = "none";
        var recovery = await Read(a);
        Check(recovery.Result.State == "succeeded" && recovery.Rows.SequenceEqual(late.Rows), "fresh child recovers after disconnect with unchanged device history");
        a.Fault = "wrong-count";
        Check((await Read(a)).Result.State == "bridge-failed", "incorrect terminal count cannot acknowledge an extraction");
        a.Fault = "wrong-serial";
        var wrong = await Read(a);
        Check(wrong.Result.State == "bridge-failed" && wrong.Rows.Count == 0, "stream serial mismatch is rejected before any tuple callback");
        a.Fault = "hang";
        var hung = Read(a, seconds: 1);
        var healthy = await Read(b);
        Check(healthy.Result.State == "succeeded" && (await hung).Result.State == "timed-out", "an unresponsive device times out while a separate device completes");
        using (var stop = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
            Check((await Read(a, stop.Token, 10)).Result.State == "interrupted", "cancellation interrupts a hanging child instead of fabricating completion");
        a.Fault = "none";
        var infoInput = new ZkOperationInput(pinA, "information.read", null, false);
        var info = await NativeProcessSupervisor.RunOperationAsync(host, assembly, infoInput, 5);
        Check(info.State == "succeeded" && info.Information is { Product: "IO-virtual-not-ZK", Records: 104, RecordCapacity: null },
            "typed information reads preserve synthetic identity and unavailable capacity without inventing a vendor model");
        var settingsInput = infoInput with { Operation = "settings.read", AllowAdvancedSettingsRead = true };
        var settings = await NativeProcessSupervisor.RunOperationAsync(host, assembly, settingsInput, 5);
        Check(settings.State == "succeeded" && settings.Settings is { OneToOne: 0, OneToN: null, RepeatTime: int.MinValue, SleepTime: int.MaxValue },
            "raw setting reads preserve zero unavailable and signed values across TCP and the child boundary");
        var clockInput = infoInput with { Operation = "clock.set", AllowClockSet = true, ClockLocal = "2026-11-09 11:12:13" };
        var clockResult = await NativeProcessSupervisor.RunOperationAsync(host, assembly, clockInput, 5);
        var clockReadback = await NativeProcessSupervisor.RunOperationAsync(host, assembly, infoInput, 5);
        Check(clockResult.State == "succeeded" && clockReadback.Information?.ClockLocal == clockInput.ClockLocal && b.ClockLocal == "2026-11-08 10:00:00",
            "explicit synthetic clock update can be read back without changing another endpoint clock");
        Check((await NativeProcessSupervisor.RunOperationAsync(host, assembly, clockInput with { AllowClockSet = false }, 5)).State == "unsupported" &&
              (await NativeProcessSupervisor.RunOperationAsync(host, assembly, settingsInput with { AllowAdvancedSettingsRead = false }, 5)).State == "unsupported" &&
              (await NativeProcessSupervisor.RunOperationAsync(host, assembly, settingsInput with { Operation = "settings.write" }, 5)).State == "unsupported",
            "virtual endpoints cannot bypass clock or settings opt-in and do not enable advanced setting writes");
        var user = new ZkUser("00001", "Synthetic", "1234", 0, true);
        Check((await Write(a, null, user)).State == "succeeded" && (await Write(b, null, user)).State == "succeeded" &&
              (await Users(a)).Single() == user && (await Users(b)).Single() == user,
            "ordinary synthetic user creation and read-back work independently on two stateful endpoints");
        var changed = user with { Name = "Changed" };
        a.Fault = "drop-write-ack";
        var unknown = await Write(a, user, changed);
        Check(unknown.State == "unknown" && a.Mutations == 2 && (await Users(a)).Single() == changed && (await Users(b)).Single() == user,
            "write applied before lost acknowledgement remains unknown; explicit subsequent read discovers it without affecting another device");
        a.Fault = "none";
        Check((await Write(a, user, changed)).State == "conflict" && a.Mutations == 2,
            "replaying a stale approved snapshot after unknown outcome never silently overwrites or re-applies a write");
        Check((await Write(a, changed, changed)).State == "unchanged" && a.Mutations == 2,
            "fresh read-back can establish unchanged state without a new SDK mutation");
        var admin = user with { Privilege = 3 };
        Check((await Write(a, changed, admin)).State == "unsupported" && a.Mutations == 2,
            "virtual lab cannot bypass production ordinary-user privilege restrictions");
        Console.WriteLine($"PASS {count} stateful TCP virtual-device laboratory checks; private test protocol only; realSdkExecuted=false hardwareVerified=false");
    }

    private sealed class Device : IAsyncDisposable, IZkUserWriteSession
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource shutdown = new();
        private readonly List<Task> clients = new();
        private readonly Task accepting;
        private readonly object gate = new();
        private readonly List<ZkAttendanceRaw> logs = new();
        private readonly Dictionary<string, ZkUserWriteExisting> users = new(StringComparer.Ordinal);
        public ZkProbeInput Pin { get; }
        public volatile string Fault = "none";
        public string ClockLocal { get; private set; } = "2026-11-08 10:00:00";
        public int Mutations { get; private set; }
        public Device(string serial)
        {
            // Synthetic test data only. Repeated identical tuples deliberately retain multiplicity.
            for (int i = 0; i < 102; i++) logs.Add(new("00001", 1, 4, 2026, 11, 8, 8, 0, i % 60, i / 2));
            logs.Add(logs[0]);
            listener.Start();
            Pin = new(Guid.NewGuid(), "127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, 1, serial, 123456);
            accepting = AcceptAsync();
        }
        public void Append(ZkAttendanceRaw row) { lock (gate) logs.Add(row); }
        private async Task AcceptAsync()
        {
            try
            {
                while (!shutdown.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(shutdown.Token);
                    clients.Add(ServeAsync(client));
                }
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        }
        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true);
                    await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
                    var chars = new StringBuilder(); var buffer = new char[1];
                    while (await reader.ReadAsync(buffer.AsMemory(), shutdown.Token) != 0 && buffer[0] != '\n')
                    { if (chars.Length >= 8192) throw new InvalidDataException(); chars.Append(buffer[0]); }
                    var text = chars.ToString(); using var doc = JsonDocument.Parse(text);
                    var nested = doc.RootElement.TryGetProperty("Connection", out var connection);
                    var pin = JsonSerializer.Deserialize<ZkProbeInput>((nested ? connection : doc.RootElement).GetRawText())!;
                    var fault = Fault;
                    Task Frame<T>(T frame) => writer.WriteLineAsync(JsonSerializer.Serialize(frame));
                    // Test transport authentication is deliberately not a claim about vendor authentication.
                    if (pin.ExpectedSerial != Pin.ExpectedSerial || pin.CommKey != Pin.CommKey)
                    { await writer.WriteLineAsync("sdk-identity-mismatch"); return; }
                    if (fault == "hang") { await Task.Delay(Timeout.Infinite, shutdown.Token); return; }
                    if (doc.RootElement.TryGetProperty("ReadAttendance", out _))
                    {
                        ZkAttendanceRaw[] snapshot; lock (gate) snapshot = logs.ToArray();
                        await Frame(new ZkAttendanceFrame("start", Serial: fault == "wrong-serial" ? "different" : Pin.ExpectedSerial));
                        // Fill more than an OS pipe buffer after the rejected header.
                        // A plain WhenAll would wait for the blocked relay until timeout.
                        if (fault == "wrong-serial")
                            for (int i = 0; i < 8192; i++) await Frame(new ZkAttendanceFrame("record", Record: snapshot[0]));
                        for (int i = 0; i < snapshot.Length; i++)
                        {
                            await Frame(new ZkAttendanceFrame("record", Record: snapshot[i]));
                            if (fault == "truncate" && i == 49) return;
                        }
                        await Frame(new ZkAttendanceFrame("end", Count: snapshot.Length + (fault == "wrong-count" ? 1 : 0), State: "succeeded"));
                    }
                    else if (doc.RootElement.TryGetProperty("WriteUser", out _))
                    {
                        ZkUserWriteOutput output;
                        lock (gate) output = ZkUserWriteExecutor.Execute(JsonSerializer.Deserialize<ZkUserWriteInput>(text)!, this);
                        if (fault != "drop-write-ack") await Frame(output);
                    }
                    else if (doc.RootElement.TryGetProperty("ReadUsers", out _))
                    {
                        ZkUser[] snapshot; lock (gate) snapshot = users.Values.Select(x => x.User).ToArray();
                        await Frame(new ZkUserFrame("start", Serial: Pin.ExpectedSerial));
                        foreach (var user in snapshot) await Frame(new ZkUserFrame("user", User: user));
                        await Frame(new ZkUserFrame("end", Count: snapshot.Length, State: "succeeded"));
                    }
                    else if (doc.RootElement.TryGetProperty("Operation", out _))
                    {
                        var operation = JsonSerializer.Deserialize<ZkOperationInput>(text)!;
                        ZkOperationOutput output;
                        lock (gate)
                        {
                            if (!operation.IsValid()) output = new("unsupported");
                            else if (operation.Operation == "information.read")
                                output = new("succeeded", new(Pin.ExpectedSerial, "IO-virtual-not-ZK", null, null, ClockLocal,
                                    logs.Count, null, users.Count, null, null, null, null, null, null, operation.AllowClockSet));
                            else if (operation.Operation == "settings.read")
                                output = new("succeeded", Settings: new(ZkAdvancedSettings.Version, Pin.ExpectedSerial, 0, null, int.MinValue, int.MaxValue, 2, -1));
                            else { ClockLocal = operation.ClockLocal!; output = new("succeeded"); }
                        }
                        await Frame(output);
                    }
                    else await writer.WriteLineAsync("sdk-serial-matched");
                }
                catch (IOException) { /* Peer may be killed by the supervisor fault scenario. */ }
                catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
            }
        }
        public ZkUserWriteExisting? ReadExact(string id) => users.GetValueOrDefault(id);
        public bool SetCard(string card) => true;
        public bool Write(ZkUser desired, string password) { users[desired.PersonIdentifier] = new(desired, password); Mutations++; return true; }
        public async ValueTask DisposeAsync()
        {
            shutdown.Cancel(); listener.Stop();
            await accepting; await Task.WhenAll(clients); shutdown.Dispose();
        }
    }
}
