using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Aldehyde.Data;

public class MapLayout(AppDbContext db, string? cachePath = null)
{
    public const int NeighbourCount = 5;

    record Layout(int[] Ids, double[] X, double[] Y, int[][] Nearest);

    public async Task<string> ComputeAsync()
    {
        var ids = await db.Perfumes.AsNoTracking().OrderBy(p => p.Id).Select(p => p.Id).ToArrayAsync();
        if (ids.Length < 2) return "Need at least 2 perfumes. Coordinates not computed.";

        var layout = cachePath is not null && File.Exists(cachePath)
            ? JsonSerializer.Deserialize<Layout>(await File.ReadAllTextAsync(cachePath))
            : null;
        if (layout is null || !layout.Ids.SequenceEqual(ids))
        {
            var rows = await db.Perfumes.AsNoTracking().OrderBy(p => p.Id).Select(p => p.EmbeddingJson).ToListAsync();
            if (rows.Count != ids.Length || rows.Any(r => r is null)) return "Not all perfumes embedded. Coordinates not computed.";
            var unit = rows.Select(r => Normalise(JsonSerializer.Deserialize<float[]>(r!)!)).ToArray();
            rows.Clear();
            var (xs, ys) = Umap2(unit);
            layout = new Layout(ids, Scale(xs), Scale(ys), Nearest(unit, NeighbourCount));
            if (cachePath is not null) await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(layout));
        }

        Npgsql.NpgsqlConnection.ClearAllPools();
        db.ChangeTracker.Clear();
        var (nx, ny, nearest) = (layout.X, layout.Y, layout.Nearest);
        foreach (var chunk in Enumerable.Range(0, ids.Length).Chunk(500))
        {
            foreach (var i in chunk)
            {
                var p = new Perfume { Id = ids[i], X = nx[i], Y = ny[i], NeighbourIds = nearest[i].Select(j => ids[j]).ToList() };
                var entry = db.Attach(p);
                entry.Property(x => x.X).IsModified = true;
                entry.Property(x => x.Y).IsModified = true;
                entry.Property(x => x.NeighbourIds).IsModified = true;
            }
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }
        if (cachePath is not null) File.Delete(cachePath);
        return $"Coordinates set for {ids.Length}.";
    }

    static (double[] Xs, double[] Ys) Umap2(float[][] vecs)
    {
        var umap = new UMAP.Umap(random: new SeededRandom(42), numberOfNeighbors: Math.Min(15, vecs.Length - 1));
        var epochs = umap.InitializeFit(vecs);
        for (var i = 0; i < epochs; i++) umap.Step();
        var embedding = umap.GetEmbedding();
        return (embedding.Select(p => (double)p[0]).ToArray(), embedding.Select(p => (double)p[1]).ToArray());
    }

    static int[][] Nearest(float[][] unit, int k)
    {
        var result = new int[unit.Length][];
        Parallel.For(0, unit.Length, i =>
        {
            var best = new List<(double Score, int Index)>(k + 1);
            for (var j = 0; j < unit.Length; j++)
            {
                if (j == i) continue;
                var s = Dot(unit[i], unit[j]);
                if (best.Count == k && s <= best[^1].Score) continue;
                var at = best.FindIndex(b => s > b.Score);
                best.Insert(at < 0 ? best.Count : at, (s, j));
                if (best.Count > k) best.RemoveAt(k);
            }
            result[i] = best.Select(b => b.Index).ToArray();
        });
        return result;
    }

    public static float[] Normalise(float[] v)
    {
        var norm = Math.Sqrt(Dot(v, v));
        return norm == 0 ? v : v.Select(x => (float)(x / norm)).ToArray();
    }

    static double[] Scale(double[] v)
    {
        var (min, max) = (v.Min(), v.Max());
        return v.Select(x => max == min ? 500 : (x - min) / (max - min) * 1000).ToArray();
    }

    public static double Dot(float[] a, float[] b) { double s = 0; for (var i = 0; i < a.Length; i++) s += a[i] * b[i]; return s; }

    sealed class SeededRandom(int seed) : UMAP.IProvideRandomValues
    {
        readonly Random rnd = new(seed);
        public bool IsThreadSafe => false;
        public int Next(int minValue, int maxValue) => rnd.Next(minValue, maxValue);
        public float NextFloat() => rnd.NextSingle();
        public void NextFloats(Span<float> buffer) { for (var i = 0; i < buffer.Length; i++) buffer[i] = rnd.NextSingle(); }
    }
}
