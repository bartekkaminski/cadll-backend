using cadll.Services;
using DotNetEnv;

Env.Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// CORS origins: env var CORS_ORIGINS (produkcja) lub appsettings.json (dev)
var corsOrigins = (
    Environment.GetEnvironmentVariable("CORS_ORIGINS")
    ?? builder.Configuration["CorsOrigins"]
    ?? "http://localhost:5199"
).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(corsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()));

builder.Services.AddScoped<OpenAiService>();
builder.Services.AddScoped<CompilerService>();

var app = builder.Build();

app.UseCors();
app.UseAuthorization();
app.MapControllers();

// Endpoint diagnostyczny — sprawdź czy pliki są na miejscu
app.MapGet("/api/diag", () =>
{
    var baseDir = AppContext.BaseDirectory;
    var netFx48Dir = Path.Combine(baseDir, "NetFx48");
    var zwcadDir = Path.Combine(baseDir, "Libraries", "Zwcad");

    return Results.Ok(new
    {
        baseDir,
        netFx48 = new
        {
            path = netFx48Dir,
            exists = Directory.Exists(netFx48Dir),
            dllCount = Directory.Exists(netFx48Dir)
                ? Directory.GetFiles(netFx48Dir, "*.dll").Length : 0
        },
        zwcad = new
        {
            path = zwcadDir,
            exists = Directory.Exists(zwcadDir),
            files = Directory.Exists(zwcadDir)
                ? Directory.GetFiles(zwcadDir, "*.dll").Select(Path.GetFileName).ToArray()
                : []
        }
    });
});

app.Run();
