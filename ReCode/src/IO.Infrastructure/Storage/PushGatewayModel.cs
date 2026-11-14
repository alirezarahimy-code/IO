using Microsoft.EntityFrameworkCore;
namespace IO.Infrastructure.Storage;
internal static class PushGatewayModel
{
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<PushGatewayBindingRecord>(b=>
        {
            b.Property(x=>x.Id).HasColumnType("uniqueidentifier");
            b.Property(x=>x.GatewayId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.DeviceId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.SourceId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.ServiceUserId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.CreatedByUserId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.CreatedAtUtc).HasColumnType("datetimeoffset");
            b.Property(x=>x.ExpectedDeviceVersion).HasColumnType("varbinary(8)");
            b.Property(x=>x.Serial).HasColumnType("nvarchar(100)");
            b.Property(x=>x.TimeZoneId).HasColumnType("nvarchar(100)");
            b.Property(x=>x.KeyVersion).HasColumnType("nvarchar(64)");
            b.Property(x=>x.ProtectedKey).HasColumnType("nvarchar(4000)");
            b.Property(x=>x.RevokedByUserId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.RevokedAtUtc).HasColumnType("datetimeoffset");
            b.Property(x=>x.Version).HasColumnType("rowversion");
            b.ToTable("PushGatewayBindings",t=>{t.HasTrigger("TR_PushGatewayBindings_History");t.UseSqlOutputClause(false);});
            b.HasKey(x=>x.Id);b.Property(x=>x.Id).ValueGeneratedNever();
            b.Property(x=>x.ExpectedDeviceVersion).HasMaxLength(8).IsRequired();
            b.Property(x=>x.Serial).HasMaxLength(100).IsRequired();b.Property(x=>x.TimeZoneId).HasMaxLength(100).IsRequired();
            b.Property(x=>x.KeyVersion).HasMaxLength(64).IsRequired();b.Property(x=>x.ProtectedKey).HasMaxLength(4000).IsRequired();
            b.Property(x=>x.Version).IsRowVersion();
            b.HasIndex(x=>new{x.GatewayId,x.DeviceId}).IsUnique().HasFilter("[RevokedAtUtc] IS NULL");
            b.HasOne<DeviceRecord>().WithMany().HasForeignKey(x=>x.DeviceId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EventSourceRecord>().WithMany().HasForeignKey(x=>x.SourceId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x=>x.ServiceUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x=>x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x=>x.RevokedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PushGatewayReceiptRecord>(b=>
        {
            b.Property(x=>x.Id).HasColumnType("uniqueidentifier");
            b.Property(x=>x.BindingId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.GatewayId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.DeviceId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.TransferId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.RequestFingerprint).HasColumnType("nvarchar(64)");
            b.ToTable("PushGatewayReceipts",t=>{t.HasTrigger("TR_PushGatewayReceipts_Immutable");t.UseSqlOutputClause(false);});
            b.HasKey(x=>x.Id);b.Property(x=>x.Id).ValueGeneratedNever();
            b.Property(x=>x.RequestFingerprint).HasMaxLength(64).IsRequired();
            b.HasIndex(x=>new{x.GatewayId,x.DeviceId,x.TransferId}).IsUnique();
            b.HasOne<PushGatewayBindingRecord>().WithMany().HasForeignKey(x=>x.BindingId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<DevicePushJournalRecord>().WithMany().HasForeignKey(x=>x.Id).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
