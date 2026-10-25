using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Aldehyde.Data;

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
        var seen = new HashSet<string>();
        var imported = 0;
        foreach (var r in selected)
        {
            var key = Key(r.Name, r.House ?? "");
            if (!seen.Add(key)) continue;
            imported++;
            var accords = r.Accords ?? [];
            foreach (var a in accords.Take(5))
                if (!FamilyClassifier.IsMapped(a))
                    unmapped[a.Trim().ToLowerInvariant()] = unmapped.GetValueOrDefault(a.Trim().ToLowerInvariant()) + 1;
            if (accords.Take(5).All(a => !FamilyClassifier.IsMapped(a))) fallbacks++;

            if (!existing.TryGetValue(key, out var p))
            {
                p = new Perfume();
                db.Perfumes.Add(p);
                existing[key] = p;
            }
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
        return $"Imported {imported} of {rows.Count} ({selected.Count - imported} repeats skipped). Families: {string.Join(", ", counts)}. Fallbacks: {fallbacks}. Unmapped: {string.Join(", ", topUnmapped)}";
    }

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
        static List<string>? List(string? v, bool weighted = false)
        {
            if (v is null) return null;
            var items = v.Split([';', '|', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return weighted ? items.Select(StripWeight).ToList() : items.ToList();
        }
        static string StripWeight(string s)
        {
            var i = s.LastIndexOf(':');
            return i > 0 && int.TryParse(s[(i + 1)..], out _) ? s[..i].Trim() : s;
        }
        return rows.Skip(1).Select(r =>
        {
            var top = List(Get(r, "notes_top", "top", "top notes", "topnotes"), weighted: true);
            var heart = List(Get(r, "notes_middle", "heart", "heart notes", "middle", "middle notes"), weighted: true);
            var baseNotes = List(Get(r, "notes_base", "base", "base notes"), weighted: true);
            top ??= List(Get(r, "notes_flat"), weighted: true);
            return new RawPerfume(
                Get(r, "name") ?? "", Get(r, "house", "brand"), int.TryParse(Get(r, "year"), out var y) ? y : null,
                Get(r, "strength"), List(Get(r, "perfumers", "perfumer")), List(Get(r, "accords"), weighted: true),
                new RawNotes(top, heart, baseNotes),
                null, null, null,
                int.TryParse(Get(r, "ratings", "ratingcount", "numratings", "reviews", "vote_count", "votecount")?.Replace(",", ""), out var n) ? n : null,
                null, null);
        }).ToList();
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
