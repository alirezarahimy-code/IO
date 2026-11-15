using IO.DeviceContracts;
using IO.Infrastructure.Devices;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
internal sealed class ProtectedEraseWorker(IServiceScopeFactory scopes,WorkerConfiguration configuration):BackgroundService
{
 protected override async Task ExecuteAsync(CancellationToken ct)
 {
  if(!OperatingSystem.IsWindows())return;
  int cursor=0;
  while(!ct.IsCancellationRequested)
  {
   try
   {
    DeviceProtectedErases.Claim? claim;
    await using(var scope=scopes.CreateAsyncScope())
    {
     var families=configuration.ProtectedEraseFamilies??[];if(families.Length==0)return;var family=families[cursor++%families.Length];
     if(!await scope.ServiceProvider.GetRequiredService<IoDbContext>().DeviceProtectedErases.AnyAsync(x=>x.Family==family&&(x.State=="proposed"||x.State=="pending"||x.State=="running"),ct)){await Task.Delay(500,ct);continue;}
     claim=await scope.ServiceProvider.GetRequiredService<DeviceProtectedErases>().ClaimAsync(configuration.ServiceUserId,family,ct);
    }
    if(claim is null){await Task.Delay(1000,ct);continue;}
    var result=await Run(claim,ct);
    await using var finish=scopes.CreateAsyncScope();await finish.ServiceProvider.GetRequiredService<DeviceProtectedErases>().CompleteAsync(configuration.ServiceUserId,claim,result,ct);
   }
   catch(OperationCanceledException)when(ct.IsCancellationRequested){return;}
   catch{Console.Error.WriteLine("Protected erase unavailable; inspect durable metadata. No protected raw material logged or destructive command replayed.");try{await Task.Delay(2000,ct);}catch(OperationCanceledException){return;}}
  }
 }
 private async Task<ProtectedEraseOutput> Run(DeviceProtectedErases.Claim claim,CancellationToken ct)
 {
  var endpoint=configuration.NativeEndpoints.SingleOrDefault(x=>x.DeviceId==claim.DeviceId);
  if(endpoint is null||!endpoint.AllowUserRead||endpoint.UserReadFormat!=claim.Approval.Inventory.UserFormat||endpoint.Port!=claim.Port||endpoint.ExpectedSerial!=claim.Approval.Inventory.Device.Serial||!System.Net.IPAddress.TryParse(claim.Host,out var ip)||!System.Net.IPAddress.Parse(endpoint.Address).Equals(ip)||endpoint.ProtectedEraseApprovals?.Count(a=>a==claim.Approval)!=1)return new(claim.Id,claim.InspectOnly?"inspection-failed":"unsupported");
  DeviceCommunicationKey? key;
  await using(var scope=scopes.CreateAsyncScope())key=await scope.ServiceProvider.GetRequiredService<DeviceProtectedErases>().CredentialAsync(configuration.ServiceUserId,claim,endpoint.AllowDefaultCommunicationKey,ct);
  if(key is null)return new(claim.Id,claim.InspectOnly?"inspection-failed":"failed-before-write");
  var input=new ProtectedEraseInput(new(claim.DeviceId,endpoint.Address,endpoint.Port,endpoint.MachineNumber,endpoint.ExpectedSerial,key.Value),claim.Id,claim.Approval,claim.Expected,claim.Count,claim.Digest,claim.InspectOnly);
  using var stop=CancellationTokenSource.CreateLinkedTokenSource(ct);
  var process=NativeProcessSupervisor.RunProtectedEraseAsync(configuration.DotnetPath!,configuration.BridgePath!,input,60,stop.Token);
  while(!process.IsCompleted)
  {
   if(await Task.WhenAny(process,Task.Delay(500,ct))==process)break;
   bool cancel;try{await using var scope=scopes.CreateAsyncScope();cancel=await scope.ServiceProvider.GetRequiredService<DeviceProtectedErases>().ShouldStopAsync(configuration.ServiceUserId,claim,ct);}catch{cancel=true;}
   if(cancel||ct.IsCancellationRequested){await stop.CancelAsync();break;}
  }
  return await process;
 }
}
