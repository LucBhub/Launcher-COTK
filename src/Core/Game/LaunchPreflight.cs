namespace COTK.Next.Core;

internal sealed record GameServiceStatus(bool ApiOnline,bool GameOnline,bool Maintenance,int Players,DateTimeOffset CheckedAt)
{
    public bool CanPlay => ApiOnline && GameOnline && !Maintenance;
}

internal static class LaunchPreflight
{
    public static void CheckClient(string directory,string templateDirectory)
    {
        foreach(var name in new[]{"H1Z1.exe","mss32.dll","steam_api64.dll","vivoxsdk_x64.dll"})
            if(!File.Exists(Path.Combine(directory,name)))
                throw new LauncherException(new LauncherError("E-GAME-009","Installation incomplète",$"Un fichier essentiel manque : {name}.","Lancez Vérifier les fichiers dans Bibliothèque."));
        StorageCheck.CheckWritable(directory);
        var config=Path.Combine(directory,"ClientConfig.ini");
        if(File.Exists(config)) {using var file=new FileStream(config,FileMode.Open,FileAccess.ReadWrite,FileShare.Read);}
        else if(!File.Exists(Path.Combine(templateDirectory,"ClientConfig.example.ini")))
            throw new LauncherException(Errors.InvalidConfig("Le modèle ClientConfig.example.ini est absent de l'installation."));
    }
}
