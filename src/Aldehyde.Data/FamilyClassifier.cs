namespace Aldehyde.Data;

public static class FamilyClassifier
{
    public static readonly Dictionary<string, string[]> ByFamily = new()
    {
        ["citrus"] = ["citrus", "fruity", "tropical"],
        ["green"] = ["green", "aromatic", "herbal", "lavender", "fresh spicy", "conifer", "tea"],
        ["aquatic"] = ["aquatic", "marine", "ozonic", "salty", "mineral"],
        ["floral"] = ["floral", "white floral", "yellow floral", "rose", "iris", "violet", "tuberose"],
        ["woody"] = ["woody", "oud", "earthy", "patchouli", "mossy", "leather", "smoky"],
        ["amber"] = ["amber", "warm spicy", "soft spicy", "vanilla", "sweet", "balsamic", "honey", "animalic", "tobacco", "caramel", "cacao", "coffee", "almond", "coconut", "lactonic", "cinnamon", "rum", "whiskey", "cherry"],
    };
    static readonly Dictionary<string, string> Lookup =
        ByFamily.SelectMany(kv => kv.Value.Select(a => (a, kv.Key))).ToDictionary(x => x.a, x => x.Key);

    public static string Classify(IReadOnlyList<string> accords)
    {
        var scores = new Dictionary<string, (int Score, int BestRank)>();
        for (var i = 0; i < Math.Min(5, accords.Count); i++)
        {
            if (!Lookup.TryGetValue(accords[i].Trim().ToLowerInvariant(), out var fam)) continue;
            var w = 5 - i;
            scores[fam] = scores.TryGetValue(fam, out var s) ? (s.Score + w, Math.Min(s.BestRank, i)) : (w, i);
        }
        if (scores.Count == 0) return "woody";
        return scores.OrderByDescending(kv => kv.Value.Score).ThenBy(kv => kv.Value.BestRank).First().Key;
    }

    public static bool IsMapped(string accord) => Lookup.ContainsKey(accord.Trim().ToLowerInvariant());
}
