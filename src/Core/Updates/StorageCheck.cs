using System.IO.Compression;

namespace COTK.Next.Core;

internal sealed record StoragePlan(string Directory,long DownloadBytes,long ExpandedBytes,long SavedBytes,long RequiredBytes,long AvailableBytes,bool Estimated);

internal static class StorageCheck
{
    public const long Margin=256L<<20;

    internal static long Required(long download,long expanded,long saved,long resumed=0)
    {
        if(download<0 || expanded<0 || saved<0 || resumed<0) throw new ArgumentOutOfRangeException();
        return checked(Math.Max(0,download-Math.Min(download,resumed))+expanded+saved+Margin);
    }

    public static long FreeBytes(string path)
    {
        try
        {
            var root=Path.GetPathRoot(Path.GetFullPath(path));
            if(string.IsNullOrEmpty(root)) throw new IOException("Drive root missing.");
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        { throw new LauncherException(new LauncherError("E-DISK-004","Espace disque non vérifiable","Impossible de vérifier l'espace libre sur le disque choisi. L'installation est bloquée.","Choisissez un disque local accessible puis réessayez.",true,ex.Message),ex); }
    }

    public static void Ensure(string path,long needed,Func<string,long>? probe=null)
    {
        long free;
        try { free=(probe??FreeBytes)(path); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or NotSupportedException)
        { throw new LauncherException(new LauncherError("E-DISK-004","Espace disque non vérifiable","Impossible de vérifier l'espace libre sur le disque choisi. L'installation est bloquée.","Choisissez un disque local accessible puis réessayez.",true,ex.Message),ex); }
        if(free<needed) throw new LauncherException(Errors.DiskFull(needed,free));
    }

    public static void CheckWritable(string path)
    {
        var existing=Path.GetFullPath(path);
        while(!Directory.Exists(existing)) existing=Path.GetDirectoryName(existing) ?? throw new IOException("Parent directory missing.");
        var temp=Path.Combine(existing,".cotk-write-test-"+Guid.NewGuid().ToString("N"));
        using var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,1,FileOptions.DeleteOnClose);
    }

    public static long SavedDataBytes(string directory)
    {
        if(!Directory.Exists(directory)) return 0;
        var all=ClientUninstaller.CanReinstall(directory);
        var options=new EnumerationOptions { RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint,IgnoreInaccessible=false };
        long bytes=0;
        foreach(var file in Directory.EnumerateFiles(directory,"*",options))
        {
            var relative=Path.GetRelativePath(directory,file);
            if(all || ManifestRules.IsPlayerData(relative)) bytes=checked(bytes+new FileInfo(file).Length);
        }
        return bytes;
    }

    public static long ExpandedZipBytes(string archive)
    {
        using var zip=ZipFile.OpenRead(archive);
        return zip.Entries.Sum(e=>e.Length);
    }
}
