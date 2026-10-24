namespace Aldehyde.Api;

// Shapes match web/src/api/types.ts. Change both sides together.
public record MapPoint(int Id, string Name, string House, string Family, double X, double Y);
public record Neighbour(int Id, string Name, List<string> SharedNotes);
public record Dupe(int Id, string Name, string Source, int Votes, List<string> SharedNotes);
public record Notes(List<string> Top, List<string> Heart, List<string> Base);
public record PerfumeDetail(int Id, string Name, string House, int? Year, string? Strength,
    List<string> Perfumers, Notes Notes, List<string> Accords, string Family,
    List<Neighbour> Neighbours, List<Dupe> Dupes);
public record SearchResult(int Id, string Name, string House, double X, double Y);
