using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aldehyde.Api;
using Aldehyde.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
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
        var res = await client.PostAsJsonAsync("/api/suggest", new { text = "rainy day in a library, reading philosophy", token = "t" });
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
    public async Task TokenlessSuggestIsRejectedWithoutUsingTheRateLimit()
    {
        using var own = new Factory();
        own.Seed();
        var c = own.CreateClient();
        for (var i = 0; i < 40; i++)
            Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/suggest", new { text = "rain" })).StatusCode);
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 31; i++)
            codes.Add((await c.PostAsJsonAsync("/api/suggest", new { text = "rain", token = "t" })).StatusCode);
        Assert.All(codes[..30], s => Assert.Equal(HttpStatusCode.OK, s));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, codes[30]);
    }

    sealed class Reply(HttpStatusCode code, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
    }

    [Theory]
    [InlineData("secret", "t", HttpStatusCode.OK, """{"success":true}""", true)]
    [InlineData("secret", "t", HttpStatusCode.OK, """{"success":false,"error-codes":["invalid-input-response"]}""", false)]
    [InlineData("secret", "t", HttpStatusCode.OK, "not json", false)]
    [InlineData("secret", "t", HttpStatusCode.InternalServerError, "", false)]
    [InlineData("secret", "", HttpStatusCode.OK, """{"success":true}""", false)]
    [InlineData("", "t", HttpStatusCode.OK, """{"success":true}""", false)]
    public async Task TurnstilePassesOnlyOnSuccess(string secret, string token, HttpStatusCode code, string body, bool passes)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Turnstile:SecretKey"] = secret,
        }).Build();
        Assert.Equal(passes, await new Turnstile(cfg, new HttpClient(new Reply(code, body))).PassesAsync(token));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, """{"result":{"data":[[0.5,0.25]]}}""", 2)]
    [InlineData(HttpStatusCode.OK, """{"errors":[]}""", 0)]
    [InlineData(HttpStatusCode.OK, "not json", 0)]
    [InlineData(HttpStatusCode.InternalServerError, "", 0)]
    public async Task QueryEmbedderReadsVectorOrReturnsNull(HttpStatusCode code, string body, int length)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WorkersAi:AccountId"] = "a", ["WorkersAi:ApiToken"] = "t", ["WorkersAi:Model"] = "m",
        }).Build();
        var vector = await new CloudflareQueryEmbedder(cfg, new HttpClient(new Reply(code, body))).EmbedAsync("rain");
        Assert.Equal(length, vector?.Length ?? 0);
    }

    [Fact]
    public async Task VectorsDropEmbeddingJson()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"aldehyde-vectors-{Guid.NewGuid()}.db");
        try
        {
            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
            using var provider = services.BuildServiceProvider();
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.EnsureCreated();
                db.Perfumes.Add(new Perfume { Name = "A", House = "H", EmbeddingJson = "[3,4]" });
                db.SaveChanges();
            }
            var cache = new PerfumeCache(provider.GetRequiredService<IServiceScopeFactory>());
            var vectors = await cache.VectorsAsync();
            Assert.Single(vectors);
            Assert.Null(vectors[0].Perfume.EmbeddingJson);
            Assert.True(Math.Abs(vectors[0].Unit[0] - 0.6f) < 0.001);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(dbPath); } catch (IOException) { }
        }
    }

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

    [Fact]
    public void RankShowsOneOfIdenticalScents()
    {
        var entries = new List<VectorEntry>
        {
            new(new Perfume { Id = 1, Name = "EDT" }, [1f, 0f]),
            new(new Perfume { Id = 2, Name = "EDP" }, [1f, 0f]),
            new(new Perfume { Id = 3, Name = "Other" }, [0.8f, 0.6f]),
        };
        Assert.Equal([1, 3], SuggestionRanker.Top([1f, 0f], entries, 2).Select(p => p.Id));
    }

    [Theory]
    [InlineData("something like Farthest")]
    [InlineData("farthest H")]
    [InlineData("H Farthest")]
    public async Task NamedPerfumeIsTheQueryAndLeftOut(string text)
    {
        var res = await client.PostAsJsonAsync("/api/suggest", new { text, token = "t" });
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal(["Second", "Closest"], doc.RootElement.EnumerateArray().Select(h => h.GetProperty("name").GetString()));
    }

    sealed class Capture : HttpMessageHandler
    {
        public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"result":{"data":[[1,0]]}}""") };
        }
    }

    [Fact]
    public async Task QueryEmbedderSendsAQueryWithInstruction()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WorkersAi:AccountId"] = "a", ["WorkersAi:ApiToken"] = "t", ["WorkersAi:Model"] = "m",
        }).Build();
        var capture = new Capture();
        await new CloudflareQueryEmbedder(cfg, new HttpClient(capture)).EmbedAsync("rain");
        using var doc = JsonDocument.Parse(capture.Body!);
        Assert.Equal("rain", doc.RootElement.GetProperty("queries")[0].GetString());
        Assert.Equal(CloudflareQueryEmbedder.Instruction, doc.RootElement.GetProperty("instruction").GetString());
        Assert.False(doc.RootElement.TryGetProperty("text", out _));
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
                        ["Turnstile:SecretKey"] = "secret",
                    }))
                .ConfigureTestServices(s =>
                {
                    s.AddSingleton<IQueryEmbedder, FakeEmbedder>();
                    s.AddHttpClient<Turnstile>().ConfigurePrimaryHttpMessageHandler(() => new Reply(HttpStatusCode.OK, """{"success":true}"""));
                });

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
