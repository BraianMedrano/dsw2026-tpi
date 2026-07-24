using Dsw2026Tpi.Api.Configurations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Dsw2026Tpi.Tests.Unit.Configuration;

public class SecurityConfigurationExtensionsTests
{
    [Fact]
    public void AddAppIdentity_RequiresPasswordsWithAtLeastEightCharacters()
    {
        var services = new ServiceCollection();

        services.AddAppIdentity();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<IdentityOptions>>().Value;

        Assert.Equal(8, options.Password.RequiredLength);
    }

    [Fact]
    public async Task AddAppAuthentication_ReturnsUniformErrorForMissingCredentials()
    {
        using var provider = BuildAuthenticationProvider();
        var options = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var context = CreateContext();
        var challenge = new JwtBearerChallengeContext(
            context,
            new AuthenticationScheme(
                JwtBearerDefaults.AuthenticationScheme,
                JwtBearerDefaults.AuthenticationScheme,
                typeof(JwtBearerHandler)),
            options,
            new AuthenticationProperties());

        await options.Events.OnChallenge(challenge);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Contains("\"errorCode\"", await ReadBody(context));
    }

    private static ServiceProvider BuildAuthenticationProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "01234567890123456789012345678901",
                ["Jwt:Issuer"] = "test-issuer",
                ["Jwt:Audience"] = "test-audience"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAppAuthentication(configuration);
        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<string> ReadBody(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync();
    }
}
