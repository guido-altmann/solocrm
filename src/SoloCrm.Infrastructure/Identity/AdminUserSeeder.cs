using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SoloCrm.Infrastructure.Identity;

/// <summary>
/// Creates the single admin user on startup if no user exists yet (US-21 AK1).
/// Runs before the server accepts requests; a failure aborts startup.
/// </summary>
internal sealed partial class AdminUserSeeder(
    IServiceScopeFactory scopeFactory,
    IOptions<AdminOptions> options,
    ILogger<AdminUserSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (await userManager.Users.AnyAsync(cancellationToken))
        {
            LogAdminSeedSkipped(logger);
            return;
        }

        var admin = options.Value;
        if (string.IsNullOrWhiteSpace(admin.Email) || string.IsNullOrWhiteSpace(admin.InitialPassword))
        {
            throw new InvalidOperationException(
                "No user exists and 'Admin:Email' / 'Admin:InitialPassword' are not configured. " +
                "Set Admin__Email and Admin__InitialPassword to create the initial admin user.");
        }

        var user = new ApplicationUser
        {
            UserName = admin.Email,
            Email = admin.Email,
            EmailConfirmed = true,
        };

        var result = await userManager.CreateAsync(user, admin.InitialPassword);
        if (!result.Succeeded)
        {
            // Error codes only: descriptions may echo user input.
            var errors = string.Join(", ", result.Errors.Select(e => e.Code));
            throw new InvalidOperationException($"Admin user could not be created: {errors}");
        }

        LogAdminCreated(logger, user.Id);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Debug, Message = "Admin seed skipped: a user already exists")]
    private static partial void LogAdminSeedSkipped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin user {UserId} created")]
    private static partial void LogAdminCreated(ILogger logger, string userId);
}
