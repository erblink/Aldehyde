using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aldehyde.Api;
using Aldehyde.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public class SuggestTests : IClassFixture<SuggestTests.Factory>
{
    readonly Factory factory;
    readonly HttpClient client;

    public SuggestTests(Factory factory)
    {
        this.factory = factory;
        client = factory.CreateClient();
        factory.Seed();
    }

    [Fact]
    public async Task SuggestReturnsClosestFirst()
    {
        var res = await client.PostAsJsonAsync("/api/suggest", new { text = "rainy day in a library, reading philosophy" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var hits = doc.RootElement.EnumerateArray().ToList();
        Assert.Equal(3, hits.Count);
        Assert.Equal("Closest", hits[0].GetProperty("name").GetString());
        Assert.Equal("woody", hits[0].GetProperty("accords")[0].GetString());
    }

    [Fact]
    public async Task SuggestRejectsShortText() =>
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/suggest", new { text = "hi" })).StatusCode);

    [Fact]
    public void RankSkipsMismatchedDimensions()
    {
        var perfume = new Perfume { Id = 1, Name = "A" };
        var entries = new List<VectorEntry>
        {
            new(perfume, [1f, 0f, 0f]),
            new(new Perfume { Id = 2, Name = "B" }, [1f, 0f]),
        };
        var top = SuggestionRanker.Top([1f, 0f], entries, 3);
        Assert.Single(top);
        Assert.Equal(2, top[0].Id);
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        readonly string dbPath = Path.Combine(Path.GetTempPath(), $"aldehyde-suggest-{Guid.NewGuid()}.db");
        bool seeded;

        sealed class FakeEmbedder : IQueryEmbedder
        {
            public Task<float[]?> EmbedAsync(string text, CancellationToken ct = default) =>
                Task.FromResult<float[]?>([1f, 0f]);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseEnvironment("Testing")
                .ConfigureAppConfiguration((_, cfg) =>
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Default"] = $"Data Source={dbPath}",
                        ["ConnectionStrings:Postgres"] = "",
                    }))
                .ConfigureTestServices(s => s.AddSingleton<IQueryEmbedder, FakeEmbedder>());

        public void Seed()
        {
            if (seeded) return;
            seeded = true;
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Perfumes.AddRange(
                new Perfume { Name = "Closest", House = "H", Family = "woody", Accords = ["woody"], EmbeddingJson = "[1,0]" },
                new Perfume { Name = "Second", House = "H", Family = "woody", Accords = ["amber"], EmbeddingJson = "[0.7,0.7]" },
                new Perfume { Name = "Farthest", House = "H", Family = "aquatic", Accords = ["aquatic"], EmbeddingJson = "[0,1]" });
            db.SaveChanges();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(dbPath); } catch (IOException) { }
        }
    }
}
