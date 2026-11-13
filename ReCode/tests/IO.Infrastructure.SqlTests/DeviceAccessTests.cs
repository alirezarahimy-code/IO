using System.Text;
using System.Text.Json;
using IO.DeviceContracts;
using IO.Infrastructure.Devices;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

internal static class DeviceAccessTests
{
 public static async Task RunAsync(string connection,Action<bool,string> check)
 {
  var services=new ServiceCollection();services.AddLogging();services.AddIoIdentityStorage(connection);await using var provider=services.BuildServiceProvider();
  async Task<T> Use<S,T>(Func<S,Task<T>> f)where S:notnull{await using var scope=provider.CreateAsyncScope();return await f(scope.ServiceProvider.GetRequiredService<S>());}
  Task<T> Q<T>(Func<IoDbContext,Task<T>> f)=>Use<IoDbContext,T>(f);
  Task<T> I<T>(Func<LoginIdentityDbContext,Task<T>> f)=>Use<LoginIdentityDbContext,T>(f);
  async Task<bool> Reject<E>(Func<Task> f)where E:Exception{try{await f();return false;}catch(E){return true;}}
  JsonElement Json(object value)=>JsonSerializer.SerializeToElement(value,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase});
  bool Only(object page,Guid device)=>Json(page).GetProperty("items").EnumerateArray().All(x=>x.GetProperty("deviceId").GetGuid()==device);
  var manager=Guid.NewGuid();var actor=Guid.NewGuid();var worker=Guid.NewGuid();var reader=Guid.NewGuid();var org=Guid.NewGuid();var company=Guid.NewGuid();var site=Guid.NewGuid();var a=Guid.NewGuid();var b=Guid.NewGuid();
  await Q(async db=>{
   db.Organizations.Add(new(){Id=org,Name="Device access org"});db.Companies.Add(new(){Id=company,OrganizationId=org,Name="Device access company"});db.Sites.Add(new(){Id=site,CompanyId=company,Name="Device scope site",TimeZoneId="UTC"});
   foreach(var(u,n,rights)in new[]{(manager,"scope-manager",new[]{DeviceAccess.Manage,DeviceRegistry.Create}),(actor,"scope-operator",PermissionCatalog.Codes.Where(x=>x!=DeviceAccess.Manage).ToArray()),(worker,"scope-worker",PermissionCatalog.Codes.ToArray()),(reader,"scope-read-only",new[]{DeviceRegistry.Read})})
   {var role=Guid.NewGuid();db.Users.Add(new(){Id=u,UserName=n+u.ToString("N"),IsActive=true});db.Roles.Add(new(){Id=role,Name=n+role.ToString("N")});db.UserRoles.Add(new(){UserId=u,RoleId=role});foreach(var code in rights){var p=db.Permissions.Local.FirstOrDefault(x=>x.Code==code)??await db.Permissions.SingleOrDefaultAsync(x=>x.Code==code);if(p is null){p=new(){Id=Guid.NewGuid(),Code=code};db.Permissions.Add(p);}db.RolePermissions.Add(new(){RoleId=role,PermissionId=p.Id});}}
   await db.SaveChangesAsync();return true;
  });
  foreach(var(id,number)in new[]{(a,"ACCESS-A"),(b,"ACCESS-B")})await Use<DeviceRegistry,Guid>(s=>s.CreateAsync(manager,id,new(site,number,number,"synthetic","zk-standalone","10.20.30.80",4370,true)));
  var av=await Q(db=>db.Devices.Where(x=>x.Id==a).Select(x=>x.Version).SingleAsync());var bv=await Q(db=>db.Devices.Where(x=>x.Id==b).Select(x=>x.Version).SingleAsync());
  await Use<DeviceAccess,Guid>(s=>s.SetPolicyAsync(manager,worker,null,true,"RESTRICT-DEVICE-OPERATOR"));
  await Use<DeviceAccess,Guid>(s=>s.GrantAsync(manager,Guid.NewGuid(),worker,a));await Use<DeviceAccess,Guid>(s=>s.GrantAsync(manager,Guid.NewGuid(),worker,b));
  var ops=new Dictionary<Guid,Guid>();var users=new Dictionary<Guid,Guid>();var logs=new Dictionary<Guid,Guid>();var files=new Dictionary<Guid,Guid>();
  foreach(var(id,v)in new[]{(a,av),(b,bv)})
  {
   var op=Guid.NewGuid();ops[id]=op;await Use<DeviceOperations,Guid>(s=>s.RequestAsync(actor,op,id,v,"information.read",30,null,null));var oc=await Use<DeviceOperations,DeviceOperations.Claim?>(s=>s.ClaimAsync(worker))??throw new InvalidOperationException();await Use<DeviceOperations,Guid>(s=>s.CompleteAsync(worker,oc.Id,oc.Token,new("failed")));
   var ud=Guid.NewGuid();users[id]=ud;await Use<DeviceUserDownloads,Guid>(s=>s.RequestAsync(actor,ud,id,v,30));var uc=await Use<DeviceUserDownloads,DeviceUserDownloads.Claim?>(s=>s.ClaimAsync(worker))??throw new InvalidOperationException();await Use<DeviceUserDownloads,long>(s=>s.AppendAsync(worker,uc.Id,uc.Token,0,[new("001","scope","123",0,true)]));await Use<DeviceUserDownloads,Guid>(s=>s.CompleteAsync(worker,uc.Id,uc.Token,"succeeded",1));
   var ld=Guid.NewGuid();logs[id]=ld;await Use<DeviceAttendanceDownloads,Guid>(s=>s.RequestAsync(actor,ld,id,v,30,new(2026,11,8,0,0,0,TimeSpan.Zero),new(2026,11,9,0,0,0,TimeSpan.Zero),4,5));var lc=await Use<DeviceAttendanceDownloads,DeviceAttendanceDownloads.Claim?>(s=>s.ClaimAsync(worker))??throw new InvalidOperationException();await Use<DeviceAttendanceDownloads,long>(s=>s.AppendAsync(worker,lc.Id,lc.Token,0,[new("001",1,4,2026,11,8,8,0,0,0)]));await Use<DeviceAttendanceDownloads,Guid>(s=>s.CompleteAsync(worker,lc.Id,lc.Token,"succeeded",1));
   var file=Guid.NewGuid();files[id]=file;await Use<DeviceFileImports,Guid>(s=>s.CreateAsync(actor,new(file,id,v,"gregorian",1,4,5,Convert.ToBase64String(Encoding.UTF8.GetBytes("001\t2026-11-08 08:00:00\t1\t4\t1")))));
   var fv=await Q(db=>db.DeviceFileImports.Where(x=>x.Id==file).Select(x=>x.Version).SingleAsync());await Use<DeviceFileImports,object>(s=>s.CommitPageAsync(actor,file,fv,0,"IMPORT-READY-ROWS"));
  }
  check(await Reject<UnauthorizedAccessException>(()=>Use<DeviceAccess,object>(s=>s.ReadAsync(actor,actor,0)))&&await Reject<ArgumentException>(()=>Use<DeviceAccess,Guid>(s=>s.SetPolicyAsync(manager,manager,null,true,"RESTRICT-DEVICE-OPERATOR"))),"device-scope independent management right and other-operator requirement prevent self elevation");
  check(Json(await Use<DeviceRegistry,object>(s=>s.ListAsync(actor,company,0))).GetProperty("items").GetArrayLength()==2,"device-scope existing global operator retains legacy access until an explicit scope policy is approved");
  await Use<DeviceAccess,Guid>(s=>s.SetPolicyAsync(manager,actor,null,true,"RESTRICT-DEVICE-OPERATOR"));
  check(Json(await Use<DeviceRegistry,object>(s=>s.ListAsync(actor,company,0))).GetProperty("items").GetArrayLength()==0&&await Reject<ArgumentException>(()=>Use<DeviceRegistry,object>(s=>s.GetAsync(actor,a))),"device-scope restricted operator with no grants sees zero devices and cannot substitute a known device id");
  var grant=Guid.NewGuid();await Use<DeviceAccess,Guid>(s=>s.GrantAsync(manager,grant,actor,a));await Use<DeviceAccess,Guid>(s=>s.GrantAsync(manager,grant,actor,a));
  check(await I(db=>db.DeviceOperatorGrants.CountAsync(x=>x.Id==grant))==1&&await Reject<DomainConflictException>(()=>Use<DeviceAccess,Guid>(s=>s.GrantAsync(manager,grant,actor,b))),"device-scope exact grant replay is idempotent while changed-target replay conflicts");
  var page=Json(await Use<DeviceRegistry,object>(s=>s.ListAsync(actor,company,0)));
  check(page.GetProperty("items").GetArrayLength()==1&&page.GetProperty("items")[0].GetProperty("id").GetGuid()==a&&await Reject<ArgumentException>(()=>Use<DeviceRegistry,object>(s=>s.GetAsync(actor,b))),"device-scope inventory paging and direct lookup filter the same selected target");
  check(Only(await Use<DeviceOperations,object>(s=>s.ListAsync(actor,null,0)),a)&&Only(await Use<DeviceCommands,object>(s=>s.ListAsync(actor,null,0)),a)&&Json(await Use<DeviceCommands,object>(s=>s.ListAsync(actor,b,0))).GetProperty("items").GetArrayLength()==0,"device-scope typed operation history and raw SQL unified command view both enforce target selection before paging");
  check(Only(await Use<DeviceUserDownloads,object>(s=>s.ListAsync(actor,null,0)),a)&&await Reject<ArgumentException>(()=>Use<DeviceUserDownloads,object>(s=>s.PreviewAsync(actor,users[b],0))),"device-scope downloaded personal user snapshots and direct preview cannot leak another device");
  check(Only(await Use<DeviceAttendanceDownloads,object>(s=>s.ListAsync(actor,null,0)),a)&&await Reject<ArgumentException>(()=>Use<DeviceAttendanceDownloads,object>(s=>s.RowsAsync(actor,logs[b],0))),"device-scope original attendance tuples are inaccessible through a foreign extraction id");
  check(Only(await Use<DeviceFileImports,object>(s=>s.ListAsync(actor,null,0)),a)&&await Reject<ArgumentException>(()=>Use<DeviceFileImports,object>(s=>s.OriginalAsync(actor,files[b]))),"device-scope file history and exact original bytes remain restricted to selected devices");
  await using(var filteredScope=provider.CreateAsyncScope())
  {var filtered=filteredScope.ServiceProvider.GetRequiredService<IoDbContext>();var transaction=filteredScope.ServiceProvider.GetRequiredService<PermissionTransaction>();var counts=await transaction.ExecuteAsync(actor,DeviceRegistry.Read,"scope-test.rows",async token=>(new[]{await filtered.DeviceDownloadedUsers.CountAsync(token),await filtered.DeviceAttendanceRows.CountAsync(token),await filtered.DeviceFileImportLines.CountAsync(token)},actor));check(counts.SequenceEqual(new[]{1,1,1}),"device-scope dependent personal rows and file receipts inherit the parent selection even without a caller-supplied device filter");}

  check(Json(await Use<DeviceHealth,object>(s=>s.ListAsync(actor,null,0,0))).GetProperty("items").EnumerateArray().All(x=>x.GetProperty("id").GetGuid()==a)&&await Reject<UnauthorizedAccessException>(()=>Use<DeviceGroups,object>(s=>s.ListAsync(actor,null,null,0))),"device-scope health counts use scoped device ids and company-wide grouping is not a restricted-operator escape");
  check(await Reject<ArgumentException>(()=>Use<DeviceOperations,Guid>(s=>s.RequestAsync(actor,Guid.NewGuid(),b,bv,"information.read",30,null,null)))&&await Reject<UnauthorizedAccessException>(()=>Use<DeviceRegistry,Guid>(s=>s.CreateAsync(actor,Guid.NewGuid(),new(site,"ACCESS-C","denied","synthetic","zk-standalone","10.20.30.81",4370,true)))),"device-scope foreign-target enqueue and new inventory creation cannot bypass a restricted selection");
  await Use<DeviceAccess,Guid>(s=>s.SetPolicyAsync(manager,reader,null,true,"RESTRICT-DEVICE-OPERATOR"));await Use<DeviceAccess,Guid>(s=>s.GrantAsync(manager,Guid.NewGuid(),reader,a));
  check(await Reject<UnauthorizedAccessException>(()=>Use<DeviceOperations,Guid>(s=>s.RequestAsync(reader,Guid.NewGuid(),a,av,"clock.set",30,new(2026,11,8,10,0,0,TimeSpan.Zero),"SET-CLOCK"))),"device-scope target assignment never grants an independent read write or sensitive-operation permission");
  var policyVersion=await I(db=>db.DeviceOperatorPolicies.Where(x=>x.UserId==actor).Select(x=>x.Version).SingleAsync());
  check(await Reject<DomainConflictException>(()=>Use<DeviceAccess,Guid>(s=>s.SetPolicyAsync(manager,actor,new byte[8],false,"ALLOW-ALL-PERMITTED-DEVICES")))&&await Reject<ArgumentException>(()=>Use<DeviceAccess,Guid>(s=>s.SetPolicyAsync(manager,actor,policyVersion,false,"RESTRICT-DEVICE-OPERATOR"))),"device-scope broadening requires a fresh policy version and the distinct global-access confirmation");
  var scheduleId=Guid.NewGuid();await Use<DevicePollSchedules,Guid>(s=>s.CreateAsync(actor,new(scheduleId,a,av,new(2026,11,8,0,0,0,TimeSpan.Zero),15,3600,0,30,4,5,false,"ENABLE-SCHEDULED-SDK-READ")));
  var pending=Guid.NewGuid();await Use<DeviceUserDownloads,Guid>(s=>s.RequestAsync(actor,pending,a,av,30));var gv=await I(db=>db.DeviceOperatorGrants.Where(x=>x.Id==grant).Select(x=>x.Version).SingleAsync());await Use<DeviceAccess,Guid>(s=>s.RevokeAsync(manager,grant,gv));
  await Use<DeviceUserDownloads,DeviceUserDownloads.Claim?>(s=>s.ClaimAsync(worker));
  var step=await Use<DevicePollSchedules,DevicePollSchedules.Step>(s=>s.StepAsync(worker));
  check(step.State=="revoked"&&await Q(db=>db.DevicePollSchedules.AnyAsync(x=>x.Id==scheduleId&&!x.Enabled&&x.CursorUtc==new DateTimeOffset(2026,11,8,0,0,0,TimeSpan.Zero))),"device-scope target revoke stops automatic polling under the original actor without fabricating cursor progress");
  check(await Q(db=>db.DeviceUserDownloads.AnyAsync(x=>x.Id==pending&&x.State=="revoked"))&&Json(await Use<DeviceRegistry,object>(s=>s.ListAsync(actor,company,0))).GetProperty("items").GetArrayLength()==0,"device-scope device revoke removes existing reads immediately and blocks already queued dispatch by the original human");
  check(await Reject<DomainConflictException>(()=>Use<DeviceAccess,Guid>(s=>s.GrantAsync(manager,grant,actor,a)))&&await Reject<DomainConflictException>(()=>Use<DeviceAccess,Guid>(s=>s.RevokeAsync(manager,grant,gv))),"device-scope revoked grant identity cannot be revived by replay or stale revoke");
  var grant2=Guid.NewGuid();await Use<DeviceAccess,Guid>(s=>s.GrantAsync(manager,grant2,actor,a));
  var running=Guid.NewGuid();await Use<DeviceOperations,Guid>(s=>s.RequestAsync(actor,running,a,av,"information.read",30,null,null));var claim=await Use<DeviceOperations,DeviceOperations.Claim?>(s=>s.ClaimAsync(worker))??throw new InvalidOperationException();var g2v=await I(db=>db.DeviceOperatorGrants.Where(x=>x.Id==grant2).Select(x=>x.Version).SingleAsync());await Use<DeviceAccess,Guid>(s=>s.RevokeAsync(manager,grant2,g2v));
  check(await Use<DeviceOperations,DeviceCommunicationKey?>(s=>s.CredentialAsync(worker,claim,true))==null,"device-scope revocation after claim still denies private credential dispatch under an otherwise authorized worker");
  await Use<DeviceOperations,Guid>(s=>s.CompleteAsync(worker,claim.Id,claim.Token,new("failed")));
  check(await Reject<SqlException>(()=>I(db=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE dbo.DeviceOperatorGrants WHERE Id={grant}")))&&await Reject<SqlException>(()=>I(db=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DeviceOperatorGrants SET RevokedAtUtc=NULL WHERE Id={grant}")))&&await Reject<SqlException>(()=>I(db=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE dbo.DeviceOperatorPolicies WHERE UserId={actor}"))),"device-scope SQL retains policy and grant history and forbids direct grant resurrection");
  check(await Reject<SqlException>(()=>I(async db=>{await db.GetService<IMigrator>().MigrateAsync("20261108232751_InitialIdentity");return true;}))&&await I(db=>db.DeviceOperatorGrants.AnyAsync(x=>x.Id==grant)),"device-scope access history remains retained after downgrade refusal");
  // Same scoped DbContext may serve sequential service calls by different actors.
  await using(var scope=provider.CreateAsyncScope())
  {var registry=scope.ServiceProvider.GetRequiredService<DeviceRegistry>();var hidden=Json(await registry.ListAsync(actor,company,0));var visible=Json(await registry.ListAsync(reader,company,0));check(hidden.GetProperty("items").GetArrayLength()==0&&visible.GetProperty("items").GetArrayLength()==1,"device-scope query parameters are rebound per actor and do not leak through a reused EF context");}
 }
}
