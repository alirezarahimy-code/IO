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
    public DbSet<SourceMappingChangeRecord> SourceMappingChanges => Set<SourceMappingChangeRecord>();
    public DbSet<RawEventConflictRecord> RawEventConflicts => Set<RawEventConflictRecord>();
    public DbSet<RawConflictNoteRecord> RawConflictNotes => Set<RawConflictNoteRecord>();
    public DbSet<ObservedRuleVersionRecord> ObservedRuleVersions => Set<ObservedRuleVersionRecord>();
    public DbSet<DailyCalculationRunRecord> DailyCalculationRuns => Set<DailyCalculationRunRecord>();
    public DbSet<CalculationInputRevisionRecord> CalculationInputRevisions => Set<CalculationInputRevisionRecord>();
    public DbSet<CorrectionPolicyRecord> CorrectionPolicies => Set<CorrectionPolicyRecord>();
    public DbSet<CorrectionRequestRecord> CorrectionRequests => Set<CorrectionRequestRecord>();
    public DbSet<CorrectionDecisionRecord> CorrectionDecisions => Set<CorrectionDecisionRecord>();
    public DbSet<PeriodDraftRecord> PeriodDrafts => Set<PeriodDraftRecord>();
    public DbSet<PeriodDiagnosticRecord> PeriodDiagnostics => Set<PeriodDiagnosticRecord>();
    public DbSet<LeaveTypeRecord> LeaveTypes => Set<LeaveTypeRecord>();
    public DbSet<LeaveLedgerEntryRecord> LeaveLedgerEntries => Set<LeaveLedgerEntryRecord>();
    public DbSet<LeaveRequestRecord> LeaveRequests => Set<LeaveRequestRecord>();
    public DbSet<LeaveDecisionRecord> LeaveDecisions => Set<LeaveDecisionRecord>();

    public DbSet<DelegationRecord> Delegations => Set<DelegationRecord>();
    public DbSet<DelegationRevocationRecord> DelegationRevocations => Set<DelegationRevocationRecord>();

    public DbSet<CorrectionSettingsRecord> CorrectionSettings => Set<CorrectionSettingsRecord>();
    public DbSet<CorrectionDelegationGrantRecord> CorrectionDelegationGrants => Set<CorrectionDelegationGrantRecord>();
    public DbSet<DelegatedCorrectionDecisionRecord> DelegatedCorrectionDecisions => Set<DelegatedCorrectionDecisionRecord>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<CorrectionDelegationGrantRecord>(b =>
        {
            b.ToTable("CorrectionDelegationGrants", t =>
            {
                t.HasCheckConstraint("CK_CorrectionDelegationGrants_Utc", "DATEPART(TZOFFSET, [GrantedAtUtc]) = 0");
                t.HasTrigger("TR_CorrectionDelegationGrants_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.DelegationId); b.Property(x => x.DelegationId).ValueGeneratedNever();
            b.HasOne<DelegationRecord>().WithMany().HasForeignKey(x => x.DelegationId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.PrincipalUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DelegatedCorrectionDecisionRecord>(b =>
        {
            b.ToTable("DelegatedCorrectionDecisions", t =>
            {
                t.HasTrigger("TR_DelegatedCorrectionDecisions_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.DecisionId); b.Property(x => x.DecisionId).ValueGeneratedNever();
            b.HasOne<CorrectionDecisionRecord>().WithMany().HasForeignKey(x => x.DecisionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<CorrectionDelegationGrantRecord>().WithMany().HasForeignKey(x => x.DelegationId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<CorrectionSettingsRecord>(b =>
        {
            b.ToTable("CorrectionSettings", t =>
            {
                t.HasCheckConstraint("CK_CorrectionSettings_Revision", "[Revision] > 0");
                t.HasCheckConstraint("CK_CorrectionSettings_Age", "[MaxRequestAgeDays] BETWEEN 0 AND 3650");
                t.HasCheckConstraint("CK_CorrectionSettings_Limits", "[DailyRequestLimit] BETWEEN 0 AND 1000 AND [MonthlyRequestLimit] BETWEEN 0 AND 1000");
                t.HasCheckConstraint("CK_CorrectionSettings_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                t.HasTrigger("TR_CorrectionSettings_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            b.HasAlternateKey(x => new {x.Id,x.CompanyId});
            b.HasIndex(x => new {x.CompanyId,x.Revision}).IsUnique();
            b.HasOne<CompanyRecord>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DelegationRecord>(b =>
        {
            b.ToTable("Delegations", t =>
            {
                t.HasCheckConstraint("CK_Delegations_Interval", "[ValidFrom] < [ValidUntilExclusive]");
                t.HasCheckConstraint("CK_Delegations_DifferentPeople", "[PrincipalUserId] <> [DelegateUserId] AND [PrincipalEmployeeId] <> [DelegateEmployeeId]");
                t.HasCheckConstraint("CK_Delegations_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                t.HasTrigger("TR_Delegations_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.HasIndex(x => new { x.PrincipalUserId, x.ValidFrom, x.ValidUntilExclusive });
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.PrincipalUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.DelegateUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x => new { x.PrincipalEmployeeId, x.CompanyId }).HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x => new { x.DelegateEmployeeId, x.CompanyId }).HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DelegationRevocationRecord>(b =>
        {
            b.ToTable("DelegationRevocations", t =>
            {
                t.HasCheckConstraint("CK_DelegationRevocations_Utc", "DATEPART(TZOFFSET, [RevokedAtUtc]) = 0");
                t.HasTrigger("TR_DelegationRevocations_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.DelegationId); b.Property(x => x.DelegationId).ValueGeneratedNever();
            b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            b.HasOne<DelegationRecord>().WithMany().HasForeignKey(x => x.DelegationId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<LeaveTypeRecord>(b =>
        {
            b.ToTable("LeaveTypes", t =>
            {
                t.HasCheckConstraint("CK_LeaveTypes_MinutesPerDay", "[MinutesPerDay] BETWEEN 1 AND 1440");
                t.HasCheckConstraint("CK_LeaveTypes_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                t.HasTrigger("TR_LeaveTypes_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Symbol).HasMaxLength(40).IsRequired();
            b.HasAlternateKey(x => new { x.Id, x.CompanyId });
            b.HasIndex(x => new { x.CompanyId, x.Symbol }).IsUnique();
            b.HasOne<CompanyRecord>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<LeaveLedgerEntryRecord>(b =>
        {
            b.ToTable("LeaveLedgerEntries", t =>
            {
                t.HasCheckConstraint("CK_LeaveLedgerEntries_Year", "[PersianYear] BETWEEN 1300 AND 1500");
                t.HasCheckConstraint("CK_LeaveLedgerEntries_Minutes", "[Minutes] BETWEEN -527040 AND 527040 AND (([Kind] = 0 AND [Minutes] >= 0) OR ([Kind] = 1 AND [Minutes] > 0) OR ([Kind] = 2 AND [Minutes] < 0) OR ([Kind] = 3 AND [Minutes] <> 0))");
                t.HasCheckConstraint("CK_LeaveLedgerEntries_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                t.HasTrigger("TR_LeaveLedgerEntries_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.DocumentNumber).HasMaxLength(80).IsRequired(); b.Property(x => x.Description).HasMaxLength(500).IsRequired();
            b.HasIndex(x => new { x.EmployeeId, x.LeaveTypeId, x.PersianYear, x.EffectiveDate });
            b.HasIndex(x => new { x.EmployeeId, x.LeaveTypeId, x.PersianYear }).IsUnique().HasFilter("[Kind] = 0");
            b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x => new { x.EmployeeId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<LeaveTypeRecord>().WithMany().HasForeignKey(x => new { x.LeaveTypeId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<LeaveRequestRecord>(b =>
        {
            b.ToTable("LeaveRequests", t =>
            {
                t.HasCheckConstraint("CK_LeaveRequests_Kind", "[Kind] IN (0,1)");
                t.HasCheckConstraint("CK_LeaveRequests_Category", "[Category] IN (0,1,2)");
                t.HasCheckConstraint("CK_LeaveRequests_OvertimeHourly", "[Category] <> 1 OR [Kind] = 1");
                t.HasCheckConstraint("CK_LeaveRequests_Minutes", "[Minutes] BETWEEN 1 AND 1440");
                t.HasCheckConstraint("CK_LeaveRequests_State", "[State] IN (0,1,2,3)");
                t.HasCheckConstraint("CK_LeaveRequests_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [FromAtUtc]) = 0 AND DATEPART(TZOFFSET, [UntilAtUtcExclusive]) = 0");
                t.HasTrigger("TR_LeaveRequests_ImmutablePayload"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Version).IsRowVersion();
            b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            b.HasIndex(x => x.OperationId).IsUnique();
            b.HasIndex(x => x.RequesterUserId);
            b.HasIndex(x => new { x.EmployeeId, x.CompanyId });
            b.HasIndex(x => new { x.LeaveTypeId, x.CompanyId });
            b.HasIndex(x => new { x.EmployeeId, x.WorkDate, x.State });
            b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x => new { x.EmployeeId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<LeaveTypeRecord>().WithMany().HasForeignKey(x => new { x.LeaveTypeId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.RequesterUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<LeaveDecisionRecord>(b =>
        {
            b.ToTable("LeaveDecisions", t =>
            {
                t.HasCheckConstraint("CK_LeaveDecisions_Decision", "[Decision] IN (1,2,3)");
                t.HasCheckConstraint("CK_LeaveDecisions_Utc", "DATEPART(TZOFFSET, [AtUtc]) = 0");
                t.HasTrigger("TR_LeaveDecisions_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Reason).HasMaxLength(500);
            b.HasIndex(x => x.ActorUserId);
            b.HasIndex(x => x.RequestId).IsUnique();
            b.HasOne<LeaveRequestRecord>().WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
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
        model.Entity<PeriodDiagnosticRecord>(b =>
        {
            b.ToTable("PeriodDiagnostics", t =>
            {
                t.HasCheckConstraint("CK_PeriodDiagnostics_Revision", "[InputRevision] IS NULL OR [InputRevision] >= 0");
                t.HasCheckConstraint("CK_PeriodDiagnostics_Json", "ISJSON([EvidenceJson]) = 1");
                t.HasCheckConstraint("CK_PeriodDiagnostics_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                t.HasTrigger("TR_PeriodDiagnostics_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.EvidenceJson).IsRequired();
            b.HasIndex(x => new { x.DraftId, x.CreatedAtUtc });
            b.HasOne<PeriodDraftRecord>().WithMany().HasForeignKey(x => x.DraftId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
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
            b.HasIndex(x => new { x.EmployeeId, x.Start, x.EndExclusive }).IsUnique();
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
            b.HasOne<CorrectionSettingsRecord>().WithMany().HasForeignKey(x => new { x.CorrectionSettingsId, x.CompanyId })
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
        model.Entity<CalculationInputRevisionRecord>(b =>
        {
            b.ToTable("CalculationInputRevision", t =>
            {
                t.HasCheckConstraint("CK_CalculationInputRevision_Singleton", "[Id] = 1 AND [Revision] >= 0");
                t.HasTrigger("TR_CalculationInputRevision_Monotonic"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
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
                t.HasCheckConstraint("CK_CalculationRuns_Revision", "[InputRevision] IS NULL OR [InputRevision] >= 0");
                t.HasCheckConstraint("CK_CalculationRuns_MonitorState", "[InputMonitorState] IS NULL OR [InputMonitorState] IN (N'present', N'unverified')");
                t.HasTrigger("TR_DailyCalculationRuns_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.InputSnapshotJson).IsRequired(); b.Property(x => x.AnomaliesJson).IsRequired();
            b.Property(x => x.InputMonitorState).HasMaxLength(16);
            b.HasIndex(x => new { x.EmployeeId, x.WorkDate, x.CreatedAtUtc });
            b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x => new { x.EmployeeId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ObservedRuleVersionRecord>().WithMany().HasForeignKey(x => new { x.RuleVersionId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EventSourceRecord>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<WorkScheduleAssignmentRecord>().WithMany().HasForeignKey(x => x.ScheduleAssignmentId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<SourceMappingChangeRecord>(b =>
        {
            b.ToTable("SourceMappingChanges", t =>
            {
                t.HasCheckConstraint("CK_SourceMappingChanges_Utc", "DATEPART(TZOFFSET, [EffectiveAtUtc]) = 0 AND DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                t.HasCheckConstraint("CK_SourceMappingChanges_Distinct", "[PreviousMappingId] <> [NewMappingId]");
                t.HasTrigger("TR_SourceMappingChanges_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            b.HasIndex(x => x.PreviousMappingId).IsUnique(); b.HasIndex(x => x.NewMappingId).IsUnique();
            b.HasOne<SourceEmployeeMappingRecord>().WithMany().HasForeignKey(x => x.PreviousMappingId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<SourceEmployeeMappingRecord>().WithMany().HasForeignKey(x => x.NewMappingId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RawConflictNoteRecord>(b =>
        {
            b.ToTable("RawConflictNotes", t =>
            {
                t.HasCheckConstraint("CK_RawConflictNotes_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                t.HasTrigger("TR_RawConflictNotes_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Note).HasMaxLength(500).IsRequired();
            b.HasIndex(x => new { x.ConflictId, x.CreatedAtUtc });
            b.HasOne<RawEventConflictRecord>().WithMany().HasForeignKey(x => x.ConflictId).OnDelete(DeleteBehavior.Restrict);
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
        // Monitored input tables have SQL revision triggers; EF must not use
        // bare OUTPUT clauses when writing to these tables.
        model.Entity<CompanyRecord>().ToTable("Companies", t => { t.HasTrigger("TR_Revision_Companies"); t.UseSqlOutputClause(false); });
        model.Entity<SiteRecord>().ToTable("Sites", t => { t.HasTrigger("TR_Revision_Sites"); t.UseSqlOutputClause(false); });
        model.Entity<EmployeeRecord>().ToTable("Employees", t => { t.HasTrigger("TR_Revision_Employees"); t.UseSqlOutputClause(false); });
        model.Entity<CalendarRecord>().ToTable("Calendars", t => { t.HasTrigger("TR_Revision_Calendars"); t.UseSqlOutputClause(false); });
        model.Entity<CalendarDayRecord>().ToTable("CalendarDays", t => { t.HasTrigger("TR_Revision_CalendarDays"); t.UseSqlOutputClause(false); });
        model.Entity<ShiftDefinitionRecord>().ToTable("ShiftDefinitions", t => { t.HasTrigger("TR_Revision_ShiftDefinitions"); t.UseSqlOutputClause(false); });
        model.Entity<WorkScheduleAssignmentRecord>().ToTable("WorkScheduleAssignments", t => { t.HasTrigger("TR_Revision_WorkScheduleAssignments"); t.UseSqlOutputClause(false); });
        model.Entity<EventSourceRecord>().ToTable("EventSources", t => { t.HasTrigger("TR_Revision_EventSources"); t.UseSqlOutputClause(false); });
        model.Entity<RawAttendanceEventRecord>().ToTable("RawAttendanceEvents", t => { t.HasTrigger("TR_Revision_RawAttendanceEvents"); t.UseSqlOutputClause(false); });
        model.Entity<RawEventConflictRecord>().ToTable("RawEventConflicts", t => { t.HasTrigger("TR_Revision_RawEventConflicts"); t.UseSqlOutputClause(false); });
        model.Entity<SourceEmployeeMappingRecord>().ToTable("SourceEmployeeMappings", t => { t.HasTrigger("TR_Revision_SourceEmployeeMappings"); t.UseSqlOutputClause(false); });
        model.Entity<ObservedRuleVersionRecord>().ToTable("ObservedRuleVersions", t => { t.HasTrigger("TR_Revision_ObservedRuleVersions"); t.UseSqlOutputClause(false); });
        model.Entity<CorrectionPolicyRecord>().ToTable("CorrectionPolicies", t => { t.HasTrigger("TR_Revision_CorrectionPolicies"); t.UseSqlOutputClause(false); });
        model.Entity<CorrectionRequestRecord>().ToTable("CorrectionRequests", t => { t.HasTrigger("TR_Revision_CorrectionRequests"); t.UseSqlOutputClause(false); });
        model.Entity<CorrectionDecisionRecord>().ToTable("CorrectionDecisions", t => { t.HasTrigger("TR_Revision_CorrectionDecisions"); t.UseSqlOutputClause(false); });
    }
}
