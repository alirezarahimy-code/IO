using IO.DeviceContracts;
using IO.Infrastructure.Devices;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/// <summary>One private, material-free IPC request; both native sessions live only in that child.</summary>
internal sealed class BiometricTransferWorker(IServiceScopeFactory scopes,WorkerConfiguration configuration):BackgroundService
{
 protected override async Task ExecuteAsync(CancellationToken ct)
 {
  if(!OperatingSystem.IsWindows())return;
  while(!ct.IsCancellationRequested)
  {
   try
   {
    DeviceBiometricTransfers.Claim? claim;
    await using(var scope=scopes.CreateAsyncScope())
    {
     if(!await scope.ServiceProvider.GetRequiredService<IoDbContext>().DeviceBiometricTransfers.AnyAsync(x=>x.State=="pending"||x.State=="running",ct)){await Task.Delay(1000,ct);continue;}
     claim=await scope.ServiceProvider.GetRequiredService<DeviceBiometricTransfers>().ClaimAsync(configuration.ServiceUserId,ct);
    }
    if(claim is null){await Task.Delay(1000,ct);continue;}
    var result=await Transfer(claim,ct);
    await using var finish=scopes.CreateAsyncScope();await finish.ServiceProvider.GetRequiredService<DeviceBiometricTransfers>().CompleteAsync(configuration.ServiceUserId,claim,result,ct);
   }
   catch(OperationCanceledException)when(ct.IsCancellationRequested){return;}
   catch
   {
    Console.Error.WriteLine("Biometric transfer unavailable; inspect durable metadata. No material logged or write replayed.");
    try{await Task.Delay(2000,ct);}catch(OperationCanceledException){return;}
   }
  }
 }
 private async Task<BiometricTransferOutput> Transfer(DeviceBiometricTransfers.Claim claim,CancellationToken ct)
 {
  var i=claim.Intent;var source=configuration.NativeEndpoints.SingleOrDefault(x=>x.DeviceId==i.SourceDeviceId);var target=configuration.NativeEndpoints.SingleOrDefault(x=>x.DeviceId==i.TargetDeviceId);
  bool Matches(NativeEndpoint? n,string host,int port,string serial)=>n is not null&&n.Port==port&&System.Net.IPAddress.TryParse(host,out var ip)&&System.Net.IPAddress.Parse(n.Address).Equals(ip)&&n.ExpectedSerial==serial&&n.BiometricTransferApprovals?.Count(a=>a==i.Approval)==1;
  if(!Matches(source,claim.SourceHost,claim.SourcePort,i.Approval.Source.Serial)||!Matches(target,claim.TargetHost,claim.TargetPort,i.Approval.Target.Serial))return new(claim.Id,"unsupported");
  DeviceBiometricTransfers.Keys? keys;
  await using(var scope=scopes.CreateAsyncScope())keys=await scope.ServiceProvider.GetRequiredService<DeviceBiometricTransfers>().CredentialAsync(configuration.ServiceUserId,claim,source!.AllowDefaultCommunicationKey,target!.AllowDefaultCommunicationKey,ct);
  if(keys is null)return new(claim.Id,"failed-before-write");
  var input=new BiometricTransferInput(new(i.SourceDeviceId,source!.Address,source.Port,source.MachineNumber,source.ExpectedSerial,keys.Source.Value),new(i.TargetDeviceId,target!.Address,target.Port,target.MachineNumber,target.ExpectedSerial,keys.Target.Value),claim.Id,i.SourcePerson,i.TargetPerson,i.Approval);
  using var stop=CancellationTokenSource.CreateLinkedTokenSource(ct);
  var process=NativeProcessSupervisor.RunBiometricTransferAsync(configuration.DotnetPath!,configuration.BridgePath!,input,i.TimeoutSeconds,stop.Token);
  while(!process.IsCompleted)
  {
   if(await Task.WhenAny(process,Task.Delay(500,ct))==process)break;
   bool shouldStop;try{await using var scope=scopes.CreateAsyncScope();shouldStop=await scope.ServiceProvider.GetRequiredService<DeviceBiometricTransfers>().ShouldStopAsync(configuration.ServiceUserId,claim,ct);}catch{shouldStop=true;}
   if(shouldStop||ct.IsCancellationRequested){await stop.CancelAsync();break;}
  }
  return await process;
 }
}
