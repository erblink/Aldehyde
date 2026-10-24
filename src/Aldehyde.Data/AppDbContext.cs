using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aldehyde.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Perfume> Perfumes => Set<Perfume>();
    public DbSet<DupeLink> DupeLinks => Set<DupeLink>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        var json = new ValueConverter<List<string>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());
        foreach (var p in new[] { nameof(Perfume.Perfumers), nameof(Perfume.TopNotes), nameof(Perfume.HeartNotes), nameof(Perfume.BaseNotes), nameof(Perfume.Accords) })
            b.Entity<Perfume>().Property<List<string>>(p).HasConversion(json);
        b.Entity<Perfume>().HasIndex(p => new { p.Name, p.House });
    }
}
