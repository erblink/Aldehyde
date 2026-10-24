using Aldehyde.Data;
using Microsoft.EntityFrameworkCore;

public class ImporterTests : IDisposable
{
    readonly string dbPath = Path.Combine(Path.GetTempPath(), $"aldehyde-test-{Guid.NewGuid()}.db");
    readonly List<string> files = [];

    AppDbContext NewDb()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}").Options);
        db.Database.EnsureCreated();
        return db;
    }

    string WriteFile(string name, string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}-{name}");
        File.WriteAllText(path, content);
        files.Add(path);
        return path;
    }

    [Fact]
    public async Task ReimportDoesNotDuplicate()
    {
        var file = WriteFile("data.json",
            """[{"name":"A","house":"H","accords":["citrus"],"notes":{"top":["bergamot"],"heart":[],"base":[]}}]""");
        using (var db = NewDb()) await new PerfumeImporter(db).ImportAsync(file);
        using (var db = NewDb()) await new PerfumeImporter(db).ImportAsync(file);
        using var check = NewDb();
        Assert.Equal(1, await check.Perfumes.CountAsync());
    }

    [Fact]
    public async Task ReimportClearsEmbeddingOnlyWhenNotesChange()
    {
        var v1 = WriteFile("v1.json", """[{"name":"A","house":"H","notes":{"top":["bergamot"]}},{"name":"B","house":"H","notes":{"top":["rose"]}}]""");
        var v2 = WriteFile("v2.json", """[{"name":"A","house":"H","notes":{"top":["bergamot","pepper"]}},{"name":"B","house":"H","notes":{"top":["rose"]}}]""");
        using (var db = NewDb())
        {
            await new PerfumeImporter(db).ImportAsync(v1);
            foreach (var p in db.Perfumes) p.EmbeddingJson = "[1,0]";
            await db.SaveChangesAsync();
        }
        using (var db = NewDb()) await new PerfumeImporter(db).ImportAsync(v2);
        using var check = NewDb();
        Assert.Null(check.Perfumes.Single(p => p.Name == "A").EmbeddingJson);
        Assert.Equal("[1,0]", check.Perfumes.Single(p => p.Name == "B").EmbeddingJson);
    }

    [Fact]
    public async Task LayoutIsTheSameOnEveryRun()
    {
        var rnd = new Random(3);
        var embs = Enumerable.Range(0, 60).Select(_ => $"[{string.Join(",", Enumerable.Range(0, 8).Select(_ => rnd.NextDouble().ToString("F3", System.Globalization.CultureInfo.InvariantCulture)))}]").ToList();
        using var db = NewDb();
        for (var i = 0; i < embs.Count; i++) db.Perfumes.Add(new Perfume { Name = $"P{i}", House = "H", EmbeddingJson = embs[i] });
        await db.SaveChangesAsync();

        await new MapLayout(db).ComputeAsync();
        var first = db.Perfumes.AsNoTracking().OrderBy(p => p.Id).Select(p => new { p.X, p.Y }).ToList();
        await new MapLayout(db).ComputeAsync();
        var second = db.Perfumes.AsNoTracking().OrderBy(p => p.Id).Select(p => new { p.X, p.Y }).ToList();
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task ImportKeepsHighestRatedUpToLimit()
    {
        var file = WriteFile("data.json",
            """[{"name":"Low","house":"H","ratings":1,"accords":[]},{"name":"High","house":"H","ratings":100,"accords":[]},{"name":"Mid","house":"H","ratings":50,"accords":[]}]""");
        using var db = NewDb();
        await new PerfumeImporter(db).ImportAsync(file, limit: 2);
        var names = await db.Perfumes.Select(p => p.Name).ToListAsync();
        Assert.Equal(2, names.Count);
        Assert.Contains("High", names);
        Assert.Contains("Mid", names);
        Assert.DoesNotContain("Low", names);
    }

    [Fact]
    public async Task CsvImportParsesNotes()
    {
        var file = WriteFile("data.csv",
            "name,house,accords,top,base,ratings\nA,H,citrus,\"bergamot, pepper\",cedar,10\n");
        using var db = NewDb();
        await new PerfumeImporter(db).ImportAsync(file);
        var p = await db.Perfumes.SingleAsync();
        Assert.Equal(["bergamot", "pepper"], p.TopNotes);
        Assert.Equal("citrus", p.Family);
    }

    [Fact]
    public async Task CoordinatesOnEmptyDbDoNotCrash()
    {
        using var db = NewDb();
        var result = await new MapLayout(db).ComputeAsync();
        Assert.StartsWith("Need at least 2", result);
    }

    [Fact]
    public async Task CoordinatesLandInRange()
    {
        using var db = NewDb();
        foreach (var (name, emb) in new[] { ("A", "[1,0,0]"), ("B", "[0,1,0]"), ("C", "[0,0,1]"), ("D", "[1,1,1]") })
            db.Perfumes.Add(new Perfume { Name = name, House = "H", EmbeddingJson = emb });
        await db.SaveChangesAsync();
        var result = await new MapLayout(db).ComputeAsync();
        Assert.StartsWith("Coordinates set for 4", result);
        Assert.All(await db.Perfumes.ToListAsync(), p =>
        {
            Assert.InRange(p.X, 0, 1000);
            Assert.InRange(p.Y, 0, 1000);
            Assert.False(double.IsNaN(p.X) || double.IsNaN(p.Y));
            Assert.Equal(3, p.NeighbourIds.Count);
            Assert.DoesNotContain(p.Id, p.NeighbourIds);
        });
    }

    [Fact]
    public async Task UmapKeepsClustersApart()
    {
        using var db = NewDb();
        for (var i = 0; i < 6; i++)
        {
            db.Perfumes.Add(new Perfume { Name = $"A{i}", House = "H", EmbeddingJson = $"[1,0.0{i},0,0]" });
            db.Perfumes.Add(new Perfume { Name = $"B{i}", House = "H", EmbeddingJson = $"[0.0{i},0,1,0]" });
        }
        await db.SaveChangesAsync();
        await new MapLayout(db).ComputeAsync();
        var all = await db.Perfumes.ToListAsync();
        static double Dist(Perfume a, Perfume b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
        var within = all.Where(p => p.Name.StartsWith('A'))
            .Select(a => all.Where(p => p.Name.StartsWith('A') && p.Id != a.Id).Min(b => Dist(a, b))).Average();
        var across = all.Where(p => p.Name.StartsWith('A'))
            .Select(a => all.Where(p => p.Name.StartsWith('B')).Min(b => Dist(a, b))).Average();
        Assert.True(within < across, $"within {within:F0} should be < across {across:F0}");
    }

    [Fact]
    public async Task NeighboursAreClosestFirst()
    {
        using var db = NewDb();
        foreach (var (name, emb) in new[] { ("A", "[1,0]"), ("Near", "[0.9,0.1]"), ("Far", "[0,1]") })
            db.Perfumes.Add(new Perfume { Name = name, House = "H", EmbeddingJson = emb });
        await db.SaveChangesAsync();
        await new MapLayout(db).ComputeAsync();
        var all = await db.Perfumes.ToDictionaryAsync(p => p.Name);
        Assert.Equal([all["Near"].Id, all["Far"].Id], all["A"].NeighbourIds);
    }

    [Fact]
    public async Task DupeSeedLinksByNameAndSkipsRepeats()
    {
        using (var db = NewDb())
        {
            // Same name at two houses must not crash the lookup.
            db.Perfumes.AddRange(
                new Perfume { Name = "Orig", House = "H" },
                new Perfume { Name = "Orig", House = "Other" },
                new Perfume { Name = "Clone", House = "C" });
            await db.SaveChangesAsync();
        }
        var file = WriteFile("dupes.json",
            """[{"dupeName":"Clone","dupeHouse":"C","originalName":"Orig","originalHouse":"H","source":"house","votes":3}]""");
        using (var db = NewDb()) await new DupeImporter(db).ImportAsync(file);
        using (var db = NewDb()) await new DupeImporter(db).ImportAsync(file);
        using var check = NewDb();
        var link = await check.DupeLinks.SingleAsync();
        Assert.Equal("house", link.Source);
        Assert.Equal(check.Perfumes.Single(p => p.Name == "Orig" && p.House == "H").Id, link.OriginalId);
    }

    public void Dispose()
    {
        foreach (var f in files) File.Delete(f);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(dbPath); } catch (IOException) { /* temp file, OS cleans up */ }
    }
}
