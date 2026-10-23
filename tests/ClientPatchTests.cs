using COTK.Next.Core;

namespace COTK.Next.Tests;

/// <summary>Correctif client « F pendant un rechargement » : code généré identique à l'outil de banc déjà validé sur le vrai
/// client, et logique d'application (garde, annulation, idempotence) testée sur une mémoire factice : aucun vrai jeu.</summary>
internal static class ClientPatchTests
{
    // Générés par tools/qa/qa_parallel_loot_client.py::build_stub (le code réellement installé le 2026-10-22).
    private const string StubNear = "4881ecc0000000488944242048894c242848895424304c894424384c894c24404c895424484c895c2450f30f7f842460000000f30f7f8c2470000000f30f7f942480000000f30f7f9c2490000000f30f7fa424a0000000f30f7fac24b00000004c89f148b870574e4001000000ffd088442458f30f6f842460000000f30f6f8c2470000000f30f6f942480000000f30f6f9c2490000000f30f6fa424a0000000f30f6fac24b0000000488b442420488b4c2428488b5424304c8b4424384c8b4c24404c8b5424484c8b5c2450807c245800488da424c00000000f85683a93f083bd50460000000f8f5b3a93f0e9363a93f0";
    private const string StubHigh = "4881ecc0000000488944242048894c242848895424304c894424384c894c24404c895424484c895c2450f30f7f842460000000f30f7f8c2470000000f30f7f942480000000f30f7f9c2490000000f30f7fa424a0000000f30f7fac24b00000004c89f148b870574ea0f67f0000ffd088442458f30f6f842460000000f30f6f8c2470000000f30f6f942480000000f30f6f9c2490000000f30f6fa424a0000000f30f6fac24b0000000488b442420488b4c2428488b5424304c8b4424384c8b4c24404c8b5424484c8b5c2450807c245800488da424c00000000f85683a93f083bd50460000000f8f5b3a93f0e9363a93f0";

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    private static byte[] Hex(string hex) => Convert.FromHexString(hex);

    private sealed class FakeMemory : ClientReloadLootPatch.IPatchMemory
    {
        public long ImageBase { get; init; } = 0x140000000;
        public readonly Dictionary<long, byte> Bytes = new();
        public readonly HashSet<long> Occupied = new();
        public readonly List<long> Allocated = new();
        public readonly List<long> Freed = new();
        public readonly List<long> Executable = new();
        public readonly List<(long Address, int Length)> Writes = new();
        public int Suspends, Resumes;
        public long FailWriteAt = -1;

        public FakeMemory(bool withOriginal = true)
        {
            if (withOriginal) Put(ImageBase + ClientReloadLootPatch.SiteRva, ClientReloadLootPatch.Original);
        }

        public void Put(long address, byte[] data) { for (var i = 0; i < data.Length; i++) Bytes[address + i] = data[i]; }

        public byte[] Read(long address, int length)
        {
            var result = new byte[length];
            for (var i = 0; i < length; i++) result[i] = Bytes.TryGetValue(address + i, out var b) ? b : (byte)0;
            return result;
        }

        public bool TryAllocate(long preferred, out long address)
        {
            address = 0;
            if (Occupied.Contains(preferred)) return false;
            address = preferred;
            Allocated.Add(preferred);
            return true;
        }

        public void Free(long address) => Freed.Add(address);

        public void WriteCode(long address, byte[] data)
        {
            if (address == FailWriteAt) throw new InvalidOperationException("écriture refusée (test)");
            Writes.Add((address, data.Length));
            Put(address, data);
        }

        public void MakeExecutable(long address) => Executable.Add(address);
        public void Suspend() => Suspends++;
        public void Resume() => Resumes++;
    }

    public static void StubMatchesValidatedTool()
    {
        Check(Convert.ToHexString(ClientReloadLootPatch.BuildStub(0x150000000, 0x140000000)).ToLowerInvariant() == StubNear,
            "le code généré diffère de celui validé sur le vrai client (adresses proches)");
        Check(Convert.ToHexString(ClientReloadLootPatch.BuildStub(0x7FF6B0000000, 0x7FF6A0000000)).ToLowerInvariant() == StubHigh,
            "le code généré diffère de celui validé sur le vrai client (adresses hautes)");
        var jump = ClientReloadLootPatch.BuildReplacement(0x140000000 + ClientReloadLootPatch.SiteRva, 0x150000000);
        Check(Convert.ToHexString(jump).ToLowerInvariant() == "e9ddc46c0f90909090",
            "le saut de remplacement diffère du reçu du test réel : " + Convert.ToHexString(jump));
        Check(jump.Length == ClientReloadLootPatch.Original.Length, "le remplacement doit couvrir exactement les 9 octets déplacés");
    }

    public static void AppliesOnce()
    {
        var memory = new FakeMemory();
        var site = memory.ImageBase + ClientReloadLootPatch.SiteRva;
        var result = ClientReloadLootPatch.Apply(memory);
        Check(result.Outcome == ClientReloadLootPatch.Outcome.Applied, "application attendue : " + result.Outcome + " " + result.Detail);
        Check(memory.Allocated.Count == 1, "une seule page doit être réservée");
        var trampoline = memory.Allocated[0];
        Check(Math.Abs(trampoline - site) < int.MaxValue, "le trampoline doit être joignable par un saut rel32");
        Check(memory.Executable.Contains(trampoline), "la page du trampoline doit devenir exécutable");
        Check(memory.Read(site, 9).AsSpan().SequenceEqual(ClientReloadLootPatch.BuildReplacement(site, trampoline)),
            "le site doit contenir le saut vers le trampoline");
        Check(memory.Read(trampoline, 241).AsSpan().SequenceEqual(ClientReloadLootPatch.BuildStub(trampoline, memory.ImageBase)),
            "le trampoline doit contenir le code généré");
        Check(memory.Suspends == 1 && memory.Resumes == 1, "le jeu doit être suspendu puis repris exactement une fois");

        var again = ClientReloadLootPatch.Apply(memory);
        Check(again.Outcome == ClientReloadLootPatch.Outcome.AlreadyApplied && memory.Allocated.Count == 1 && memory.Suspends == 1,
            "une deuxième application ne doit rien réserver ni suspendre");
    }

    public static void RefusesUnknownBytes()
    {
        var memory = new FakeMemory(withOriginal: false);
        memory.Put(memory.ImageBase + ClientReloadLootPatch.SiteRva, Hex("83bd50460000007f21"));    // un octet de différence
        var result = ClientReloadLootPatch.Apply(memory);
        Check(result.Outcome == ClientReloadLootPatch.Outcome.NotReady, "octets inattendus : refus attendu, obtenu " + result.Outcome);
        Check(memory.Writes.Count == 0 && memory.Allocated.Count == 0 && memory.Suspends == 0,
            "aucune écriture, réservation ou suspension quand les octets ne sont pas ceux analysés");
    }

    public static void SkipsOccupiedPages()
    {
        var memory = new FakeMemory();
        for (var i = 0; i < 5; i++) memory.Occupied.Add(memory.ImageBase + 0x10000000 + i * 0x10000L);
        var result = ClientReloadLootPatch.Apply(memory);
        Check(result.Outcome == ClientReloadLootPatch.Outcome.Applied, "pages occupées : le suivant doit être essayé");
        Check(memory.Allocated[0] == memory.ImageBase + 0x10000000 + 5 * 0x10000L, "la première page libre doit être utilisée");
    }

    public static void RollsBackOnFailure()
    {
        var memory = new FakeMemory();
        var site = memory.ImageBase + ClientReloadLootPatch.SiteRva;
        memory.FailWriteAt = site;                                    // le stub s'écrit, pas le saut
        var result = ClientReloadLootPatch.Apply(memory);
        Check(result.Outcome == ClientReloadLootPatch.Outcome.Failed, "échec attendu");
        Check(memory.Read(site, 9).AsSpan().SequenceEqual(ClientReloadLootPatch.Original), "le site doit rester intact");
        Check(memory.Freed.Count == 1, "la page réservée doit être libérée");
        Check(memory.Suspends == 1 && memory.Resumes == 1, "le jeu ne doit jamais rester suspendu");
    }

    public static void OnlyThePinnedClient(string temp)
    {
        var other = Path.Combine(temp, "H1Z1-other.exe");
        File.WriteAllBytes(other, new byte[] { 1, 2, 3, 4 });
        Check(!ClientReloadLootPatch.IsPinnedClient(other), "un autre exécutable ne doit jamais être modifié");
        Environment.SetEnvironmentVariable("COTK_DISABLE_CLIENT_PATCH", "1");
        try
        {
            Check(ClientReloadLootPatch.Disabled, "COTK_DISABLE_CLIENT_PATCH=1 doit désactiver le correctif");
            var result = ClientReloadLootPatch.TryApply(System.Diagnostics.Process.GetCurrentProcess(), other);
            Check(result.Outcome == ClientReloadLootPatch.Outcome.Disabled, "désactivé : aucune action, obtenu " + result.Outcome);
        }
        finally
        {
            Environment.SetEnvironmentVariable("COTK_DISABLE_CLIENT_PATCH", null);
        }
        var refused = ClientReloadLootPatch.TryApply(System.Diagnostics.Process.GetCurrentProcess(), other);
        Check(refused.Outcome == ClientReloadLootPatch.Outcome.NotPinnedClient, "exécutable inconnu : refus attendu, obtenu " + refused.Outcome);
    }
}
