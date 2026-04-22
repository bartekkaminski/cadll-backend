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

GenerateApiDocs(app.Logger);

app.UseCors();
app.UseAuthorization();
app.MapControllers();

app.Run();

static void GenerateApiDocs(ILogger logger)
{
    try
    {
        var zwcadDir = Path.Combine(AppContext.BaseDirectory, "Libraries", "Zwcad");
        var apiSummary = ZwcadApiService.GetApiSummary(zwcadDir);

        var docsPath = Path.Combine(AppContext.BaseDirectory, "zwcad-api-docs.md");
        var content = $"""
            # ZWCAD API — dokumentacja wysyłana do GPT
            
            Wygenerowano: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
            Źródło DLL: `{zwcadDir}`
            
            ---
            
            {apiSummary}
            """;

        File.WriteAllText(docsPath, content);
        logger.LogInformation("Dokumentacja ZWCAD API zapisana: {Path}", docsPath);
    }
    catch (Exception ex)
    {
        logger.LogWarning("Nie można wygenerować dokumentacji API: {Message}", ex.Message);
    }
}
