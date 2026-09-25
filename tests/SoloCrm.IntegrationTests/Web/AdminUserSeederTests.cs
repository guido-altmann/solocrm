using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Infrastructure.Identity;
using static SoloCrm.IntegrationTests.Web.CrmWebApplicationFactory;

namespace SoloCrm.IntegrationTests.Web;

[Trait("Category", "Integration")]
[Collection(WebHostTests.Name)]
public sealed class AdminUserSeederTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task StartAsync_NoUserExists_CreatesConfirmedAdmin()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await CreateDatabaseAsync(postgres, ct);

        await using (var factory = new CrmWebApplicationFactory(connectionString, AdminEmail, AdminPassword))
        {
            factory.CreateClient();
        }

        await using var db = CreateDbContext(connectionString);
        var user = await db.Users.SingleAsync(ct);
        user.Email.Should().Be(AdminEmail);
        user.UserName.Should().Be(AdminEmail);
        user.EmailConfirmed.Should().BeTrue();
        new PasswordHasher<ApplicationUser>().VerifyHashedPassword(user, user.PasswordHash!, AdminPassword)
            .Should().NotBe(PasswordVerificationResult.Failed);
    }

    [Fact]
    public async Task StartAsync_UserAlreadyExists_DoesNotCreateAdmin()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await CreateDatabaseAsync(postgres, ct);

        // First start seeds the admin, second start with other credentials must leave it untouched.
        await using (var factory = new CrmWebApplicationFactory(connectionString, AdminEmail, AdminPassword))
        {
            factory.CreateClient();
        }

        await using (var factory = new CrmWebApplicationFactory(connectionString, "other@example.test", "Other-Passw0rd!"))
        {
            factory.CreateClient();
        }

        await using var db = CreateDbContext(connectionString);
        var emails = await db.Users.Select(u => u.Email).ToListAsync(ct);
        emails.Should().Equal(AdminEmail);
    }

    [Fact]
    public async Task StartAsync_NoUserAndNoAdminConfig_FailsStartup()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await CreateDatabaseAsync(postgres, ct);

        await using var factory = new CrmWebApplicationFactory(connectionString, adminEmail: null, adminPassword: null);

        var start = () => factory.CreateClient();

        start.Should().Throw<InvalidOperationException>().WithMessage("*Admin__Email*");
    }

    [Fact]
    public async Task StartAsync_PasswordViolatesPolicy_FailsStartupWithoutCreatingUser()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await CreateDatabaseAsync(postgres, ct);

        await using (var factory = new CrmWebApplicationFactory(connectionString, AdminEmail, "short"))
        {
            var start = () => factory.CreateClient();

            start.Should().Throw<InvalidOperationException>().WithMessage("*PasswordTooShort*");
        }

        await using var db = CreateDbContext(connectionString);
        (await db.Users.AnyAsync(ct)).Should().BeFalse();
    }
}
