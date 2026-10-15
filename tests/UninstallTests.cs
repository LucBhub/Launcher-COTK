using System.Diagnostics;
using System.Text.Json;
using COTK.Next.Core;

namespace COTK.Next.Tests;

internal static class UninstallTests
{
    private static string Fixture(string parent,string name,string[]? inventory=null)
    {
        var root=Path.Combine(parent,name,"client");
        Directory.CreateDirectory(Path.Combine(root,"Assets"));
        Directory.CreateDirectory(Path.Combine(root,"UI","Settings"));
        Directory.CreateDirectory(Path.Combine(root,"Logs"));
        File.WriteAllText(Path.Combine(root,"H1Z1.exe"),"test exe");
        File.WriteAllText(Path.Combine(root,"Assets","a.pack"),"test pack");
        File.WriteAllText(Path.Combine(root,"Assets","a.pack.cotk-part"),"partial");
        File.WriteAllText(Path.Combine(root,".cotk-client-version"),"0.1.0");
        File.WriteAllText(Path.Combine(root,".cotk-hash-cache.json"),"{}");
        File.WriteAllText(Path.Combine(root,"UserOptions.ini"),"custom graphics");
        File.WriteAllText(Path.Combine(root,"InputProfile_User.xml"),"custom keys");
        File.WriteAllText(Path.Combine(root,"SoundSettings.xml"),"custom sound");
        File.WriteAllText(Path.Combine(root,"UI","Settings","a.xml"),"custom ui");
        File.WriteAllText(Path.Combine(root,"Logs","a.log"),"personal logs");
        File.WriteAllText(Path.Combine(root,"screenshot.txt"),"unknown user file");
        var files=inventory ?? new[]{"H1Z1.exe","Assets/a.pack","UserOptions.ini","InputProfile_User.xml","SoundSettings.xml","UI/Settings/a.xml","Logs/a.log"};
        File.WriteAllText(Path.Combine(root,".cotk-managed-files.json"),JsonSerializer.Serialize(files));
        return root;
    }

    private static void Check(bool value,string message) { if(!value) throw new Exception(message); }

    public static void PreservesData(string parent)
    {
        var root=Fixture(parent,"uninstall-preserve");
        var plan=ClientUninstaller.Prepare(root);
        Check(!plan.Files.Any(n=>n.EndsWith(".ini")),"settings included in deletion");
        var result=ClientUninstaller.Execute(plan,()=>false);
        Check(result.FilesRemoved>0,"no game file deleted");
        Check(!File.Exists(Path.Combine(root,"H1Z1.exe")) && !Directory.Exists(Path.Combine(root,"Assets")),"game files remain");
        foreach(var name in new[]{"UserOptions.ini","InputProfile_User.xml","SoundSettings.xml","UI/Settings/a.xml","Logs/a.log","screenshot.txt"})
            Check(File.Exists(Path.Combine(root,name)),"personal data lost: "+name);
        Check(Directory.Exists(root),"root was recursively removed");
        Check(ClientUninstaller.CanReinstall(root),"reinstallation not enabled");
        Check(!ClientUninstaller.IsAvailable(root),"completed uninstall still available");
    }

    public static void RefusesUnsafe(string parent)
    {
        var root=Fixture(parent,"uninstall-unsafe",new[]{"H1Z1.exe","../outside.txt"});
        var outside=Path.Combine(Path.GetDirectoryName(root)!,"outside.txt"); File.WriteAllText(outside,"safe");
        try { ClientUninstaller.Prepare(root); throw new Exception("traversal accepted"); }
        catch(LauncherException ex) { Check(ex.Error.Code=="E-UNINSTALL-001","wrong unsafe code"); }
        Check(File.Exists(Path.Combine(root,"H1Z1.exe")) && File.ReadAllText(outside)=="safe","unsafe plan mutated files");
        Check(!File.Exists(Path.Combine(root,ClientUninstaller.ReceiptName)),"unsafe plan wrote receipt");
        var unknown=Fixture(parent,"uninstall-unknown"); File.Delete(Path.Combine(unknown,".cotk-client-version"));
        try { ClientUninstaller.Prepare(unknown); throw new Exception("unrecognized directory accepted"); }
        catch(LauncherException) { }
        Check(File.Exists(Path.Combine(unknown,"H1Z1.exe")),"unrecognized exe deleted");
        foreach(var path in new[]{Path.GetPathRoot(root)!,AppPaths.InstallDir,Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)})
        {
            try { ClientUninstaller.Prepare(path); throw new Exception("protected directory accepted"); }
            catch(LauncherException) { }
        }
    }

    public static void RefusesRunningGame(string parent)
    {
        var root=Fixture(parent,"uninstall-running");
        var plan=ClientUninstaller.Prepare(root);
        try { ClientUninstaller.Execute(plan,()=>true); throw new Exception("running game ignored"); }
        catch(LauncherException ex) { Check(ex.Error.Code=="E-GAME-002","wrong running-game code"); }
        Check(File.Exists(Path.Combine(root,"H1Z1.exe")) && !File.Exists(Path.Combine(root,ClientUninstaller.ReceiptName)),"running game caused deletion");
    }

    public static void RetryLockedFile(string parent)
    {
        var root=Fixture(parent,"uninstall-locked");
        var plan=ClientUninstaller.Prepare(root);
        using(var locked=new FileStream(Path.Combine(root,"Assets","a.pack"),FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            try { ClientUninstaller.Execute(plan,()=>false); throw new Exception("sharing violation ignored"); }
            catch(LauncherException ex) { Check(ex.Error.Code=="E-UNINSTALL-003","locked error not mapped"); }
            Check(ClientUninstaller.IsPending(root),"no recovery receipt");
            Check(!ClientUninstaller.CanReinstall(root),"unfinished uninstall accepted for reinstall");
            Check(File.Exists(Path.Combine(root,"UserOptions.ini")),"locked uninstall lost settings");
        }
        var retry=ClientUninstaller.Prepare(root);
        ClientUninstaller.Execute(retry,()=>false);
        Check(!ClientUninstaller.IsPending(root) && ClientUninstaller.CanReinstall(root),"retry did not finish");
    }

    public static void RefusesJunction(string parent)
    {
        var root=Fixture(parent,"uninstall-junction");
        var foreign=Path.Combine(parent,"junction-target"); Directory.CreateDirectory(foreign); File.WriteAllText(Path.Combine(foreign,"a.pack"),"outside file");
        Directory.Delete(Path.Combine(root,"Assets"),true);
        using var process=Process.Start(new ProcessStartInfo("cmd.exe",$"/c mklink /J \"{Path.Combine(root,"Assets")}\" \"{foreign}\"") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true })!;
        process.WaitForExit(); if(process.ExitCode!=0) throw new Exception("could not create junction fixture: "+process.StandardError.ReadToEnd());
        try { ClientUninstaller.Prepare(root); throw new Exception("junction accepted"); }
        catch(LauncherException) { }
        Check(File.ReadAllText(Path.Combine(foreign,"a.pack"))=="outside file","junction target affected");
    }

    public static void LegacyCatalogue(string parent)
    {
        var root=Fixture(parent,"uninstall-legacy"); File.Delete(Path.Combine(root,".cotk-managed-files.json"));
        try { ClientUninstaller.Prepare(root); throw new Exception("missing inventory accepted without catalogue"); }
        catch(LauncherException ex) { Check(ex.Error.Code=="E-UNINSTALL-002","missing inventory message"); }
        var plan=ClientUninstaller.Prepare(root,new[]{"H1Z1.exe","Assets/a.pack"});
        ClientUninstaller.Execute(plan,()=>false);
        Check(File.ReadAllText(Path.Combine(root,"screenshot.txt"))=="unknown user file","legacy uninstall lost unknown file");
    }
}
