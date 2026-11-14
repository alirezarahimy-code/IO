using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace IO.DeviceContracts;

/// <summary>Classic seven-column ATTLOG wire data only. Not device authentication or attendance ingestion.</summary>
public sealed record ZkPushAttendanceRow(int LineNumber,string Status,string PayloadHash,string? Pin=null,
    string? LocalTime=null,DateTimeOffset? OccurredAtUtc=null,string? PunchState=null,string? VerifyMode=null,
    string? WorkCode=null,string? Reserved1=null,string? Reserved2=null,int? MultiplicityRank=null)
{
    public override string ToString()=>$"PUSH row {LineNumber}: {Status} [personal data redacted]";
}

public sealed class ZkPushAttendanceBatch
{
    public string Contract=>ZkPushAttendanceCodec.Contract;
    public string Serial {get;}
    // Stamp is an opaque vendor cursor, NOT an authenticated nonce, timestamp or event identifier.
    public string? Stamp {get;}
    public string PayloadHash {get;}
    public string TimeZoneId {get;}
    private readonly byte[] original;
    public ReadOnlyMemory<byte> Original=>new((byte[])original.Clone());
    public IReadOnlyList<ZkPushAttendanceRow> Rows {get;}
    public bool FullyParsed=>Rows.Count>0&&Rows.All(x=>x.Status=="parsed-requires-mapping-review");
    internal ZkPushAttendanceBatch(string serial,string? stamp,byte[] bytes,string zone,ZkPushAttendanceRow[] rows)
    {
        Serial=serial;Stamp=stamp;original=(byte[])bytes.Clone();TimeZoneId=zone;
        PayloadHash=Convert.ToHexString(SHA256.HashData(original));Rows=Array.AsReadOnly(rows);
    }
    public override string ToString()=>$"PUSH batch: {Rows.Count} rows [serial, cursor and personal data redacted]";
}

/// <summary>Explicit Attendance PUSH 20200325 section11.2, Gregorian UTF8 seven-tab-column profile.
/// No autodetection of Security PUSH rtlog, five-column USB, biometrics, compressed or encrypted bodies.
/// Bounds protect a single message; they do not limit registered devices, users or total history.</summary>
public static class ZkPushAttendanceCodec
{
    public const string Contract="zk-attendance-push-20200325-attlog-seven-utf8-v1";
    public const int MaximumBytes=1024*1024,MaximumRows=10000,MaximumLineCharacters=1024;
    public static ZkPushAttendanceBatch Decode(string serial,string? stamp,string table,ReadOnlySpan<byte> body,TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if(table!="ATTLOG"||!Text(serial,100,false)||stamp is not null&&!Text(stamp,200,true)||body.Length is 0 or >MaximumBytes)
            throw new ArgumentException("Explicit serial, opaque bounded cursor and bounded nonempty body required.");
        string text;
        try{text=new UTF8Encoding(false,true).GetString(body);}
        catch(DecoderFallbackException){throw new ArgumentException("Unsupported PUSH body encoding; no fallback decoder.");}
        // Unlike the USB file parser, this wire profile does not silently strip a BOM.
        if(text.StartsWith('\uFEFF'))throw new ArgumentException("BOM is not part of the selected PUSH wire profile.");
        var lines=text.Replace("\r\n","\n",StringComparison.Ordinal).Split('\n');
        var length=lines.Length-(lines[^1].Length==0?1:0);
        if(length is 0 or >MaximumRows)throw new ArgumentException("PUSH message exceeds its record budget or contains no records.");
        var rows=new List<ZkPushAttendanceRow>(length);
        var multiplicity=new Dictionary<string,int>(StringComparer.Ordinal);
        for(var i=0;i<length;i++)
        {
            var raw=lines[i];var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
            ZkPushAttendanceRow Invalid(string status)=>new(i+1,status,hash);
            if(raw.Length is 0 or >MaximumLineCharacters||raw.Any(c=>char.IsControl(c)&&c!='\t'))
            {rows.Add(Invalid("malformed-line"));continue;}
            var f=raw.Split('\t');
            if(f.Length!=7){rows.Add(Invalid("layout-mismatch"));continue;}
            if(!Text(f[0],128,false)){rows.Add(Invalid("invalid-person"));continue;}
            // Raw vendor codes stay raw, including leading zeros, unknown codes and reserved values.
            // This decoder does not infer IN/OUT, privilege, payroll, matching or employee identity.
            if(!Text(f[2],128,false)||!Text(f[3],128,false)||!Text(f[4],128,true)||!Text(f[5],128,true)||!Text(f[6],128,true))
            {rows.Add(Invalid("invalid-fields"));continue;}
            if(!DateTime.TryParseExact(f[1],"yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture,DateTimeStyles.None,out var local)||local.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture)!=f[1])
            {rows.Add(Invalid("invalid-time"));continue;}
            local=DateTime.SpecifyKind(local,DateTimeKind.Unspecified);
            if(zone.IsInvalidTime(local)){rows.Add(Invalid("nonexistent-local-time"));continue;}
            if(zone.IsAmbiguousTime(local)){rows.Add(Invalid("ambiguous-local-time"));continue;}
            DateTimeOffset utc;
            try{utc=new(TimeZoneInfo.ConvertTimeToUtc(local,zone));if(TimeZoneInfo.ConvertTimeFromUtc(utc.UtcDateTime,zone)!=local){rows.Add(Invalid("invalid-time"));continue;}}
            catch(ArgumentException){rows.Add(Invalid("invalid-time"));continue;}
            var rank=multiplicity.TryGetValue(hash,out var previous)?previous+1:1;multiplicity[hash]=rank;
            rows.Add(new(i+1,"parsed-requires-mapping-review",hash,f[0],f[1],utc,f[2],f[3],f[4],f[5],f[6],rank));
        }
        return new(serial,stamp,body.ToArray(),zone.Id,rows.ToArray());
    }
    // A pure validation helper for a FUTURE trusted SQL-commit caller, not a receipt/authentication API.
    // Receipt id/hash/count must come from the committed journal, never request parameters.
    public static string? FormatCommittedAcknowledgement(ZkPushAttendanceBatch batch,Guid committedReceiptId,string? committedHash,int committedRows)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return batch.FullyParsed&&committedReceiptId!=Guid.Empty&&committedHash==batch.PayloadHash&&committedRows==batch.Rows.Count
            ?"OK: "+committedRows.ToString(CultureInfo.InvariantCulture):null;
    }
    private static bool Text(string? value,int maximum,bool empty)=>value is not null&&value.Length<=maximum&&
        (empty||value.Length>0)&&!value.Any(c=>char.IsControl(c)||c=='\uFEFF');
}
