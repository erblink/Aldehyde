using System.Net.Http.Json;
using System.Text.Json;
using Aldehyde.Data;

namespace Aldehyde.Api;

public interface IQueryEmbedder
{
    Task<float[]?> EmbedAsync(string text, CancellationToken ct = default);
}

public sealed class CloudflareQueryEmbedder(IConfiguration cfg, HttpClient http) : IQueryEmbedder
{
    public async Task<float[]?> EmbedAsync(string text, CancellationToken ct = default)
    {
        var account = cfg["WorkersAi:AccountId"];
        var token = cfg["WorkersAi:ApiToken"];
        if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(token)) return null;
        var model = cfg["WorkersAi:Model"] ?? "@cf/qwen/qwen3-embedding-0.6b";
        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.cloudflare.com/client/v4/accounts/{account}/ai/run/{model}");
        req.Headers.Authorization = new("Bearer", token);
        req.Content = JsonContent.Create(new { text = new[] { text } });
        using var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        return JsonSerializer.Deserialize<float[]>(doc.RootElement.GetProperty("result").GetProperty("data")[0].GetRawText());
    }
}

public static class SuggestionRanker
{
    public static List<Perfume> Top(float[] query, IReadOnlyList<VectorEntry> entries, int count)
    {
        var q = Normalise(query);
        var best = new List<(double Score, Perfume Perfume)>(count + 1);
        foreach (var e in entries)
        {
            if (e.Unit.Length != q.Length) continue;
            double s = 0;
            for (var i = 0; i < q.Length; i++) s += q[i] * e.Unit[i];
            var at = best.FindIndex(b => s > b.Score);
            if (at < 0 && best.Count == count) continue;
            best.Insert(at < 0 ? best.Count : at, (s, e.Perfume));
            if (best.Count > count) best.RemoveAt(count);
        }
        return best.Select(b => b.Perfume).ToList();
    }

    internal static float[] Normalise(float[] v)
    {
        double norm = 0;
        foreach (var x in v) norm += x * x;
        norm = Math.Sqrt(norm);
        return norm == 0 ? v : v.Select(x => (float)(x / norm)).ToArray();
    }
}
