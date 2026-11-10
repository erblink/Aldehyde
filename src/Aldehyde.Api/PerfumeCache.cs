using System.Data;
using System.Text.Json;
using Aldehyde.Data;
using Microsoft.EntityFrameworkCore;

namespace Aldehyde.Api;

public record VectorEntry(Perfume Perfume, float[] Unit);

public sealed class PerfumeCache(IServiceScopeFactory scopes)
{
    List<Perfume>? all;
    List<SearchEntry>? index;
    List<VectorEntry>? vectors;
    Dictionary<int, Perfume>? byId;
    ILookup<int, DupeLink>? dupes;
    readonly SemaphoreSlim gate = new(1, 1);

    public async Task<List<Perfume>> AllAsync() => all ?? await Locked(async () => all ??= await LoadAllAsync());

    public async Task<List<SearchEntry>> IndexAsync() =>
        index ?? await Locked(async () => index ??= PerfumeSearch.BuildIndex(all ??= await LoadAllAsync()));

    public async Task<Dictionary<int, Perfume>> ByIdAsync() =>
        byId ?? await Locked(async () => byId ??= (all ??= await LoadAllAsync()).ToDictionary(p => p.Id));

    public async Task<ILookup<int, DupeLink>> DupesAsync() => dupes ?? await Locked(async () => dupes ??= await LoadDupesAsync());

    public async Task<List<VectorEntry>> VectorsAsync() => vectors ?? await Locked(async () => vectors ??= await LoadVectorsAsync());

    async Task<T> Locked<T>(Func<Task<T>> load)
    {
        await gate.WaitAsync();
        try { return await load(); }
        finally { gate.Release(); }
    }

    async Task<List<Perfume>> LoadAllAsync()
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Sort here: Ratings has no index, so ordering by it in SQL makes the small DB sort every row.
        var perfumes = await WithoutEmbedding(db).OrderBy(p => p.Id).ToListAsync();
        return perfumes.OrderByDescending(p => p.Ratings).ToList();
    }

    async Task<ILookup<int, DupeLink>> LoadDupesAsync()
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.DupeLinks.AsNoTracking().ToListAsync()).ToLookup(d => d.OriginalId);
    }

    async Task<List<VectorEntry>> LoadVectorsAsync()
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var list = new List<VectorEntry>();
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """SELECT "Id", "Name", "House", "Family", "X", "Y", "Accords", "Embedding" FROM "Perfumes" WHERE "Embedding" IS NOT NULL""";
            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
            while (await reader.ReadAsync())
            {
                var id = reader.GetInt32(0);
                var name = reader.GetString(1);
                var house = reader.GetString(2);
                var family = reader.GetString(3);
                var x = reader.GetDouble(4);
                var y = reader.GetDouble(5);
                var accordsJson = reader.IsDBNull(6) ? null : reader.GetString(6);
                var embedding = Embedder.Unpack(reader.GetFieldValue<byte[]>(7));
                if (embedding.Length == 0) continue;
                var p = new Perfume
                {
                    Id = id, Name = name, House = house, Family = family, X = x, Y = y,
                    Accords = accordsJson is null ? [] : JsonSerializer.Deserialize<List<string>>(accordsJson) ?? [],
                };
                list.Add(new VectorEntry(p, MapLayout.Normalise(embedding)));
            }
        }
        finally { await conn.CloseAsync(); }
        return list;
    }

    static IQueryable<Perfume> WithoutEmbedding(AppDbContext db) =>
        db.Perfumes.AsNoTracking().Select(p => new Perfume
        {
            Id = p.Id, Name = p.Name, House = p.House, Year = p.Year, Strength = p.Strength, Ratings = p.Ratings,
            Perfumers = p.Perfumers, TopNotes = p.TopNotes, HeartNotes = p.HeartNotes, BaseNotes = p.BaseNotes,
            Accords = p.Accords, Family = p.Family, X = p.X, Y = p.Y, NeighbourIds = p.NeighbourIds,
        });
}
