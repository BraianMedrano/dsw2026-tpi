using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dsw2026Tpi.Api;
using Dsw2026Tpi.Api.Configurations;
using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.CrossCutting.Resources;
using Dsw2026Tpi.Data;
using Dsw2026Tpi.Data.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Dsw2026Tpi.Tests.Integration.RateLimiting;

public sealed class RateLimitingTests
{
    private const string AdministratorEmail = "rate-limit-admin@example.com";
    private const string AdministratorPassword = "Admin1!x";

    [Fact]
    public async Task AdminLoginUsesConfiguredIpLimitAndIgnoresForwardedHeader()
    {
        await using var factory = CreateFactory(new()
        {
            ["RateLimiting:AdminLoginPermitLimit"] = "2"
        });
        using var client = factory.CreateClient();

        var first = await PostInvalidAdminLogin(client, "192.0.2.10", "198.51.100.1");
        var second = await PostInvalidAdminLogin(client, "192.0.2.10", "198.51.100.2");
        var rejected = await PostInvalidAdminLogin(client, "192.0.2.10", "198.51.100.3");
        var otherIp = await PostInvalidAdminLogin(client, "192.0.2.11", "198.51.100.3");

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, otherIp.StatusCode);
        await AssertRateLimitError(rejected);
    }

    [Fact]
    public async Task SensitiveRoutesConsumeOnlyTheirOwnBucket()
    {
        await using var factory = CreateFactory(new()
        {
            ["RateLimiting:AdminLoginPermitLimit"] = "1",
            ["RateLimiting:PatientLoginPermitLimit"] = "1",
            ["RateLimiting:AppointmentCreationPermitLimit"] = "1",
            ["RateLimiting:AuthenticatedPermitLimit"] = "1"
        });
        using var client = factory.CreateClient();
        var settings = factory.Services.GetRequiredService<IOptions<ApiRateLimitingOptions>>().Value;
        Assert.Equal(1, settings.AppointmentCreationPermitLimit);
        Assert.Equal(0, settings.QueueLimit);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await PostInvalidAdminLogin(client, "192.0.2.20", "203.0.113.1")).StatusCode);
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            (await PostInvalidAdminLogin(client, "192.0.2.20", "203.0.113.2")).StatusCode);

        var patient = await LoginPatient(client, "one-bucket@example.com", "12345678");
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            (await client.PostAsJsonAsync("/api/auth/patient/login", new { })).StatusCode);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", patient.Token);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/appointments", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/appointments/patient?dni=12345678")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/appointments", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/appointments/patient?dni=12345678")).StatusCode);
    }

    [Fact]
    public async Task GeneralLimitIsPartitionedByAuthenticatedUser()
    {
        await using var factory = CreateFactory(new()
        {
            ["RateLimiting:PatientLoginPermitLimit"] = "5",
            ["RateLimiting:AuthenticatedPermitLimit"] = "1"
        });
        using var loginClient = factory.CreateClient();
        var firstPatient = await LoginPatient(loginClient, "partition-one@example.com", "23456789");
        var secondPatient = await LoginPatient(loginClient, "partition-two@example.com", "34567890");

        using var firstClient = factory.CreateClient();
        firstClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", firstPatient.Token);
        using var secondClient = factory.CreateClient();
        secondClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", secondPatient.Token);

        Assert.Equal(HttpStatusCode.OK, (await firstClient.GetAsync("/api/appointments/patient?dni=23456789")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await firstClient.GetAsync("/api/appointments/patient?dni=23456789")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await secondClient.GetAsync("/api/appointments/patient?dni=34567890")).StatusCode);
    }

    [Fact]
    public async Task StartupRejectsAConfiguredQueue()
    {
        await using var factory = CreateFactory(new()
        {
            ["RateLimiting:QueueLimit"] = "1"
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(nameof(ApiRateLimitingOptions.QueueLimit), exception.ToString());
    }

    private static async Task<HttpResponseMessage> PostInvalidAdminLogin(
        HttpClient client,
        string remoteIp,
        string forwardedIp)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/admin/login")
        {
            Content = JsonContent.Create(new LoginAdminModel.Request(
                "missing@example.com",
                "Wrong1!x"))
        };
        request.Headers.Add(TestRemoteIpStartupFilter.HeaderName, remoteIp);
        request.Headers.Add("X-Forwarded-For", forwardedIp);
        return await client.SendAsync(request);
    }

    private static async Task<LoginPatientModel.Response> LoginPatient(
        HttpClient client,
        string email,
        string dni)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/patient/login",
            new LoginPatientModel.Request(email, dni));
        response.EnsureSuccessStatusCode();
        return Assert.IsType<LoginPatientModel.Response>(
            await response.Content.ReadFromJsonAsync<LoginPatientModel.Response>());
    }

    private static async Task AssertRateLimitError(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            nameof(ErrorCodes.RATE_LIMIT_EXCEEDED),
            body.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal(
            ErrorCodes.RATE_LIMIT_EXCEEDED,
            body.RootElement.GetProperty("message").GetString());
    }

    private static RateLimitingApiFactory CreateFactory(
        Dictionary<string, string?>? overrides = null) => new(
            Path.Combine(
                Path.GetTempPath(),
                $"dsw2026-rate-limiting-{Guid.NewGuid():N}.db"),
            overrides ?? []);

    private sealed class RateLimitingApiFactory(
        string databasePath,
        Dictionary<string, string?> overrides)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            var connectionString =
                $"Data Source={databasePath};Foreign Keys=True;Pooling=False";
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var values = new Dictionary<string, string?>(overrides)
                {
                    ["ConnectionStrings:DefaultConnection"] = connectionString,
                    ["InitialAdministrator:Email"] = AdministratorEmail,
                    ["InitialAdministrator:Password"] = AdministratorPassword
                };
                configuration.AddInMemoryCollection(values);
            });
            builder.ConfigureServices(services =>
                services.AddSingleton<IStartupFilter, TestRemoteIpStartupFilter>());
            builder.ConfigureTestServices(services =>
            {
                // Program registra los DbContext antes de que WebApplicationFactory agregue su configuración.
                services.RemoveAll<Dsw2026TpiDbContext>();
                services.RemoveAll<DbContextOptions<Dsw2026TpiDbContext>>();
                services.AddDbContext<Dsw2026TpiDbContext>(options => options.UseSqlite(connectionString));
                services.RemoveAll<AuthenticationDbContext>();
                services.RemoveAll<DbContextOptions<AuthenticationDbContext>>();
                services.AddDbContext<AuthenticationDbContext>(options => options.UseSqlite(connectionString));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            foreach (var suffix in new[] { string.Empty, "-shm", "-wal" })
            {
                var path = databasePath + suffix;
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    private sealed class TestRemoteIpStartupFilter : IStartupFilter
    {
        public const string HeaderName = "X-Test-Remote-IP";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) =>
            {
                if (context.Request.Headers.TryGetValue(HeaderName, out var value) &&
                    IPAddress.TryParse(value, out var address))
                {
                    context.Connection.RemoteIpAddress = address;
                }
                await continuation();
            });
            next(app);
        };
    }
}
