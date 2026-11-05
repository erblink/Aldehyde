using System.Globalization;
using System.Text;
using Aldehyde.Data;

namespace Aldehyde.Api;

public record SearchEntry(Perfume Perfume, string Name, string NameAndHouse, string Everything);

public static class PerfumeSearch
{
    static readonly HashSet<string> Filler =
        ["something", "anything", "like", "similar", "to", "smells", "smelling", "a", "an", "the",
         "perfume", "fragrance", "scent", "but", "cheaper", "dupe", "alternative", "of"];

    public static List<SearchEntry> BuildIndex(List<Perfume> perfumes) =>
        perfumes.Select(p =>
        {
            var name = Fold(p.Name);
            var nameAndHouse = $"{name} {Fold(p.House)}";
            return new SearchEntry(p, $" {name} ", $" {nameAndHouse} ",
                $" {nameAndHouse} {Fold(string.Join(' ', p.AllNotes.Concat(p.Accords)))} ");
        }).ToList();

    public static List<SearchEntry> Find(string phrase, List<SearchEntry> index, int count)
    {
        var whole = $" {phrase} ";
        var start = $" {phrase}";
        var wholeTerms = phrase.Split(' ').Select(t => $" {t} ").ToArray();
        var startTerms = phrase.Split(' ').Select(t => $" {t}").ToArray();
        return index.Select(e => (Entry: e, Rank: Rank(e, whole, start, wholeTerms, startTerms))).Where(h => h.Rank >= 0)
            .OrderBy(h => h.Rank).Take(count).Select(h => h.Entry).ToList();
    }

    public static SearchEntry? NamedIn(string text, List<SearchEntry> index)
    {
        var folded = Fold(text);
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
}
