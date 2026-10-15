using System.Globalization;
using System.Text.RegularExpressions;

namespace COTK.Next.Core;

/// <summary>Localizes application-owned labels only. French source labels remain the stable catalogue keys;
/// user names, server announcements and log excerpts are explicitly displayed as raw content.</summary>
internal static partial class L
{
    private sealed record Entry(string Fr, string En, string Zh, Regex? Pattern, string Prefix);
    private static readonly Dictionary<string, Entry> Exact = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<Entry> Templates = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);
    private static readonly Regex Slot = new(@"\{(\d+)\}", RegexOptions.CultureInvariant);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.CultureInvariant);
    private static readonly Regex SizeValue = new(@"^([0-9 ,\.]+) (Go|Mo|Ko|o)(/s)?$", RegexOptions.CultureInvariant);
    private static readonly Regex SpeedAndEta = new(@"^([0-9 ,\.]+ (?:Go|Mo|Ko|o)/s) · (.+)$", RegexOptions.CultureInvariant);
    private static string _language = "en";

    public static event Action? Changed;
    public static string Language => _language;
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(_language == "fr" ? "fr-FR" : _language == "zh-CN" ? "zh-CN" : "en-US");
    public static string NormalizeLanguage(string? value) => value is "fr" ? "fr" : value is "zh" or "zh-CN" or "zh-Hans" ? "zh-CN" : "en";

    static L()
    {
        foreach (var line in Catalogue.Split('\n'))
        {
            var cells = line.Trim().Split('|');
            if (cells.Length != 3) continue;
            var fr = Normalize(cells[0]);
            Regex? pattern = null;
            var prefix = fr;
            if (Slot.IsMatch(fr))
            {
                var regex = "^"; var pos = 0;
                foreach (Match match in Slot.Matches(fr))
                {
                    regex += Regex.Escape(fr[pos..match.Index]) + "(?<v" + match.Groups[1].Value + ">.*?)";
                    pos = match.Index + match.Length;
                }
                regex += Regex.Escape(fr[pos..]) + "$";
                pattern = new Regex(regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(50));
                prefix = fr[..fr.IndexOf('{')];
            }
            var entry = new Entry(fr, cells[1], cells[2], pattern, prefix);
            if (pattern is null) Exact[fr] = entry; else Templates.Add(entry);
        }
        // Prefer specific patterns over short/general prefixes.
        Templates.Sort((a,b) => b.Prefix.Length.CompareTo(a.Prefix.Length));
    }

    public static void Initialize(string? overrideLanguage = null)
    {
        _language = NormalizeLanguage(overrideLanguage ?? Settings.Language);
        Cache.Clear();
        CultureInfo.CurrentUICulture = Culture;
    }

    public static void Select(string language)
    {
        var selected = NormalizeLanguage(language);
        if (selected == _language)
        {
            if (Settings.Language != selected) Settings.Language=selected;
            return;
        }
        Settings.Language = selected;
        _language = selected;
        CultureInfo.CurrentUICulture = Culture;
        Cache.Clear();
        Changed?.Invoke();
    }

    private static string Normalize(string value) => Spaces.Replace(value.Replace("\u200A", ""), " ").Trim();

    public static string T(string? source)
    {
        if (source is null) return "";
        if (_language == "fr" || source.Length == 0 || !source.Any(char.IsLetter)) return source;
        if (Cache.TryGetValue(source,out var cached)) return cached;
        if (source.Contains('\n')) return string.Join('\n', source.Split('\n').Select(T));
        if (source.StartsWith("[E-", StringComparison.Ordinal) && source.IndexOf(']') is var end && end > 0)
            return source[..(end+1)] + " " + T(source[(end+1)..].Trim());
        if (source.StartsWith("→ ", StringComparison.Ordinal)) return "→ " + T(source[2..]);
        var normalized = Normalize(source);
        var result = Translate(normalized);
        if (result == normalized && source.Contains(" — ", StringComparison.Ordinal))
            result = string.Join(" — ", source.Split(" — ").Select(T));
        if (result == normalized) result = source;
        else
        {
            if (source == source.ToUpperInvariant()) result = result.ToUpperInvariant();
            if (source.Contains('\u200A') && _language != "zh-CN") result = string.Join('\u200A', result.ToUpperInvariant().Select(c=>c.ToString()));
            if (source.StartsWith(' ')) result = " " + result;
            if (source.EndsWith(' ')) result += " ";
        }
        if (Cache.Count > 1024) Cache.Clear();
        Cache[source] = result;
        return result;
    }

    private static string Translate(string text)
    {
        if (Exact.TryGetValue(text, out var entry)) return _language == "zh-CN" ? entry.Zh : entry.En;
        var speed = SpeedAndEta.Match(text);
        if (speed.Success) return T(speed.Groups[1].Value) + " · " + T(speed.Groups[2].Value);
        var unit = SizeValue.Match(text);
        if (unit.Success)
        {
            var suffix = unit.Groups[2].Value switch { "Go"=>"GB", "Mo"=>"MB", "Ko"=>"KB", _ => _language=="zh-CN" ? "字节" : "B" };
            return unit.Groups[1].Value.Replace(',', '.') + " " + suffix + unit.Groups[3].Value;
        }
        foreach (var candidate in Templates)
        {
            if (!text.StartsWith(candidate.Prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var match = candidate.Pattern!.Match(text);
            if (!match.Success) continue;
            var output = _language == "zh-CN" ? candidate.Zh : candidate.En;
            return Slot.Replace(output, slot =>
            {
                var value = match.Groups["v" + slot.Groups[1].Value].Value;
                var size = SizeValue.Match(value);
                if (size.Success) return Translate(value);
                if (candidate.Fr.StartsWith("Le client a planté",StringComparison.Ordinal) && value.TrimStart().StartsWith("(code ",StringComparison.Ordinal)) return T(value);
                if ((candidate.Fr.StartsWith("Prochain palier") || candidate.Fr.Contains(" pour ")) &&
                    Exact.TryGetValue(value,out var tier)) return _language == "zh-CN" ? tier.Zh : tier.En;
                if (candidate.Fr.StartsWith("Launcher ",StringComparison.Ordinal) && Exact.TryGetValue(value,out var metadata))
                    return _language=="zh-CN" ? metadata.Zh : metadata.En;
                return value;
            });
        }
        return text;
    }

    public static string F(string frenchTemplate, params object?[] args) => T(string.Format(CultureInfo.GetCultureInfo("fr-FR"), frenchTemplate, args));
    internal static int EntryCount => Exact.Count + Templates.Count;
}
