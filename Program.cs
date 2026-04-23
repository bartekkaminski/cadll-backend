using cadll.Services;
using DotNetEnv;

Env.Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// CORS origins: env var CORS_ORIGINS (produkcja) lub appsettings.json (dev)
var corsOrigins = (
    Environment.GetEnvironmentVariable("CORS_ORIGINS")
    ?? builder.Configuration["CorsOrigins"]
    ?? "http://localhost:5199,https://cadll.pl,https://www.cadll.pl"
).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(corsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()));

var aiProvider = (Environment.GetEnvironmentVariable("AI_PROVIDER") ?? "anthropic")
    .ToLowerInvariant().Trim();

builder.Services.AddScoped<ICodeGeneratorService>(sp =>
{
    var lf = sp.GetRequiredService<ILoggerFactory>();
    return aiProvider switch
    {
        "openai" => (ICodeGeneratorService) new OpenAiService(lf.CreateLogger<OpenAiService>()),
        _        => new AnthropicService(lf.CreateLogger<AnthropicService>())
    };
});

builder.Services.AddScoped<CompilerService>();
builder.Services.AddSingleton<JobStore>();

var app = builder.Build();

app.UseCors();
app.UseAuthorization();
app.MapControllers();

app.Run();
