using System.Text.Json.Serialization;
using Aldehyde.Api;
using Aldehyde.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
builder.Services.AddResponseCompression();
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=aldehyde.db"));
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated(); // shortcut: migrations when Neon lands

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
