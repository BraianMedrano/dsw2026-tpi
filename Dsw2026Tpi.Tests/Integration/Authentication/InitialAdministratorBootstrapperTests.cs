using Dsw2026Tpi.Api.Services;
using Dsw2026Tpi.CrossCutting.Identity;
using Dsw2026Tpi.Data.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dsw2026Tpi.Tests.Integration.Authentication;

public sealed class InitialAdministratorBootstrapperTests : IAsyncLifetime
{
    private const string AdministratorEmail = "administrator@example.com";
    private const string AdministratorPassword = "Admin1!x";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    [Fact]
    public async Task InitializeAsync_CreatesAdministratorOnFirstInitialization()
    {
        await using var provider = await CreateProvider(
            AdministratorEmail,
            AdministratorPassword);

        await InitializeAdministrator(provider);

        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var user = await userManager.FindByEmailAsync(AdministratorEmail);

        Assert.NotNull(user);
        Assert.True(await roleManager.RoleExistsAsync(Roles.Administrator));
        Assert.True(await userManager.IsInRoleAsync(user, Roles.Administrator));
        Assert.True(await userManager.CheckPasswordAsync(user, AdministratorPassword));
    }

    [Fact]
    public async Task InitializeAsync_DoesNotDuplicateOrUpdateExistingAdministrator()
    {
        await using var provider = await CreateProvider(
            AdministratorEmail,
            AdministratorPassword);
        await InitializeAdministrator(provider);

        string originalPasswordHash;
        DateTime originalUpdatedAt;
        await using (var scope = provider.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = Assert.IsType<ApplicationUser>(
                await userManager.FindByEmailAsync(AdministratorEmail));
            originalPasswordHash = user.PasswordHash!;
            originalUpdatedAt = user.UpdatedAt;
        }

        await InitializeAdministrator(provider);

        await using var verificationScope = provider.CreateAsyncScope();
        var verificationUserManager =
            verificationScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var administrators =
            await verificationUserManager.GetUsersInRoleAsync(Roles.Administrator);
        var administrator = Assert.Single(administrators);
        Assert.Equal(originalPasswordHash, administrator.PasswordHash);
        Assert.Equal(originalUpdatedAt, administrator.UpdatedAt);
    }

    [Theory]
    [InlineData(null, AdministratorPassword)]
    [InlineData(AdministratorEmail, null)]
    [InlineData("not-an-email", AdministratorPassword)]
    [InlineData(AdministratorEmail, "weak")]
    public async Task InitializeAsync_RejectsMissingOrInvalidConfiguration(
        string? email,
        string? password)
    {
        await using var provider = await CreateProvider(email, password);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => InitializeAdministrator(provider));

        Assert.Contains("InitialAdministrator", exception.Message);
        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Empty(await userManager.Users.ToListAsync());
    }

    [Fact]
    public async Task InitializeAsync_OtherAdministratorExists_CreatesConfiguredAdministrator()
    {
        await using var provider = await CreateProvider(
            AdministratorEmail,
            AdministratorPassword);
        await using (var scope = provider.CreateAsyncScope())
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            Assert.True((await roleManager.CreateAsync(
                new IdentityRole(Roles.Administrator))).Succeeded);

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var now = DateTime.UtcNow;
            var otherAdministrator = new ApplicationUser
            {
                UserName = "other-administrator@example.com",
                Email = "other-administrator@example.com",
                CreatedAt = now,
                UpdatedAt = now
            };
            Assert.True((await userManager.CreateAsync(
                otherAdministrator,
                AdministratorPassword)).Succeeded);
            Assert.True((await userManager.AddToRoleAsync(
                otherAdministrator,
                Roles.Administrator)).Succeeded);
        }

        await InitializeAdministrator(provider);

        await using var verificationScope = provider.CreateAsyncScope();
        var verificationUserManager =
            verificationScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var configuredAdministrator = Assert.IsType<ApplicationUser>(
            await verificationUserManager.FindByEmailAsync(AdministratorEmail));
        Assert.True(await verificationUserManager.IsInRoleAsync(
            configuredAdministrator,
            Roles.Administrator));
        Assert.Equal(2, (await verificationUserManager.GetUsersInRoleAsync(
            Roles.Administrator)).Count);
    }

    [Fact]
    public async Task InitializeAsync_DoesNotElevateExistingConflictingAccount()
    {
        await using var provider = await CreateProvider(
            AdministratorEmail,
            AdministratorPassword);
        await using (var scope = provider.CreateAsyncScope())
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            Assert.True((await roleManager.CreateAsync(new IdentityRole(Roles.Patient))).Succeeded);

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var now = DateTime.UtcNow;
            var user = new ApplicationUser
            {
                UserName = AdministratorEmail,
                Email = AdministratorEmail,
                CreatedAt = now,
                UpdatedAt = now
            };
            Assert.True((await userManager.CreateAsync(user, AdministratorPassword)).Succeeded);
            Assert.True((await userManager.AddToRoleAsync(user, Roles.Patient)).Succeeded);
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => InitializeAdministrator(provider));

        Assert.Contains("sin el rol de administrador", exception.Message);
        await using var verificationScope = provider.CreateAsyncScope();
        var verificationUserManager =
            verificationScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var existingUser = Assert.IsType<ApplicationUser>(
            await verificationUserManager.FindByEmailAsync(AdministratorEmail));
        Assert.False(await verificationUserManager.IsInRoleAsync(
            existingUser,
            Roles.Administrator));
        Assert.True(await verificationUserManager.IsInRoleAsync(existingUser, Roles.Patient));
    }

    [Fact]
    public async Task InitializeAsync_RoleAssignmentFailureRollsBackNewUser()
    {
        await using var provider = await CreateProvider(
            AdministratorEmail,
            AdministratorPassword);
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
            await context.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER RejectAdministratorRole " +
                "BEFORE INSERT ON UsersRoles " +
                "BEGIN SELECT RAISE(FAIL, 'forced role assignment failure'); END;");
        }

        await Assert.ThrowsAsync<DbUpdateException>(
            () => InitializeAdministrator(provider));

        await using var verificationScope = provider.CreateAsyncScope();
        var verificationUserManager =
            verificationScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Null(await verificationUserManager.FindByEmailAsync(AdministratorEmail));
        Assert.Empty(await verificationUserManager.Users.ToListAsync());
    }

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private async Task<ServiceProvider> CreateProvider(string? email, string? password)
    {
        var values = new Dictionary<string, string?>();
        if (email is not null)
        {
            values["InitialAdministrator:Email"] = email;
        }
        if (password is not null)
        {
            values["InitialAdministrator:Password"] = password;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddDbContext<AuthenticationDbContext>(
            options => options.UseSqlite(_connection));
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireDigit = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AuthenticationDbContext>();
        services.AddScoped<InitialAdministratorBootstrapper>();

        var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        await context.Database.EnsureCreatedAsync();
        return provider;
    }

    private static async Task InitializeAdministrator(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var bootstrapper =
            scope.ServiceProvider.GetRequiredService<InitialAdministratorBootstrapper>();
        await bootstrapper.InitializeAsync();
    }
}
