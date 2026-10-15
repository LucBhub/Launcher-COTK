using System.Security.Cryptography;
using System.Text.Json;
using COTK.Next.Core;

internal static class ReleasePublisher
{
    private const string KeyName="COTK.Launcher.Next.ProductionSigning";
    private static int Main(string[] args)
    {
        if(args.Length==0) return 1;
        if(!CngKey.Exists(KeyName))
        {
            if(args[0]!="key") throw new InvalidOperationException("Create publisher key first.");
            using var created=CngKey.Create(CngAlgorithm.ECDsaP256,KeyName,new CngKeyCreationParameters{ExportPolicy=CngExportPolicies.None,KeyUsage=CngKeyUsages.Signing});
        }
        using var key=CngKey.Open(KeyName);using var signer=new ECDsaCng(key);
        if(args[0]=="key") { Console.WriteLine(Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo())); return 0; }
        if(args[0]=="sign" && args.Length==3)
        {
            var payload=JsonSerializer.Deserialize<SignedPayload>(File.ReadAllText(args[1]),ApiClient.Json)!;
            File.WriteAllText(args[2],JsonSerializer.Serialize(SignedRelease.Sign(payload,signer),ApiClient.Json));
            Console.WriteLine("Signed with protected, non-exportable Windows publisher key.");return 0;
        }
        return 1;
    }
}
