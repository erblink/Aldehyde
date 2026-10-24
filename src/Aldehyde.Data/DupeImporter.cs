using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Aldehyde.Data;

// Seed file: [{"dupeName":"...","dupeHouse":"...","originalName":"...","originalHouse":"...","source":"house","votes":0}]
public class DupeImporter(AppDbContext db)
{
    static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    static readonly string[] Sources = ["house", "community", "suggested"];

    public async Task<string> ImportAsync(string path)
    {
        var seeds = JsonSerializer.Deserialize<List<DupeSeed>>(await File.ReadAllTextAsync(path), JsonOpts) ?? [];
        var byKey = await db.Perfumes.ToDictionaryAsync(p => PerfumeImporter.Key(p.Name, p.House));
        var linked = (await db.DupeLinks.Select(d => new { d.DupeId, d.OriginalId }).ToListAsync())
            .Select(d => (d.DupeId, d.OriginalId)).ToHashSet();
        var added = 0; var skipped = 0;
        foreach (var s in seeds)
        {
            if (!Sources.Contains(s.Source) ||
                !byKey.TryGetValue(PerfumeImporter.Key(s.DupeName, s.DupeHouse), out var dupe) ||
                !byKey.TryGetValue(PerfumeImporter.Key(s.OriginalName, s.OriginalHouse), out var orig) ||
                !linked.Add((dupe.Id, orig.Id)))
            { skipped++; continue; }
            db.DupeLinks.Add(new DupeLink { DupeId = dupe.Id, OriginalId = orig.Id, Source = s.Source, Votes = s.Votes });
            added++;
        }
        await db.SaveChangesAsync();
        return $"Dupes: added {added}, skipped {skipped}.";
    }
}

public record DupeSeed(string DupeName, string DupeHouse, string OriginalName, string OriginalHouse, string Source, int Votes);
