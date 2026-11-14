using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
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

internal static class DevicePushReviewTests
{
    public static async Task RunAsync(string connection,Action<bool,string> check)
    {
        var services=new ServiceCollection();services.AddLogging();services.AddIoIdentityStorage(connection);
        await using var provider=services.BuildServiceProvider();
        async Task<T> Use<S,T>(Func<S,Task<T>> f)where S:notnull
        {await using var scope=provider.CreateAsyncScope();return await f(scope.ServiceProvider.GetRequiredService<S>());}
        Task<T> Q<T>(Func<IoDbContext,Task<T>> f)=>Use<IoDbContext,T>(f);
        Task<T> I<T>(Func<LoginIdentityDbContext,Task<T>> f)=>Use<LoginIdentityDbContext,T>(f);
        JsonElement Json(object value)=>JsonSerializer.SerializeToElement(value,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase});
        async Task<bool> Reject<E>(Func<Task> action)where E:Exception{try{await action();return false;}catch(E){return true;}}
        var manager=Guid.NewGuid();var retainer=Guid.NewGuid();var reader=Guid.NewGuid();var role=Guid.NewGuid();
        var org=Guid.NewGuid();var company=Guid.NewGuid();var site=Guid.NewGuid();var device=Guid.NewGuid();var otherDevice=Guid.NewGuid();var employee=Guid.NewGuid();
        const string password="Synthetic!PushReview-2026";var readerName="push-review-"+reader.ToString("N");var retainName="push-retain-"+retainer.ToString("N");
        await Q(async db=>
        {
            await db.Database.MigrateAsync();
            db.Organizations.Add(new(){Id=org,Name="PUSH review org"});db.Companies.Add(new(){Id=company,OrganizationId=org,Name="PUSH review company"});
            db.Sites.Add(new(){Id=site,CompanyId=company,Name="PUSH review site",TimeZoneId="UTC"});
            db.Employees.Add(new(){Id=employee,CompanyId=company,PersonnelNumber="PUSH-REVIEW",DisplayName="Not exposed by review"});
            foreach(var(user,name,rights)in new[]{(manager,"push-manager-"+manager.ToString("N"),new[]{DeviceRegistry.Create,DeviceAccess.Manage}),
                (retainer,retainName,new[]{DevicePushJournal.Retain}),(reader,readerName,new[]{DevicePushJournal.Read})})
            {
                var r=user==reader?role:Guid.NewGuid();db.Users.Add(new(){Id=user,UserName=name,IsActive=true});db.Roles.Add(new(){Id=r,Name=name});db.UserRoles.Add(new(){UserId=user,RoleId=r});
                foreach(var code in rights){var p=db.Permissions.Local.FirstOrDefault(x=>x.Code==code)??await db.Permissions.SingleOrDefaultAsync(x=>x.Code==code);
                    if(p is null){p=new(){Id=Guid.NewGuid(),Code=code};db.Permissions.Add(p);}db.RolePermissions.Add(new(){RoleId=r,PermissionId=p.Id});}
            }
            await db.SaveChangesAsync();return true;
        });
        foreach(var(key,number)in new[]{(device,"PUSH-REVIEW-A"),(otherDevice,"PUSH-REVIEW-B")})
            await Use<DeviceRegistry,Guid>(s=>s.CreateAsync(manager,key,new(site,number,number,"synthetic unverified","zk-push",null,null,true)));
        await Q(async db=>
        {
            db.SourceEmployeeMappings.AddRange(new SourceEmployeeMappingRecord{Id=Guid.NewGuid(),SourceId=device,SourcePersonId="001",EmployeeId=employee,CompanyId=company,
                ValidFromUtc=new(2026,11,8,8,0,0,TimeSpan.Zero),ValidUntilUtcExclusive=new(2026,11,8,9,0,0,TimeSpan.Zero)},
                new SourceEmployeeMappingRecord{Id=Guid.NewGuid(),SourceId=device,SourcePersonId="002",EmployeeId=employee,CompanyId=company,
                ValidFromUtc=new(2026,11,8,7,0,0,TimeSpan.Zero),ValidUntilUtcExclusive=new(2026,11,8,8,0,0,TimeSpan.Zero)});
            await db.SaveChangesAsync();return true;
        });
        var line="001\t2026-11-08 08:00:00\t0\t15\t000\t0\t0";
        var lines=new[]{line,line,line.Replace("001\t","1\t",StringComparison.Ordinal),line.Replace("001\t","002\t",StringComparison.Ordinal),"bad-line",line.Replace("001\t",new string('9',101)+"\t",StringComparison.Ordinal)}.Concat(Enumerable.Repeat(line,49));
        var body=Encoding.UTF8.GetBytes(string.Join("\r\n",lines)+"\r\n");
        var version=await Q(db=>db.Devices.Where(x=>x.Id==device).Select(x=>x.Version).SingleAsync());var evidence=Guid.NewGuid();
        var input=new DevicePushJournal.Input(evidence,device,version,"CLAIMED-REVIEW","not-a-nonce",body);
        for(var n=0;n<51;n++)await Use<DevicePushJournal,DevicePushJournal.RetentionReceipt>(s=>s.RetainForReviewAsync(retainer,input with{OperationId=n==0?evidence:Guid.NewGuid()}));
        var ov=await Q(db=>db.Devices.Where(x=>x.Id==otherDevice).Select(x=>x.Version).SingleAsync());var otherEvidence=Guid.NewGuid();
        await Use<DevicePushJournal,DevicePushJournal.RetentionReceipt>(s=>s.RetainForReviewAsync(retainer,input with{OperationId=otherEvidence,DeviceId=otherDevice,ExpectedDeviceVersion=ov}));
        Task<object> List(Guid? filter,int offset=0,Guid? actor=null)=>Use<DevicePushReview,object>(s=>s.ListAsync(actor??reader,filter,offset));
        Task<object> Rows(int after=0,Guid? actor=null,Guid? id=null)=>Use<DevicePushReview,object>(s=>s.RowsAsync(actor??reader,id??evidence,after));
        check(await Reject<UnauthorizedAccessException>(()=>List(null,actor:retainer))&&await Reject<UnauthorizedAccessException>(()=>Rows(actor:retainer))&&
            await Reject<UnauthorizedAccessException>(()=>Use<DevicePushReview,object>(s=>s.OriginalAsync(retainer,evidence))),
            "R08 review retain permission implies no metadata rows or original-body read");
        check(await Reject<ArgumentException>(()=>List(null,-1))&&await Reject<ArgumentException>(()=>List(Guid.Empty))&&
            await Reject<ArgumentException>(()=>Rows(-1))&&await Reject<ArgumentException>(()=>Rows(10001)),
            "R08 review rejects negative oversized cursors and an empty device filter");
        var first=Json(await List(device));var second=Json(await List(device,50));
        check(first.GetProperty("items").GetArrayLength()==50&&first.GetProperty("hasMore").GetBoolean()&&second.GetProperty("items").GetArrayLength()==1&&
            !second.GetProperty("hasMore").GetBoolean()&&first.GetProperty("items").EnumerateArray().Select(x=>x.GetProperty("id").GetGuid()).Intersect(second.GetProperty("items").EnumerateArray().Select(x=>x.GetProperty("id").GetGuid())).Count()==0,
            "R08 review message pagination exposes50 plus1 stable separate operation identities without hash deduplication");
        check(!first.GetRawText().Contains("CLAIMED-REVIEW",StringComparison.Ordinal)&&!first.GetRawText().Contains("rowsJson",StringComparison.Ordinal)&&
            !first.GetRawText().Contains("originalBody",StringComparison.Ordinal),"R08 review list projects bounded metadata instead of embedding serial original bytes or full row snapshots");
        var page=Json(await Rows());var last=Json(await Rows(50));var rows=page.GetProperty("items").EnumerateArray().ToArray();
        check(rows.Length==50&&page.GetProperty("nextAfter").GetInt32()==50&&page.GetProperty("hasMore").GetBoolean()&&
            last.GetProperty("items").GetArrayLength()==5&&!last.GetProperty("hasMore").GetBoolean()&&last.GetProperty("nextAfter").GetInt32()==55,
            "R08 review immutable snapshot rows page50 plus5 without dropping duplicate multiplicity");
        check(rows[0].GetProperty("mappingStatus").GetString()=="exact-mapping-found"&&rows[0].GetProperty("quarantineStatus").GetString()=="unverified-device-identity"&&
            rows[1].GetProperty("multiplicityRank").GetInt32()==2&&rows[1].GetProperty("quarantineStatus").GetString()=="repeated-row-requires-review"&&
            rows[2].GetProperty("mappingStatus").GetString()=="unknown-employee"&&rows[3].GetProperty("mappingStatus").GetString()=="unknown-employee",
            "R08 review uses exact binary PIN and half-open mapping intervals but keeps mapped and repeated rows unverified");
        check(rows[4].GetProperty("wireStatus").GetString()=="layout-mismatch"&&rows[5].GetProperty("mappingStatus").GetString()=="unsupported-person-length"&&
            rows[0].GetProperty("punchState").GetString()=="0"&&rows[0].GetProperty("workCode").GetString()=="000"&&
            !page.GetProperty("deviceIdentityVerified").GetBoolean()&&!page.GetProperty("attendanceWritten").GetBoolean()&&!page.GetProperty("vendorAckAvailable").GetBoolean(),
            "R08 review separates malformed unsupported-person raw-code and trust states without inferring IN OUT processing or ACK");
        await Q(db=>db.SourceEmployeeMappings.Where(x=>x.SourceId==device&&x.SourcePersonId=="001").ExecuteDeleteAsync());
        check(Json(await Rows()).GetProperty("items")[0].GetProperty("mappingStatus").GetString()=="unknown-employee"&&
            !await Q(db=>db.RawAttendanceEvents.AnyAsync(x=>x.SourceId==device)),"R08 review observes current mapping changes without mutating original snapshots employees or raw attendance");
        await Q(db=>db.Sites.Where(x=>x.Id==site).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.TimeZoneId,"Asia/Tehran")));
        var changed=Json(await Rows());
        check(!changed.GetProperty("sameTarget").GetBoolean()&&changed.GetProperty("items")[0].GetProperty("quarantineStatus").GetString()=="target-changed"&&
            changed.GetProperty("items")[0].GetProperty("occurredAtUtc").GetDateTimeOffset()==new DateTimeOffset(2026,11,8,8,0,0,TimeSpan.Zero),
            "R08 review marks changed installation quarantine without reinterpreting the pinned UTC snapshot");
        await Use<DeviceAccess,Guid>(s=>s.SetPolicyAsync(manager,reader,null,true,"RESTRICT-DEVICE-OPERATOR"));
        var grant=Guid.NewGuid();await Use<DeviceAccess,Guid>(s=>s.GrantAsync(manager,grant,reader,device));
        check(Json(await List(null)).GetProperty("items").EnumerateArray().All(x=>x.GetProperty("deviceId").GetGuid()==device)&&
            Json(await List(otherDevice)).GetProperty("items").GetArrayLength()==0&&await Reject<ArgumentException>(()=>Rows(id:otherEvidence))&&
            await Reject<ArgumentException>(()=>Use<DevicePushReview,object>(s=>s.OriginalAsync(reader,otherEvidence))),
            "R08 review device scopes filter metadata and deny foreign known-id rows and original bytes");
        // Real HTTPS/session tests use the same SQL-backed services; synthetic evidence, never hardware acceptance.
        foreach(var(user,name)in new[]{(reader,readerName),(retainer,retainName)})
            await Use<UserManager<LoginAccount>,bool>(async users=>{var result=await users.CreateAsync(new(){Id=user,UserName=name,LockoutEnabled=true},password);if(!result.Succeeded)throw new InvalidOperationException("PUSH review account creation failed.");return true;});
        using var key=RSA.Create(2048);var certRequest=new CertificateRequest("CN=localhost",key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        using var cert=certRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1));
        var app=IoWebHost.Build([],builder=>{builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{{"IO:EnableExperimentalIdentity","true"},{"ConnectionStrings:IO",connection}});builder.Logging.ClearProviders();builder.WebHost.ConfigureKestrel(k=>k.Listen(IPAddress.Loopback,0,l=>l.UseHttps(cert)));});
        try
        {
            await app.StartAsync();var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            HttpClient Client()=>new(new HttpClientHandler{CookieContainer=new(),ServerCertificateCustomValidationCallback=(_,_,_,_)=>true}){BaseAddress=new(address)};
            async Task Login(HttpClient client,string name){using var csrf=JsonDocument.Parse(await client.GetStringAsync("/auth/csrf"));using var request=new HttpRequestMessage(HttpMethod.Post,"/auth/login");request.Headers.Add("X-CSRF-TOKEN",csrf.RootElement.GetProperty("token").GetString());request.Content=JsonContent.Create(new{UserName=name,Password=password});using var response=await client.SendAsync(request);response.EnsureSuccessStatusCode();}
            using var anon=Client();using var read=Client();using var retain=Client();await Login(read,readerName);await Login(retain,retainName);
            check((await anon.GetAsync("/device-push-evidence")).StatusCode==HttpStatusCode.Unauthorized&&
                (await retain.GetAsync("/device-push-evidence")).StatusCode==HttpStatusCode.Forbidden&&
                (await retain.GetAsync("/help/topic/device-push-evidence")).StatusCode==HttpStatusCode.Forbidden,
                "HTTP R08 anonymous and retain-only sessions cannot read evidence or protected review help");
            using var listed=JsonDocument.Parse(await read.GetStringAsync("/device-push-evidence?deviceId="+device));
            using var detail=JsonDocument.Parse(await read.GetStringAsync("/device-push-evidence/"+evidence+"/rows?after=50"));
            using var original=JsonDocument.Parse(await read.GetStringAsync("/device-push-evidence/"+evidence+"/original"));
            check(listed.RootElement.GetProperty("items").GetArrayLength()==50&&detail.RootElement.GetProperty("items").GetArrayLength()==5&&
                Convert.FromBase64String(original.RootElement.GetProperty("originalBase64").GetString()!).SequenceEqual(body)&&
                (await read.GetAsync("/help/topic/device-push-evidence")).StatusCode==HttpStatusCode.OK,
                "HTTP R08 independently granted scoped reader obtains paged SQL evidence protected help and exact original CRLF bytes");
            check((await read.GetAsync("/device-push-evidence?offset=-1")).StatusCode==HttpStatusCode.BadRequest&&
                (await read.GetAsync("/device-push-evidence/"+evidence+"/rows?after=10001")).StatusCode==HttpStatusCode.BadRequest&&
                (await read.GetAsync("/device-push-evidence/"+otherEvidence+"/original")).StatusCode==HttpStatusCode.BadRequest,
                "HTTP R08 cursor validation and device scope remain server enforced for direct URL substitution");
            check((await read.PostAsJsonAsync("/device-push-evidence",new{})).StatusCode==HttpStatusCode.MethodNotAllowed&&
                (await read.GetAsync("/iclock/cdata?SN=CLAIMED-REVIEW&table=ATTLOG")).StatusCode==HttpStatusCode.NotFound,
                "HTTP R08 review exposes no mutation endpoint or SN-only vendor ingress");
            var gv=await I(db=>db.DeviceOperatorGrants.Where(x=>x.Id==grant).Select(x=>x.Version).SingleAsync());
            await Use<DeviceAccess,Guid>(s=>s.RevokeAsync(manager,grant,gv));
            check((await read.GetAsync("/device-push-evidence/"+evidence+"/rows")).StatusCode==HttpStatusCode.BadRequest&&
                (await read.GetAsync("/device-push-evidence/"+evidence+"/original")).StatusCode==HttpStatusCode.BadRequest,
                "HTTP R08 device-grant revocation removes personal evidence access on the same authenticated session");
            await Q(db=>db.RolePermissions.Where(x=>x.RoleId==role).ExecuteDeleteAsync());
            using var caps=JsonDocument.Parse(await read.GetStringAsync("/workspace/capabilities"));
            check((await read.GetAsync("/device-push-evidence")).StatusCode==HttpStatusCode.Forbidden&&
                (await read.GetAsync("/help/topic/device-push-evidence")).StatusCode==HttpStatusCode.Forbidden&&
                !caps.RootElement.GetProperty("pages").EnumerateArray().Any(x=>x.GetProperty("route").GetString()=="/workspace/device-push-evidence"),
                "HTTP R08 read permission revocation removes list help and capability navigation without re-login");
        }
        finally{await app.StopAsync();await app.DisposeAsync();}
    }
}
