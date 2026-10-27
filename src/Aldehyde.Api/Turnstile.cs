using System.Text.Json;

namespace Aldehyde.Api;

public sealed class Turnstile(IConfiguration cfg, HttpClient http)
{
    public async Task<bool> PassesAsync(string? token, CancellationToken ct = default)
    {
        var secret = cfg["Turnstile:SecretKey"];
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(token)) return false;
        try
        {
            using var res = await http.PostAsync("https://challenges.cloudflare.com/turnstile/v0/siteverify",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["secret"] = secret, ["response"] = token }), ct);
            if (!res.IsSuccessStatusCode) return false;
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            return doc.RootElement.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.True;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException
            || e is TaskCanceledException && !ct.IsCancellationRequested)
        {
            return false;
        }
    }
}
