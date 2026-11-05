using System.Threading.RateLimiting;
using Aldehyde.Data;

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

    static async Task<IResult> GetDetail(int id, PerfumeCache cache)
    {
        var byId = await cache.ByIdAsync();
        if (!byId.TryGetValue(id, out var p)) return Results.NotFound();
        var dupeLinks = (await cache.DupesAsync())[id];

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
        var phrase = PerfumeSearch.Fold(q ?? "");
        if (phrase.Length == 0) return Results.BadRequest();
        var hits = PerfumeSearch.Find(phrase, await cache.IndexAsync(), 30);
        return Results.Ok(hits.Select(e => new SearchResult(e.Perfume.Id, e.Perfume.Name, e.Perfume.House)).ToList());
    }

    static async Task<IResult> Suggest(SuggestRequest? req, PerfumeCache cache, IQueryEmbedder embedder,
        Turnstile turnstile, RateLimiter limiter, CancellationToken ct)
    {
        var text = req?.Text?.Trim() ?? "";
        if (text.Length is < 3 or > 500) return Results.BadRequest();
        if (!await turnstile.PassesAsync(req?.Token, ct)) return Results.StatusCode(StatusCodes.Status403Forbidden);
        var vectors = await cache.VectorsAsync();
        var named = PerfumeSearch.NamedIn(text, await cache.IndexAsync());
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

    static List<string> SharedNotes(Perfume a, Perfume b) =>
        a.AllNotes.Intersect(b.AllNotes, StringComparer.OrdinalIgnoreCase).ToList();
}
