using System.Globalization;
using System.Text;
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

    public async Task<List<Perfume>> AllAsync()
    {
        if (all is not null) return all;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return all = await PerfumeEndpoints.WithoutEmbedding(db).OrderBy(p => p.Id).ToListAsync();
    }

    public async Task<List<VectorEntry>> VectorsAsync()
    {
        if (vectors is not null) return vectors;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.Perfumes.AsNoTracking().Where(p => p.EmbeddingJson != null)
            .Select(p => new Perfume
            {
                Id = p.Id, Name = p.Name, House = p.House, Family = p.Family, X = p.X, Y = p.Y,
                Accords = p.Accords, EmbeddingJson = p.EmbeddingJson,
            }).ToListAsync();
        return vectors = rows
            .Select(p => new VectorEntry(p, SuggestionRanker.Normalise(p.Embedding!)))
            .Where(e => e.Unit.Length > 0).ToList();
    }

    public async Task<List<SearchEntry>> IndexAsync() =>
        index ??= (await AllAsync()).Select(p =>
        {
            var name = Fold(p.Name);
            var nameAndHouse = $"{name} {Fold(p.House)}";
            return new SearchEntry(p, name, nameAndHouse, $"{nameAndHouse} {Fold(string.Join(' ', p.AllNotes.Concat(p.Accords)))}");
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
