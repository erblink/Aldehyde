using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Aldehyde.Data;

public class Embedder(AppDbContext db)
{
    public static string BuildText(Perfume p) =>
        $"{p.House} {p.Name}. Accords: {string.Join(", ", p.Accords)}. Top: {string.Join(", ", p.TopNotes)}. Heart: {string.Join(", ", p.HeartNotes)}. Base: {string.Join(", ", p.BaseNotes)}.";

    // Resumable: only rows without embedding, saved per batch, stops at neuron cap.
    public async Task<string> EmbedPendingAsync(HttpClient http, string accountId, string token, string model, int neuronCap = 8000)
    {
        var pending = await db.Perfumes.Where(p => p.EmbeddingJson == null).ToListAsync();
        double used = 0; var done = 0;
        foreach (var batch in pending.Chunk(100))
        {
            var texts = batch.Select(BuildText).ToArray();
            var est = texts.Sum(t => t.Length) / 4.0 * 0.006058; // shortcut: bge-base rate, reprice before qwen3 run
            if (used + est > neuronCap) return $"Stopped at cap. Done {done}, missing {pending.Count - done}. Run again after 00:00 UTC.";
            using var req = new HttpRequestMessage(HttpMethod.Post, $"https://api.cloudflare.com/client/v4/accounts/{accountId}/ai/run/{model}");
            req.Headers.Authorization = new("Bearer", token);
            req.Content = JsonContent.Create(new { text = texts });
            using var res = await http.SendAsync(req);
            if ((int)res.StatusCode == 429) return $"Quota hit. Done {done}, missing {pending.Count - done}. Run again after 00:00 UTC.";
            // Other 4xx (bad token, over the daily limit): stop cleanly, finished batches are already saved.
            if ((int)res.StatusCode is >= 400 and < 500) return $"Workers AI returned {(int)res.StatusCode}. Done {done}, missing {pending.Count - done}.";
            res.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var data = doc.RootElement.GetProperty("result").GetProperty("data");
            for (var i = 0; i < batch.Length; i++)
                batch[i].EmbeddingJson = data[i].GetRawText();
            await db.SaveChangesAsync();
            used += est; done += batch.Length;
        }
        return $"Embedded {done}. Missing 0.";
    }
}
