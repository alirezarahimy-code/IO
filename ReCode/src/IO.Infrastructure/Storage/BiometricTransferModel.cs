using Microsoft.EntityFrameworkCore;
namespace IO.Infrastructure.Storage;
internal static class BiometricTransferModel
{
 internal static void Configure(ModelBuilder model)=>model.Entity<DeviceBiometricTransferRecord>(b=>
 {
  b.ToTable("DeviceBiometricTransfers",t=>{t.HasTrigger("TR_DeviceBiometricTransfers_History");t.HasTrigger("TR_DeviceBiometricTransfers_Inspection");t.UseSqlOutputClause(false);});
  b.HasKey(x=>x.Id);b.Property(x=>x.Id).ValueGeneratedNever();
  b.Property(x=>x.InspectionOfId).HasColumnType("uniqueidentifier");b.HasIndex(x=>x.InspectionOfId);b.HasOne<DeviceBiometricTransferRecord>().WithMany().HasForeignKey(x=>x.InspectionOfId).OnDelete(DeleteBehavior.Restrict);
  b.Property(x=>x.Id).HasColumnType("uniqueidentifier");b.Property(x=>x.SourceDeviceId).HasColumnType("uniqueidentifier");b.Property(x=>x.TargetDeviceId).HasColumnType("uniqueidentifier");b.Property(x=>x.EmployeeId).HasColumnType("uniqueidentifier");b.Property(x=>x.RequestedByUserId).HasColumnType("uniqueidentifier");
  b.Property(x=>x.RequestedAtUtc).HasColumnType("datetimeoffset");
  b.Property(x=>x.SourceDeviceVersion).HasColumnType("varbinary(8)").HasMaxLength(8).IsRequired();b.Property(x=>x.TargetDeviceVersion).HasColumnType("varbinary(8)").HasMaxLength(8).IsRequired();
  b.Property(x=>x.SourcePerson).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();b.Property(x=>x.TargetPerson).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
  b.Property(x=>x.ApprovalJson).HasColumnType("nvarchar(max)").IsRequired();b.Property(x=>x.RequestHash).HasColumnType("nvarchar(64)").HasMaxLength(64).IsRequired();b.Property(x=>x.State).HasColumnType("nvarchar(40)").HasMaxLength(40).IsRequired();b.Property(x=>x.TimeoutSeconds).HasColumnType("int");
  b.Property(x=>x.ServiceUserId).HasColumnType("uniqueidentifier");b.Property(x=>x.ClaimId).HasColumnType("uniqueidentifier");
  b.Property(x=>x.ClaimedAtUtc).HasColumnType("datetimeoffset");b.Property(x=>x.CancelRequestedAtUtc).HasColumnType("datetimeoffset");b.Property(x=>x.FinishedAtUtc).HasColumnType("datetimeoffset");b.Property(x=>x.Version).HasColumnType("rowversion").IsRowVersion().IsRequired();
  b.HasIndex(x=>new{x.SourceDeviceId,x.State});b.HasIndex(x=>new{x.TargetDeviceId,x.State});b.HasIndex(x=>x.EmployeeId);b.HasIndex(x=>x.RequestedByUserId);b.HasIndex(x=>x.ServiceUserId);
  b.HasOne<DeviceRecord>().WithMany().HasForeignKey(x=>x.SourceDeviceId).OnDelete(DeleteBehavior.Restrict);b.HasOne<DeviceRecord>().WithMany().HasForeignKey(x=>x.TargetDeviceId).OnDelete(DeleteBehavior.Restrict);
  b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x=>x.EmployeeId).OnDelete(DeleteBehavior.Restrict);b.HasOne<UserRecord>().WithMany().HasForeignKey(x=>x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);b.HasOne<UserRecord>().WithMany().HasForeignKey(x=>x.ServiceUserId).OnDelete(DeleteBehavior.Restrict);
 });
}
