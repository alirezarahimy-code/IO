using IO.Infrastructure.Security;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace IO.Infrastructure.IdentityMigrations;

[DbContext(typeof(LoginIdentityDbContext))]
[Migration("20261113100000_DeviceOperatorAccess")]
public sealed class DeviceOperatorAccess : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable(name:"DeviceOperatorPolicies",columns:t=>new
        {
            UserId=t.Column<Guid>(type:"uniqueidentifier",nullable:false),
            Restricted=t.Column<bool>(type:"bit",nullable:false),
            ChangedAtUtc=t.Column<DateTimeOffset>(type:"datetimeoffset",nullable:false),
            Version=t.Column<byte[]>(type:"rowversion",rowVersion:true,nullable:false)
        },constraints:t=>{t.PrimaryKey("PK_DeviceOperatorPolicies",x=>x.UserId);t.CheckConstraint("CK_DeviceOperatorPolicies_Utc","DATEPART(TZOFFSET,[ChangedAtUtc])=0");});
        m.CreateTable(name:"DeviceOperatorGrants",columns:t=>new
        {
            Id=t.Column<Guid>(type:"uniqueidentifier",nullable:false),UserId=t.Column<Guid>(type:"uniqueidentifier",nullable:false),DeviceId=t.Column<Guid>(type:"uniqueidentifier",nullable:false),
            GrantedAtUtc=t.Column<DateTimeOffset>(type:"datetimeoffset",nullable:false),RevokedAtUtc=t.Column<DateTimeOffset>(type:"datetimeoffset",nullable:true),
            Version=t.Column<byte[]>(type:"rowversion",rowVersion:true,nullable:false)
        },constraints:t=>{t.PrimaryKey("PK_DeviceOperatorGrants",x=>x.Id);t.CheckConstraint("CK_DeviceOperatorGrants_Utc","DATEPART(TZOFFSET,[GrantedAtUtc])=0 AND DATEPART(TZOFFSET,[RevokedAtUtc])=0 AND ([RevokedAtUtc] IS NULL OR [RevokedAtUtc]>=[GrantedAtUtc])");});
        m.CreateIndex(name:"IX_DeviceOperatorGrants_UserId_DeviceId",table:"DeviceOperatorGrants",columns:new[]{"UserId","DeviceId"},unique:true,filter:"[RevokedAtUtc] IS NULL");
        // Cross-context authorization FKs: both contexts intentionally use the same DB.
        m.Sql("ALTER TABLE dbo.DeviceOperatorPolicies ADD CONSTRAINT FK_DeviceOperatorPolicies_Users FOREIGN KEY(UserId) REFERENCES dbo.Users(Id);");
        m.Sql("ALTER TABLE dbo.DeviceOperatorGrants ADD CONSTRAINT FK_DeviceOperatorGrants_Users FOREIGN KEY(UserId) REFERENCES dbo.Users(Id), CONSTRAINT FK_DeviceOperatorGrants_Devices FOREIGN KEY(DeviceId) REFERENCES dbo.Devices(Id);");
        m.Sql("""
CREATE TRIGGER dbo.TR_DeviceOperatorPolicies_History ON dbo.DeviceOperatorPolicies AFTER UPDATE, DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT UserId FROM deleted EXCEPT SELECT UserId FROM inserted) THROW 51000,'Device operator policies cannot be removed or retargeted.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON i.UserId=d.UserId WHERE i.ChangedAtUtc<d.ChangedAtUtc) THROW 51000,'Policy time cannot move backward.',1;
END
""");
        m.Sql("""
CREATE TRIGGER dbo.TR_DeviceOperatorGrants_History ON dbo.DeviceOperatorGrants AFTER UPDATE, DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT Id FROM deleted EXCEPT SELECT Id FROM inserted) THROW 51000,'Device grants cannot be removed or retargeted.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON i.Id=d.Id WHERE i.UserId<>d.UserId OR i.DeviceId<>d.DeviceId OR i.GrantedAtUtc<>d.GrantedAtUtc OR d.RevokedAtUtc IS NOT NULL OR i.RevokedAtUtc IS NULL) THROW 51000,'Only first explicit device grant revocation is allowed.',1;
END
""");
    }
    protected override void Down(MigrationBuilder m)
    {
        m.Sql("IF EXISTS(SELECT 1 FROM dbo.DeviceOperatorPolicies) OR EXISTS(SELECT 1 FROM dbo.DeviceOperatorGrants) THROW 51000,'Retained device access policy or grant history prevents downgrade.',1;");
        m.DropTable("DeviceOperatorGrants");m.DropTable("DeviceOperatorPolicies");
    }
}
