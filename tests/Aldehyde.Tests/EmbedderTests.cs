using System.Net;
using System.Text;
using Aldehyde.Data;
using Microsoft.EntityFrameworkCore;

public class EmbedderTests : IDisposable
{
    readonly string dbPath = Path.Combine(Path.GetTempPath(), $"aldehyde-embed-{Guid.NewGuid()}.db");

    AppDbContext NewDb()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}").Options);
        db.Database.EnsureCreated();
        return db;
    }

    // First call returns one vector per text, every later call answers 429.
    sealed class FirstBatchThenQuota : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Calls++ > 0) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
            var vectors = string.Join(",", Enumerable.Repeat("[0.1,0.2]", 100));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"result\":{\"data\":[" + vectors + "]}}", Encoding.UTF8, "application/json"),
            });
        }
    }

    async Task Seed(int count)
    {
        using var db = NewDb();
        for (var i = 0; i < count; i++) db.Perfumes.Add(new Perfume { Name = $"P{i}", House = "H" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task QuotaStopKeepsFinishedBatch()
    {
        await Seed(150);
        var handler = new FirstBatchThenQuota();
        string result;
        using (var db = NewDb())
            result = await new Embedder(db).EmbedPendingAsync(new HttpClient(handler), "acc", "tok", "model");

        Assert.StartsWith("Quota hit. Done 100, missing 50", result);
        using var check = NewDb();
        Assert.Equal(100, await check.Perfumes.CountAsync(p => p.EmbeddingJson != null));
    }

    [Fact]
    public async Task CapStopsBeforeCallingTheApi()
    {
        await Seed(10);
        var handler = new FirstBatchThenQuota();
        using var db = NewDb();
        var result = await new Embedder(db).EmbedPendingAsync(new HttpClient(handler), "acc", "tok", "model", neuronCap: 0);

        Assert.StartsWith("Stopped at cap. Done 0, missing 10", result);
        Assert.Equal(0, handler.Calls);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(dbPath); } catch (IOException) { /* temp file, OS cleans up */ }
    }
}
