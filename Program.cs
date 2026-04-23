using System.Threading.RateLimiting;
using cadll.Data;
using cadll.Services;
using DotNetEnv;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

Env.Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("per-ip-daily", context =>
    {
        var ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault()
                 ?? context.Connection.RemoteIpAddress?.ToString()
                 ?? "unknown";
        var key = ip.Split(',')[0].Trim();

        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit       = 10,
            Window            = TimeSpan.FromHours(24),
            QueueLimit        = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
    });

    options.RejectionStatusCode = 429;
    options.OnRejected = async (ctx, _) =>
    {
        ctx.HttpContext.Response.ContentType = "application/json";
        await ctx.HttpContext.Response.WriteAsync(
            "{\"error\":\"Przekroczono dzienny limit generowań. Wróć jutro lub skontaktuj się z nami na kontakt@cadll.pl, jeśli potrzebujesz większego dostępu.\"}");
    };
});

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(
                  "http://localhost:5199",
                  "https://cadll.pl",
                  "https://www.cadll.pl")
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

var connStr = Environment.GetEnvironmentVariable("DBConnectionString")
    ?? throw new InvalidOperationException("Missing environment variable DBConnectionString.");
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(connStr));

var app = builder.Build();

// Tabele tworzone ręcznie SQL-em — brak automatycznej migracji

app.UseCors();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();

app.Run();
