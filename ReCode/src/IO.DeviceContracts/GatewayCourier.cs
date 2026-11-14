using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace IO.DeviceContracts;

public sealed record GatewayCourierConfiguration(string Server,Guid BindingId,Guid GatewayId,Guid DeviceId,Guid SourceId,string Serial,string TimeZoneId,string KeyVersion,string KeyBase64)
{public override string ToString()=>"Gateway courier configuration [key redacted]";}
public sealed record GatewayCourierTransfer(Guid TransferId,string BodyFile,string? Stamp);
public sealed record GatewayCourierReceipt(Guid ReceiptId,Guid TransferId,string PayloadHash,int RetainedRows,bool GatewayAuthenticated,bool DeviceIdentityVerified,string State);
public static class DurableGatewayFiles
{
    public static async Task WriteAsync(string path,byte[] bytes,CancellationToken ct=default)
    {
        await using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,8192,FileOptions.WriteThrough|FileOptions.Asynchronous);
        await file.WriteAsync(bytes,ct);await file.FlushAsync(ct);file.Flush(true);
    }
}
public static class GatewayCourier
{
    public static async Task<GatewayCourierReceipt> SendAsync(GatewayCourierConfiguration config,GatewayCourierTransfer transfer,string transferPath,CancellationToken ct=default)
    {
        var options=new JsonSerializerOptions{UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,PropertyNameCaseInsensitive=true};
    if(config.BindingId==Guid.Empty||transfer.TransferId==Guid.Empty||!Uri.TryCreate(config.Server,UriKind.Absolute,out var server)||server.Scheme!="https"||server.AbsolutePath!="/"||!string.IsNullOrEmpty(server.Query)||!string.IsNullOrEmpty(server.UserInfo)||!string.IsNullOrEmpty(server.Fragment)||!Path.IsPathFullyQualified(transfer.BodyFile))throw new ArgumentException();
    var key=Convert.FromBase64String(config.KeyBase64);
    var binding=new PushGatewayBinding(config.GatewayId,config.DeviceId,config.SourceId,config.Serial,config.KeyVersion,TimeZoneInfo.FindSystemTimeZoneById(config.TimeZoneId),key);CryptographicOperations.ZeroMemory(key);
    using var stream=File.OpenRead(transfer.BodyFile);using var bytes=new MemoryStream();var buffer=new byte[8192];int count;
    while((count=await stream.ReadAsync(buffer))>0){if(bytes.Length+count>ZkPushAttendanceCodec.MaximumBytes)throw new ArgumentException();bytes.Write(buffer,0,count);}
    var body=bytes.ToArray();var batch=ZkPushAttendanceCodec.Decode(binding.Serial,transfer.Stamp,"ATTLOG",body,binding.Zone);
    var sent=DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    using var handler=new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false,AutomaticDecompression=DecompressionMethods.None};
    using var client=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(30)};
    using var request=new HttpRequestMessage(HttpMethod.Post,new Uri(server,AuthenticatedPushGateway.Path));
    request.Headers.Add("X-IO-Binding",config.BindingId.ToString("D"));request.Headers.Add("X-IO-Transfer",transfer.TransferId.ToString("D"));request.Headers.Add("X-IO-Sent",sent.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
    request.Headers.Add("X-IO-Signature",AuthenticatedPushGateway.Sign(binding,transfer.TransferId,sent,transfer.Stamp,body));
    if(transfer.Stamp is not null)request.Headers.Add("X-IO-Stamp",Convert.ToBase64String(Encoding.UTF8.GetBytes(transfer.Stamp)));
    request.Content=new ByteArrayContent(body);request.Content.Headers.ContentType=new("application/octet-stream");
    using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
    if(response.StatusCode!=HttpStatusCode.OK)throw new InvalidOperationException();
    await using var content=await response.Content.ReadAsStreamAsync();using var receiptBytes=new MemoryStream();
    while((count=await content.ReadAsync(buffer,ct))>0){if(receiptBytes.Length+count>4096)throw new InvalidOperationException();receiptBytes.Write(buffer,0,count);}
    var receipt=JsonSerializer.Deserialize<GatewayCourierReceipt>(receiptBytes.ToArray(),options);
    if(receipt is null||receipt.ReceiptId==Guid.Empty||receipt.TransferId!=transfer.TransferId||receipt.PayloadHash!=batch.PayloadHash||receipt.RetainedRows!=batch.Rows.Count||!receipt.GatewayAuthenticated||receipt.DeviceIdentityVerified||receipt.State!="gateway-retained-device-unverified")throw new InvalidOperationException();
    // A retry MUST use the same transfer file/id and unchanged body; sender never deletes source evidence or auto-generates a replacement id.
    var target=transferPath+".receipt.json";var temp=target+"."+Guid.NewGuid().ToString("N")+".tmp";
    try{await DurableGatewayFiles.WriteAsync(temp,receiptBytes.ToArray(),ct);File.Move(temp,target,true);}finally{if(File.Exists(temp))File.Delete(temp);}
        return receipt;
    }
}
