using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Dsw2026Tpi.Api;
using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Data;
using Dsw2026Tpi.Data.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Dsw2026Tpi.Tests.Integration.Authentication;

public sealed class JwtRevocationApiTests
{
    private const string AdministratorEmail = "administrator@example.com";
    private const string AdministratorPassword = "Admin1!x";

    [Fact]
    public async Task Logout_RevokesOnlyTheCurrentTokenAndPersistsAcrossHostRestart()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"dsw2026-jwt-revocation-{Guid.NewGuid():N}.db");
        string firstToken;
        string secondToken;
        string firstJti;

        try
        {
            using (var firstFactory = new JwtRevocationApiFactory(databasePath))
            using (var client = firstFactory.CreateClient())
            {
                firstToken = await Login(client);
                secondToken = await Login(client);
                firstJti = new JwtSecurityTokenHandler().ReadJwtToken(firstToken).Id;
                var secondJti = new JwtSecurityTokenHandler().ReadJwtToken(secondToken).Id;
                Assert.NotEqual(firstJti, secondJti);

                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", firstToken);
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health-check")).StatusCode);

                var logoutResponse = await client.PostAsync("/api/auth/logout", content: null);

                Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);
                Assert.Equal("ok", await logoutResponse.Content.ReadAsStringAsync());
                Assert.Equal(
                    HttpStatusCode.Unauthorized,
                    (await client.GetAsync("/health-check")).StatusCode);

                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", secondToken);
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health-check")).StatusCode);

                await using var scope = firstFactory.Services.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
                var persisted = await context.RevokedTokens.AsNoTracking().SingleAsync();
                Assert.Equal(firstJti, persisted.Jti);
                Assert.True(persisted.ExpiresAtUtc > DateTime.UtcNow);
            }

            using var restartedFactory = new JwtRevocationApiFactory(databasePath);
            using var restartedClient = restartedFactory.CreateClient();
            restartedClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", firstToken);
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await restartedClient.GetAsync("/health-check")).StatusCode);

            restartedClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", secondToken);
            Assert.Equal(
                HttpStatusCode.OK,
                (await restartedClient.GetAsync("/health-check")).StatusCode);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteIfExists(databasePath);
            DeleteIfExists($"{databasePath}.domain");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    public async Task ProtectedEndpoint_RejectsMissingOrMalformedBearerToken(string token)
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"dsw2026-jwt-malformed-{Guid.NewGuid():N}.db");

        try
        {
            using var factory = new JwtRevocationApiFactory(databasePath);
            using var client = factory.CreateClient();
            if (token.Length > 0)
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
            }

            var response = await client.GetAsync("/health-check");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteIfExists(databasePath);
            DeleteIfExists($"{databasePath}.domain");
        }
    }

    [Fact]
    public async Task ProtectedEndpoint_RejectsOtherwiseValidTokenWithoutJti()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"dsw2026-jwt-missing-jti-{Guid.NewGuid():N}.db");

        try
        {
            using var factory = new JwtRevocationApiFactory(databasePath);
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                JwtRevocationApiFactory.CreateTokenWithoutJti());

            var response = await client.GetAsync("/health-check");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteIfExists(databasePath);
            DeleteIfExists($"{databasePath}.domain");
        }
    }

    private static async Task<string> Login(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/admin/login",
            new LoginAdminModel.Request(AdministratorEmail, AdministratorPassword));
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<LoginAdminModel.Response>();
        return result?.Token ??
            throw new InvalidOperationException("Login returned no token.");
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

public sealed class JwtRevocationApiFactory : WebApplicationFactory<Program>
{
    private const string AdministratorEmail = "administrator@example.com";
    private const string AdministratorPassword = "Admin1!x";
    private const string JwtKey = "jwt-revocation-tests-signing-key-with-more-than-thirty-two-bytes";
    private readonly string _identityDatabasePath;
    private readonly SqliteConnection _identityConnection;
    private readonly SqliteConnection _domainConnection;

    public JwtRevocationApiFactory(string identityDatabasePath)
    {
        _identityDatabasePath = identityDatabasePath;
        _identityConnection = new SqliteConnection(
            $"Data Source={identityDatabasePath};Pooling=False");
        _domainConnection = new SqliteConnection(
            $"Data Source={identityDatabasePath}.domain;Pooling=False");
        _identityConnection.Open();
        _domainConnection.Open();

        var options = new DbContextOptionsBuilder<AuthenticationDbContext>()
            .UseSqlite(_identityConnection)
            .Options;
        using var context = new AuthenticationDbContext(options);
        context.Database.EnsureCreated();
    }

    public static string CreateTokenWithoutJti()
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "jwt-revocation-tests",
            audience: "jwt-revocation-tests",
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("AllowedHosts", "*");
        builder.UseSetting(
            "ConnectionStrings:DefaultConnection",
            $"Data Source={_identityDatabasePath};Pooling=False");
        builder.UseSetting("Jwt:Key", JwtKey);
        builder.UseSetting("Jwt:Issuer", "jwt-revocation-tests");
        builder.UseSetting("Jwt:Audience", "jwt-revocation-tests");
        builder.UseSetting("Jwt:ExpiresInMinutes", "30");
        builder.UseSetting("InitialAdministrator:Email", AdministratorEmail);
        builder.UseSetting("InitialAdministrator:Password", AdministratorPassword);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    $"Data Source={_identityDatabasePath};Pooling=False",
                ["AllowedHosts"] = "*",
                ["Jwt:Key"] = JwtKey,
                ["Jwt:Issuer"] = "jwt-revocation-tests",
                ["Jwt:Audience"] = "jwt-revocation-tests",
                ["Jwt:ExpiresInMinutes"] = "30",
                ["InitialAdministrator:Email"] = AdministratorEmail,
                ["InitialAdministrator:Password"] = AdministratorPassword
            });
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<Dsw2026TpiDbContext>();
            services.RemoveAll<DbContextOptions<Dsw2026TpiDbContext>>();
            services.AddDbContext<Dsw2026TpiDbContext>(
                options => options.UseSqlite(_domainConnection));

            services.RemoveAll<AuthenticationDbContext>();
            services.RemoveAll<DbContextOptions<AuthenticationDbContext>>();
            services.AddDbContext<AuthenticationDbContext>(
                options => options.UseSqlite(_identityConnection));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<Dsw2026TpiDbContext>()
            .Database.EnsureCreated();
        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _identityConnection.Dispose();
            _domainConnection.Dispose();
        }
    }
}
