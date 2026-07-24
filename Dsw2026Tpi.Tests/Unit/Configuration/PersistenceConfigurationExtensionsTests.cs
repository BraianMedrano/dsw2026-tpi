using Dsw2026Tpi.Api.Configurations;
using Dsw2026Tpi.Data;
using Dsw2026Tpi.Data.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dsw2026Tpi.Tests.Unit.Configuration;

public class PersistenceConfigurationExtensionsTests
{
    [Fact]
    public void AddApplicationPersistence_UsesSqliteForBothContexts()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddApplicationPersistence(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var domainContext = scope.ServiceProvider.GetRequiredService<Dsw2026TpiDbContext>();
        var identityContext = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();

        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", domainContext.Database.ProviderName);
        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", identityContext.Database.ProviderName);
    }
}
