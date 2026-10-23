using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace COTK.Next.Core;

/// <summary>
/// Correctif EN MÉMOIRE du comportement natif « F pendant un rechargement ».
///
/// Constat (client H1Z1.exe épinglé, analyse REA/Ghidra + mesures sur le vrai client) : quand l'arme est en état
/// de rechargement et que le joueur appuie sur F sur un objet au sol, <c>FUN_1409339c0</c> appelle
/// <c>FUN_140928e10</c> (« Cancelling reload »), annule le rechargement LOCALEMENT et n'envoie pas le ramassage.
/// Le serveur ne peut pas l'empêcher : la décision est prise avant l'envoi du paquet.
///
/// Ce module remplace les 9 octets du <c>cmp [rbp+0x4650],0 ; jg</c> (RVA 0x933B1E) par un saut vers un petit
/// code qui, si la cible déjà validée est un objet au sol (prédicat natif RVA 0x4E5770), poursuit directement vers
/// le chemin d'envoi ; pour toute autre cible il exécute exactement l'instruction déplacée puis l'ancien chemin.
///
/// Garde-fous : le fichier sur disque n'est JAMAIS modifié ; uniquement le client dont le SHA-256 est celui analysé ;
/// les octets d'origine doivent être présents en mémoire ; échec = on ne fait rien (le jeu se lance normalement) ;
/// annulation complète si l'écriture échoue. Désactivable : variable d'environnement COTK_DISABLE_CLIENT_PATCH=1.
/// </summary>
internal static class ClientReloadLootPatch
{
    /// <summary>SHA-256 du H1Z1.exe analysé. Tout autre exécutable est laissé intact.</summary>
    internal const string ClientSha256 = "a03e8f6d7d5db772ea2b94aa17f8d2849b1127c5c249cc8cb3bb6bd2df98a112";

    internal const long SiteRva = 0x933B1E;
    internal const long SendRva = 0x933B47;
    internal const long CancelRva = 0x933B27;
    internal const long WorldPredicateRva = 0x4E5770;

    /// <summary>cmp dword [rbp+0x4650], 0 ; jg +0x20 — les octets déplacés.</summary>
    internal static readonly byte[] Original = Convert.FromHexString("83bd50460000007f20");

    private const int AllocationAttempts = 8192;          // image+0x10000000 .. +0x30000000, dans la portée rel32
    private const long AllocationStart = 0x10000000;
    private const long AllocationStep = 0x10000;

    internal enum Outcome { Applied, AlreadyApplied, Disabled, NotPinnedClient, NotReady, Failed }

    internal sealed record Result(Outcome Outcome, string Detail);

    /// <summary>Accès mémoire du processus cible, abstrait pour pouvoir tester la logique sans vrai jeu.</summary>
    internal interface IPatchMemory
    {
        long ImageBase { get; }
        byte[] Read(long address, int length);
        /// <summary>Réserve une page lisible/inscriptible près de <paramref name="preferred"/> ; false si occupée.</summary>
        bool TryAllocate(long preferred, out long address);
        void Free(long address);
        void WriteCode(long address, byte[] data);
        void MakeExecutable(long address);
        void Suspend();
        void Resume();
    }

    // ---------------------------------------------------------------- code généré

    internal static byte[] Rel32(byte[] opcode, long at, long target)
    {
        var offset = checked((int)(target - (at + opcode.Length + 4)));
        var bytes = new byte[opcode.Length + 4];
        opcode.CopyTo(bytes, 0);
        BitConverter.GetBytes(offset).CopyTo(bytes, opcode.Length);
        return bytes;
    }

    /// <summary>Même code que l'outil de banc <c>tools/qa/qa_parallel_loot_client.py::build_stub</c>.</summary>
    internal static byte[] BuildStub(long address, long imageBase)
    {
        var code = new List<byte>();
        code.AddRange(new byte[] { 0x48, 0x81, 0xEC, 0xC0, 0x00, 0x00, 0x00 });   // sub rsp, 0xc0 (inclut le shadow space)
        foreach (var hex in new[] { "4889442420", "48894c2428", "4889542430", "4c89442438", "4c894c2440", "4c89542448", "4c895c2450" })
            code.AddRange(Convert.FromHexString(hex));                              // sauvegarde rax rcx rdx r8 r9 r10 r11
        for (var register = 0; register < 6; register++) code.AddRange(Xmm(0x7F, register, 0x60 + 16 * register));
        code.AddRange(new byte[] { 0x4C, 0x89, 0xF1 });                             // rcx = r14 : l'entité déjà validée
        code.AddRange(new byte[] { 0x48, 0xB8 });                                   // mov rax, prédicat « objet au sol »
        code.AddRange(BitConverter.GetBytes(imageBase + WorldPredicateRva));
        code.AddRange(new byte[] { 0xFF, 0xD0 });                                   // call rax
        code.AddRange(new byte[] { 0x88, 0x44, 0x24, 0x58 });                       // garde le booléen
        for (var register = 0; register < 6; register++) code.AddRange(Xmm(0x6F, register, 0x60 + 16 * register));
        foreach (var hex in new[] { "488b442420", "488b4c2428", "488b542430", "4c8b442438", "4c8b4c2440", "4c8b542448", "4c8b5c2450" })
            code.AddRange(Convert.FromHexString(hex));                              // restaure les volatils
        code.AddRange(new byte[] { 0x80, 0x7C, 0x24, 0x58, 0x00 });                // cmp résultat, 0
        code.AddRange(new byte[] { 0x48, 0x8D, 0xA4, 0x24, 0xC0, 0x00, 0x00, 0x00 }); // lea rsp,[rsp+0xc0] (flags intacts)
        // Objet au sol : envoi direct, l'état local de l'arme et l'animation de rechargement restent intacts.
        code.AddRange(Rel32(new byte[] { 0x0F, 0x85 }, address + code.Count, imageBase + SendRva));
        // Toute autre cible : exactement le cmp/jg déplacé, puis l'ancien chemin d'annulation.
        code.AddRange(Original.Take(7));
        code.AddRange(Rel32(new byte[] { 0x0F, 0x8F }, address + code.Count, imageBase + SendRva));
        code.AddRange(Rel32(new byte[] { 0xE9 }, address + code.Count, imageBase + CancelRva));
        return code.ToArray();

        static byte[] Xmm(byte op, int register, int displacement)
        {
            var bytes = new byte[] { 0xF3, 0x0F, op, (byte)(0x84 | (register << 3)), 0x24, 0, 0, 0, 0 };
            BitConverter.GetBytes(displacement).CopyTo(bytes, 5);
            return bytes;
        }
    }

    internal static byte[] BuildReplacement(long site, long trampoline)
    {
        var jump = Rel32(new byte[] { 0xE9 }, site, trampoline);                    // jmp rel32 (5 octets)
        var bytes = new byte[Original.Length];
        jump.CopyTo(bytes, 0);
        for (var i = jump.Length; i < bytes.Length; i++) bytes[i] = 0x90;            // nop de remplissage
        return bytes;
    }

    // ---------------------------------------------------------------- logique d'application

    /// <summary>Applique le correctif à un processus dont l'exécutable est déjà validé. N'élève jamais d'exception.</summary>
    internal static Result Apply(IPatchMemory memory)
    {
        long site = memory.ImageBase + SiteRva;
        long allocation = 0;
        var suspended = false;
        var installed = false;
        try
        {
            var current = memory.Read(site, Original.Length);
            if (current[0] == 0xE9 && current.Skip(5).All(b => b == 0x90))
                return new Result(Outcome.AlreadyApplied, "déjà installé");
            if (!current.AsSpan().SequenceEqual(Original))
                return new Result(Outcome.NotReady, "les instructions d'origine ne sont pas (encore) en mémoire");

            for (var step = 0; step < AllocationAttempts && allocation == 0; step++)
                if (memory.TryAllocate(memory.ImageBase + AllocationStart + step * AllocationStep, out var candidate))
                    allocation = candidate;
            if (allocation == 0)
                return new Result(Outcome.Failed, "aucune page proche de l'image n'a pu être réservée");

            memory.WriteCode(allocation, BuildStub(allocation, memory.ImageBase));
            memory.MakeExecutable(allocation);

            memory.Suspend();
            suspended = true;
            // Dernière vérification sous suspension : personne n'a modifié le site entre-temps.
            if (!memory.Read(site, Original.Length).AsSpan().SequenceEqual(Original))
                throw new InvalidOperationException("le site a changé pendant la préparation");
            memory.WriteCode(site, BuildReplacement(site, allocation));
            installed = true;
            return new Result(Outcome.Applied, $"trampoline 0x{allocation:X}");
        }
        catch (Exception ex)
        {
            try
            {
                if (installed) memory.WriteCode(site, Original);
                if (allocation != 0 && !installed) memory.Free(allocation);
            }
            catch
            {
                // Rien de plus à tenter : le jeu continue sans correctif ou avec le saut valide.
            }
            return new Result(Outcome.Failed, ex.Message);
        }
        finally
        {
            if (suspended)
            {
                try { memory.Resume(); } catch { /* le jeu ne doit jamais rester suspendu par notre faute */ }
            }
        }
    }

    // ---------------------------------------------------------------- validation de l'exécutable

    private static string? _pinnedPath;

    /// <summary>Vrai seulement pour l'exécutable analysé (SHA-256). Le résultat est mémorisé par chemin.</summary>
    internal static bool IsPinnedClient(string exePath)
    {
        if (string.Equals(_pinnedPath, exePath, StringComparison.OrdinalIgnoreCase)) return true;
        using var stream = File.OpenRead(exePath);
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (hash != ClientSha256) return false;
        _pinnedPath = exePath;
        return true;
    }

    internal static bool Disabled => Environment.GetEnvironmentVariable("COTK_DISABLE_CLIENT_PATCH") == "1";

    /// <summary>Installe le correctif dans le jeu lancé par le launcher. Silencieux et sans effet si une garde échoue.</summary>
    public static Result TryApply(Process process, string exePath)
    {
        try
        {
            if (Disabled) return new Result(Outcome.Disabled, "COTK_DISABLE_CLIENT_PATCH=1");
            if (!IsPinnedClient(exePath)) return new Result(Outcome.NotPinnedClient, "exécutable différent de celui analysé");
            var main = process.MainModule ?? throw new InvalidOperationException("module principal introuvable");
            if (!string.Equals(Path.GetFullPath(main.FileName), Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase))
                return new Result(Outcome.NotPinnedClient, "le processus n'exécute pas ce fichier");
            using var memory = new ProcessMemory(process, main.BaseAddress.ToInt64());
            return Apply(memory);
        }
        catch (Exception ex)
        {
            return new Result(Outcome.Failed, ex.Message);
        }
    }

    // ---------------------------------------------------------------- vrai processus

    private sealed class ProcessMemory : IPatchMemory, IDisposable
    {
        private const uint ProcessAll = 0x1F0FFF;
        private const uint MemCommitReserve = 0x3000;
        private const uint MemRelease = 0x8000;
        private const uint PageReadWrite = 0x04;
        private const uint PageExecuteRead = 0x20;
        private const uint PageExecuteReadWrite = 0x40;

        private readonly IntPtr _handle;

        public ProcessMemory(Process process, long imageBase)
        {
            ImageBase = imageBase;
            _handle = OpenProcess(ProcessAll, false, (uint)process.Id);
            if (_handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        public long ImageBase { get; }

        public byte[] Read(long address, int length)
        {
            var buffer = new byte[length];
            if (!ReadProcessMemory(_handle, new IntPtr(address), buffer, (nuint)length, out var read) || (int)read != length)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return buffer;
        }

        public bool TryAllocate(long preferred, out long address)
        {
            var result = VirtualAllocEx(_handle, new IntPtr(preferred), 0x1000, MemCommitReserve, PageReadWrite);
            address = result.ToInt64();
            return result != IntPtr.Zero;
        }

        public void Free(long address) => VirtualFreeEx(_handle, new IntPtr(address), 0, MemRelease);

        public void WriteCode(long address, byte[] data)
        {
            if (!VirtualProtectEx(_handle, new IntPtr(address), (nuint)data.Length, PageExecuteReadWrite, out var old))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                if (!WriteProcessMemory(_handle, new IntPtr(address), data, (nuint)data.Length, out var written) || (int)written != data.Length)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                if (!FlushInstructionCache(_handle, new IntPtr(address), (nuint)data.Length))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                VirtualProtectEx(_handle, new IntPtr(address), (nuint)data.Length, old, out _);
            }
        }

        public void MakeExecutable(long address)
        {
            if (!VirtualProtectEx(_handle, new IntPtr(address), 0x1000, PageExecuteRead, out _))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        public void Suspend()
        {
            if (NtSuspendProcess(_handle) != 0) throw new InvalidOperationException("suspension impossible");
        }

        public void Resume() => NtResumeProcess(_handle);

        public void Dispose() => CloseHandle(_handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, nuint size, out nuint read);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, nuint size, out nuint written);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, nuint size, uint type, uint protect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, nuint size, uint type);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtectEx(IntPtr process, IntPtr address, nuint size, uint protect, out uint old);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, nuint size);

        [DllImport("ntdll.dll")]
        private static extern int NtSuspendProcess(IntPtr process);

        [DllImport("ntdll.dll")]
        private static extern int NtResumeProcess(IntPtr process);
    }
}
