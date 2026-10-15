using System.Security.Cryptography;
using System.Text.Json;

namespace COTK.Next.Core;

internal sealed record SignedEnvelope(string Payload,string Signature);
internal sealed record SignedPayload(int SchemaVersion,string Product,string Channel,DateTimeOffset PublishedAt,DateTimeOffset ExpiresAt,UpdateManifest Release);

internal static class SignedRelease
{
    // Root is supplied by the publisher tooling. No private key is present in the repository or API container.
    internal const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEWykhH/7LXl5pgwpRmv81Jg1tBAlcLcfzCcp3oVznkJ4yfXaTVl0lBVXJuzsdkXz+ydgMD1mhbC02CSG0q3HTIw==";
    public const string ManifestUrl="https://dl.cotk.fr/launcher-next/manifest.signed.json";

    public static SignedPayload Verify(SignedEnvelope envelope,string? key=null,DateTimeOffset? now=null)
    {
        try
        {
            if(envelope.Payload.Length>128*1024 || envelope.Signature.Length>256) throw new CryptographicException("Envelope too large.");
            var payload=Convert.FromBase64String(envelope.Payload);var signature=Convert.FromBase64String(envelope.Signature);
            using var verifier=ECDsa.Create();verifier.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key??PublicKey),out _);
            if(!verifier.VerifyData(payload,signature,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) throw new CryptographicException("Signature mismatch.");
            var signed=JsonSerializer.Deserialize<SignedPayload>(payload,ApiClient.Json) ?? throw new JsonException();
            var clock=now??DateTimeOffset.UtcNow;
            if(signed.SchemaVersion!=1 || signed.Product!="COTK.Launcher.Next" || signed.Channel!="stable" || signed.PublishedAt>clock.AddMinutes(10) || signed.ExpiresAt<=clock || signed.ExpiresAt<=signed.PublishedAt)
                throw new CryptographicException("Product/channel/date mismatch.");
            ManifestRules.Validate(signed.Release);
            if(signed.Release.Client.FilesUrl is not null && signed.Release.Client.FilesSha256 is not {Length:64}) throw new CryptographicException("Catalogue is not pinned.");
            return signed;
        }
        catch(Exception ex) when(ex is CryptographicException or FormatException or JsonException or ArgumentException)
        { throw new LauncherException(Errors.UnsafeUpdate("La signature ou la validité de la publication est incorrecte. La mise à jour est refusée."),ex); }
    }

    public static SignedEnvelope Sign(SignedPayload payload,ECDsa publisher)
    {
        var bytes=JsonSerializer.SerializeToUtf8Bytes(payload,ApiClient.Json);
        return new SignedEnvelope(Convert.ToBase64String(bytes),Convert.ToBase64String(publisher.SignData(bytes,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
    }
}
