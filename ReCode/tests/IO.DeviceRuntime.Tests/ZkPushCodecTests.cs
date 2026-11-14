using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using IO.DeviceContracts;

internal static class ZkPushCodecTests
{
    public static void Run()
    {
        var count=0;void Check(bool condition,string name){if(!condition)throw new InvalidOperationException(name);Console.WriteLine("PASS PUSH codec "+name);count++;}
        bool Reject(Action action){try{action();return false;}catch(ArgumentException){return true;}}
        const string raw="0001452\t2015-07-30 15:16:28\t00\t1\twork/raw\t\tunknown";
        var bytes=Encoding.UTF8.GetBytes(raw+"\r\n"+raw+"\r\n");
        ZkPushAttendanceBatch Parse(string s,TimeZoneInfo? zone=null)=>ZkPushAttendanceCodec.Decode("synthetic-serial","opaque:9999","ATTLOG",Encoding.UTF8.GetBytes(s),zone??TimeZoneInfo.Utc);
        var batch=ZkPushAttendanceCodec.Decode("synthetic-serial","opaque:9999","ATTLOG",bytes,TimeZoneInfo.Utc);
        Check(batch.Contract==ZkPushAttendanceCodec.Contract&&batch.FullyParsed&&batch.Rows.Count==2,"exact selected seven-column layout and final terminator");
        Check(batch.Rows[0].Pin=="0001452"&&batch.Rows[0].PunchState=="00"&&batch.Rows[0].WorkCode=="work/raw"&&batch.Rows[0].Reserved1==""&&batch.Rows[0].Reserved2=="unknown","all seven raw fields preserved without integer coercion or IN OUT guesses");
        Check(batch.Rows[0].OccurredAtUtc==new DateTimeOffset(2015,7,30,15,16,28,TimeSpan.Zero)&&batch.Rows[0].LocalTime=="2015-07-30 15:16:28","explicit Gregorian wall time and configured UTC zone");
        Check(batch.Rows.Select(x=>x.MultiplicityRank).SequenceEqual(new int?[]{1,2})&&batch.Rows[0].PayloadHash==batch.Rows[1].PayloadHash,"identical lines retain multiplicity rather than dropping a physical event");
        Check(batch.PayloadHash==Convert.ToHexString(SHA256.HashData(bytes))&&batch.Original.Span.SequenceEqual(bytes),"original CRLF payload and whole-body byte hash retained exactly");
        bytes[0]=(byte)'x';Check(batch.Original.Span[0]!=(byte)'x',"caller cannot mutate stored original through input array");
        var original=batch.Original;MemoryMarshal.TryGetArray(original,out var segment);segment.Array![segment.Offset]=(byte)'y';Check(batch.Original.Span[0]!=(byte)'y',"returned memory cannot mutate stored original");
        Check(batch.Serial=="synthetic-serial"&&batch.Stamp=="opaque:9999"&&!batch.ToString().Contains("synthetic-serial",StringComparison.Ordinal)&&!batch.Rows[0].ToString().Contains("0001452",StringComparison.Ordinal),"diagnostic rendering does not disclose serial cursor or personal identifiers");
        var lf=Parse(raw+"\n"+raw);Check(lf.Rows.Count==2&&lf.Rows[0].PayloadHash==batch.Rows[0].PayloadHash&&lf.PayloadHash!=batch.PayloadHash,"line canonical hashes survive framing changes without pretending batch bytes are identical");
        var changed=Parse(raw.Replace("work/raw","different",StringComparison.Ordinal));Check(changed.Rows[0].PayloadHash!=batch.Rows[0].PayloadHash&&changed.Rows[0].MultiplicityRank==1,"work and reserved fields participate in tuple identity");
        Check(Parse(raw.Replace("0001452","badge-الف",StringComparison.Ordinal)).Rows[0].Pin=="badge-الف","strict UTF8 identifier stays a string, not Int64");
        Check(Parse(raw.Replace("2015-07-30 15:16:28","2015-07-30 15:16:28",StringComparison.Ordinal)+"\n").Rows.Count==1,"one LF is a terminator, not an invented row");
        foreach(var bad in new[]{"", "x", "1\t2015-07-30 15:16:28\t1\t0\t0", raw+"\textra",raw.Replace('\t',' '),raw.Replace("0001452","",StringComparison.Ordinal),raw.Replace("2015-07-30","2015-02-30",StringComparison.Ordinal),raw.Replace("15:16:28","24:16:28",StringComparison.Ordinal),raw.Replace("15:16:28","15:16:28Z",StringComparison.Ordinal),raw+"\0",raw+"\r"})
        {
            if(bad==""){Check(Reject(()=>Parse(bad)),"empty HTTP entity rejected");continue;}
            var result=Parse(bad);Check(!result.FullyParsed&&ZkPushAttendanceCodec.FormatCommittedAcknowledgement(result,Guid.NewGuid(),result.PayloadHash,result.Rows.Count) is null,"malformed unsupported or noncanonical row gets no success formatter: "+count);
        }
        var mixed=Parse(raw+"\n\n"+raw+"\n");Check(mixed.Rows.Count==3&&!mixed.FullyParsed&&mixed.Rows[1].Status=="malformed-line"&&mixed.Rows[2].MultiplicityRank==2,"blank middle row is quarantined with original line numbering, not silently erased");
        foreach(var table in new[]{"rtlog","BIODATA","OPERLOG","ATTPHOTO","attlog","ATTLOG "})
            Check(Reject(()=>ZkPushAttendanceCodec.Decode("s",null,table,Encoding.UTF8.GetBytes(raw),TimeZoneInfo.Utc)),"no fallback or biometric decoding for table "+table);
        Check(Reject(()=>ZkPushAttendanceCodec.Decode("s",null,"ATTLOG",new byte[]{0xc0,0xaf},TimeZoneInfo.Utc))&&Reject(()=>Parse("\uFEFF"+raw)),"invalid UTF8 and BOM rejected without locale fallback");
        Check(Reject(()=>ZkPushAttendanceCodec.Decode("s\nspoof",null,"ATTLOG",Encoding.UTF8.GetBytes(raw),TimeZoneInfo.Utc))&&Reject(()=>ZkPushAttendanceCodec.Decode("s","cursor\rinjection","ATTLOG",Encoding.UTF8.GetBytes(raw),TimeZoneInfo.Utc)),"serial and cursor header control characters rejected");
        Check(Reject(()=>ZkPushAttendanceCodec.Decode("s",null,"ATTLOG",new byte[ZkPushAttendanceCodec.MaximumBytes+1],TimeZoneInfo.Utc)),"message byte budget is not a user or device count limit");
        Check(Reject(()=>Parse(string.Join('\n',Enumerable.Repeat(raw,10001))))&&Parse(string.Join('\n',Enumerable.Repeat(raw,10000))).Rows.Count==10000,"row budget checks actual rows with or without final newline");
        var oversized=Parse(new string('1',1025));Check(oversized.Rows[0].Status=="malformed-line","line length budget yields quarantine, not truncated data");
        var zone=TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows()?"Pacific Standard Time":"America/Los_Angeles");
        Check(Parse(raw.Replace("2015-07-30 15:16:28","2026-03-08 02:30:00",StringComparison.Ordinal),zone).Rows[0].Status=="nonexistent-local-time","DST spring gap never shifts to a fabricated instant");
        Check(Parse(raw.Replace("2015-07-30 15:16:28","2026-11-01 01:30:00",StringComparison.Ordinal),zone).Rows[0].Status=="ambiguous-local-time","DST fall overlap requires independent disambiguation, not the host default offset");
        Check(Parse(raw,zone).Rows[0].OccurredAtUtc==new DateTimeOffset(2015,7,30,22,16,28,TimeSpan.Zero),"site zone, not machine local zone, determines the UTC instant");
        var offset=TimeZoneInfo.CreateCustomTimeZone("synthetic-plus14",TimeSpan.FromHours(14),"synthetic-plus14","synthetic-plus14");
        Check(Parse(raw.Replace("2015-07-30 15:16:28","0001-01-01 00:00:00",StringComparison.Ordinal),offset).Rows[0].Status=="invalid-time","date boundary conversion must roundtrip without silent clamping");
        Check(ZkPushAttendanceCodec.FormatCommittedAcknowledgement(batch,Guid.Empty,batch.PayloadHash,2) is null&&ZkPushAttendanceCodec.FormatCommittedAcknowledgement(batch,Guid.NewGuid(),"foreign",2) is null,"success formatter rejects missing or mismatched committed receipt metadata");
        foreach(var n in new[]{-1,0,1,3,int.MaxValue})Check(ZkPushAttendanceCodec.FormatCommittedAcknowledgement(batch,Guid.NewGuid(),batch.PayloadHash,n) is null,"success formatter requires exact processed row count "+n.ToString(CultureInfo.InvariantCulture));
        Check(ZkPushAttendanceCodec.FormatCommittedAcknowledgement(batch,Guid.NewGuid(),batch.PayloadHash,2)=="OK: 2","vendor success response count is encoded exactly; helper does not perform or prove a SQL commit");
        var replay=ZkPushAttendanceCodec.Decode("synthetic-serial","a-different-stamp","ATTLOG",batch.Original.Span,TimeZoneInfo.Utc);
        Check(replay.PayloadHash==batch.PayloadHash&&replay.Rows[0].PayloadHash==batch.Rows[0].PayloadHash&&replay.Stamp!=batch.Stamp,"Stamp is not trusted as a nonce or a new attendance-event identity");
        Console.WriteLine($"PASS {count} PUSH codec checks; NO authentication, HTTP endpoint, SQL journal or hardware acceptance");
    }
}
