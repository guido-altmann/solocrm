using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using MudBlazor.Services;
using Serilog;
using Serilog.Formatting.Compact;
using SoloCrm.Application;
using SoloCrm.Infrastructure;
using SoloCrm.Infrastructure.Identity;
using SoloCrm.Infrastructure.Persistence;
using SoloCrm.Web.Components;
using SoloCrm.Web.Components.Account;
using SoloCrm.Web.Components.Shared;
using SoloCrm.Web.Endpoints;
using SoloCrm.Web.Hosting;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new RenderedCompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSerilog((services, logger) => logger
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(new RenderedCompactJsonFormatter()));

    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    builder.Services.AddMudServices();
    builder.Services.AddScoped<QuickAddService>();
    builder.Services.AddScoped<CommandPaletteService>();

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddOutboxProcessing(builder.Configuration);

    builder.Services.AddReverseProxySupport(builder.Configuration);
    builder.Services.AddPersistentDataProtection(builder.Configuration);
    builder.Services.AddCrmHealthChecks();
    builder.Services.AddLoginRateLimiting();
    builder.Services.AddCrmApi();

    builder.Services.AddCascadingAuthenticationState();
    builder.Services.AddScoped<IdentityRedirectManager>();
    builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

    builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = IdentityConstants.ApplicationScheme;
            options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
        })
        .AddApiKey()
        .AddIdentityCookies();

    // Every endpoint requires a signed-in user unless it opts out with [AllowAnonymous].
    builder.Services.AddAuthorizationBuilder()
        .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
        .AddApiKeyPolicy();

    builder.Services.AddDatabaseDeveloperPageExceptionFilter();

    builder.Services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.SignIn.RequireConfirmedAccount = true;
            options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
        })
        .AddEntityFrameworkStores<CrmDbContext>()
        .AddSignInManager()
        .AddDefaultTokenProviders();

    builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
    builder.Services.AddAdminSeeding(builder.Configuration);

    var app = builder.Build();

    // Before the hosted services (admin seeding) start, which already need the schema.
    await app.MigrateDatabaseInDevelopmentAsync();

    // Must run first so scheme, host and client IP reflect the original request behind Traefik.
    app.UseForwardedHeaders();

    if (app.Environment.IsDevelopment())
    {
        app.UseMigrationsEndPoint();
    }
    else
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
    }

    // The API answers unhandled exceptions with Problem Details instead of the error page.
    app.UseWhen(context => context.Request.Path.StartsWithSegments(ApiEndpoints.BasePath), api => api.UseExceptionHandler());

    app.UseSerilogRequestLogging();
    app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
    app.UseHttpsRedirection();
    // The rate limiter runs before authentication, so that requests over the limit cost no key lookup.
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    app.UseAntiforgery();

    app.MapCrmHealthChecks();
    app.MapCrmApi();
    app.MapStaticAssets().AllowAnonymous();
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    // Add additional endpoints required by the Identity /Account Razor components.
    app.MapAdditionalIdentityEndpoints();

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Entry point; exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program;
