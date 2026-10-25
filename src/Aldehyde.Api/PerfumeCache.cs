using Aldehyde.Data;
using Microsoft.EntityFrameworkCore;

namespace Aldehyde.Api;

public sealed class PerfumeCache(IServiceScopeFactory scopes)
{
    List<Perfume>? all;

    public async Task<List<Perfume>> AllAsync()
    {
        if (all is not null) return all;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return all = await PerfumeEndpoints.WithoutEmbedding(db).OrderBy(p => p.Id).ToListAsync();
    }
}
