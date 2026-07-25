using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Dsw2026Tpi.Api;
using Dsw2026Tpi.Api.Controllers;
using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.Application.Services;
using Dsw2026Tpi.CrossCutting.Identity;
using Dsw2026Tpi.Data;
using Dsw2026Tpi.Data.Identity;
using Dsw2026Tpi.Domain.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dsw2026Tpi.Tests.Integration.Specialties;

public class SpecialtiesApiTests : IClassFixture<SpecialtiesApiFactory>
{
    private readonly SpecialtiesApiFactory _factory;

    public SpecialtiesApiTests(SpecialtiesApiFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(Roles.Patient, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Administrator, HttpStatusCode.OK)]
    public async Task GetAll_EnforcesAdministratorPolicy(string? role, HttpStatusCode expectedStatus)
    {
        using var client = _factory.CreateClientForRole(role);

        var response = await client.GetAsync("/api/specialties");

        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Fact]
    public void CompositionRoot_ActivatesSpecialtyServiceAndController()
    {
        using var scope = _factory.Services.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<ISpecialtyService>();
        var controller = ActivatorUtilities.CreateInstance<SpecialtiesController>(scope.ServiceProvider);

        Assert.IsType<SpecialtyService>(service);
        Assert.NotNull(controller);
    }

    [Fact]
    public async Task Create_ReturnsResolvableResourceLocation()
    {
        using var client = _factory.CreateAdministratorClient();
        var request = ValidRequest(UniqueName("Clínica"));

        var createResponse = await client.PostAsJsonAsync("/api/specialties", request);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<SpecialtyModel.Response>();
        Assert.NotNull(created);
        Assert.Equal($"/api/specialties/{created.Id}", createResponse.Headers.Location?.PathAndQuery);

        var getResponse = await client.GetAsync(createResponse.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(created, await getResponse.Content.ReadFromJsonAsync<SpecialtyModel.Response>());
    }

    [Theory]
    [InlineData("   ", "Descripción válida")]
    [InlineData("ab", "Descripción válida")]
    [InlineData(
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        "Descripción válida")]
    [InlineData("Nombre válido", "   ")]
    [InlineData("Nombre válido", "123456789")]
    [InlineData(
        "Nombre válido",
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Create_RejectsInvalidRequiredAndLengthBoundaries(string name, string description)
    {
        using var client = _factory.CreateAdministratorClient();

        var response = await client.PostAsJsonAsync(
            "/api/specialties",
            new SpecialtyModel.Request(name, description));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertValidationError(response);
    }

    [Theory]
    [InlineData(3, 10)]
    [InlineData(100, 100)]
    public async Task Create_AcceptsInclusiveLengthBoundaries(int nameLength, int descriptionLength)
    {
        using var client = _factory.CreateAdministratorClient();
        var request = new SpecialtyModel.Request(
            new string('N', nameLength),
            new string('D', descriptionLength));

        var response = await client.PostAsJsonAsync("/api/specialties", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData("?pageIndex=-1")]
    [InlineData("?pageIndex=1000001")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?name=ab")]
    public async Task GetAll_RejectsInvalidPaginationAndFilterBoundaries(string query)
    {
        using var client = _factory.CreateAdministratorClient();

        var response = await client.GetAsync($"/api/specialties{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertValidationError(response);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 100)]
    [InlineData(1_000_000, 1)]
    public async Task GetAll_AcceptsInclusivePaginationBoundaries(int pageIndex, int pageSize)
    {
        using var client = _factory.CreateAdministratorClient();

        var response = await client.GetAsync(
            $"/api/specialties?pageIndex={pageIndex}&pageSize={pageSize}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_FiltersCountsAndPaginatesInStableOrder()
    {
        using var client = _factory.CreateAdministratorClient();
        var marker = Guid.NewGuid().ToString("N")[..8];
        await Create(client, $"Cardio {marker} C");
        await Create(client, $"Cardio {marker} A");
        await Create(client, $"Cardio {marker} B");

        var firstResponse = await client.GetAsync(
            $"/api/specialties?name={Uri.EscapeDataString(marker)}&pageSize=2&pageIndex=0");
        var secondResponse = await client.GetAsync(
            $"/api/specialties?name={Uri.EscapeDataString(marker)}&pageSize=2&pageIndex=1");

        firstResponse.EnsureSuccessStatusCode();
        secondResponse.EnsureSuccessStatusCode();
        var first = await firstResponse.Content.ReadFromJsonAsync<SpecialtyModel.PagedResponse>();
        var second = await secondResponse.Content.ReadFromJsonAsync<SpecialtyModel.PagedResponse>();
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(3, first.Total);
        Assert.Equal(2, first.Data.Count);
        Assert.Single(second.Data);
        Assert.Equal(
            [$"Cardio {marker} A", $"Cardio {marker} B"],
            first.Data.Select(item => item.Name));
        Assert.Equal($"Cardio {marker} C", second.Data[0].Name);
    }

    [Fact]
    public async Task Update_ChangesAnActiveSpecialty()
    {
        using var client = _factory.CreateAdministratorClient();
        var created = await Create(client, UniqueName("Trauma"));
        var updatedRequest = ValidRequest(UniqueName("Trauma actualizada"));

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/specialties/{created.Id}",
            updatedRequest);

        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);
        var updated = await client.GetFromJsonAsync<SpecialtyModel.Response>(
            $"/api/specialties/{created.Id}");
        Assert.NotNull(updated);
        Assert.Equal(updatedRequest.Name, updated.Name);
        Assert.Equal(updatedRequest.Description, updated.Description);
    }

    [Fact]
    public async Task Delete_PersistsLogicalDeletionAndExcludesTheSpecialty()
    {
        using var client = _factory.CreateAdministratorClient();
        var marker = Guid.NewGuid().ToString("N")[..8];
        var created = await Create(client, $"Neurología {marker}");

        var deleteResponse = await client.DeleteAsync($"/api/specialties/{created.Id}");
        var getResponse = await client.GetAsync($"/api/specialties/{created.Id}");
        var listResponse = await client.GetFromJsonAsync<SpecialtyModel.PagedResponse>(
            $"/api/specialties?name={marker}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        Assert.NotNull(listResponse);
        Assert.Equal(0, listResponse.Total);
        Assert.Empty(listResponse.Data);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<Dsw2026TpiDbContext>();
        var persisted = await context.Set<Speciality>()
            .IgnoreQueryFilters()
            .SingleAsync(speciality => speciality.Id == created.Id);
        Assert.True(persisted.Deleted);
    }

    [Fact]
    public async Task DeletedOrMissingSpecialty_ReturnsUniformNotFound()
    {
        using var client = _factory.CreateAdministratorClient();
        var created = await Create(client, UniqueName("Pediatría"));
        await client.DeleteAsync($"/api/specialties/{created.Id}");

        var repeatedDelete = await client.DeleteAsync($"/api/specialties/{created.Id}");
        var updateAfterDelete = await client.PutAsJsonAsync(
            $"/api/specialties/{created.Id}",
            ValidRequest(UniqueName("Pediatría actualizada")));
        var missingUpdate = await client.PutAsJsonAsync(
            $"/api/specialties/{Guid.NewGuid()}",
            ValidRequest(UniqueName("Inexistente")));

        await AssertNotFound(repeatedDelete);
        await AssertNotFound(updateAfterDelete);
        await AssertNotFound(missingUpdate);
    }

    private static SpecialtyModel.Request ValidRequest(string name) =>
        new(name, "Descripción válida de la especialidad");

    private static string UniqueName(string prefix) =>
        $"{prefix} {Guid.NewGuid():N}";

    private static async Task<SpecialtyModel.Response> Create(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/specialties", ValidRequest(name));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SpecialtyModel.Response>())!;
    }

    private static async Task AssertValidationError(HttpResponseMessage response)
    {
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = payload.RootElement;
        Assert.Equal("VALIDATION_ERROR", root.GetProperty("errorCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("message").GetString()));
        Assert.NotEmpty(root.GetProperty("details").EnumerateArray());
    }

    private static async Task AssertNotFound(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = payload.RootElement;
        Assert.Equal("ENTITY_NOTFOUND", root.GetProperty("errorCode").GetString());
        Assert.Contains("Especialidad", root.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("details").ValueKind);
    }
}

public sealed class SpecialtiesApiFactory : WebApplicationFactory<Program>
{
    private const string AdministratorEmail = "administrator@example.com";
    private const string AdministratorPassword = "Admin1!x";
    private const string AuthenticationScheme = "SpecialtiesTest";
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly SqliteConnection _identityConnection = new("Data Source=:memory:");

    public SpecialtiesApiFactory()
    {
        _connection.Open();
        _identityConnection.Open();

        // Program ejecuta el bootstrap antes de crear el cliente; por eso Identity
        // necesita su esquema y credenciales de prueba desde el inicio del host.
        var options = new DbContextOptionsBuilder<AuthenticationDbContext>()
            .UseSqlite(_identityConnection)
            .Options;
        using var context = new AuthenticationDbContext(options);
        context.Database.EnsureCreated();
    }

    public HttpClient CreateAdministratorClient() =>
        CreateClientForRole(Roles.Administrator);

    public HttpClient CreateClientForRole(string? role)
    {
        var client = CreateClient();
        if (role is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RoleHeader, role);
        }

        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("AllowedHosts", "*");
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Data Source=:memory:");
        builder.UseSetting("Jwt:Key", "01234567890123456789012345678901");
        builder.UseSetting("Jwt:Issuer", "test-issuer");
        builder.UseSetting("Jwt:Audience", "test-audience");
        builder.UseSetting("InitialAdministrator:Email", AdministratorEmail);
        builder.UseSetting("InitialAdministrator:Password", AdministratorPassword);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:",
                ["AllowedHosts"] = "*",
                ["Jwt:Key"] = "01234567890123456789012345678901",
                ["Jwt:Issuer"] = "test-issuer",
                ["Jwt:Audience"] = "test-audience",
                ["InitialAdministrator:Email"] = AdministratorEmail,
                ["InitialAdministrator:Password"] = AdministratorPassword
            });
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<Dsw2026TpiDbContext>();
            services.RemoveAll<DbContextOptions<Dsw2026TpiDbContext>>();
            services.AddSingleton(_connection);
            services.AddDbContext<Dsw2026TpiDbContext>(options => options.UseSqlite(_connection));

            services.RemoveAll<AuthenticationDbContext>();
            services.RemoveAll<DbContextOptions<AuthenticationDbContext>>();
            services.AddDbContext<AuthenticationDbContext>(
                options => options.UseSqlite(_identityConnection));

            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = AuthenticationScheme;
                    options.DefaultChallengeScheme = AuthenticationScheme;
                    options.DefaultForbidScheme = AuthenticationScheme;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    AuthenticationScheme,
                    _ => { });
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<Dsw2026TpiDbContext>().Database.EnsureCreated();
        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
            _identityConnection.Dispose();
        }
    }
}

public sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string RoleHeader = "X-Test-Role";

    public TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(RoleHeader, out var role))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "specialties-test-user"),
                new Claim(ClaimTypes.Role, role.ToString())
            ],
            Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
