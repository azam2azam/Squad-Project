using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Api.Common;

public static class AuthSetup
{
    /// <summary>
    /// JWT bearer authentication, configured to be OIDC-ready: set Jwt:Authority and the
    /// deployment federates with a corporate identity provider instead of validating
    /// locally-issued tokens (spec section 8).
    /// </summary>
    public static IServiceCollection AddJwtAuth(
        this IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection("Jwt");
        var authority = jwt["Authority"];

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                if (!string.IsNullOrWhiteSpace(authority))
                {
                    // Federated: keys come from the provider's discovery document.
                    options.Authority = authority;
                    options.Audience = jwt["Audience"];
                }
                else
                {
                    var signingKey = jwt["SigningKey"];
                    if (string.IsNullOrWhiteSpace(signingKey) || signingKey.Length < 32)
                    {
                        throw new InvalidOperationException(
                            "Jwt:SigningKey must be at least 32 characters. Provide it via " +
                            "environment variable or secret store — never commit it.");
                    }

                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = jwt["Issuer"] ?? "squad-status-board",
                        ValidAudience = jwt["Audience"] ?? "squad-status-board-web",
                        IssuerSigningKey = new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(signingKey)),
                        // No tolerance for expiry drift beyond a few seconds.
                        ClockSkew = TimeSpan.FromSeconds(30)
                    };
                }

                // SignalR cannot set an Authorization header on the WebSocket handshake,
                // so the token arrives as a query parameter for hub routes only.
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;

                        if (!string.IsNullOrEmpty(accessToken)
                            && path.StartsWithSegments("/hubs"))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            // Every endpoint requires authentication unless it opts out with
            // [AllowAnonymous] — a new controller is secure by default rather than
            // accidentally public.
            options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;
    }
}
