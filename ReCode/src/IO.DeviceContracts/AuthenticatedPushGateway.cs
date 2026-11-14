using System.Security.Cryptography;
using System.Text;

namespace IO.DeviceContracts;

/// <summary>NEW gateway-to-server protocol, NOT ZKTeco native authentication. Deployment supplies the binding/key.
/// No public HTTP endpoint or SQL implementation is provided by this portable boundary.</summary>
public sealed class PushGatewayBinding
{
    public Guid GatewayId {get;}
    public Guid DeviceId {get;}
    public Guid SourceId {get;}
    public string Serial {get;}
    public string KeyVersion {get;}
    public TimeZoneInfo Zone {get;}
    private readonly byte[] key;
    public PushGatewayBinding(Guid gatewayId,Guid deviceId,Guid sourceId,string serial,string keyVersion,TimeZoneInfo zone,ReadOnlySpan<byte> key)
    {
        if(gatewayId==Guid.Empty||deviceId==Guid.Empty||sourceId==Guid.Empty||!Text(serial,100)||!Text(keyVersion,64)||zone is null||!Text(zone.Id,100)||key.Length!=32)
            throw new ArgumentException("An explicit gateway/device/source/zone and a32-byte independently provisioned gateway key are required.");
        GatewayId=gatewayId;DeviceId=deviceId;SourceId=sourceId;Serial=serial;KeyVersion=keyVersion;Zone=zone;this.key=key.ToArray();
    }
    internal byte[] Mac(byte[] bytes)=>HMACSHA256.HashData(key,bytes);
    internal static bool Text(string? text,int max)=>!string.IsNullOrWhiteSpace(text)&&text.Length<=max&&!text.Any(c=>char.IsControl(c)||c=='\uFEFF');
    public override string ToString()=>"PUSH gateway binding [key and identity redacted]";
}

public sealed record PushGatewayEnvelope(Guid TransferId,DateTimeOffset SentAtUtc,string? Stamp,byte[] Body,string Signature)
{
    public override string ToString()=>"PUSH gateway envelope [signature, cursor and personal data redacted]";
}

/// <summary>The implementing trusted SQL transaction MUST recheck gateway grants/binding/key revocation,
/// pin the interpretation, enforce unique(gateway,device,transfer), compare exact stable request on retry,
/// and commit immutable original bytes, parsed snapshot and receipt atomically BEFORE returning.
/// This is a mandatory storage boundary, not an in-memory replay cache or a native vendor processed receipt.</summary>
public interface IPushGatewayCommitStore
{
    Task<PushGatewayCommit> CommitAsync(PushGatewayBinding binding,Guid transferId,ZkPushAttendanceBatch batch,CancellationToken ct);
}
public sealed record PushGatewayCommit(Guid ReceiptId,Guid GatewayId,Guid DeviceId,Guid SourceId,Guid TransferId,string PayloadHash,int RetainedRows,string RequestFingerprint);
public sealed class PushGatewayRetention
{
    public Guid ReceiptId {get;}
    public Guid TransferId {get;}
    public string PayloadHash {get;}
    public int RetainedRows {get;}
    public bool GatewayAuthenticated=>true;
    public bool DeviceIdentityVerified=>false;
    public string State=>"gateway-retained-device-unverified";
    internal PushGatewayRetention(Guid receiptId,Guid transferId,string payloadHash,int retainedRows)
    {ReceiptId=receiptId;TransferId=transferId;PayloadHash=payloadHash;RetainedRows=retainedRows;}
    public override string ToString()=>"Gateway retention [not a native vendor ACK]";
}

public static class AuthenticatedPushGateway
{
    public const string Contract="io-push-gateway-hmac-sha256-v1";
    public const string Method="POST",Path="/gateway/v1/attendance";
    // New gateway protocol budgets, never attributed to native vendor firmware.
    public const int MaximumAgeSeconds=120,MaximumFutureSeconds=30;
    public static string Sign(PushGatewayBinding binding,Guid transferId,DateTimeOffset sentAt,string? stamp,ReadOnlySpan<byte> body)
    {
        Validate(binding,transferId,sentAt,stamp,body.Length);
        return Convert.ToHexString(binding.Mac(Canonical(binding,transferId,sentAt,stamp,body)));
    }
    public static async Task<PushGatewayRetention> ReceiveAsync(PushGatewayBinding binding,PushGatewayEnvelope envelope,
        IPushGatewayCommitStore store,TimeProvider clock,CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(envelope);ArgumentNullException.ThrowIfNull(store);ArgumentNullException.ThrowIfNull(clock);
        if(envelope.Body is null)throw new ArgumentException("Body required.");
        Validate(binding,envelope.TransferId,envelope.SentAtUtc,envelope.Stamp,envelope.Body.Length);
        // Freeze request buffers before any await; auth and retained bytes must be identical.
        var bytes=(byte[])envelope.Body.Clone();
        if(envelope.Signature is not{Length:64}||!envelope.Signature.All(c=>c is >= '0' and <= '9' or >= 'A' and <= 'F'))
            throw new UnauthorizedAccessException("Gateway authentication rejected.");
        var age=(clock.GetUtcNow()-envelope.SentAtUtc).TotalSeconds;
        if(age>MaximumAgeSeconds||age< -MaximumFutureSeconds||!CryptographicOperations.FixedTimeEquals(
            binding.Mac(Canonical(binding,envelope.TransferId,envelope.SentAtUtc,envelope.Stamp,bytes)),Convert.FromHexString(envelope.Signature)))
            throw new UnauthorizedAccessException("Gateway authentication rejected.");
        ct.ThrowIfCancellationRequested();
        var batch=ZkPushAttendanceCodec.Decode(binding.Serial,envelope.Stamp,"ATTLOG",bytes,binding.Zone);
        var receipt=await store.CommitAsync(binding,envelope.TransferId,batch,ct);
        if(receipt is null||receipt.ReceiptId==Guid.Empty||receipt.GatewayId!=binding.GatewayId||receipt.DeviceId!=binding.DeviceId||
            receipt.SourceId!=binding.SourceId||receipt.TransferId!=envelope.TransferId||receipt.PayloadHash!=batch.PayloadHash||receipt.RetainedRows!=batch.Rows.Count||
            receipt.RequestFingerprint!=TransferFingerprint(binding,envelope.TransferId,batch))
            throw new InvalidOperationException("Committed gateway receipt did not match the retained transfer.");
        // Deliberately not OK:n. This authenticates a gateway and retains quarantine, not a native device event.
        return new(receipt.ReceiptId,receipt.TransferId,receipt.PayloadHash,receipt.RetainedRows);
    }
    public static string TransferFingerprint(PushGatewayBinding binding,Guid transfer,ZkPushAttendanceBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        Validate(binding,transfer,DateTimeOffset.UnixEpoch,batch.Stamp,batch.Original.Length);
        if(batch.Serial!=binding.Serial||batch.TimeZoneId!=binding.Zone.Id)throw new ArgumentException("Interpretation binding mismatch.");
        // A renewed signing timestamp may retry the same transfer; every stable binding/interpretation field remains pinned.
        return Convert.ToHexString(SHA256.HashData(Canonical(binding,transfer,DateTimeOffset.UnixEpoch,batch.Stamp,batch.Original.Span)));
    }
    private static void Validate(PushGatewayBinding binding,Guid transfer,DateTimeOffset sent,string? stamp,int length)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if(transfer==Guid.Empty||sent.Offset!=TimeSpan.Zero||sent.Ticks%TimeSpan.TicksPerSecond!=0||length is 0 or >ZkPushAttendanceCodec.MaximumBytes||
            stamp is not null&&(stamp.Length>200||stamp.Any(c=>char.IsControl(c)||c=='\uFEFF')))
            throw new ArgumentException("Explicit transfer, whole UTC seconds and bounded wire evidence required.");
    }
    private static byte[] Canonical(PushGatewayBinding b,Guid transfer,DateTimeOffset sent,string? stamp,ReadOnlySpan<byte> body)
    {
        // BinaryWriter's length-prefixed UTF8, fixed field order, little-endian Int64; null stamp != empty stamp.
        // Domain separation binds method/path, gateway/device/source/serial/zone/key version and exact body digest.
        using var output=new MemoryStream();using(var writer=new BinaryWriter(output,new UTF8Encoding(false,true),true))
        {
            foreach(var text in new[]{Contract,Method,Path,b.GatewayId.ToString("D"),b.DeviceId.ToString("D"),b.SourceId.ToString("D"),
                b.Serial,b.Zone.Id,b.KeyVersion,transfer.ToString("D")})writer.Write(text);
            writer.Write(sent.ToUnixTimeSeconds());writer.Write(stamp is not null);if(stamp is not null)writer.Write(stamp);
            writer.Write(body.Length);writer.Write(SHA256.HashData(body));
        }
        return output.ToArray();
    }
}
