using IO.DeviceContracts;
using IO.Infrastructure.Devices;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
internal sealed class AttendanceEraseWorker(IServiceScopeFactory scopes,WorkerConfiguration configuration):BackgroundService
{
 protected override async Task ExecuteAsync(CancellationToken ct)
 {
  if(!OperatingSystem.IsWindows())return;
  while(!ct.IsCancellationRequested)
  {
   try
   {
    DeviceAttendanceErases.Claim? claim;
    await using(var scope=scopes.CreateAsyncScope())
    {
     if(!await scope.ServiceProvider.GetRequiredService<IoDbContext>().DeviceAttendanceErases.AnyAsync(x=>x.State=="proposed"||x.State=="pending"||x.State=="running",ct)){await Task.Delay(1000,ct);continue;}
     claim=await scope.ServiceProvider.GetRequiredService<DeviceAttendanceErases>().ClaimAsync(configuration.ServiceUserId,ct);
    }
    if(claim is null){await Task.Delay(1000,ct);continue;}
    var result=await Run(claim,ct);
    await using var finish=scopes.CreateAsyncScope();await finish.ServiceProvider.GetRequiredService<DeviceAttendanceErases>().CompleteAsync(configuration.ServiceUserId,claim,result,ct);
   }
   catch(OperationCanceledException)when(ct.IsCancellationRequested){return;}
   catch{Console.Error.WriteLine("Attendance erase unavailable; inspect durable metadata. No raw tuples logged or destructive command replayed.");try{await Task.Delay(2000,ct);}catch(OperationCanceledException){return;}}
  }
 }
 private async Task<AttendanceEraseOutput> Run(DeviceAttendanceErases.Claim claim,CancellationToken ct)
 {
  var endpoint=configuration.NativeEndpoints.SingleOrDefault(x=>x.DeviceId==claim.DeviceId);
  if(endpoint is null||!endpoint.AllowAttendanceRead||endpoint.AttendanceReadFormat!=claim.Approval.ReadFormat||endpoint.Port!=claim.Port||endpoint.ExpectedSerial!=claim.Approval.Device.Serial||!System.Net.IPAddress.TryParse(claim.Host,out var ip)||!System.Net.IPAddress.Parse(endpoint.Address).Equals(ip)||endpoint.AttendanceEraseApprovals?.Count(a=>a==claim.Approval)!=1)return new(claim.Id,"unsupported");
  DeviceCommunicationKey? key;
  await using(var scope=scopes.CreateAsyncScope())key=await scope.ServiceProvider.GetRequiredService<DeviceAttendanceErases>().CredentialAsync(configuration.ServiceUserId,claim,endpoint.AllowDefaultCommunicationKey,ct);
  if(key is null)return new(claim.Id,"failed-before-write");
  var input=new AttendanceEraseInput(new(claim.DeviceId,endpoint.Address,endpoint.Port,endpoint.MachineNumber,endpoint.ExpectedSerial,key.Value),claim.Id,claim.Approval,claim.Count,claim.Digest);
  using var stop=CancellationTokenSource.CreateLinkedTokenSource(ct);
  var process=NativeProcessSupervisor.RunAttendanceEraseAsync(configuration.DotnetPath!,configuration.BridgePath!,input,60,stop.Token);
  while(!process.IsCompleted)
  {
   if(await Task.WhenAny(process,Task.Delay(500,ct))==process)break;
   bool cancel;try{await using var scope=scopes.CreateAsyncScope();cancel=await scope.ServiceProvider.GetRequiredService<DeviceAttendanceErases>().ShouldStopAsync(configuration.ServiceUserId,claim,ct);}catch{cancel=true;}
   if(cancel||ct.IsCancellationRequested){await stop.CancelAsync();break;}
  }
  return await process;
 }
}
