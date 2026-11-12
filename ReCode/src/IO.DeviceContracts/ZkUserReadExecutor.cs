using System.Globalization;
namespace IO.DeviceContracts;

public static class ZkUserReadFormats
{
    public const string Ssr = "ssr-v1", Legacy = "legacy-int32-v1";
    public static bool IsKnown(string? value) => value is Ssr or Legacy;
    public static bool IsCanonicalLegacyIdentity(string? value) => int.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out var number) && number>=0 && value==LegacyIdentity(number);
    // Legacy SDK exposes an integer, not the original badge text. Never pad it or
    // normalize an SSR string to pretend the two identity contracts are equal.
    public static string LegacyIdentity(int value) => value >= 0 ? value.ToString(CultureInfo.InvariantCulture) : throw new ArgumentException("Negative legacy identifier.");
}
public interface IZkUserReadSession
{
    string? Serial();
    int? UserCount();
    bool Begin();
    ZkUser? Next(string format);
}
public static class ZkUserReadExecutor
{
    public static void Execute(ZkUserReadInput input, IZkUserReadSession session, Action<ZkUserFrame> emit)
    {
        long count = 0;
        void End(string state) => emit(new("end", Count: count, State: state));
        if (!input.IsValid()) { End("unsupported"); return; }
        try
        {
            if (session.Serial() != input.Connection.ExpectedSerial) { End("identity-mismatch"); return; }
            var before = session.UserCount();
            if (before is null or < 0 || !session.Begin()) { End("failed"); return; }
            emit(new("start", Serial: input.Connection.ExpectedSerial,ReadFormat: input.ReadFormat));
            while (session.Next(input.ReadFormat) is { } user)
            {
                if (!user.IsValid() || (input.ReadFormat==ZkUserReadFormats.Legacy&&!ZkUserReadFormats.IsCanonicalLegacyIdentity(user.PersonIdentifier))) { End("incomplete"); return; }
                emit(new("user", User: user)); count++;
            }
            var after = session.UserCount();
            End(session.Serial() != input.Connection.ExpectedSerial ? "failed" : before == after && after == count ? "succeeded" : "incomplete");
        }
        catch { End("failed"); }
    }
}
