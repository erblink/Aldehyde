using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Aldehyde.Data;

public class Embedder(AppDbContext db)
{
    public static string BuildText(Perfume p) =>
        $"Accords: {string.Join(", ", p.Accords)}. Top: {string.Join(", ", p.TopNotes)}. Heart: {string.Join(", ", p.HeartNotes)}. Base: {string.Join(", ", p.BaseNotes)}.";

    public async Task<string> EmbedPendingAsync(HttpClient http, string accountId, string token, string model, int neuronCap)
    {
        var pending = await db.Perfumes.Where(p => p.EmbeddingJson == null).ToListAsync();
        double used = 0; var done = 0;
        foreach (var batch in pending.Chunk(100))
        {
            var texts = batch.Select(BuildText).ToArray();
            var est = texts.Sum(t => t.Length) / 4.0 * 0.001075;
            if (used + est > neuronCap) return $"Stopped at cap. Done {done}, missing {pending.Count - done}. Run again after 00:00 UTC.";
            var (status, vectors) = await RunAsync(http, accountId, token, model, new { text = texts });
            if (status == 429) return $"Quota hit. Done {done}, missing {pending.Count - done}. Run again after 00:00 UTC.";
            if (vectors is null) return $"Workers AI returned {status}. Done {done}, missing {pending.Count - done}.";
            for (var i = 0; i < batch.Length; i++)
                batch[i].EmbeddingJson = JsonSerializer.Serialize(vectors[i]);
            await db.SaveChangesAsync();
            used += est; done += batch.Length;
        }
        return $"Embedded {done}. Missing 0.";
    }

    public static async Task<(int Status, float[][]? Vectors)> RunAsync(HttpClient http, string accountId, string token, string model, object input, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://api.cloudflare.com/client/v4/accounts/{accountId}/ai/run/{model}");
        req.Headers.Authorization = new("Bearer", token);
        req.Content = JsonContent.Create(input);
        using var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return ((int)res.StatusCode, null);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        return ((int)res.StatusCode, doc.RootElement.GetProperty("result").GetProperty("data").Deserialize<float[][]>());
    }
}
