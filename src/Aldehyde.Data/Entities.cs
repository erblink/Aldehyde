using System.Text.Json;

namespace Aldehyde.Data;

public class Perfume
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string House { get; set; } = "";
    public int? Year { get; set; }
    public string? Strength { get; set; }
    public int Ratings { get; set; }
    public List<string> Perfumers { get; set; } = [];
    public List<string> TopNotes { get; set; } = [];
    public List<string> HeartNotes { get; set; } = [];
    public List<string> BaseNotes { get; set; } = [];
    public List<string> Accords { get; set; } = [];
    public string Family { get; set; } = "woody";
    public double X { get; set; }
    public double Y { get; set; }
    // Nearest perfumes by vector, closest first. Set by MapLayout so requests do no vector maths.
    public List<int> NeighbourIds { get; set; } = [];
    // JSON array in SQLite; pgvector column when Neon lands.
    public string? EmbeddingJson { get; set; }
    public float[]? Embedding => EmbeddingJson is null ? null : JsonSerializer.Deserialize<float[]>(EmbeddingJson);
    public IEnumerable<string> AllNotes => TopNotes.Concat(HeartNotes).Concat(BaseNotes);
}

public class DupeLink
{
    public int Id { get; set; }
    public int DupeId { get; set; }
    public int OriginalId { get; set; }
    public string Source { get; set; } = "suggested"; // house | community | suggested
    public int Votes { get; set; }
}
