using Dsw2026Tpi.Api.Services;
using Dsw2026Tpi.CrossCutting.Identity;
using Dsw2026Tpi.Data;
using Dsw2026Tpi.Data.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Dsw2026Tpi.Tests.Integration.Authentication;

public sealed class ApplicationStartupInitializerTests
{
    private const string AdministratorEmail = "administrator@example.com";
    private const string AdministratorPassword = "Admin1!x";

    [Fact]
    public async Task InitializeAsync_DevelopmentFreshDatabase_MigratesBothContextsBeforeBootstrap()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            await using var provider = CreateProvider(databasePath, Environments.Development);

            await InitializeApplicationAsync(provider);

            await using var scope = provider.CreateAsyncScope();
            var domainContext = scope.ServiceProvider.GetRequiredService<Dsw2026TpiDbContext>();
            var authenticationContext =
                scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var administrator = Assert.IsType<ApplicationUser>(
                await userManager.FindByEmailAsync(AdministratorEmail));

            Assert.Empty(await domainContext.Database.GetPendingMigrationsAsync());
            Assert.Empty(await authenticationContext.Database.GetPendingMigrationsAsync());
            Assert.True(await userManager.IsInRoleAsync(administrator, Roles.Administrator));
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task InitializeAsync_NonDevelopmentFreshDatabase_RequiresExplicitProvisioning()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            await using var provider = CreateProvider(databasePath, Environments.Production);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => InitializeApplicationAsync(provider));

            Assert.Contains("debe estar provisionado", exception.Message);
            await using var scope = provider.CreateAsyncScope();
            var domainContext = scope.ServiceProvider.GetRequiredService<Dsw2026TpiDbContext>();
            var authenticationContext =
                scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
            Assert.Empty(await domainContext.Database.GetAppliedMigrationsAsync());
            Assert.Empty(await authenticationContext.Database.GetAppliedMigrationsAsync());
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task InitializeAsync_ConcurrentBootstrap_ConvergesToSingleAdministrator()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            await using var provider = CreateProvider(databasePath, Environments.Development);
            await ApplyMigrationsAsync(provider);

            await Task.WhenAll(
                InitializeAdministratorAsync(provider),
                InitializeAdministratorAsync(provider));

            await using var scope = provider.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var administrators = await userManager.GetUsersInRoleAsync(Roles.Administrator);
            var administrator = Assert.Single(administrators);
            Assert.Equal(AdministratorEmail, administrator.Email);
            Assert.Single(await userManager.Users.ToListAsync());
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    private static ServiceProvider CreateProvider(string databasePath, string environmentName)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InitialAdministrator:Email"] = AdministratorEmail,
                ["InitialAdministrator:Password"] = AdministratorPassword
            })
            .Build();
        var connectionString = $"Data Source={databasePath};Default Timeout=5;Pooling=False";
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(environmentName));
        services.AddLogging();
        services.AddDbContext<Dsw2026TpiDbContext>(options => options.UseSqlite(connectionString));
        services.AddDbContext<AuthenticationDbContext>(options => options.UseSqlite(connectionString));
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
        services.AddScoped<ApplicationStartupInitializer>();
        return services.BuildServiceProvider();
    }

    private static async Task ApplyMigrationsAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<Dsw2026TpiDbContext>()
            .Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>()
            .Database.MigrateAsync();
    }

    private static async Task InitializeApplicationAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationStartupInitializer>()
            .InitializeAsync();
    }

    private static async Task InitializeAdministratorAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<InitialAdministratorBootstrapper>()
            .InitializeAsync();
    }

    private static string CreateDatabasePath()
    {
        return Path.Combine(Path.GetTempPath(), $"dsw2026-auth-{Guid.NewGuid():N}.db");
    }

    private static void DeleteDatabase(string databasePath)
    {
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Dsw2026Tpi.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
