using System.Text.Json;
using Aldehyde.Data;

namespace Aldehyde.Api;

public interface IQueryEmbedder
{
    Task<float[]?> EmbedAsync(string text, CancellationToken ct = default);
}

public sealed class CloudflareQueryEmbedder(IConfiguration cfg, HttpClient http) : IQueryEmbedder
{
    public const string Instruction = "Given a description of a mood, place or moment, retrieve perfumes whose notes and accords evoke it";

    public async Task<float[]?> EmbedAsync(string text, CancellationToken ct = default)
    {
        var account = cfg["WorkersAi:AccountId"];
        var token = cfg["WorkersAi:ApiToken"];
        var model = cfg["WorkersAi:Model"];
        if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(model)) return null;
        try
        {
            var input = new { queries = new[] { text }, instruction = Instruction };
            return (await Embedder.RunAsync(http, account, token, model, input, ct)).Vectors?.FirstOrDefault();
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException
            || e is TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }
}
