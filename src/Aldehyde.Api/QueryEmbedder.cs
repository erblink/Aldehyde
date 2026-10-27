using System.Text.Json;
using Aldehyde.Data;

namespace Aldehyde.Api;

public interface IQueryEmbedder
{
    Task<float[]?> EmbedAsync(string text, CancellationToken ct = default);
}

public sealed class CloudflareQueryEmbedder(IConfiguration cfg, HttpClient http) : IQueryEmbedder
{
    public const string Instruction = "Given a description of a mood, place or moment, retrieve perfumes whose notes and accords evoke it";

    public async Task<float[]?> EmbedAsync(string text, CancellationToken ct = default)
    {
        var account = cfg["WorkersAi:AccountId"];
        var token = cfg["WorkersAi:ApiToken"];
        var model = cfg["WorkersAi:Model"];
        if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(model)) return null;
        try
        {
            var input = new { queries = new[] { text }, instruction = Instruction };
            return (await Embedder.RunAsync(http, account, token, model, input, ct)).Vectors?.FirstOrDefault();
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException
            || e is TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }
}

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
