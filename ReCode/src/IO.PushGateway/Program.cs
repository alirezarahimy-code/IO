using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IO.DeviceContracts;

// Single-shot durable-file courier. No unauthenticated native listener or vendor OK:n.
// Config and transfer spool must be protected with Windows service-account ACLs. No personal data or keys in logs/arguments.
if(args.Length!=2){Console.Error.WriteLine("Usage: IO.PushGateway <absolute-config-path> <absolute-transfer-path>");return 2;}
try
{
    foreach(var path in args)if(!Path.IsPathFullyQualified(path)||!File.Exists(path)||new FileInfo(path).Length>16384)throw new ArgumentException();
    var options=new JsonSerializerOptions{UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,PropertyNameCaseInsensitive=true};
    var config=JsonSerializer.Deserialize<GatewayCourierConfiguration>(await File.ReadAllTextAsync(args[0]),options)??throw new ArgumentException();
    var transfer=JsonSerializer.Deserialize<GatewayCourierTransfer>(await File.ReadAllTextAsync(args[1]),options)??throw new ArgumentException();
    await GatewayCourier.SendAsync(config,transfer,args[1]);
    Console.WriteLine("Gateway quarantine receipt saved. Device identity remains unverified; no vendor ACK or attendance processing implied.");return 0;
}
catch{Console.Error.WriteLine("Transfer not acknowledged locally. Preserve the original transfer id and bytes; inspect server authority/configuration. No automatic replay or deletion.");return 1;}
