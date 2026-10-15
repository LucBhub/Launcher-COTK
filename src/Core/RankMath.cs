namespace COTK.Next.Core;

/// <summary>Calculs d'affichage du classement (purs, testés).</summary>
internal static class RankMath
{
    /// <summary>Position (0…1) sur la piste Bronze → Royauté. Les 6 paliers sont répartis à égale distance :
    /// le remplissage atteint exactement un écusson quand le score atteint le seuil de ce palier.</summary>
    public static double TrackFraction(long score, int rankedMatches, RankThresholds t)
    {
        if (rankedMatches < t.MinMatch) return 0;
        long[] lines = [0, t.Silver, t.Gold, t.Platinum, t.Diamond, t.Royalty];
        if (score >= t.Royalty) return 1;
        for (var k = 0; k < 5; k++)
        {
            if (score >= lines[k + 1]) continue;
            var span = Math.Max(1, lines[k + 1] - lines[k]);
            var within = Math.Clamp((double)(score - lines[k]) / span, 0, 1);
            return (k + within) / 5.0;
        }
        return 1;
    }

    /// <summary>Palier suivant (null si Royauté atteinte ou joueur non classé) avec le score qui manque.</summary>
    public static (int Tier, string Name, long Missing)? Next(long score, int tier, RankThresholds t)
    {
        if (tier < 1 || tier >= 6) return null;
        var next = tier + 1;
        return (next, Tiers.Display(next), Math.Max(0, t.LineFor(next) - score));
    }
}
