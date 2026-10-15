namespace COTK.Next.Core;

internal sealed record Account(string Username, string Role, DateTimeOffset ExpiresAt)
{
    public bool IsAdmin => string.Equals(Role, "admin", StringComparison.Ordinal);

    /// <summary>Staff = admin ou modérateur : console F8, jamais de session persistée.</summary>
    public bool IsStaff => IsAdmin || string.Equals(Role, "moderator", StringComparison.Ordinal);
}

internal sealed record Session(string AccessToken, Account Account);

internal sealed record GameTicket(string Value, DateTimeOffset ExpiresAt);

internal sealed record PatchNote(string Title, string Body, DateTimeOffset PublishedAt)
{
    /// <summary>Première ligne utile (sans puces/emoji de tête) pour les listes.</summary>
    public string Summary
    {
        get
        {
            foreach (var raw in Body.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length > 3) return line.Length > 140 ? line[..137] + "…" : line;
            }
            return string.Empty;
        }
    }
}

internal sealed record DeviceStart(string DeviceCode, string UserCode, string VerificationUri, int ExpiresIn, int Interval);

internal enum DevicePollStatus { Pending, Approved }

internal sealed record DevicePollResult(DevicePollStatus Status, string? AccessToken = null, string? Username = null, DateTimeOffset? ExpiresAt = null);

// ---------------------------------------------------------------- classement / profil

internal sealed record RankMatch(DateTimeOffset EndedAt, int Score, int Placement, int Kills);

internal sealed record RankingSummary(
    long Top10Total,
    int Tier,
    string TierName,
    int RankedMatches,
    DateTimeOffset? LastMatch,
    List<RankMatch> Recent)
{
    public static RankingSummary Empty { get; } = new(0, 0, "Unranked", 0, null, new List<RankMatch>());

    public int Wins => Recent.Count(m => m.Placement == 1);
    public int BestPlacement => Recent.Count == 0 ? 0 : Recent.Min(m => m.Placement);
    public int Kills => Recent.Sum(m => m.Kills);
}

internal sealed record RankThresholds(long Silver, long Gold, long Platinum, long Diamond, long Royalty, long MinMatch)
{
    /// <summary>Valeurs du serveur (ranking.go) — repli si l'API ne les fournit pas.</summary>
    public static RankThresholds Default { get; } = new(450_000, 700_000, 1_125_000, 1_500_000, 2_065_000, 10);

    /// <summary>Score requis pour atteindre le palier <paramref name="tier"/> (1 = Bronze… 6 = Royalty).</summary>
    public long LineFor(int tier) => tier switch
    {
        <= 1 => 0,
        2 => Silver,
        3 => Gold,
        4 => Platinum,
        5 => Diamond,
        _ => Royalty,
    };
}

internal sealed record Profile(string Username, string Role, string Season, RankingSummary Ranking, RankThresholds Thresholds);

internal sealed record LeaderEntry(
    int Rank,
    string AccountId,
    string Username,
    string CharacterName,
    long Top10Total,
    int Tier,
    string TierName,
    int RankedMatches,
    DateTimeOffset? LastMatch);

internal sealed record Leaderboard(List<LeaderEntry> Entries, string Season);

internal static class Tiers
{
    public static readonly string[] Names = ["Non classé", "Bronze", "Argent", "Or", "Platine", "Diamant", "Royauté"];

    public static string Display(int tier) => tier is >= 0 and <= 6 ? Names[tier] : Names[0];
}
