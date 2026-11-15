using Microsoft.EntityFrameworkCore;
namespace IO.Infrastructure.Storage;
internal static class BiometricRecoveryModel
{
 internal static void Configure(ModelBuilder model)=>model.Entity<DeviceBiometricRecoveryRecord>(b=>
 {
  b.ToTable("DeviceBiometricRecoveries",t=>{t.HasTrigger("TR_DeviceBiometricRecoveries_History");t.UseSqlOutputClause(false);});
  b.HasKey(x=>x.Id);b.Property(x=>x.Id).ValueGeneratedNever();
  b.Property(x=>x.Id).HasColumnType("uniqueidentifier");b.Property(x=>x.TransferId).HasColumnType("uniqueidentifier");b.Property(x=>x.InspectionId).HasColumnType("uniqueidentifier");b.Property(x=>x.SourceDeviceId).HasColumnType("uniqueidentifier");b.Property(x=>x.TargetDeviceId).HasColumnType("uniqueidentifier");b.Property(x=>x.ProposedByUserId).HasColumnType("uniqueidentifier");b.Property(x=>x.ApprovedByUserId).HasColumnType("uniqueidentifier");
  b.Property(x=>x.ProposedAtUtc).HasColumnType("datetimeoffset");b.Property(x=>x.ResetAtUtc).HasColumnType("datetimeoffset");b.Property(x=>x.ApprovedAtUtc).HasColumnType("datetimeoffset");
  b.Property(x=>x.ApprovalJson).HasColumnType("nvarchar(max)").IsRequired();b.Property(x=>x.ProcessStoppedEvidence).HasColumnType("nvarchar(200)").HasMaxLength(200).IsRequired();b.Property(x=>x.ResetEvidence).HasColumnType("nvarchar(200)").HasMaxLength(200).IsRequired();b.Property(x=>x.ReviewEvidence).HasColumnType("nvarchar(200)").HasMaxLength(200);b.Property(x=>x.RequestHash).HasColumnType("nvarchar(64)").HasMaxLength(64).IsRequired();b.Property(x=>x.Version).HasColumnType("rowversion").IsRowVersion().IsRequired();
  b.HasIndex(x=>x.TransferId).IsUnique().HasFilter("[ApprovedAtUtc] IS NOT NULL");b.HasIndex(x=>x.InspectionId).IsUnique();b.HasIndex(x=>x.SourceDeviceId);b.HasIndex(x=>x.TargetDeviceId);b.HasIndex(x=>x.ProposedByUserId);b.HasIndex(x=>x.ApprovedByUserId);
  b.HasOne<DeviceBiometricTransferRecord>().WithMany().HasForeignKey(x=>x.TransferId).OnDelete(DeleteBehavior.Restrict);b.HasOne<DeviceBiometricTransferRecord>().WithMany().HasForeignKey(x=>x.InspectionId).OnDelete(DeleteBehavior.Restrict);
  b.HasOne<DeviceRecord>().WithMany().HasForeignKey(x=>x.SourceDeviceId).OnDelete(DeleteBehavior.Restrict);b.HasOne<DeviceRecord>().WithMany().HasForeignKey(x=>x.TargetDeviceId).OnDelete(DeleteBehavior.Restrict);b.HasOne<UserRecord>().WithMany().HasForeignKey(x=>x.ProposedByUserId).OnDelete(DeleteBehavior.Restrict);b.HasOne<UserRecord>().WithMany().HasForeignKey(x=>x.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);
 });
}
