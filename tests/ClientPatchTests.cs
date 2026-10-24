using COTK.Next.Core;

namespace COTK.Next.Tests;

/// <summary>Correctif client « F pendant un rechargement » : code généré identique à l'outil de banc déjà validé sur le vrai
/// client, et logique d'application (garde, annulation, idempotence) testée sur une mémoire factice : aucun vrai jeu.</summary>
internal static class ClientPatchTests
{
    // SHA du code v2 généré par l'outil Python dont les 300 branches/états ont été émulés sous Unicorn.
    private const string StubNearSha = "14c1b678a7e32c3d0d8a538510f97de6b90d2a77b684a5aceef4d741ef9970dc";
    private const string StubHighSha = "dcd5abda94a41d408b5457d44ae451003e049220401dc9fa7d78e367143bcdf1";

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
        public bool FailAfterPartialSiteWrite;

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
            if (address == FailWriteAt)
            {
                FailWriteAt = -1;
                throw new InvalidOperationException("écriture refusée (test)");
            }
            if (FailAfterPartialSiteWrite && address == ImageBase + ClientReloadLootPatch.SiteRva)
            {
                FailAfterPartialSiteWrite = false;
                Put(address, data.Take(3).ToArray());
                throw new InvalidOperationException("écriture partielle du saut (test)");
            }
            Writes.Add((address, data.Length));
            Put(address, data);
        }

        public void MakeExecutable(long address) => Executable.Add(address);
        public void Suspend() => Suspends++;
        public void Resume() => Resumes++;
    }

    public static void StubMatchesValidatedTool()
    {
        static string Sha(byte[] code) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(code)).ToLowerInvariant();
        Check(Sha(ClientReloadLootPatch.BuildStub(0x150000000, 0x140000000)) == StubNearSha,
            "le code généré diffère de la v2 émulée (adresses proches)");
        Check(Sha(ClientReloadLootPatch.BuildStub(0x7FF6B0000000, 0x7FF6A0000000)) == StubHighSha,
            "le code généré diffère de la v2 émulée (adresses hautes)");
        var jump = ClientReloadLootPatch.BuildReplacement(0x140000000 + ClientReloadLootPatch.SiteRva, 0x150000000);
        Check(Convert.ToHexString(jump).ToLowerInvariant() == "e946c56c0f90909090",
            "le saut de remplacement ne vise pas le nouveau site avant la garde pending : " + Convert.ToHexString(jump));
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
        Check(memory.Read(trampoline, 259).AsSpan().SequenceEqual(ClientReloadLootPatch.BuildStub(trampoline, memory.ImageBase)),
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

        var foreign = new FakeMemory();
        var site = foreign.ImageBase + ClientReloadLootPatch.SiteRva;
        foreign.Put(site, ClientReloadLootPatch.BuildReplacement(site, 0x150000000));
        Check(ClientReloadLootPatch.Apply(foreign).Outcome == ClientReloadLootPatch.Outcome.NotReady,
            "un saut étranger avec les mêmes NOP ne prouve pas que notre correctif est installé");
        Check(foreign.Writes.Count == 0, "ne pas écraser un autre correctif");
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

        var partial = new FakeMemory { FailAfterPartialSiteWrite = true };
        Check(ClientReloadLootPatch.Apply(partial).Outcome == ClientReloadLootPatch.Outcome.Failed,
            "l'écriture partielle doit être signalée comme un échec");
        Check(partial.Read(site, 9).AsSpan().SequenceEqual(ClientReloadLootPatch.Original),
            "restaurer les instructions même si l'échec survient après seulement trois octets écrits");
        Check(partial.Freed.Count == 1 && partial.Resumes == 1,
            "ne libérer la page qu'après restauration et reprendre le client");
    }

    public static void NeverBeforeTheWorldIsLoaded()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var stage in new[] { ClientStage.Starting, ClientStage.TitleScreen, ClientStage.LoadingWorld })
        {
            Check(!ClientReloadLootPatch.IsSafeMoment(stage, null, now), $"jamais pendant {stage}");
            Check(!ClientReloadLootPatch.IsSafeMoment(stage, now.AddMinutes(-1), now),
                $"jamais pendant {stage}, même si un ancien épisode était stable");
        }
        Check(!ClientReloadLootPatch.IsSafeMoment(ClientStage.InGame, null, now), "sans preuve du monde chargé : on attend");
        Check(!ClientReloadLootPatch.IsSafeMoment(ClientStage.InGame, now.AddSeconds(-19), now), "attendre les vingt secondes réelles");
        Check(ClientReloadLootPatch.IsSafeMoment(ClientStage.InGame, now.AddSeconds(-20), now), "monde chargé depuis vingt secondes : autorisé");

        const string menu = "newState=GAMESTATE_TITLESCREEN\nnewState=cClientRunStateRunning\n";
        Check(GameFiles.DetectClientStage(menu) == ClientStage.InGame, "reproduire l'ambiguïté du détecteur général");
        Check(ClientReloadLootPatch.WorldReadyMarker(menu) == -1, "Running seul ne suffit jamais, même au menu depuis longtemps");
        var loaded = menu + "newState=GAMESTATE_LOADINGSCREEN\nnewState=GAMESTATE_INGAME\n";
        Check(ClientReloadLootPatch.WorldReadyMarker(loaded) >= 0, "preuve explicite du chargement du monde");
        foreach (var barrier in new[] { "GAMESTATE_LOADINGSCREEN", "GAMESTATE_TITLESCREEN", "cClientRunStateWaitForZoneLoad", "cClientRunStateShuttingDown" })
            Check(ClientReloadLootPatch.WorldReadyMarker(loaded + "newState=" + barrier) == -1,
                "une nouvelle transition invalide le précédent monde chargé : " + barrier);
        Check(ClientReloadLootPatch.WorldReadyMarker("") == -1, "pas de journal = pas de preuve");
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
