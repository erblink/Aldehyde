using System.Text.Json.Serialization;
using Aldehyde.Api;
using Aldehyde.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
builder.Services.AddResponseCompression();
// Neon Postgres when ConnectionStrings:Postgres is set (user-secrets locally, Azure secret in production), else local SQLite.
builder.Services.AddDbContext<AppDbContext>((sp, o) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var postgres = cfg.GetConnectionString("Postgres");
    if (!string.IsNullOrEmpty(postgres)) o.UseNpgsql(postgres);
    else o.UseSqlite(cfg.GetConnectionString("Default") ?? "Data Source=aldehyde.db");
});
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated(); // shortcut: no migrations; a schema change means dropping the tables and re-importing

if (Cli.IsCommand(args))
{
    Console.WriteLine(await Cli.RunAsync(args, app.Services, app.Configuration));
    return;
}

app.UseResponseCompression();
app.UseCors();
app.MapPerfumeEndpoints();
app.Run();

public partial class Program;
