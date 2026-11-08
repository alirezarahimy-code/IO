using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Storage;

/// <summary>Initial SQL Server schema only; no automatic migration at application startup.</summary>
public sealed class IoDbContext(DbContextOptions<IoDbContext> options) : DbContext(options)
{
    public DbSet<OrganizationRecord> Organizations => Set<OrganizationRecord>();
    public DbSet<CompanyRecord> Companies => Set<CompanyRecord>();
    public DbSet<SiteRecord> Sites => Set<SiteRecord>();
    public DbSet<OrgUnitRecord> OrgUnits => Set<OrgUnitRecord>();
    public DbSet<EmployeeRecord> Employees => Set<EmployeeRecord>();
    public DbSet<EmployeeAssignmentRecord> EmployeeAssignments => Set<EmployeeAssignmentRecord>();
    public DbSet<UserRecord> Users => Set<UserRecord>();
    public DbSet<RoleRecord> Roles => Set<RoleRecord>();
    public DbSet<PermissionRecord> Permissions => Set<PermissionRecord>();
    public DbSet<UserRoleRecord> UserRoles => Set<UserRoleRecord>();
    public DbSet<RolePermissionRecord> RolePermissions => Set<RolePermissionRecord>();
    public DbSet<UserOrgScopeRecord> UserOrgScopes => Set<UserOrgScopeRecord>();
    public DbSet<EventSourceRecord> EventSources => Set<EventSourceRecord>();
    public DbSet<RawAttendanceEventRecord> RawAttendanceEvents => Set<RawAttendanceEventRecord>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<OrganizationRecord>(b =>
        {
            b.ToTable("Organizations"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Version).IsRowVersion();
        });
        model.Entity<CompanyRecord>(b =>
        {
            b.ToTable("Companies"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Version).IsRowVersion();
            b.HasOne<OrganizationRecord>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<SiteRecord>(b =>
        {
            b.ToTable("Sites"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.TimeZoneId).HasMaxLength(100).IsRequired(); b.Property(x => x.Version).IsRowVersion();
            b.HasOne<CompanyRecord>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<OrgUnitRecord>(b =>
        {
            b.ToTable("OrgUnits", t => t.HasCheckConstraint("CK_OrgUnits_NotOwnParent", "[ParentId] IS NULL OR [ParentId] <> [Id]"));
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Version).IsRowVersion();
            b.HasOne<CompanyRecord>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OrgUnitRecord>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<EmployeeRecord>(b =>
        {
            b.ToTable("Employees"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.PersonnelNumber).HasMaxLength(80).IsRequired();
            b.Property(x => x.DisplayName).HasMaxLength(200).IsRequired(); b.Property(x => x.Version).IsRowVersion();
            b.HasIndex(x => new { x.CompanyId, x.PersonnelNumber }).IsUnique();
            b.HasOne<CompanyRecord>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<EmployeeAssignmentRecord>(b =>
        {
            b.ToTable("EmployeeAssignments", t => t.HasCheckConstraint("CK_EmployeeAssignments_Interval",
                "[ValidUntilExclusive] IS NULL OR [ValidUntilExclusive] > [ValidFrom]"));
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Version).IsRowVersion();
            b.HasIndex(x => new { x.EmployeeId, x.ValidFrom });
            b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OrgUnitRecord>().WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<UserRecord>(b =>
        {
            b.ToTable("Users"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.UserName).HasMaxLength(200).IsRequired(); b.Property(x => x.Version).IsRowVersion();
            b.Property(x => x.IsActive).HasDefaultValue(false);
            b.HasIndex(x => x.UserName).IsUnique();
            b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RoleRecord>(b =>
        {
            b.ToTable("Roles"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(150).IsRequired(); b.Property(x => x.Version).IsRowVersion();
            b.HasIndex(x => x.Name).IsUnique();
        });
        model.Entity<PermissionRecord>(b =>
        {
            b.ToTable("Permissions"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Code).HasMaxLength(150).IsRequired(); b.Property(x => x.Version).IsRowVersion();
            b.HasIndex(x => x.Code).IsUnique();
        });
        model.Entity<UserRoleRecord>(b =>
        {
            b.ToTable("UserRoles"); b.HasKey(x => new { x.UserId, x.RoleId });
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<RoleRecord>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RolePermissionRecord>(b =>
        {
            b.ToTable("RolePermissions"); b.HasKey(x => new { x.RoleId, x.PermissionId });
            b.HasOne<RoleRecord>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<PermissionRecord>().WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<UserOrgScopeRecord>(b =>
        {
            b.ToTable("UserOrgScopes", t => t.HasCheckConstraint("CK_UserOrgScopes_Interval",
                "[ValidUntilExclusive] IS NULL OR [ValidUntilExclusive] > [ValidFrom]"));
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Version).IsRowVersion();
            b.HasIndex(x => new { x.UserId, x.OrgUnitId, x.ValidFrom });
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OrgUnitRecord>().WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<EventSourceRecord>(b =>
        {
            b.ToTable("EventSources"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Version).IsRowVersion();
            b.HasOne<SiteRecord>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RawAttendanceEventRecord>(b =>
        {
            b.ToTable("RawAttendanceEvents", t =>
            {
                t.HasCheckConstraint("CK_RawEvents_ReceivedUtc", "DATEPART(TZOFFSET, [ReceivedAtUtc]) = 0");
                t.HasCheckConstraint("CK_RawEvents_OccurredUtc", "[OccurredAtUtc] IS NULL OR DATEPART(TZOFFSET, [OccurredAtUtc]) = 0");
                t.HasTrigger("TR_RawAttendanceEvents_Immutable");
                t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.ExternalEventId).HasMaxLength(180);
            b.Property<byte[]?>("ExternalEventIdBytes").HasColumnType("varbinary(360)")
                .HasComputedColumnSql("CONVERT(varbinary(360), [ExternalEventId])", stored: true);
            b.HasIndex(nameof(RawAttendanceEventRecord.SourceId), "ExternalEventIdBytes").IsUnique()
                .HasFilter("[ExternalEventId] IS NOT NULL");
            b.Property(x => x.SourceEmployeeIdentifier).HasMaxLength(100);
            b.Property(x => x.SourceTimeZoneId).HasMaxLength(100);
            b.Property(x => x.ReportedEventType).HasMaxLength(80);
            b.Property(x => x.SourcePayload).IsRequired();
            b.HasIndex(x => new { x.SourceId, x.OccurredAtUtc });
            b.HasOne<EventSourceRecord>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
