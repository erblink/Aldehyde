using Aldehyde.Data;

namespace Aldehyde.Api;

static class Cli
{
    const string Usage = "Usage: import <dataset.json|jsonl|csv> [limit] | import-dupes <dupes.json> | embed | coords";

    public static bool IsCommand(string[] args) =>
        args.Length > 0 && args[0] is "import" or "import-dupes" or "embed" or "coords";

    public static async Task<string> RunAsync(string[] args, IServiceProvider services, IConfiguration cfg)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            return args switch
            {
                ["import", var path] => await new PerfumeImporter(db).ImportAsync(path),
                ["import", var path, var limit] when int.TryParse(limit, out var n) => await new PerfumeImporter(db).ImportAsync(path, n),
                ["import-dupes", var path] => await new DupeImporter(db).ImportAsync(path),
                ["embed"] => await EmbedAsync(db, cfg),
                ["coords"] => await new MapLayout(db, Path.Combine(Path.GetTempPath(), "aldehyde-coords.json")).ComputeAsync(),
                _ => Usage,
            };
        }
        catch (FileNotFoundException e)
        {
            return $"File not found: {e.FileName}. Use an absolute path.";
        }
    }

    static async Task<string> EmbedAsync(AppDbContext db, IConfiguration cfg)
    {
        var account = cfg["WorkersAi:AccountId"];
        var token = cfg["WorkersAi:ApiToken"];
        if (account is null || token is null) return "Set WorkersAi:AccountId and WorkersAi:ApiToken via user-secrets first.";
        using var http = new HttpClient();
        return await new Embedder(db).EmbedPendingAsync(http, account, token,
            cfg["WorkersAi:Model"] ?? "@cf/qwen/qwen3-embedding-0.6b", cfg.GetValue("WorkersAi:NeuronCap", 8000));
    }
}
