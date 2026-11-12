using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace IO.DeviceContracts;

public sealed record UsbPreviewLine(int LineNumber, string? PersonIdentifier, DateTimeOffset? OccurredAtUtc, int? MachineNumber,
    int? CheckType, int? VerifyMode, string Status, string PayloadHash, [property:JsonIgnore] string SourceLine)
{
    public override string ToString() => $"USB preview line {LineNumber}: {Status} [personal data redacted]";
}
public sealed record UsbPreview(string FileHash, string Layout, string Calendar, string TimeZoneId, IReadOnlyList<UsbPreviewLine> Lines);

/// <summary>Explicit five-column Personnel reference layout, NOT a universal ZK attlog autodetector.
/// Preview only: no identity creation, attendance write, duplicate deletion or time-zone guessing.</summary>
public static class PersonnelUsbPreview
{
    public const string Layout = "personnel-five-columns-v1";
    public static UsbPreview Parse(ReadOnlySpan<byte> file, string calendar, TimeZoneInfo zone, int expectedMachineNumber)
    {
        if (file.Length is 0 or > 1024*1024 || calendar is not ("gregorian" or "persian") || expectedMachineNumber <= 0)
            throw new ArgumentException("Explicit calendar, machine and bounded nonempty file required.");
        ArgumentNullException.ThrowIfNull(zone);
        string text;
        try { text = new UTF8Encoding(false, true).GetString(file); }
        catch (DecoderFallbackException) { throw new ArgumentException("Only strict UTF-8 is supported; select another verified decoder before importing."); }
        if (text.StartsWith('\uFEFF')) text = text[1..];
        var lines = text.Replace("\r\n","\n",StringComparison.Ordinal).Split('\n');
        if (lines.Length > 10001) throw new ArgumentException("File exceeds the preview row budget.");
        var result = new List<UsbPreviewLine>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var n=0;n<lines.Length;n++)
        {
            var raw=lines[n];
            if (raw.Length==0 && n==lines.Length-1) continue;
            var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
            UsbPreviewLine Invalid(string status)=>new(n+1,null,null,null,null,null,status,hash,raw);
            if (raw.Length is 0 or > 1024 || raw.Any(c=>char.IsControl(c)&&c!='\t')) { result.Add(Invalid("malformed-line"));continue; }
            var fields=raw.Split('\t');
            if (fields.Length!=5) { result.Add(Invalid("layout-mismatch"));continue; }
            if (!Regex.IsMatch(fields[0],"^[0-9]{1,40}$",RegexOptions.CultureInvariant)) { result.Add(Invalid("invalid-person"));continue; }
            bool Number(string s,out int value)=>int.TryParse(s,NumberStyles.None,CultureInfo.InvariantCulture,out value);
            if (!Number(fields[2],out var machine)||machine<=0||!Number(fields[3],out var check)||check>255||!Number(fields[4],out var verify)||verify>255)
            { result.Add(Invalid("invalid-codes"));continue; }
            DateTime local;
            if (!Regex.IsMatch(fields[1],"^[0-9]{4}-[0-9]{2}-[0-9]{2} [0-9]{2}:[0-9]{2}:[0-9]{2}$",RegexOptions.CultureInvariant))
            { result.Add(Invalid("invalid-time"));continue; }
            try
            {
                if(calendar=="gregorian") local=DateTime.SpecifyKind(DateTime.ParseExact(fields[1],"yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture,DateTimeStyles.None),DateTimeKind.Unspecified);
                else
                {
                    var parts=fields[1].Split('-', ' ', ':').Select(s=>int.Parse(s,CultureInfo.InvariantCulture)).ToArray();
                    local=DateTime.SpecifyKind(new PersianCalendar().ToDateTime(parts[0],parts[1],parts[2],parts[3],parts[4],parts[5],0),DateTimeKind.Unspecified);
                }
            }
            catch(ArgumentException) { result.Add(Invalid("invalid-time"));continue; }
            catch(FormatException) { result.Add(Invalid("invalid-time"));continue; }
            DateTimeOffset? utc=null;
            var state=machine!=expectedMachineNumber?"machine-mismatch":zone.IsInvalidTime(local)?"invalid-local-time":zone.IsAmbiguousTime(local)?"ambiguous-local-time":"parsed-requires-mapping-review";
            if(state=="parsed-requires-mapping-review")
            {
                utc=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local,zone));
                if(!seen.Add(hash))state="duplicate-in-file-requires-review";
            }
            // Every repeated row is retained; a content hash is NOT declared a device-native event id.
            result.Add(new(n+1,fields[0],utc,machine,check,verify,state,hash,raw));
        }
        return new(Convert.ToHexString(SHA256.HashData(file)),Layout,calendar,zone.Id,result);
    }
}
