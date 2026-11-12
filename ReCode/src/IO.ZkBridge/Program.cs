using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using IO.DeviceContracts;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // No database, network discovery, receive/send, licensing, enrollment or destructive operation here.
        try
        {
            using var stdin = Console.OpenStandardInput();
            using var bytes = new MemoryStream();
            var buffer = new byte[1024]; int count;
            while ((count = stdin.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (bytes.Length + count > 8192) return Result("invalid-input");
                bytes.Write(buffer, 0, count);
            }
            var input = JsonSerializer.Deserialize<ZkProbeInput>(new UTF8Encoding(false, true).GetString(bytes.ToArray()));
            if (input is null || !input.IsValid()) return Result("invalid-input");
            if (!OperatingSystem.IsWindows()) return Result("unsupported");
            return Result(ProbeWindows(input));
        }
        catch { return Result("sdk-unavailable"); } // Never print native exceptions or input secrets.
    }

    private static int Result(string state) { Console.Out.WriteLine(state); return 0; }

    [SupportedOSPlatform("windows")]
    private static string ProbeWindows(ZkProbeInput input)
    {
        object? instance = null; bool connected = false;
        try
        {
            // CLSID and method sequence independently transcribed from Personnel.ZK.ZKDevice / IZKEM.
            // The SDK must be legitimately installed in a worker with matching architecture; never auto-register DLLs.
            var type = Type.GetTypeFromCLSID(new Guid("00853A19-BD51-419B-9269-2DABE57EB61F"), throwOnError: true)!;
            instance = Activator.CreateInstance(type) ?? throw new InvalidOperationException();
            dynamic sdk = instance;
            if (!(bool)sdk.SetCommPassword(input.CommKey)) return "unreachable";
            connected = sdk.Connect_Net(input.Address, input.Port);
            if (!connected) return "unreachable";
            string serial;
            if (!(bool)sdk.GetSerialNumber(input.MachineNumber, out serial) || string.IsNullOrWhiteSpace(serial) || serial.Length > 100 || serial.Any(char.IsControl))
                return "sdk-identity-mismatch";
            // Serial pin is operational identity, not a claim that this legacy protocol is cryptographically authenticated.
            return string.Equals(serial.Trim(), input.ExpectedSerial, StringComparison.Ordinal) ? "sdk-serial-matched" : "sdk-identity-mismatch";
        }
        catch { return "sdk-unavailable"; }
        finally
        {
            if (instance is not null)
            {
                try { if (connected) ((dynamic)instance).Disconnect(); } catch { }
                try { if (Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance); } catch { }
            }
        }
    }
}
