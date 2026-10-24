using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Aldehyde.Data;

// Swappable dataset importer. Input: JSON array, JSONL, or CSV with columns
// name, house, year, strength, perfumers, accords, top, heart, base, ratings.
public class PerfumeImporter(AppDbContext db)
{
    static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<string> ImportAsync(string path, int limit = 5000)
    {
        var rows = await ReadRowsAsync(path);
        var selected = rows.Where(r => !string.IsNullOrWhiteSpace(r.Name))
            .OrderByDescending(r => r.RatingsValue).Take(limit).ToList();

        var existing = await db.Perfumes.ToDictionaryAsync(p => Key(p.Name, p.House));
        var unmapped = new Dictionary<string, int>();
        var fallbacks = 0;
        foreach (var r in selected)
        {
            var accords = r.Accords ?? [];
            foreach (var a in accords.Take(5))
                if (!FamilyClassifier.IsMapped(a))
                    unmapped[a.Trim().ToLowerInvariant()] = unmapped.GetValueOrDefault(a.Trim().ToLowerInvariant()) + 1;
            if (accords.Take(5).All(a => !FamilyClassifier.IsMapped(a))) fallbacks++;

            var key = Key(r.Name, r.House ?? "");
            if (!existing.TryGetValue(key, out var p))
            {
                p = new Perfume();
                db.Perfumes.Add(p);
                existing[key] = p;
            }
            // Upsert: re-import updates facts but keeps embedding and position, unless the embedded text changed.
            var embeddedText = Embedder.BuildText(p);
            p.Name = r.Name.Trim(); p.House = (r.House ?? "").Trim();
            p.Year = r.Year; p.Strength = r.Strength; p.Ratings = r.RatingsValue;
            p.Perfumers = r.Perfumers ?? []; p.Accords = accords;
            p.Family = FamilyClassifier.Classify(accords);
            p.TopNotes = r.Notes?.Top ?? r.TopNotes ?? [];
            p.HeartNotes = r.Notes?.Heart ?? r.HeartNotes ?? [];
            p.BaseNotes = r.Notes?.Base ?? r.BaseNotes ?? [];
            if (Embedder.BuildText(p) != embeddedText) p.EmbeddingJson = null;
        }
        await db.SaveChangesAsync();
        var counts = db.Perfumes.GroupBy(p => p.Family).Select(g => $"{g.Key}:{g.Count()}");
        var topUnmapped = unmapped.OrderByDescending(kv => kv.Value).Take(20).Select(kv => $"{kv.Key}:{kv.Value}");
        return $"Imported {selected.Count} of {rows.Count}. Families: {string.Join(", ", counts)}. Fallbacks: {fallbacks}. Unmapped: {string.Join(", ", topUnmapped)}";
    }

    // Perfume identity: the same name exists at several houses.
    internal static string Key(string name, string house) => $"{name.Trim().ToLowerInvariant()}|{house.Trim().ToLowerInvariant()}";

    async Task<List<RawPerfume>> ReadRowsAsync(string path)
    {
        var text = await File.ReadAllTextAsync(path);
        if (Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            return ParseCsv(text);
        if (text.TrimStart().StartsWith('['))
            return JsonSerializer.Deserialize<List<RawPerfume>>(text, JsonOpts) ?? [];
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => JsonSerializer.Deserialize<RawPerfume>(l, JsonOpts)
                ?? throw new InvalidDataException($"Bad JSONL line: {l[..Math.Min(80, l.Length)]}"))
            .ToList();
    }

    static List<RawPerfume> ParseCsv(string text)
    {
        var rows = Csv.Parse(text);
        if (rows.Count < 2) return [];
        var head = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        string? Get(List<string> row, params string[] names)
        {
            var i = head.FindIndex(h => names.Contains(h));
            return i >= 0 && i < row.Count && !string.IsNullOrWhiteSpace(row[i]) ? row[i].Trim() : null;
        }
        static List<string>? List(string? v) => v is null ? null :
            v.Split([';', '|', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        return rows.Skip(1).Select(r => new RawPerfume(
            Get(r, "name") ?? "", Get(r, "house", "brand"), int.TryParse(Get(r, "year"), out var y) ? y : null,
            Get(r, "strength"), List(Get(r, "perfumers", "perfumer")), List(Get(r, "accords")),
            new RawNotes(List(Get(r, "top", "top notes", "topnotes")), List(Get(r, "heart", "heart notes", "middle", "middle notes")), List(Get(r, "base", "base notes"))),
            null, null, null,
            int.TryParse(Get(r, "ratings", "ratingcount", "numratings", "reviews")?.Replace(",", ""), out var n) ? n : null,
            null, null)).ToList();
    }
}

static class Csv
{
    public static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>(); var row = new List<string>(); var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted && c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
            else if (c == '"') quoted = !quoted;
            else if (!quoted && c == ',') { row.Add(cell.ToString()); cell.Clear(); }
            else if (!quoted && c == '\n') { row.Add(cell.ToString()); cell.Clear(); rows.Add(row); row = []; }
            else if (c != '\r') cell.Append(c);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
        return rows;
    }
}

public record RawPerfume(string Name, string? House, int? Year, string? Strength,
    List<string>? Perfumers, List<string>? Accords, RawNotes? Notes,
    List<string>? TopNotes, List<string>? HeartNotes, List<string>? BaseNotes,
    int? Ratings = null, int? RatingCount = null, int? NumRatings = null)
{
    public int RatingsValue => Ratings ?? RatingCount ?? NumRatings ?? 0;
}
public record RawNotes(List<string>? Top, List<string>? Heart, List<string>? Base);
