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
    public DbSet<EmploymentProfileRecord> EmploymentProfiles => Set<EmploymentProfileRecord>();
    public DbSet<EmployeeSiteAssignmentRecord> EmployeeSiteAssignments => Set<EmployeeSiteAssignmentRecord>();
    public DbSet<UserRecord> Users => Set<UserRecord>();
    public DbSet<RoleRecord> Roles => Set<RoleRecord>();
    public DbSet<PermissionRecord> Permissions => Set<PermissionRecord>();
    public DbSet<UserRoleRecord> UserRoles => Set<UserRoleRecord>();
    public DbSet<RolePermissionRecord> RolePermissions => Set<RolePermissionRecord>();
    public DbSet<UserOrgScopeRecord> UserOrgScopes => Set<UserOrgScopeRecord>();
    public DbSet<CalendarRecord> Calendars => Set<CalendarRecord>();
    public DbSet<CalendarDayRecord> CalendarDays => Set<CalendarDayRecord>();
    public DbSet<ShiftDefinitionRecord> ShiftDefinitions => Set<ShiftDefinitionRecord>();
    public DbSet<WorkScheduleAssignmentRecord> WorkScheduleAssignments => Set<WorkScheduleAssignmentRecord>();
    public DbSet<EventSourceRecord> EventSources => Set<EventSourceRecord>();
    public DbSet<RawAttendanceEventRecord> RawAttendanceEvents => Set<RawAttendanceEventRecord>();
    public DbSet<SourceEmployeeMappingRecord> SourceEmployeeMappings => Set<SourceEmployeeMappingRecord>();
    public DbSet<RawEventConflictRecord> RawEventConflicts => Set<RawEventConflictRecord>();
    public DbSet<ObservedRuleVersionRecord> ObservedRuleVersions => Set<ObservedRuleVersionRecord>();
    public DbSet<DailyCalculationRunRecord> DailyCalculationRuns => Set<DailyCalculationRunRecord>();
    public DbSet<CorrectionPolicyRecord> CorrectionPolicies => Set<CorrectionPolicyRecord>();
    public DbSet<CorrectionRequestRecord> CorrectionRequests => Set<CorrectionRequestRecord>();
    public DbSet<CorrectionDecisionRecord> CorrectionDecisions => Set<CorrectionDecisionRecord>();
    public DbSet<PeriodDraftRecord> PeriodDrafts => Set<PeriodDraftRecord>();

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
            b.HasAlternateKey(x => new { x.Id, x.CompanyId });
            b.HasOne<CompanyRecord>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<OrgUnitRecord>(b =>
        {
            b.ToTable("OrgUnits", t => t.HasCheckConstraint("CK_OrgUnits_NotOwnParent", "[ParentId] IS NULL OR [ParentId] <> [Id]"));
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Version).IsRowVersion();
            b.HasAlternateKey(x => new { x.Id, x.CompanyId });
            b.HasOne<CompanyRecord>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OrgUnitRecord>().WithMany().HasForeignKey(x => new { x.ParentId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<EmployeeRecord>(b =>
        {
            b.ToTable("Employees"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.PersonnelNumber).HasMaxLength(80).IsRequired();
            b.Property(x => x.DisplayName).HasMaxLength(200).IsRequired(); b.Property(x => x.Version).IsRowVersion();
            b.HasIndex(x => new { x.CompanyId, x.PersonnelNumber }).IsUnique();
            b.HasAlternateKey(x => new { x.Id, x.CompanyId });
            b.HasOne<CompanyRecord>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<EmployeeAssignmentRecord>(b =>
        {
            b.ToTable("EmployeeAssignments", t => t.HasCheckConstraint("CK_EmployeeAssignments_Interval",
                "[ValidUntilExclusive] IS NULL OR [ValidUntilExclusive] > [ValidFrom]"));
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Version).IsRowVersion();
            b.HasIndex(x => new { x.EmployeeId, x.ValidFrom });
            b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x => new { x.EmployeeId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OrgUnitRecord>().WithMany().HasForeignKey(x => new { x.OrgUnitId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<EmploymentProfileRecord>(b =>
        {
            b.ToTable("EmploymentProfiles", t => t.HasCheckConstraint("CK_EmploymentProfiles_Interval",
                "[ValidUntilExclusive] IS NULL OR [ValidUntilExclusive] > [ValidFrom]"));
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Version).IsRowVersion();
            b.Property(x => x.StatusCode).HasMaxLength(80).IsRequired(); b.Property(x => x.Title).HasMaxLength(200);
            b.HasIndex(x => new { x.EmployeeId, x.ValidFrom });
            b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x => new { x.EmployeeId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<EmployeeSiteAssignmentRecord>(b =>
        {
            b.ToTable("EmployeeSiteAssignments", t => t.HasCheckConstraint("CK_EmployeeSiteAssignments_Interval",
                "[ValidUntilExclusive] IS NULL OR [ValidUntilExclusive] > [ValidFrom]"));
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Version).IsRowVersion();
            b.HasIndex(x => new { x.EmployeeId, x.ValidFrom });
            b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x => new { x.EmployeeId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<SiteRecord>().WithMany().HasForeignKey(x => new { x.SiteId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
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
        model.Entity<CalendarRecord>(b =>
        {
            b.ToTable("Calendars"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Version).IsRowVersion();
            b.HasAlternateKey(x => new { x.Id, x.CompanyId });
            b.HasOne<CompanyRecord>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<CalendarDayRecord>(b =>
        {
            b.ToTable("CalendarDays", t =>
            {
                t.HasCheckConstraint("CK_CalendarDays_Kind", "[Kind] IN (0, 1, 2)");
                t.HasCheckConstraint("CK_CalendarDays_RecordedUtc", "DATEPART(TZOFFSET, [RecordedAtUtc]) = 0");
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Note).HasMaxLength(500);
            b.HasIndex(x => new { x.CalendarId, x.Date }).IsUnique();
            b.HasOne<CalendarRecord>().WithMany().HasForeignKey(x => x.CalendarId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ShiftDefinitionRecord>(b =>
        {
            b.ToTable("ShiftDefinitions", t => t.HasCheckConstraint("CK_ShiftDefinitions_Interval",
                "([EndDayOffset] = 0 AND [EndsAt] > [StartsAt]) OR ([EndDayOffset] = 1 AND [EndsAt] <= [StartsAt])"));
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Version).IsRowVersion();
            b.HasAlternateKey(x => new { x.Id, x.CompanyId });
            b.HasOne<SiteRecord>().WithMany().HasForeignKey(x => new { x.SiteId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<WorkScheduleAssignmentRecord>(b =>
        {
            b.ToTable("WorkScheduleAssignments", t => t.HasCheckConstraint("CK_WorkScheduleAssignments_Interval",
                "[ValidUntilExclusive] IS NULL OR [ValidUntilExclusive] > [ValidFrom]"));
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Version).IsRowVersion();
            b.HasIndex(x => new { x.EmployeeId, x.ValidFrom });
            b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x => new { x.EmployeeId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ShiftDefinitionRecord>().WithMany().HasForeignKey(x => new { x.ShiftId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<CalendarRecord>().WithMany().HasForeignKey(x => new { x.CalendarId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<EventSourceRecord>(b =>
        {
            b.ToTable("EventSources"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Version).IsRowVersion();
            b.HasOne<SiteRecord>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PeriodDraftRecord>(b =>
        {
            b.ToTable("PeriodDrafts", t =>
            {
                t.HasCheckConstraint("CK_PeriodDrafts_Interval", "[EndExclusive] > [Start] AND DATEDIFF(day, [Start], [EndExclusive]) <= 62");
                t.HasCheckConstraint("CK_PeriodDrafts_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                t.HasTrigger("TR_PeriodDrafts_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.HasIndex(x => new { x.EmployeeId, x.Start, x.EndExclusive });
            b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x => new { x.EmployeeId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<CorrectionPolicyRecord>(b =>
        {
            b.ToTable("CorrectionPolicies", t =>
            {
                t.HasCheckConstraint("CK_CorrectionPolicies_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                t.HasTrigger("TR_CorrectionPolicies_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.HasAlternateKey(x => new { x.Id, x.CompanyId });
            b.HasOne<CompanyRecord>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.ApproverUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<CorrectionRequestRecord>(b =>
        {
            b.ToTable("CorrectionRequests", t =>
            {
                t.HasCheckConstraint("CK_CorrectionRequests_State", "[State] IN (0,1,2,3)");
                t.HasCheckConstraint("CK_CorrectionRequests_Kind", "[PunchKind] IN (0,1)");
                t.HasCheckConstraint("CK_CorrectionRequests_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [ProposedAtUtc]) = 0");
                t.HasTrigger("TR_CorrectionRequests_ImmutablePayload"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Version).IsRowVersion();
            b.Property(x => x.Reason).HasMaxLength(500).IsRequired(); b.HasIndex(x => new { x.EmployeeId, x.WorkDate, x.State });
            b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x => new { x.EmployeeId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<CorrectionPolicyRecord>().WithMany().HasForeignKey(x => new { x.PolicyId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.RequesterUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<CorrectionDecisionRecord>(b =>
        {
            b.ToTable("CorrectionDecisions", t =>
            {
                t.HasCheckConstraint("CK_CorrectionDecisions_Decision", "[Decision] IN (1,2,3)");
                t.HasCheckConstraint("CK_CorrectionDecisions_Utc", "DATEPART(TZOFFSET, [AtUtc]) = 0");
                t.HasTrigger("TR_CorrectionDecisions_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Reason).HasMaxLength(500);
            b.HasIndex(x => x.RequestId).IsUnique();
            b.HasOne<CorrectionRequestRecord>().WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ObservedRuleVersionRecord>(b =>
        {
            b.ToTable("ObservedRuleVersions", t =>
            {
                t.HasCheckConstraint("CK_ObservedRules_Margins", "[BeforeMinutes] BETWEEN 0 AND 720 AND [AfterMinutes] BETWEEN 0 AND 720");
                t.HasCheckConstraint("CK_ObservedRules_Algorithm", "[Algorithm] = N'paired-v1'");
                t.HasCheckConstraint("CK_ObservedRules_CreatedUtc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                t.HasTrigger("TR_ObservedRuleVersions_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Algorithm).HasMaxLength(32).IsRequired();
            b.HasAlternateKey(x => new { x.Id, x.CompanyId });
            b.HasOne<CompanyRecord>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DailyCalculationRunRecord>(b =>
        {
            b.ToTable("DailyCalculationRuns", t =>
            {
                t.HasCheckConstraint("CK_CalculationRuns_Window", "[WindowEndUtc] > [WindowStartUtc]");
                t.HasCheckConstraint("CK_CalculationRuns_Utc", "DATEPART(TZOFFSET, [WindowStartUtc]) = 0 AND DATEPART(TZOFFSET, [WindowEndUtc]) = 0 AND DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                t.HasCheckConstraint("CK_CalculationRuns_Ticks", "[ObservedTicks] IS NULL OR [ObservedTicks] >= 0");
                t.HasTrigger("TR_DailyCalculationRuns_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.InputSnapshotJson).IsRequired(); b.Property(x => x.AnomaliesJson).IsRequired();
            b.HasIndex(x => new { x.EmployeeId, x.WorkDate, x.CreatedAtUtc });
            b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x => new { x.EmployeeId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ObservedRuleVersionRecord>().WithMany().HasForeignKey(x => new { x.RuleVersionId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EventSourceRecord>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<WorkScheduleAssignmentRecord>().WithMany().HasForeignKey(x => x.ScheduleAssignmentId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<SourceEmployeeMappingRecord>(b =>
        {
            b.ToTable("SourceEmployeeMappings", t =>
            {
                t.HasCheckConstraint("CK_SourceEmployeeMappings_Interval", "[ValidUntilUtcExclusive] IS NULL OR [ValidUntilUtcExclusive] > [ValidFromUtc]");
                t.HasCheckConstraint("CK_SourceEmployeeMappings_StartUtc", "DATEPART(TZOFFSET, [ValidFromUtc]) = 0");
                t.HasCheckConstraint("CK_SourceEmployeeMappings_EndUtc", "[ValidUntilUtcExclusive] IS NULL OR DATEPART(TZOFFSET, [ValidUntilUtcExclusive]) = 0");
                t.HasCheckConstraint("CK_SourceEmployeeMappings_Identifier", "DATALENGTH([SourcePersonId]) > 0");
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Version).IsRowVersion();
            b.Property(x => x.SourcePersonId).HasMaxLength(100).IsRequired();
            b.Property<byte[]>("SourcePersonIdBytes").HasColumnType("varbinary(200)")
                .HasComputedColumnSql("CONVERT(varbinary(200), [SourcePersonId])", stored: true);
            b.Property<int>("SourcePersonIdByteLength").HasComputedColumnSql("DATALENGTH([SourcePersonId])", stored: true);
            b.HasIndex(nameof(SourceEmployeeMappingRecord.SourceId), "SourcePersonIdBytes", "SourcePersonIdByteLength", nameof(SourceEmployeeMappingRecord.ValidFromUtc));
            b.HasOne<EventSourceRecord>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x => new { x.EmployeeId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RawEventConflictRecord>(b =>
        {
            b.ToTable("RawEventConflicts", t =>
            {
                t.HasCheckConstraint("CK_RawEventConflicts_ReceivedUtc", "DATEPART(TZOFFSET, [ReceivedAtUtc]) = 0");
                t.HasCheckConstraint("CK_RawEventConflicts_OccurredUtc", "[OccurredAtUtc] IS NULL OR DATEPART(TZOFFSET, [OccurredAtUtc]) = 0");
                t.HasTrigger("TR_RawEventConflicts_Immutable");
                t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.ExternalEventId).HasMaxLength(180).IsRequired();
            b.Property(x => x.SourceEmployeeIdentifier).HasMaxLength(100); b.Property(x => x.SourceTimeZoneId).HasMaxLength(100);
            b.Property(x => x.ReportedEventType).HasMaxLength(80); b.Property(x => x.SourcePayload).IsRequired();
            b.HasIndex(x => new { x.SourceId, x.ReceivedAtUtc });
            b.HasOne<RawAttendanceEventRecord>().WithMany().HasForeignKey(x => x.OriginalEventId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EventSourceRecord>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RawAttendanceEventRecord>(b =>
        {
            b.ToTable("RawAttendanceEvents", t =>
            {
                t.HasCheckConstraint("CK_RawEvents_ReceivedUtc", "DATEPART(TZOFFSET, [ReceivedAtUtc]) = 0");
                t.HasCheckConstraint("CK_RawEvents_OccurredUtc", "[OccurredAtUtc] IS NULL OR DATEPART(TZOFFSET, [OccurredAtUtc]) = 0");
                t.HasCheckConstraint("CK_RawEvents_ExternalIdNotEmpty", "[ExternalEventId] IS NULL OR DATALENGTH([ExternalEventId]) > 0");
                t.HasTrigger("TR_RawAttendanceEvents_Immutable");
                t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.ExternalEventId).HasMaxLength(180);
            b.Property<byte[]?>("ExternalEventIdBytes").HasColumnType("varbinary(360)")
                .HasComputedColumnSql("CONVERT(varbinary(360), [ExternalEventId])", stored: true);
            // SQL Server can pad binary comparisons with zeroes. Include byte length
            // so distinct trailing NUL code units are not collapsed as duplicates.
            b.Property<int?>("ExternalEventIdByteLength")
                .HasComputedColumnSql("DATALENGTH([ExternalEventId])", stored: true);
            b.HasIndex(nameof(RawAttendanceEventRecord.SourceId), "ExternalEventIdBytes", "ExternalEventIdByteLength").IsUnique()
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
