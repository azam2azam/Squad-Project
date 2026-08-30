using Application.Abstractions;
using Infrastructure.Auth;
using Infrastructure.Integrations;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers persistence and integrations. The database provider is chosen by
    /// Database:Provider (SqlServer | Postgres) so the same build runs on either.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"] ?? "SqlServer";
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Default is not configured.");

        services.AddDbContext<AppDbContext>(options =>
        {
            switch (provider.ToLowerInvariant())
            {
                case "postgres":
                case "postgresql":
                    options.UseNpgsql(connectionString, npgsql =>
                    {
                        npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                        npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                    });
                    break;

                case "sqlserver":
                    options.UseSqlServer(connectionString, sql =>
                    {
                        sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                        sql.EnableRetryOnFailure(3);
                        // Boards fan out to members and then to people. Split queries avoid
                        // the cartesian duplication a single join would produce. Set here
                        // rather than per-query so the Application layer stays free of
                        // relational concerns.
                        sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                    });
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported Database:Provider '{provider}'. Use SqlServer or Postgres.");
            }
        });

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // Integrations are abstracted so they can be swapped or mocked (spec section 10).
        // The Jira implementation lands in M5; the notifier is replaced by the SignalR
        // one in the API composition root.
        services.AddSingleton<IBoardNotifier, NullBoardNotifier>();

        // Auth (spec section 8).
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddScoped<IBoardAuthorizer, BoardAuthorizer>();

        // Jira is config-gated. Registering the null object when it is off means the
        // capability endpoint reports it honestly instead of the UI discovering at
        // click time that nothing is configured.
        if (configuration.GetValue("Jira:Enabled", false))
        {
            services.AddHttpClient<IJiraClient, JiraClient>();
        }
        else
        {
            services.AddSingleton<IJiraClient, DisabledJiraClient>();
        }

        // Server-side export needs a headless browser, which not every host has.
        // Off by default so a deployment opts in rather than discovering at runtime
        // that the first export tries to download Chromium.
        if (configuration.GetValue("Export:Enabled", false))
        {
            services.AddSingleton<IExportRenderer, ChromiumExportRenderer>();
        }
        else
        {
            services.AddSingleton<IExportRenderer, UnavailableExportRenderer>();
        }

        return services;
    }
}
