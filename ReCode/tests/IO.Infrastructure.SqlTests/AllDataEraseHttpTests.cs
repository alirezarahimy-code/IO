using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using IO.Infrastructure.Devices;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
internal static class AllDataEraseHttpTests
{
 internal static async Task RunAsync(ServiceProvider provider,Guid actor,DeviceAllDataErases.Input input,string family,Action<bool,string> check)
 {
  var route="/devices/all-data-erases";
  var pageRoute="device-all-data-erases";
  string connection,name;
  const string password="Synthetic!Erase-Test-2026";
  await using(var scope=provider.CreateAsyncScope())
  {
   var db=scope.ServiceProvider.GetRequiredService<IoDbContext>();connection=db.Database.GetConnectionString()!;name=await db.Users.Where(x=>x.Id==actor).Select(x=>x.UserName).SingleAsync();
   var manager=scope.ServiceProvider.GetRequiredService<UserManager<LoginAccount>>();if(await manager.FindByIdAsync(actor.ToString()) is null){var created=await manager.CreateAsync(new LoginAccount{Id=actor,UserName=name,LockoutEnabled=true},password);if(!created.Succeeded)throw new InvalidOperationException("Synthetic HTTP erase identity creation failed.");}
  }
  using var rsa=RSA.Create(2048);var req=new CertificateRequest("CN=localhost",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
  using var generated=req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1));
  using var cert=X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx),null,X509KeyStorageFlags.UserKeySet|X509KeyStorageFlags.PersistKeySet|X509KeyStorageFlags.Exportable);
  await using var app=IoWebHost.Build([],b=>{b.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{["IO:EnableExperimentalIdentity"]="true",["ConnectionStrings:IO"]=connection});b.Logging.ClearProviders();b.WebHost.ConfigureKestrel(k=>k.Listen(IPAddress.Loopback,0,l=>l.UseHttps(cert)));});
  await app.StartAsync();
  try
  {
   var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
   using var client=new HttpClient(new HttpClientHandler{CookieContainer=new CookieContainer(),AllowAutoRedirect=false,ServerCertificateCustomValidationCallback=(_,_,_,_)=>true}){BaseAddress=new(address)};
   check((await client.GetAsync("/devices/transport-support")).StatusCode==HttpStatusCode.Unauthorized,"R16 real HTTPS transport support is not an unauthenticated metadata endpoint");
   check((await client.GetAsync(route)).StatusCode==HttpStatusCode.Unauthorized,"actual HTTPS protected "+family+" erase metadata denies unauthenticated caller");
   async Task<string> Csrf(){using var r=await client.GetAsync("/auth/csrf");r.EnsureSuccessStatusCode();var j=await r.Content.ReadFromJsonAsync<JsonElement>();return j.GetProperty("token").GetString()!;}
   using var login=new HttpRequestMessage(HttpMethod.Post,"/auth/login"){Content=JsonContent.Create(new{UserName=name,Password=password})};login.Headers.Add("X-CSRF-TOKEN",await Csrf());
   check((await client.SendAsync(login)).StatusCode==HttpStatusCode.NoContent,"actual HTTPS protected "+family+" erase test logs in through production identity and antiforgery");
   using(var support=await client.GetAsync("/devices/transport-support")){support.EnsureSuccessStatusCode();var body=await support.Content.ReadFromJsonAsync<JsonElement>();var direct=body.GetProperty("result").GetProperty("transports").EnumerateArray().Where(e=>e.GetProperty("kind").GetString()!.EndsWith("-direct",StringComparison.Ordinal)).ToArray();check(direct.Length==2&&direct.All(e=>!e.GetProperty("softwareRouteAvailable").GetBoolean()&&e.GetProperty("status").GetString()=="unsupported-in-this-delivery"),"R16 actual HTTPS reports product disposition without claiming unsupported hardware");}
   using var page=await client.GetAsync("/workspace/"+pageRoute);using var script=await client.GetAsync("/assets/"+pageRoute+".js");
   check(page.StatusCode==HttpStatusCode.OK&&script.StatusCode==HttpStatusCode.OK&&(await page.Content.ReadAsStringAsync()).Contains("intent-form",StringComparison.Ordinal)&&(await script.Content.ReadAsStringAsync()).Contains("X-CSRF-TOKEN",StringComparison.Ordinal),"published embedded erase page and local asset are mapped behind authenticated grants (page="+(int)page.StatusCode+", script="+(int)script.StatusCode+")");
   check((await client.PostAsJsonAsync(route+"/preview",input)).StatusCode==HttpStatusCode.BadRequest,"actual HTTPS protected "+family+" erase preview rejects missing CSRF before any side effect");
   async Task<HttpResponseMessage> Preview(){using var request=new HttpRequestMessage(HttpMethod.Post,route+"/preview"){Content=JsonContent.Create(input)};request.Headers.Add("X-CSRF-TOKEN",await Csrf());return await client.SendAsync(request);}
   using(var reset=new HttpRequestMessage(HttpMethod.Post,route+"/preview"){Content=JsonContent.Create(new{input.Id,input.DeviceId,input.ArchiveId,input.DeviceVersion,input.ArchiveVersion,input.QuiescedAtUtc,input.EvidenceReference,input.Approval,input.AttendanceArchiveId,input.AttendanceArchiveVersion,factoryReset=true})}){reset.Headers.Add("X-CSRF-TOKEN",await Csrf());check((await client.SendAsync(reset)).StatusCode==HttpStatusCode.BadRequest,"R15 actual HTTPS rejects unsupported factoryReset field rather than silently changing deletion meaning");}
   using var disabled=await Preview();check(disabled.StatusCode==HttpStatusCode.Conflict,"actual HTTPS valid-CSRF erase request remains default-off independently of browser approval");
   Guid role,permission;
   await using(var scope=provider.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<IoDbContext>();role=await db.UserRoles.Where(x=>x.UserId==actor).Select(x=>x.RoleId).SingleAsync();permission=await db.Permissions.Where(x=>x.Code==DeviceAllDataErases.Right(family,"propose")).Select(x=>x.Id).SingleAsync();db.RolePermissions.Remove(await db.RolePermissions.SingleAsync(x=>x.RoleId==role&&x.PermissionId==permission));await db.SaveChangesAsync();}
   try{using var revoked=await Preview();check(revoked.StatusCode==HttpStatusCode.Forbidden,"actual HTTPS protected "+family+" erase permission revocation invalidates an already authenticated session immediately");}
   finally{await using var scope=provider.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<IoDbContext>();db.RolePermissions.Add(new(){RoleId=role,PermissionId=permission});await db.SaveChangesAsync();}
  }
  finally{await app.StopAsync();}
 }
}
