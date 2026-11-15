using System.Text.Json;
using IO.DeviceContracts;
using IO.Infrastructure.Devices;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class LocalTransportOperationalTests
{
 internal static async Task RunAsync(ServiceProvider provider,Guid actor,Guid service,Guid reader,Guid site,Action<bool,string> check)
 {
  async Task<T> Use<S,T>(Func<S,Task<T>> f)where S:notnull{await using var scope=provider.CreateAsyncScope();return await f(scope.ServiceProvider.GetRequiredService<S>());}
  Task<T> Q<T>(Func<IoDbContext,Task<T>> f)=>Use<IoDbContext,T>(f);
  async Task<bool> Reject<E>(Func<Task> f)where E:Exception{try{await f();return false;}catch(E){return true;}}
  void Check(bool value,string message)=>check(value,"R16 local "+message);
  foreach(var (user,codes) in new[]{(actor,new[]{DeviceRegistry.Read,DeviceRegistry.Create,DeviceRegistry.Credentials,DeviceRegistry.Update,DeviceConnections.Test,DeviceOperations.Read,DeviceUserDownloads.Download,DeviceUserDownloads.Read,DeviceUserUploads.Upload,DeviceAttendanceDownloads.Download,DeviceAttendanceDownloads.Read}),
   (service,new[]{DeviceConnections.Execute,DeviceOperations.Execute,DeviceUserDownloads.Execute,DeviceUserUploads.Execute,DeviceAttendanceDownloads.Execute})})
   await Q(async db=>{var role=await db.UserRoles.Where(x=>x.UserId==user).Select(x=>x.RoleId).SingleAsync();foreach(var code in codes){var p=await db.Permissions.SingleOrDefaultAsync(x=>x.Code==code);if(p is null){p=new(){Id=Guid.NewGuid(),Code=code};db.Permissions.Add(p);}if(!await db.RolePermissions.AnyAsync(x=>x.RoleId==role&&x.PermissionId==p.Id))db.RolePermissions.Add(new(){RoleId=role,PermissionId=p.Id});}await db.SaveChangesAsync();return true;});
  var station=Environment.MachineName.ToUpperInvariant();
  var host=Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),"..","..","..","dotnet.exe"));
  var relay=Path.Combine(AppContext.BaseDirectory,"virtual-relay","IO.DeviceRuntime.Tests.dll");
  foreach(var kind in new[]{"usb","serial"})
  {
   var profile=kind=="usb"?"zk-usb-direct":"zk-serial-direct";var port=kind=="usb"?0:17;var id=Guid.NewGuid();
   var registration=new DeviceRegistry.Input(site,"SYNTHETIC-LOCAL-"+kind.ToUpperInvariant(),"SYNTHETIC LOCAL","unverified",profile,station,port,true);
   Check(await Use<DeviceRegistry,Guid>(x=>x.CreateAsync(actor,id,registration))==id,"durable "+kind+" registration uses station and physical endpoint, not an IP alias");
   Check(await Reject<UnauthorizedAccessException>(()=>Use<DeviceRegistry,Guid>(x=>x.CreateAsync(reader,Guid.NewGuid(),registration with{Number="SYNTHETIC-DENIED"}))),"restricted reader cannot register local endpoint");
   Check(await Reject<DomainConflictException>(()=>Use<DeviceRegistry,Guid>(x=>x.CreateAsync(actor,Guid.NewGuid(),registration with{Number="SYNTHETIC-DUPLICATE"}))),"duplicate physical endpoint cannot create a second device alias");
   Check(await Reject<ArgumentException>(()=>Use<DeviceRegistry,Guid>(x=>x.CreateAsync(actor,Guid.NewGuid(),registration with{Number="SYNTHETIC-INVALID",Host="COM1/invalid"}))),"raw device path is not a station selector");
   var version=await Q(db=>db.Devices.Where(x=>x.Id==id).Select(x=>x.Version).SingleAsync());
   // Direct SQL bypass still cannot create an invalid local endpoint.
   bool sqlRejected=false;try{await Q(async db=>{await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.Devices SET Port={-1} WHERE Id={id}");return true;});}catch(SqlException e)when(e.Number==547){sqlRejected=true;}
   Check(sqlRejected,"SQL constraint independently rejects malformed physical selector");
   var local=new ZkLocalTransport(kind,station,port,kind=="usb"?0:57600,"SYNTHETIC-DRIVER","SYNTHETIC-SINGLE-DEVICE-ONLY",true);
   var native=new NativeEndpoint(id,station,port,7,"SYNTHETIC-LOCAL",true,AllowUserRead:true,AttendanceOnlyUserWriteApproved:true,AllowAttendanceRead:true,LocalTransport:local);
   var cfg=new WorkerConfiguration(service,[],[native],host,relay,true,true,true,true,false,false);
   Check(await native.RegisteredAsync(provider.GetRequiredService<IServiceScopeFactory>(),CancellationToken.None),"worker local binding matches real current registry profile station and selector");
   Check(!await (native with{LocalTransport=local with{Kind=kind=="usb"?"serial":"usb"}}).RegisteredAsync(provider.GetRequiredService<IServiceScopeFactory>(),CancellationToken.None),"worker rejects cross-profile retargeting even with same device UUID");
   LocalExchangeFixtureStore.Save(id,LocalExchangeState.Initial(ZkUserReadFormats.Ssr));
   using var worker=new DiagnosticWorker(provider.GetRequiredService<IServiceScopeFactory>(),cfg);await worker.StartAsync(CancellationToken.None);
   try
   {
    async Task Wait(Func<Task<string>> state,string expected)
    {
     using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));string current;
     do{await Task.Delay(75,deadline.Token);current=await state();}while(current is "pending" or "running");
     Check(current==expected,"actual Windows worker/owned credential/local IPC/fake SDK durable outcome "+expected+" ("+kind+")");
    }
    var probe=Guid.NewGuid();await Use<DeviceConnections,Guid>(x=>x.RequestAsync(actor,probe,id,version,20));
    await Wait(()=>Q(db=>db.DeviceConnections.Where(x=>x.Id==probe).Select(x=>x.State).SingleAsync()),"sdk-serial-matched");
    var info=Guid.NewGuid();await Use<DeviceOperations,Guid>(x=>x.RequestAsync(actor,info,id,version,"information.read",20,null,null));
    await Wait(()=>Q(db=>db.DeviceOperations.Where(x=>x.Id==info).Select(x=>x.State).SingleAsync()),"succeeded");
    var users=Guid.NewGuid();await Use<DeviceUserDownloads,Guid>(x=>x.RequestAsync(actor,users,id,version,20));
    await Wait(()=>Q(db=>db.DeviceUserDownloads.Where(x=>x.Id==users).Select(x=>x.State).SingleAsync()),"succeeded");
    Check(await Q(db=>db.DeviceDownloadedUsers.AnyAsync(x=>x.DownloadId==users&&x.PersonIdentifier=="001")),"native local user stream preserves exact leading-zero identity in SQL");
    var logs=Guid.NewGuid();await Use<DeviceAttendanceDownloads,Guid>(x=>x.RequestAsync(actor,logs,id,version,20,new DateTimeOffset(2026,11,8,0,0,0,TimeSpan.Zero),new DateTimeOffset(2026,11,9,0,0,0,TimeSpan.Zero),0,1));
    await Wait(()=>Q(db=>db.DeviceAttendanceDownloads.Where(x=>x.Id==logs).Select(x=>x.State).SingleAsync()),"succeeded");
    Check(await Q(db=>db.DeviceAttendanceRows.CountAsync(x=>x.DownloadId==logs))==2,"native local archive preserves duplicate tuples without implicit raw ingestion");
    await LocalUserUploadOperationalTests.RunAsync(provider,actor,service,reader,id,version,users,Check);
    Check(await Reject<UnauthorizedAccessException>(()=>Use<DeviceUserDownloads,Guid>(x=>x.RequestAsync(reader,Guid.NewGuid(),id,version,20))),"local transport never bypasses ordinary request grants");
   }
   finally{await worker.StopAsync(CancellationToken.None);LocalExchangeFixtureStore.Delete(id);}
  }
 }
}
