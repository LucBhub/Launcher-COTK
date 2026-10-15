using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace COTK.Next.Core;

internal sealed record ReportSection(string Name, string Content);
internal sealed record ReportSubmission(bool Consent, string LauncherVersion, string ClientVersion, string ErrorCode, List<ReportSection> Sections);
internal sealed record ReportReceipt(string Id, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
internal sealed record ReceivedReport(string Id, string Username, string LauncherVersion, string ErrorCode, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
internal sealed record ReportList(List<ReceivedReport> Reports);
internal sealed record ReportDetails(string Id, string Excerpt, DateTimeOffset ExpiresAt);

internal static class SupportReport
{
    private static readonly Regex Ticket = new(@"(?i)\b(?:lp2\.|bearer\s+)[A-Za-z0-9._-]+", RegexOptions.Compiled);
    private static readonly Regex Secret = new(@"(?i)(""?(?:password|accessToken|refreshToken|deviceCode|SessionId|ticket|authorization|cookie|internal_api_key|h1z1_ticket_secret)""?\s*[:=]\s*)[^\r\n]+", RegexOptions.Compiled);
    private static readonly Regex Home = new(@"(?i)[A-Z]:\\Users\\[^\\\r\n"" ]+", RegexOptions.Compiled);
    private static readonly Regex Email = new(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);
    private static readonly Regex Address = new(@"\b(?:[0-9]{1,3}\.){3}[0-9]{1,3}\b", RegexOptions.Compiled);
    private static readonly Regex Machine = new(@"(?i)\b(?:DESKTOP|LAPTOP)-[A-Z0-9]+\b", RegexOptions.Compiled);

    public static string Redact(string text)
    {
        text = Ticket.Replace(text, "[SECRET]");
        text = Secret.Replace(text, "$1[SECRET]");
        text = Home.Replace(text, @"C:\Users\[USER]");
        text = Email.Replace(text, "[EMAIL]");
        text = Address.Replace(text, m => IPAddress.TryParse(m.Value, out _) ? "[IP]" : m.Value);
        text = Machine.Replace(text, "[PC]");
        if (Environment.MachineName.Length > 2) text = text.Replace(Environment.MachineName, "[PC]", StringComparison.OrdinalIgnoreCase);
        return text;
    }

    // Read only a bounded tail, never the whole (potentially multi-GB) client log.
    internal static string ReadTail(string file, int lines)
    {
        if (!File.Exists(file)) return "";
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var offset = Math.Max(0, stream.Length - 128 * 1024);
        stream.Seek(offset, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        if (offset > 0) reader.ReadLine();
        var tail = reader.ReadToEnd().Replace("\0", "").Replace("\r\n", "\n").Split('\n').TakeLast(Math.Clamp(lines, 1, 500));
        var result = Redact(string.Join('\n', tail));
        // A bounded preview is exactly what is sent, including truncation. 5*6000 UTF-16 chars
        // remain below the JSON body limit even with non-ASCII escaping.
        return result.Length > 6000 ? "[Extrait limité en taille]\n" + result[^5900..] : result;
    }

    public static ReportSubmission Prepare(int lines, bool includeClient, string version, string? errorCode = null)
    {
        var sections = new List<ReportSection>();
        void Add(string name, string file)
        {
            var text = ReadTail(file, lines);
            if (!string.IsNullOrWhiteSpace(text)) sections.Add(new ReportSection(name, text));
        }
        Add("launcher", Log.FilePath);
        if (includeClient)
            foreach (var name in new[] { "H1Z1.log", "H1Z1 PlayClient (Live).log", "Login.log", "NetInfo.log" })
                Add(name, Path.Combine(GameFiles.LogsDir, name));
        return new ReportSubmission(false, Config.Version, version ?? "", errorCode ?? "", sections);
    }

    public static string Preview(ReportSubmission report) => string.Join("\n\n", report.Sections.Select(s => $"--- {s.Name} ---\n{s.Content}"));
}
