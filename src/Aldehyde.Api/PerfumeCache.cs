using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Aldehyde.Data;
using Microsoft.EntityFrameworkCore;

namespace Aldehyde.Api;

public record SearchEntry(Perfume Perfume, string Name, string NameAndHouse, string Everything);
public record VectorEntry(Perfume Perfume, float[] Unit);

public sealed class PerfumeCache(IServiceScopeFactory scopes)
{
    List<Perfume>? all;
    List<SearchEntry>? index;
    List<VectorEntry>? vectors;
    readonly SemaphoreSlim gate = new(1, 1);

    public async Task<List<Perfume>> AllAsync() => all ?? await Locked(async () => all ??= await LoadAllAsync());

    public async Task<List<SearchEntry>> IndexAsync() =>
        index ?? await Locked(async () => index ??= BuildIndex(all ??= await LoadAllAsync()));

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
        var perfumes = await PerfumeEndpoints.WithoutEmbedding(db).OrderBy(p => p.Id).ToListAsync();
        return perfumes.OrderByDescending(p => p.Ratings).ToList();
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
            cmd.CommandText = """SELECT "Id", "Name", "House", "Family", "X", "Y", "Accords", "EmbeddingJson" FROM "Perfumes" WHERE "EmbeddingJson" IS NOT NULL""";
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
                var embedding = JsonSerializer.Deserialize<float[]>(reader.GetString(7));
                if (embedding is not { Length: > 0 }) continue;
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

    static List<SearchEntry> BuildIndex(List<Perfume> perfumes) =>
        perfumes.Select(p =>
        {
            var name = Fold(p.Name);
            var nameAndHouse = $"{name} {Fold(p.House)}";
            return new SearchEntry(p, $" {name} ", $" {nameAndHouse} ",
                $" {nameAndHouse} {Fold(string.Join(' ', p.AllNotes.Concat(p.Accords)))} ");
        }).ToList();

    public static string Fold(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark || c is '\'' or '’') continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
