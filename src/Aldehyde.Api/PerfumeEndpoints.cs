using System.Threading.RateLimiting;
using Aldehyde.Data;
using Microsoft.EntityFrameworkCore;

namespace Aldehyde.Api;

static class PerfumeEndpoints
{
    public static void MapPerfumeEndpoints(this WebApplication app)
    {
        app.MapGet("/api/perfumes", GetAll);
        app.MapGet("/api/perfumes/{id:int}", GetDetail);
        app.MapGet("/api/search", Search);
        app.MapPost("/api/suggest", Suggest);
    }

    static async Task<List<MapPoint>> GetAll(PerfumeCache cache, HttpContext ctx)
    {
        ctx.Response.Headers.CacheControl = "public, max-age=300";
        return (await cache.AllAsync()).Select(p => new MapPoint(p.Id, p.Name, p.House, p.Family, Math.Round(p.X, 1), Math.Round(p.Y, 1))).ToList();
    }

    static async Task<IResult> GetDetail(int id, AppDbContext db)
    {
        var p = await WithoutEmbedding(db).FirstOrDefaultAsync(x => x.Id == id);
        if (p is null) return Results.NotFound();
        var dupeLinks = await db.DupeLinks.AsNoTracking().Where(d => d.OriginalId == id).ToListAsync();
        var ids = p.NeighbourIds.Concat(dupeLinks.Select(d => d.DupeId)).Distinct().ToList();
        var byId = await WithoutEmbedding(db).Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);

        var neighbours = p.NeighbourIds.Where(byId.ContainsKey)
            .Select(n => new Neighbour(n, byId[n].Name, SharedNotes(p, byId[n]))).ToList();
        var dupes = dupeLinks.Where(d => byId.ContainsKey(d.DupeId))
            .OrderBy(d => d.Source switch { "house" => 0, "community" => 1, _ => 2 }).ThenByDescending(d => d.Votes)
            .Select(d => new Dupe(d.DupeId, byId[d.DupeId].Name, d.Source, d.Votes, SharedNotes(p, byId[d.DupeId]))).ToList();
        return Results.Ok(new PerfumeDetail(p.Id, p.Name, p.House, p.Year, p.Strength, p.Perfumers,
            new Notes(p.TopNotes, p.HeartNotes, p.BaseNotes), p.Accords, p.Family, neighbours, dupes));
    }

    static async Task<IResult> Search(string? q, PerfumeCache cache)
    {
        var phrase = PerfumeCache.Fold(q ?? "");
        if (phrase.Length == 0) return Results.BadRequest();
        var whole = $" {phrase} ";
        var start = $" {phrase}";
        var wholeTerms = phrase.Split(' ').Select(t => $" {t} ").ToArray();
        var startTerms = phrase.Split(' ').Select(t => $" {t}").ToArray();
        var index = await cache.IndexAsync();
        var hits = index.Select(e => (Entry: e, Rank: Rank(e, whole, start, wholeTerms, startTerms))).Where(h => h.Rank >= 0)
            .OrderBy(h => h.Rank).Take(30)
            .Select(h => new SearchResult(h.Entry.Perfume.Id, h.Entry.Perfume.Name, h.Entry.Perfume.House)).ToList();
        return Results.Ok(hits);
    }

    static async Task<IResult> Suggest(SuggestRequest? req, PerfumeCache cache, IQueryEmbedder embedder,
        Turnstile turnstile, RateLimiter limiter, CancellationToken ct)
    {
        var text = req?.Text?.Trim() ?? "";
        if (text.Length is < 3 or > 500) return Results.BadRequest();
        if (!await turnstile.PassesAsync(req?.Token, ct)) return Results.StatusCode(StatusCodes.Status403Forbidden);
        var vectors = await cache.VectorsAsync();
        var named = NamedIn(text, await cache.IndexAsync());
        var besides = named is null ? null : vectors.FirstOrDefault(v => v.Perfume.Id == named.Perfume.Id);
        var query = besides?.Unit;
        if (query is null)
        {
            using var lease = limiter.AttemptAcquire();
            if (!lease.IsAcquired) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            query = await embedder.EmbedAsync(text, ct);
            if (query is null || query.Length == 0) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        var top = SuggestionRanker.Top(query, vectors, 3, besides);
        return Results.Ok(top.Select(p => new Suggestion(p.Id, p.Name, p.House, p.Accords.Take(5).ToList())).ToList());
    }

    static readonly HashSet<string> Filler =
        ["something", "anything", "like", "similar", "to", "smells", "smelling", "a", "an", "the",
         "perfume", "fragrance", "scent", "but", "cheaper", "dupe", "alternative", "of"];

    static SearchEntry? NamedIn(string text, List<SearchEntry> index)
    {
        var folded = PerfumeCache.Fold(text);
        foreach (var phrase in new[] { folded, string.Join(' ', folded.Split(' ').Where(w => !Filler.Contains(w))) })
        {
            if (phrase.Length == 0) continue;
            var whole = $" {phrase} ";
            var words = phrase.Split(' ').Select(w => $" {w} ").ToArray();
            var hit = index.FirstOrDefault(e => e.Name == whole)
                ?? index.FirstOrDefault(e => e.NameAndHouse.Length == whole.Length && HasAll(e.NameAndHouse, words));
            if (hit is not null) return hit;
        }
        return null;
    }

    static int Rank(SearchEntry e, string whole, string start, string[] wholeTerms, string[] startTerms) =>
        e.Name == whole ? 0
        : e.Name.Contains(whole, StringComparison.Ordinal) ? 1
        : HasAll(e.NameAndHouse, wholeTerms) ? 2
        : e.Name.Contains(start, StringComparison.Ordinal) ? 3
        : HasAll(e.NameAndHouse, startTerms) ? 4
        : HasAll(e.Everything, wholeTerms) ? 5
        : HasAll(e.Everything, startTerms) ? 6
        : -1;

    static bool HasAll(string text, string[] words)
    {
        foreach (var w in words)
            if (!text.Contains(w, StringComparison.Ordinal)) return false;
        return true;
    }

    internal static IQueryable<Perfume> WithoutEmbedding(AppDbContext db) =>
        db.Perfumes.AsNoTracking().Select(p => new Perfume
        {
            Id = p.Id, Name = p.Name, House = p.House, Year = p.Year, Strength = p.Strength, Ratings = p.Ratings,
            Perfumers = p.Perfumers, TopNotes = p.TopNotes, HeartNotes = p.HeartNotes, BaseNotes = p.BaseNotes,
            Accords = p.Accords, Family = p.Family, X = p.X, Y = p.Y, NeighbourIds = p.NeighbourIds,
        });

    static List<string> SharedNotes(Perfume a, Perfume b) =>
        a.AllNotes.Intersect(b.AllNotes, StringComparer.OrdinalIgnoreCase).ToList();
}
