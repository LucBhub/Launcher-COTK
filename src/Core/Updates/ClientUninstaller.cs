using System.Text.Json;

namespace COTK.Next.Core;

internal sealed record UninstallPlan(string Directory, string Version, string[] Files, long Bytes);
internal sealed record UninstallResult(int FilesRemoved, long BytesRemoved);

/// <summary>Deletes only registered game files, never recursively deletes the client folder.
/// A bounded, path-bound receipt enables retry after a sharing violation and reinstall with saved data.</summary>
internal static class ClientUninstaller
{
    internal const string ReceiptName = ".cotk-uninstall.json";
    private const string InventoryName = ".cotk-managed-files.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record Receipt(int SchemaVersion, string Directory, string Version, bool Complete, string[] Files);

    public static bool IsAvailable(string directory) =>
        File.Exists(Path.Combine(directory,"H1Z1.exe")) || IsPending(directory);

    public static bool IsPending(string directory)
    {
        try { return ReadReceipt(Path.GetFullPath(directory)) is { Complete:false }; }
        catch { return false; }
    }

    public static bool CanReinstall(string directory)
    {
        if (!File.Exists(Path.Combine(directory,ReceiptName))) return false;
        var root=ValidateDirectory(directory);
        return ReadReceipt(root) is { Complete:true };
    }

    public static bool HasInventory(string directory) => File.Exists(Path.Combine(directory,InventoryName)) || IsPending(directory);

    public static UninstallPlan Prepare(string directory, IEnumerable<string>? legacyCatalogue = null)
    {
        var root=ValidateDirectory(directory);
        var receipt=ReadReceipt(root);
        var pending=receipt is { Complete:false };
        string version;
        if(pending) version=receipt!.Version;
        else
        {
            var marker=ClientInstaller.LocalPath(root,".cotk-client-version");
            if (!File.Exists(ClientInstaller.LocalPath(root,"H1Z1.exe")) || !File.Exists(marker))
                throw Failure("E-UNINSTALL-001","Installation non reconnue","Le dossier ne contient pas un client COTK identifié. Aucun fichier ne sera supprimé.");
            version=ReadSmall(marker).Trim();
            if (!Version.TryParse(version,out _)) throw BadInventory();
        }

        string[] registered;
        if(pending) registered=receipt!.Files;
        else if(File.Exists(Path.Combine(root,InventoryName)))
        {
            try { registered=JsonSerializer.Deserialize<string[]>(ReadSmall(ClientInstaller.LocalPath(root,InventoryName)),Json) ?? throw BadInventory(); }
            catch(JsonException ex) { throw new LauncherException(BadInventory().Error,ex); }
        }
        else if(legacyCatalogue is not null) registered=legacyCatalogue.ToArray();
        else throw Failure("E-UNINSTALL-002","Liste de fichiers indisponible","Impossible d'identifier les fichiers du client. Connectez-vous pour récupérer son catalogue puis réessayez.");

        ValidateNames(registered);
        if(!registered.Any(n=>n.Equals("H1Z1.exe",StringComparison.OrdinalIgnoreCase))) throw BadInventory();
        var selected=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var name in registered)
        {
            var rel=name.Replace('\\','/');
            if(Keep(rel)) continue;
            selected.Add(rel);
            if(!rel.EndsWith(".cotk-part",StringComparison.OrdinalIgnoreCase)) selected.Add(rel+".cotk-part");
        }
        foreach(var metadata in new[]{".cotk-client-version",".cotk-hash-cache.json",InventoryName}) selected.Add(metadata);
        long bytes=0;
        foreach(var rel in selected)
        {
            var file=ClientInstaller.LocalPath(root,rel);
            if(Directory.Exists(file)) throw BadInventory();
            if(File.Exists(file)) bytes+=new FileInfo(file).Length;
        }
        // Remove the executable first so a partial uninstall cannot be classified as ready to play.
        var files=selected.OrderBy(n=>n.Equals("H1Z1.exe",StringComparison.OrdinalIgnoreCase)?0:1).ThenBy(n=>n,StringComparer.OrdinalIgnoreCase).ToArray();
        return new UninstallPlan(root,version,files,bytes);
    }

    public static UninstallResult Execute(UninstallPlan plan, Func<bool> gameRunning, IProgress<TransferProgress>? progress=null)
    {
        var root=ValidateDirectory(plan.Directory);
        if(gameRunning()) throw new LauncherException(Errors.AlreadyRunning());
        ValidateNames(plan.Files);
        if(!plan.Files.Any(n=>n.Equals("H1Z1.exe",StringComparison.OrdinalIgnoreCase)) || plan.Files.Any(Keep)) throw BadInventory();
        var existing=ReadReceipt(root);
        if(existing is not { Complete:false })
        {
            var exe=ClientInstaller.LocalPath(root,"H1Z1.exe");
            var marker=ClientInstaller.LocalPath(root,".cotk-client-version");
            if(!File.Exists(exe) || !File.Exists(marker) || ReadSmall(marker).Trim()!=plan.Version) throw BadInventory();
        }
        foreach(var file in plan.Files) _=ClientInstaller.LocalPath(root,file); // validate the complete list before any write/delete
        var receipt=new Receipt(1,root,plan.Version,false,plan.Files);
        WriteReceipt(root,receipt);
        int removed=0,done=0; long bytes=0;
        var directories=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var rel in plan.Files)
        {
            if(gameRunning()) throw new LauncherException(Errors.AlreadyRunning());
            var file=ClientInstaller.LocalPath(root,rel);
            if(File.Exists(file))
            {
                var length=new FileInfo(file).Length;
                try { File.Delete(file); }
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
                {
                    throw new LauncherException(new LauncherError("E-UNINSTALL-003","Désinstallation interrompue",
                        $"Impossible de supprimer : {rel}.","Fermez le programme qui verrouille ce fichier puis relancez la désinstallation. Vos données conservées sont intactes.",true,ex.Message),ex);
                }
                removed++; bytes+=length;
            }
            var parent=Path.GetDirectoryName(file);
            while(parent is not null && !parent.Equals(root,StringComparison.OrdinalIgnoreCase) && parent.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            { directories.Add(parent); parent=Path.GetDirectoryName(parent); }
            done++;
            if(done%25==0 || done==plan.Files.Length)
                progress?.Report(new TransferProgress(TransferPhase.Uninstalling,"Suppression des fichiers du client…",done,plan.Files.Length,0,null,done,plan.Files.Length));
        }
        // Non-recursive cleanup only; unknown files, settings, logs and links remain untouched.
        foreach(var dir in directories.OrderByDescending(d=>d.Length))
        {
            try { if(Directory.Exists(dir) && (File.GetAttributes(dir)&FileAttributes.ReparsePoint)==0 && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir,false); }
            catch(IOException) { }
        }
        WriteReceipt(root,receipt with { Complete=true });
        return new UninstallResult(removed,bytes);
    }

    /// <summary>On reinstall, retained unknown files are copied too; a conflict or link blocks the swap.</summary>
    public static void RestoreRetainedFiles(string root,string stage)
    {
        if(!CanReinstall(root)) return;
        var stack=new Stack<string>(); stack.Push(root);
        while(stack.Count>0)
        {
            foreach(var entry in Directory.EnumerateFileSystemEntries(stack.Pop()))
            {
                var attrs=File.GetAttributes(entry);
                if((attrs&FileAttributes.ReparsePoint)!=0) throw BadInventory();
                if((attrs&FileAttributes.Directory)!=0) { stack.Push(entry); continue; }
                var rel=Path.GetRelativePath(root,entry).Replace('\\','/');
                if(rel==ReceiptName) continue;
                var dest=ClientInstaller.LocalPath(stage,rel);
                if(File.Exists(dest) && !Keep(rel))
                    throw Failure("E-UNINSTALL-004","Fichier personnel en conflit",$"Un fichier conservé existe aussi dans le nouveau client : {rel}.");
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(entry,dest,true);
            }
        }
    }

    private static bool Keep(string rel) => rel.Equals(ReceiptName,StringComparison.OrdinalIgnoreCase) ||
        (ManifestRules.IsPlayerData(rel) && !new[]{".cotk-client-version",".cotk-hash-cache.json",InventoryName}.Contains(rel,StringComparer.OrdinalIgnoreCase) && !rel.EndsWith(".cotk-part",StringComparison.OrdinalIgnoreCase));

    private static void ValidateNames(string[]? names)
    {
        if(names is null || names.Length==0 || names.Length>100_000 || names.Any(n=>!ManifestRules.IsSafeRelative(n))) throw BadInventory();
    }

    private static string ValidateDirectory(string directory)
    {
        if(!Path.IsPathRooted(directory)) throw BadInventory();
        var root=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        if(root.Length==0 || root.Equals(Path.GetPathRoot(root)?.TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase) || !Directory.Exists(root)) throw BadInventory();
        var protectedPaths=new[]{AppPaths.InstallDir,AppPaths.SettingsDir,AppPaths.LogDir,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)};
        foreach(var path in protectedPaths.Where(p=>!string.IsNullOrWhiteSpace(p)))
        {
            var safe=Path.GetFullPath(path).TrimEnd('\\','/');
            if(safe.Equals(root,StringComparison.OrdinalIgnoreCase) || safe.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw BadInventory();
        }
        string? current=root;
        while(!string.IsNullOrEmpty(current))
        {
            if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0) throw BadInventory();
            current=Path.GetDirectoryName(current);
        }
        return root;
    }

    private static Receipt? ReadReceipt(string root)
    {
        var file=Path.Combine(root,ReceiptName);
        if(!File.Exists(file)) return null;
        try
        {
            var receipt=JsonSerializer.Deserialize<Receipt>(ReadSmall(ClientInstaller.LocalPath(root,ReceiptName)),Json);
            if(receipt is null || receipt.SchemaVersion!=1 || !string.Equals(receipt.Directory,root,StringComparison.OrdinalIgnoreCase) || !Version.TryParse(receipt.Version,out _)) throw BadInventory();
            ValidateNames(receipt.Files);
            return receipt;
        }
        catch(JsonException ex) { throw new LauncherException(BadInventory().Error,ex); }
    }

    private static string ReadSmall(string path)
    {
        if(new FileInfo(path).Length>4*1024*1024) throw BadInventory();
        return File.ReadAllText(path);
    }

    private static void WriteReceipt(string root,Receipt receipt)
    {
        var file=ClientInstaller.LocalPath(root,ReceiptName);
        var temporary=ClientInstaller.LocalPath(root,ReceiptName+"."+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            File.WriteAllText(temporary,JsonSerializer.Serialize(receipt,Json));
            File.Move(temporary,file,true);
        }
        finally { if(File.Exists(temporary)) File.Delete(temporary); }
    }

    private static LauncherException BadInventory() => Failure("E-UNINSTALL-001","Désinstallation refusée",
        "Le dossier ou sa liste de fichiers n'est pas sûr. Aucun fichier personnel ne sera supprimé.");
    private static LauncherException Failure(string code,string title,string message) => new(new LauncherError(code,title,message));
}
