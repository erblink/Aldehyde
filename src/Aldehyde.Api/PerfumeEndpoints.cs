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
        var terms = phrase.Split(' ');
        var index = await cache.IndexAsync();
        var hits = index.Select(e => (Entry: e, Rank: Rank(e, phrase, terms))).Where(h => h.Rank >= 0)
            .OrderBy(h => h.Rank).ThenBy(h => h.Entry.Perfume.Id).Take(30)
            .Select(h => new SearchResult(h.Entry.Perfume.Id, h.Entry.Perfume.Name, h.Entry.Perfume.House, h.Entry.Perfume.X, h.Entry.Perfume.Y)).ToList();
        return Results.Ok(hits);
    }

    static int Rank(SearchEntry e, string phrase, string[] terms) =>
        e.Name == phrase ? 0
        : HasWord(e.Name, phrase, whole: true) ? 1
        : terms.All(t => HasWord(e.NameAndHouse, t, whole: true)) ? 2
        : HasWord(e.Name, phrase, whole: false) ? 3
        : terms.All(t => HasWord(e.NameAndHouse, t, whole: false)) ? 4
        : terms.All(t => HasWord(e.Everything, t, whole: true)) ? 5
        : terms.All(t => HasWord(e.Everything, t, whole: false)) ? 6
        : -1;

    static bool HasWord(string text, string word, bool whole) =>
        $" {text}{(whole ? " " : "")}".Contains($" {word}{(whole ? " " : "")}", StringComparison.Ordinal);

    internal static IQueryable<Perfume> WithoutEmbedding(AppDbContext db) =>
        db.Perfumes.AsNoTracking().Select(p => new Perfume
        {
            Id = p.Id, Name = p.Name, House = p.House, Year = p.Year, Strength = p.Strength,
            Perfumers = p.Perfumers, TopNotes = p.TopNotes, HeartNotes = p.HeartNotes, BaseNotes = p.BaseNotes,
            Accords = p.Accords, Family = p.Family, X = p.X, Y = p.Y, NeighbourIds = p.NeighbourIds,
        });

    static List<string> SharedNotes(Perfume a, Perfume b) =>
        a.AllNotes.Intersect(b.AllNotes, StringComparer.OrdinalIgnoreCase).ToList();
}
