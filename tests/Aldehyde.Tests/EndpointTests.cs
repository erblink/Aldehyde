using System.Net;
using System.Text.Json;
using Aldehyde.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public class EndpointTests : IClassFixture<EndpointTests.Factory>
{
    readonly Factory factory;
    readonly HttpClient client;

    public EndpointTests(Factory factory)
    {
        this.factory = factory;
        client = factory.CreateClient();
        factory.Seed();
    }

    [Fact]
    public async Task PerfumesReturnsPoints()
    {
        var json = await client.GetStringAsync("/api/perfumes");
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(4, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task UnknownPerfumeReturns404() =>
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/perfumes/9999")).StatusCode);

    [Fact]
    public async Task EmptySearchReturns400() =>
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/search?q=")).StatusCode);

    [Fact]
    public async Task SearchFindsNote()
    {
        var json = await client.GetStringAsync("/api/search?q=bergamot");
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task DetailOrdersDupesHouseCommunitySuggested()
    {
        int origId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            origId = db.Perfumes.Single(p => p.Name == "Orig").Id;
        }
        var json = await client.GetStringAsync($"/api/perfumes/{origId}");
        using var doc = JsonDocument.Parse(json);
        var sources = doc.RootElement.GetProperty("dupes").EnumerateArray()
            .Select(d => d.GetProperty("source").GetString()!).ToArray();
        Assert.Equal(["house", "community", "suggested"], sources);
    }

    [Fact]
    public async Task DetailListsStoredNeighboursWithSharedNotes()
    {
        int origId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            origId = db.Perfumes.Single(p => p.Name == "Orig").Id;
        }
        var json = await client.GetStringAsync($"/api/perfumes/{origId}");
        using var doc = JsonDocument.Parse(json);
        var n = doc.RootElement.GetProperty("neighbours")[0];
        Assert.Equal("HouseDupe", n.GetProperty("name").GetString());
        Assert.Equal("bergamot", n.GetProperty("sharedNotes")[0].GetString());
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        readonly string dbPath = Path.Combine(Path.GetTempPath(), $"aldehyde-api-{Guid.NewGuid()}.db");
        bool seeded;

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
            builder.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:Default"] = $"Data Source={dbPath}" }));

        public void Seed()
        {
            if (seeded) return;
            seeded = true;
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var orig = new Perfume { Name = "Orig", House = "H", TopNotes = ["bergamot"] };
            var house = new Perfume { Name = "HouseDupe", House = "C", TopNotes = ["bergamot"] };
            var comm = new Perfume { Name = "CommDupe", House = "C" };
            var sugg = new Perfume { Name = "SuggDupe", House = "C" };
            db.Perfumes.AddRange(orig, house, comm, sugg);
            db.SaveChanges();
            orig.NeighbourIds = [house.Id, comm.Id];
            db.SaveChanges();
            db.DupeLinks.AddRange(
                new DupeLink { DupeId = sugg.Id, OriginalId = orig.Id, Source = "suggested", Votes = 99 },
                new DupeLink { DupeId = comm.Id, OriginalId = orig.Id, Source = "community", Votes = 5 },
                new DupeLink { DupeId = house.Id, OriginalId = orig.Id, Source = "house", Votes = 0 });
            db.SaveChanges();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(dbPath); } catch (IOException) { /* temp file, OS cleans up */ }
        }
    }
}
