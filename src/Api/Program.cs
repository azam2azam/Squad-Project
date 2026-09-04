using Api.Common;
using Api.Hubs;
using Application;
using Application.Abstractions;
using Infrastructure;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Structured logging from the first line, so startup failures are legible too.
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<ICurrentUserContext, HttpCurrentUserContext>();

builder.Services.AddJwtAuth(builder.Configuration);

builder.Services.AddSignalR();

// Replaces the no-op notifier registered by Infrastructure: handlers publish through
// IBoardNotifier and this is the only place that knows the transport is SignalR.
builder.Services.AddSingleton<IBoardNotifier, SignalRBoardNotifier>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// An explicit bound on spreadsheet uploads rather than inheriting a framework default
// that could change between versions. Matches the per-action RequestSizeLimit.
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 20 * 1024 * 1024;
});

// RFC 7807 for every unhandled failure and every 4xx/5xx status (spec section 7).
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database");

const string WebCorsPolicy = "web";
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddCors(options => options.AddPolicy(WebCorsPolicy, policy =>
{
    // Locked to the configured web origin; credentials are required for SignalR.
    policy.WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
}));

// Polls for Jira updates on the admin-configured interval. Inert until an admin turns
// auto-apply on, so it costs nothing in a deployment that does not use Jira.
builder.Services.AddHostedService<Api.Workers.JiraSyncWorker>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
    app.Use(async (context, next) =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        await next();
    });
}

app.UseSerilogRequestLogging();
app.UseCors(WebCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapHub<BoardsHub>("/hubs/boards");

await MigrateAndSeedAsync(app);

// Lets a Kubernetes Job run migrations once per release and exit, instead of every
// replica racing to alter the same schema on startup. See deploy/k8s/api.yaml.
if (app.Configuration.GetValue("RunMigrationsAndExit", false))
{
    app.Logger.LogInformation("RunMigrationsAndExit is set — migrations applied, exiting.");
    return;
}

app.Run();

/// <summary>
/// Applies migrations and demo data. Migration is automatic in development and must be
/// opted into elsewhere (spec section 11) so a production deploy never silently alters schema.
/// </summary>
static async Task MigrateAndSeedAsync(WebApplication app)
{
    var config = app.Configuration;
    var autoMigrate = config.GetValue("Database:AutoMigrate", app.Environment.IsDevelopment());

    // Seeding an administrator is on by default: without it a fresh install cannot be
    // signed into at all. Demo content is off by default — a clean deployment does not
    // want example boards in it.
    var seedOptions = new SeedOptions
    {
        SeedAdminUser = config.GetValue("Database:SeedAdminUser", true),
        SeedDemoData = config.GetValue("Database:SeedDemoData", false),
        // Blank, not just missing, falls back: an empty value in appsettings.json is a
        // placeholder, and must never become a literal empty password.
        AdminEmail = Or(config["Database:AdminEmail"], "admin@pirt.example"),
        AdminDisplayName = Or(config["Database:AdminDisplayName"], "Administrator"),
        AdminPassword = Or(config["Database:AdminPassword"], "Admin!Pass123")
    };

    static string Or(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    if (!autoMigrate && !seedOptions.SeedAdminUser && !seedOptions.SeedDemoData)
    {
        return;
    }

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider
        .GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

    if (autoMigrate)
    {
        logger.LogInformation("Applying database migrations.");
        await db.Database.MigrateAsync();
    }

    if (seedOptions.SeedAdminUser || seedOptions.SeedDemoData)
    {
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await DbSeeder.SeedAsync(db, logger, passwordHasher, seedOptions);
    }
}

/// <summary>Exposed so the integration test host can reference the entry point assembly.</summary>
public partial class Program;
