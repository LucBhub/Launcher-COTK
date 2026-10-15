using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

// Apphost minimal pour cette machine sans SDK. Le publish SDK crée son propre apphost.
// Ce fichier ne fait pas partie du launcher WPF et n'a aucun accès aux identifiants.
internal static class StartLauncher
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var root = AppDomain.CurrentDomain.BaseDirectory;
            var dll = Path.Combine(root, "COTK.Launcher.Next.dll");
            var dotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
            if (!File.Exists(dotnet) || !File.Exists(dll))
                throw new InvalidOperationException("Le launcher requiert .NET Desktop Runtime 10 x64 et ses fichiers doivent rester ensemble. Consultez le README.");
            var command = new StringBuilder(Quote(dll));
            foreach (var arg in args) command.Append(' ').Append(Quote(arg));
            using (var process = Process.Start(new ProcessStartInfo(dotnet, command.ToString())
            {
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true
            }))
            {
                process.WaitForExit();
                return process.ExitCode;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "COTK — démarrage impossible", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    // Quoting Windows : les antislashs terminaux sont doublés avant le guillemet final.
    private static string Quote(string value)
    {
        var result = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') result.Append('\\', slashes * 2 + 1).Append(c);
            else result.Append('\\', slashes).Append(c);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
}
