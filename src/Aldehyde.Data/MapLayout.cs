using Microsoft.EntityFrameworkCore;

namespace Aldehyde.Data;

// Runs once every perfume has a vector: places each point (x, y in 0–1000) and stores its nearest neighbours.
public class MapLayout(AppDbContext db)
{
    public const int NeighbourCount = 5;

    public async Task<string> ComputeAsync()
    {
        var all = await db.Perfumes.OrderBy(p => p.Id).ToListAsync();
        if (all.Count < 2) return "Need at least 2 perfumes. Coordinates not computed.";
        var vecs = all.Select(p => p.Embedding).ToArray();
        if (vecs.Any(v => v is null)) return "Not all perfumes embedded. Coordinates not computed.";

        var unit = vecs.Select(v => Normalise(v!)).ToArray();
        var (xs, ys) = Umap2(unit);
        var nx = Scale(xs); var ny = Scale(ys);
        var nearest = Nearest(unit, NeighbourCount);
        for (var i = 0; i < all.Count; i++)
        {
            all[i].X = nx[i]; all[i].Y = ny[i];
            all[i].NeighbourIds = nearest[i].Select(j => all[j].Id).ToList();
        }
        await db.SaveChangesAsync();
        return $"Coordinates set for {all.Count}.";
    }

    // UMAP (UMAP-Sharp, pure C#): cosine distance on unit vectors, 15 neighbours or fewer on tiny sets.
    static (double[] Xs, double[] Ys) Umap2(float[][] vecs)
    {
        var umap = new UMAP.Umap(random: new SeededRandom(42), numberOfNeighbors: Math.Min(15, vecs.Length - 1));
        var epochs = umap.InitializeFit(vecs);
        for (var i = 0; i < epochs; i++) umap.Step();
        var embedding = umap.GetEmbedding();
        return (embedding.Select(p => (double)p[0]).ToArray(), embedding.Select(p => (double)p[1]).ToArray());
    }

    // Brute force over unit vectors, so dot product = cosine similarity. 5k perfumes take seconds.
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

    static float[] Normalise(float[] v)
    {
        var norm = Math.Sqrt(Dot(v, v));
        return norm == 0 ? v : v.Select(x => (float)(x / norm)).ToArray();
    }

    static double[] Scale(double[] v)
    {
        var (min, max) = (v.Min(), v.Max());
        return v.Select(x => max == min ? 500 : (x - min) / (max - min) * 1000).ToArray();
    }

    static double Dot(double[] a, double[] b) { double s = 0; for (var i = 0; i < a.Length; i++) s += a[i] * b[i]; return s; }
    static double Dot(float[] a, float[] b) { double s = 0; for (var i = 0; i < a.Length; i++) s += a[i] * b[i]; return s; }

    // UMAP-Sharp has no seed option. A fixed seed, single-threaded, keeps points in place between runs.
    sealed class SeededRandom(int seed) : UMAP.IProvideRandomValues
    {
        readonly Random rnd = new(seed);
        public bool IsThreadSafe => false;
        public int Next(int minValue, int maxValue) => rnd.Next(minValue, maxValue);
        public float NextFloat() => rnd.NextSingle();
        public void NextFloats(Span<float> buffer) { for (var i = 0; i < buffer.Length; i++) buffer[i] = rnd.NextSingle(); }
    }
}
