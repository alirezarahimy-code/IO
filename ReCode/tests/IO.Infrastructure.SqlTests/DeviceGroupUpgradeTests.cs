using IO.Infrastructure.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

internal static class DeviceGroupUpgradeTests
{
    public static async Task RunAsync(string connection,Action<bool,string> check)
    {
        var builder=new SqlConnectionStringBuilder(connection){InitialCatalog="IO_GroupUpgrade_"+Guid.NewGuid().ToString("N")};
        await using var db=new IoDbContext(new DbContextOptionsBuilder<IoDbContext>().UseSqlServer(builder.ConnectionString).Options);
        try
        {
            var migrator=db.GetService<IMigrator>();await migrator.MigrateAsync("20261112000000_DeviceRegistry");
            var org=Guid.NewGuid();var company=Guid.NewGuid();var site=Guid.NewGuid();var actor=Guid.NewGuid();var device=Guid.NewGuid();var now=DateTimeOffset.UtcNow;
            db.Organizations.Add(new(){Id=org,Name="Group upgrade"});db.Companies.Add(new(){Id=company,OrganizationId=org,Name="Group upgrade company"});db.Sites.Add(new(){Id=site,CompanyId=company,Name="Group upgrade site",TimeZoneId="UTC"});db.Users.Add(new(){Id=actor,UserName="group-upgrade",IsActive=true});db.EventSources.Add(new(){Id=device,SiteId=site,Name="Legacy device"});await db.SaveChangesAsync();
            var fingerprint=new string('A',64);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.Devices(Id,SiteId,SourceId,Number,Name,Model,Profile,Host,Port,Enabled,CreationFingerprint,CreatedByUserId,CreatedAtUtc,UpdatedAtUtc) VALUES({device},{site},{device},{"GROUP-UPGRADE"},{"Legacy device"},{"Declared model"},{"offline-file"},NULL,NULL,{true},{fingerprint},{actor},{now},{now})");
            db.DeviceSecrets.Add(new(){DeviceId=device,ProtectedValue="opaque-legacy-protected-value"});await db.SaveChangesAsync();
            await migrator.MigrateAsync();await migrator.MigrateAsync();
            var upgraded=await db.Devices.AsNoTracking().SingleAsync();
            check(upgraded.GroupId==null&&upgraded.SourceId==device&&upgraded.CreationFingerprint==fingerprint&&await db.DeviceSecrets.Select(s=>s.ProtectedValue).SingleAsync()=="opaque-legacy-protected-value","device group upgrade preserves legacy device source fingerprint and protected credential with no invented membership");
            await migrator.MigrateAsync("20261112000000_DeviceRegistry");await migrator.MigrateAsync();
            check(await db.Devices.CountAsync()==1&&await db.DeviceSecrets.CountAsync()==1&&await db.DeviceGroups.CountAsync()==0,"device group empty-schema downgrade and reupgrade preserve preexisting device inventory");
            var group=Guid.NewGuid();db.DeviceGroups.Add(new(){Id=group,CompanyId=company,Name="Upgrade group",CreationFingerprint=fingerprint,CreatedByUserId=actor,CreatedAtUtc=now,UpdatedAtUtc=now});await db.SaveChangesAsync();
            var refused=false;try{await migrator.MigrateAsync("20261112000000_DeviceRegistry");}catch(SqlException e)when(e.Number==51015){refused=true;}
            check(refused&&await db.DeviceGroups.CountAsync()==1&&await db.Devices.CountAsync()==1,"device group downgrade refuses destruction after group or tombstone history exists");
        }
        finally{await db.Database.EnsureDeletedAsync();}
    }
}
