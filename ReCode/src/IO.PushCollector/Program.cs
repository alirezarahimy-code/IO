using System.Net;
using System.Text;
using System.Text.Json;
using IO.DeviceContracts;
using Microsoft.AspNetCore.Server.Kestrel.Core;

if(args is not ["--config",var path]||!Path.IsPathFullyQualified(path)||!File.Exists(path)||new FileInfo(path).Length>32768)
    throw new InvalidOperationException("An absolute ACL-protected collector configuration is required.");
var configuration=JsonSerializer.Deserialize<NativePushCollectorConfiguration>(await File.ReadAllTextAsync(path),new JsonSerializerOptions{UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow})??throw new InvalidOperationException();
await IoPushCollectorHost.Build(configuration).RunAsync();

public sealed record NativePushCollectorConfiguration(GatewayCourierConfiguration Gateway,string ListenAddress,int ListenPort,string DeviceAddress,string SpoolDirectory,
    string InstalledModelEvidence,string DurableReceiptAckApproval,string OptionsResponse,bool EnableNativeCollector=false,bool AllowUnverifiedLanDevice=false,bool AllowDurableQuarantineAck=false,int MaximumSpoolEntries=1000,long MaximumSpoolBytes=67108864)
{
    public override string ToString()=>"Native PUSH collector configuration [secrets and identity redacted]";
}
/// <summary>Classic unencrypted ATTLOG data plane inside an explicitly isolated LAN. IP/SN are restrictions, NOT authentication.
/// Vendor OK:n means durable quarantine receipt only, not employee mapping/attendance/payroll acceptance.</summary>
public static class IoPushCollectorHost
{
    private static bool Text(string value,int limit)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=limit&&!value.Any(char.IsControl);
    private static bool Private(IPAddress a)=>IPAddress.IsLoopback(a)||a.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork&&
        (a.GetAddressBytes()[0]==10||a.GetAddressBytes()[0]==172&&a.GetAddressBytes()[1] is >=16 and <=31||a.GetAddressBytes()[0]==192&&a.GetAddressBytes()[1]==168);
    public static WebApplication Build(NativePushCollectorConfiguration config,bool allowEphemeralLoopbackTestPort=false)
    {
        if(!config.EnableNativeCollector||!config.AllowUnverifiedLanDevice||!config.AllowDurableQuarantineAck||!Text(config.InstalledModelEvidence,200)||!Text(config.DurableReceiptAckApproval,200)||
           !IPAddress.TryParse(config.ListenAddress,out var listen)||!Private(listen)||listen.Equals(IPAddress.Any)||!IPAddress.TryParse(config.DeviceAddress,out var device)||!Private(device)||
           config.ListenPort is <0 or >65535||config.ListenPort==0&&(!allowEphemeralLoopbackTestPort||!IPAddress.IsLoopback(listen))||
           !Path.IsPathFullyQualified(config.SpoolDirectory)||!Directory.Exists(config.SpoolDirectory)||config.MaximumSpoolEntries is <1 or >10000||config.MaximumSpoolBytes is <1048576 or >1073741824)
            throw new InvalidOperationException("Explicit isolated LAN pins, model/ACK approval and bounded existing spool required; collector is default-off.");
        if(config.OptionsResponse is null||config.OptionsResponse.Length>2048||config.OptionsResponse.Contains('\r')||!config.OptionsResponse.Split('\n',StringSplitOptions.RemoveEmptyEntries).All(line=>
            System.Text.RegularExpressions.Regex.IsMatch(line,@"\A(ATTLOGStamp|OPERLOGStamp|ATTPHOTOStamp|ErrorDelay|Delay|TransTimes|TransInterval|TransFlag|TimeZone|Realtime|Encrypt|ServerVer|PushProtVer)=[\x20-\x7E]{1,100}\z"))||
            config.OptionsResponse.Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Split('=')[0]).Distinct(StringComparer.Ordinal).Count()!=config.OptionsResponse.Split('\n',StringSplitOptions.RemoveEmptyEntries).Length||!config.OptionsResponse.Split('\n').Contains("Encrypt=0"))
            throw new InvalidOperationException("An installed-approved, bounded classic plaintext options snapshot including Encrypt=0 is required.");
        if(config.Gateway is null||config.Gateway.BindingId==Guid.Empty||!Uri.TryCreate(config.Gateway.Server,UriKind.Absolute,out var server)||server.Scheme!="https"||server.AbsolutePath!="/"||server.UserInfo.Length>0||server.Query.Length>0||server.Fragment.Length>0)throw new InvalidOperationException("Pinned valid HTTPS gateway origin required.");
        var key=Convert.FromBase64String(config.Gateway.KeyBase64);
        _=new PushGatewayBinding(config.Gateway.GatewayId,config.Gateway.DeviceId,config.Gateway.SourceId,config.Gateway.Serial,config.Gateway.KeyVersion,TimeZoneInfo.FindSystemTimeZoneById(config.Gateway.TimeZoneId),key);
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ContentRootPath=AppContext.BaseDirectory});builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k=>{k.Listen(listen,config.ListenPort);k.Limits.MaxRequestBodySize=ZkPushAttendanceCodec.MaximumBytes;k.Limits.RequestHeadersTimeout=TimeSpan.FromSeconds(10);k.Limits.MaxRequestLineSize=4096;k.Limits.MinRequestBodyDataRate=new MinDataRate(240,TimeSpan.FromSeconds(5));});
        builder.Services.AddSingleton(config);builder.Services.AddSingleton<CollectorSpool>();builder.Services.AddHostedService<SpoolDelivery>();
        var app=builder.Build();
        app.Use(async(c,next)=>
        {
            c.Response.Headers.CacheControl="no-store";
            if(c.Connection.RemoteIpAddress is not{} remote||!remote.MapToIPv6().Equals(device.MapToIPv6())||
               c.Request.Query["SN"].Count!=1||c.Request.Query["SN"].ToString()!=config.Gateway.Serial){c.Response.StatusCode=403;return;}
            await next(c);
        });
        app.MapGet("/iclock/cdata",(HttpContext c)=>c.Request.Query["options"].ToString()=="all"&&c.Request.Query.Keys.All(x=>x is "SN" or "options")?
            Results.Text("GET OPTION FROM: "+config.Gateway.Serial+"\n"+config.OptionsResponse+"\n","text/plain",Encoding.ASCII):Results.BadRequest());
        app.MapGet("/iclock/getrequest",(HttpContext c)=>c.Request.Query.Keys.All(x=>x=="SN")?Results.Text("OK","text/plain",Encoding.ASCII):Results.BadRequest());
        app.MapPost("/iclock/cdata",async(HttpContext c,CollectorSpool spool)=>
        {
            if(c.Request.Query["table"].Count!=1||c.Request.Query["table"].ToString()!="ATTLOG"||c.Request.Query["Stamp"].Count>1||!c.Request.Query.Keys.All(x=>x is "SN" or "table" or "Stamp")||c.Request.Headers.ContainsKey("Content-Encoding"))return Results.BadRequest();
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted);deadline.CancelAfter(TimeSpan.FromSeconds(40));
            if(!await spool.Gate.WaitAsync(0,deadline.Token))return Results.StatusCode(429);
            try
            {
                if(!spool.HasCapacity())return Results.StatusCode(507);
                using var body=new MemoryStream();var buffer=new byte[8192];int n;
                while((n=await c.Request.Body.ReadAsync(buffer,deadline.Token))>0){if(body.Length+n>ZkPushAttendanceCodec.MaximumBytes)return Results.StatusCode(413);body.Write(buffer,0,n);}
                var stamp=c.Request.Query.ContainsKey("Stamp")?c.Request.Query["Stamp"].ToString():null;
                var saved=await spool.SaveAsync(body.ToArray(),stamp,deadline.Token);
                if(saved.Transfer is null)return Results.BadRequest();
                await GatewayCourier.SendAsync(config.Gateway,saved.Transfer,saved.Path,deadline.Token);
                if(!saved.FullyParsed)return Results.BadRequest();
                // Persist ACK intent before writing vendor response. Delivery to the terminal is never inferred from this file.
                await DurableGatewayFiles.WriteAsync(saved.Path+".ack-intent.json",JsonSerializer.SerializeToUtf8Bytes(new{meaning="durable-quarantine-only-device-unverified",rows=saved.Count}),deadline.Token);
                return Results.Text("OK: "+saved.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),"text/plain",Encoding.ASCII);
            }
            catch(OperationCanceledException){return Results.StatusCode(503);}
            catch(Exception){return Results.StatusCode(503);}
            finally{spool.Gate.Release();}
        });
        return app;
    }
    internal sealed class CollectorSpool(NativePushCollectorConfiguration config)
    {
        internal SemaphoreSlim Gate {get;}=new(1,1);
        private int retryCursor;
        internal bool HasCapacity()
        {
            var dirs=Directory.EnumerateDirectories(config.SpoolDirectory).Take(config.MaximumSpoolEntries+1).ToArray();
            return dirs.Length<config.MaximumSpoolEntries&&dirs.SelectMany(Directory.EnumerateFiles).Sum(x=>new FileInfo(x).Length)+ZkPushAttendanceCodec.MaximumBytes+32768<=config.MaximumSpoolBytes;
        }
        internal async Task<(GatewayCourierTransfer? Transfer,string Path,bool FullyParsed,int Count)> SaveAsync(byte[] body,string? stamp,CancellationToken ct)
        {
            var id=Guid.NewGuid();var dir=Path.Combine(config.SpoolDirectory,id.ToString("N"));Directory.CreateDirectory(dir);
            var original=Path.Combine(dir,"original.attlog");await DurableGatewayFiles.WriteAsync(original,body,ct);
            var path=Path.Combine(dir,"transfer.json");ZkPushAttendanceBatch batch;
            try{batch=ZkPushAttendanceCodec.Decode(config.Gateway.Serial,stamp,"ATTLOG",body,TimeZoneInfo.FindSystemTimeZoneById(config.Gateway.TimeZoneId));}
            catch(ArgumentException){await DurableGatewayFiles.WriteAsync(Path.Combine(dir,"invalid-wire.json"),JsonSerializer.SerializeToUtf8Bytes(new{state="invalid-wire-no-ack"}),ct);return(null,path,false,0);}
            var transfer=new GatewayCourierTransfer(id,original,stamp);var temp=path+".tmp";await DurableGatewayFiles.WriteAsync(temp,JsonSerializer.SerializeToUtf8Bytes(transfer),ct);File.Move(temp,path);
            return(transfer,path,batch.FullyParsed,batch.Rows.Count);
        }
        internal async Task RetryAsync(CancellationToken ct)
        {
            if(!await Gate.WaitAsync(0,ct))return;
            try
            {
                var entries=Directory.EnumerateDirectories(config.SpoolDirectory).OrderBy(x=>x,StringComparer.Ordinal).Take(config.MaximumSpoolEntries).ToArray();
                for(var visited=0;visited<entries.Length;visited++)

                {
                    if(retryCursor>=entries.Length)retryCursor=0;var dir=entries[retryCursor++];
                    var path=Path.Combine(dir,"transfer.json");if(!File.Exists(path)||File.Exists(path+".receipt.json"))continue;
                    if(new FileInfo(path).Length>16384)continue;
                    var transfer=JsonSerializer.Deserialize<GatewayCourierTransfer>(await File.ReadAllTextAsync(path,ct));
                    if(transfer is null||transfer.BodyFile!=Path.Combine(dir,"original.attlog")||dir!=Path.Combine(config.SpoolDirectory,transfer.TransferId.ToString("N")))continue;
                    try{await GatewayCourier.SendAsync(config.Gateway,transfer,path,ct);}catch{ /* preserve original id/bytes; never synthesize an ACK for a closed connection */ }
                    break; // bounded one delivery per tick; no automatic evidence deletion
                }
            }
            finally{Gate.Release();}
        }
    }
    private sealed class SpoolDelivery(CollectorSpool spool):BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken ct)
        {while(!ct.IsCancellationRequested){try{await Task.Delay(3000,ct);await spool.RetryAsync(ct);}catch(OperationCanceledException)when(ct.IsCancellationRequested){return;}catch{Console.Error.WriteLine("PUSH delivery unavailable; original spool preserved, no native ACK.");}}}
    }
}
