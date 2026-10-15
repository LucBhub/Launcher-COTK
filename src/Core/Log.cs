using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace COTK.Next.Core;

internal enum LogLevel { Debug, Info, Warn, Error }

/// <summary>Journal du launcher : fichier unique a rotation (5 x 1 Mo), thread-safe,
/// secrets masques (tickets de jeu, jetons Bearer, mots de passe) AVANT l'ecriture,
/// et un tampon memoire des dernieres lignes pour l'export de diagnostic.</summary>
internal static class Log
{
    private const long MaxBytes = 1_000_000;
    private const int Keep = 5;
    private const int RingSize = 300;

    private static readonly object Gate = new();
    private static readonly Queue<string> Ring = new();
    private static readonly Regex[] Secrets =
    [
        new(@"lp2\.[A-Za-z0-9._\-]{8,}", RegexOptions.Compiled),
        new(@"(?i)(bearer\s+)[A-Za-z0-9._\-]{16,}", RegexOptions.Compiled),
        new(@"(?i)(""?(?:accessToken|deviceCode|password|ticket)""?\s*[:=]\s*""?)[^""\s,}]{6,}", RegexOptions.Compiled),
    ];

    public static LogLevel Minimum { get; set; } = LogLevel.Info;

    public static string FilePath => Path.Combine(AppPaths.LogDir, "launcher.log");

    public static void Debug(string message) => Write(LogLevel.Debug, message, null);
    public static void Info(string message) => Write(LogLevel.Info, message, null);
    public static void Warn(string message, Exception? ex = null) => Write(LogLevel.Warn, message, ex);
    public static void Error(string message, Exception? ex = null) => Write(LogLevel.Error, message, ex);

    internal static string Scrub(string text)
    {
        foreach (var rx in Secrets)
        {
            text = rx.Replace(text, m => m.Groups.Count > 1 && m.Groups[1].Success
                ? m.Groups[1].Value + "***"
                : "lp2.***");
        }
        return text;
    }

    public static void Write(LogLevel level, string message, Exception? ex)
    {
        if (level < Minimum) return;
        var text = Scrub(ex is null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}");
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level.ToString().ToUpperInvariant(),-5} {text}";
        lock (Gate)
        {
            Ring.Enqueue(line);
            while (Ring.Count > RingSize) Ring.Dequeue();
            try
            {
                Directory.CreateDirectory(AppPaths.LogDir);
                RotateIfNeeded();
                File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Un journal ne doit JAMAIS faire tomber le launcher.
            }
        }
    }

    private static void RotateIfNeeded()
    {
        var info = new FileInfo(FilePath);
        if (!info.Exists || info.Length < MaxBytes) return;
        for (var i = Keep - 1; i >= 1; i--)
        {
            var from = Path.Combine(AppPaths.LogDir, $"launcher.{i}.log");
            var to = Path.Combine(AppPaths.LogDir, $"launcher.{i + 1}.log");
            if (File.Exists(to)) File.Delete(to);
            if (File.Exists(from)) File.Move(from, to);
        }
        File.Move(FilePath, Path.Combine(AppPaths.LogDir, "launcher.1.log"), overwrite: true);
    }

    public static string[] Recent()
    {
        lock (Gate) return Ring.ToArray();
    }

    /// <summary>Rapport pour le support : versions, derniers logs, logs client, SANS secret.</summary>
    public static string ExportDiagnostics(string? clientLogsDir, string headerInfo)
    {
        Directory.CreateDirectory(AppPaths.DiagnosticsDir);
        var target = Path.Combine(AppPaths.DiagnosticsDir, $"cotk-diagnostic-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        using (var zip = ZipFile.Open(target, ZipArchiveMode.Create))
        {
            var info = zip.CreateEntry("info.txt");
            using (var w = new StreamWriter(info.Open(), Encoding.UTF8))
                w.Write(Scrub(headerInfo));

            lock (Gate)
            {
                foreach (var file in Directory.EnumerateFiles(AppPaths.LogDir, "launcher*.log"))
                    AddScrubbed(zip, file, "launcher/" + Path.GetFileName(file));
            }

            if (clientLogsDir is not null && Directory.Exists(clientLogsDir))
            {
                foreach (var name in new[] { "H1Z1.log", "H1Z1 PlayClient (Live).log", "Login.log", "NetInfo.log" })
                {
                    var path = Path.Combine(clientLogsDir, name);
                    if (File.Exists(path)) AddScrubbed(zip, path, "client/" + name);
                }
            }
        }
        Info($"diagnostic exported: {target}");
        PruneDiagnostics();
        return target;
    }

    private static void AddScrubbed(ZipArchive zip, string path, string entryName)
    {
        string content;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            content = reader.ReadToEnd();
        }
        catch (IOException) { return; }

        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(Scrub(content));
    }

    private static void PruneDiagnostics()
    {
        try
        {
            foreach (var old in new DirectoryInfo(AppPaths.DiagnosticsDir).GetFiles("cotk-diagnostic-*.zip")
                         .OrderByDescending(f => f.CreationTimeUtc).Skip(5))
                old.Delete();
        }
        catch { }
    }
}
