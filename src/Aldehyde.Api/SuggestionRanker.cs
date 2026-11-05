using Aldehyde.Data;

namespace Aldehyde.Api;

public static class SuggestionRanker
{
    public const double SameScent = 0.99;

    public static List<Perfume> Top(float[] query, IReadOnlyList<VectorEntry> entries, int count, VectorEntry? besides = null)
    {
        var q = MapLayout.Normalise(query);
        var pool = count * 4;
        var best = new List<(double Score, VectorEntry Entry)>(pool + 1);
        foreach (var e in entries)
        {
            if (e.Unit.Length != q.Length) continue;
            var s = MapLayout.Dot(q, e.Unit);
            var at = best.FindIndex(b => s > b.Score);
            if (at < 0 && best.Count == pool) continue;
            best.Insert(at < 0 ? best.Count : at, (s, e));
            if (best.Count > pool) best.RemoveAt(pool);
        }
        List<VectorEntry> seen = besides is null ? [] : [besides];
        var top = new List<Perfume>(count);
        foreach (var (_, e) in best)
        {
            if (top.Count == count) break;
            if (seen.Any(p => MapLayout.Dot(p.Unit, e.Unit) >= SameScent)) continue;
            seen.Add(e);
            top.Add(e.Perfume);
        }
        return top;
    }
}
