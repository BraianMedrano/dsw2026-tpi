using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Dsw2026Tpi.Api;
using Dsw2026Tpi.Application.Dtos;
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

namespace Dsw2026Tpi.Tests.Integration.Doctors;

public class DoctorsApiTests : IClassFixture<DoctorsApiFactory>
{
    private readonly DoctorsApiFactory _factory;

    public DoctorsApiTests(DoctorsApiFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(Roles.Patient, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Administrator, HttpStatusCode.OK)]
    public async Task GetAll_EnforcesAdministratorPolicy(string? role, HttpStatusCode expected)
    {
        using var client = _factory.CreateClientForRole(role);

        var response = await client.GetAsync("/api/doctors");

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Create_ReturnsCreatedAndPersistsNormalizedDoctor()
    {
        var speciality = await CreateSpeciality("Cardiología");
        using var client = _factory.CreateAdministratorClient();

        var response = await client.PostAsJsonAsync(
            "/api/doctors",
            new DoctorModel.Request("  Ana Médica  ", "  MP-100  ", speciality.Id));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<DoctorModel.Response>();
        Assert.NotNull(created);
        Assert.Equal("Ana Médica", created.Name);
        Assert.Equal("MP-100", created.LicenseNumber);
        Assert.Equal(speciality.Id, created.Specialty.Id);
        Assert.Equal(speciality.Name, created.Specialty.Name);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<Dsw2026TpiDbContext>();
        var persisted = await context.Set<Doctor>()
            .Include(doctor => doctor.Speciality)
            .SingleAsync(doctor => doctor.Id == created.Id);
        Assert.True(persisted.IsActive);
        Assert.Equal("Ana Médica", persisted.Name);
        Assert.Equal("MP-100", persisted.LicenseNumber);
        Assert.Equal(speciality.Id, persisted.SpecialityId);
    }

    [Fact]
    public async Task Create_ValidatesAndPersistsTheNormalizedName()
    {
        var speciality = await CreateSpeciality("Normalización");
        using var client = _factory.CreateAdministratorClient();

        var tooShort = await client.PostAsJsonAsync(
            "/api/doctors",
            new DoctorModel.Request(" a ", "MP-NORMALIZADA-1", speciality.Id));
        var accepted = await client.PostAsJsonAsync(
            "/api/doctors",
            new DoctorModel.Request("  abc  ", "MP-NORMALIZADA-2", speciality.Id));
        var rawLongerThanMaximum = await client.PostAsJsonAsync(
            "/api/doctors",
            new DoctorModel.Request(
                $"  {new string('N', DoctorModel.MaxNameLength)}  ",
                "MP-NORMALIZADA-3",
                speciality.Id));

        await AssertValidationError(tooShort);
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Equal(
            "abc",
            (await accepted.Content.ReadFromJsonAsync<DoctorModel.Response>())!.Name);
        Assert.Equal(HttpStatusCode.Created, rawLongerThanMaximum.StatusCode);
        Assert.Equal(
            DoctorModel.MaxNameLength,
            (await rawLongerThanMaximum.Content.ReadFromJsonAsync<DoctorModel.Response>())!.Name.Length);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("ab")]
    [InlineData(
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Create_RejectsInvalidName(string name)
    {
        var speciality = await CreateSpeciality("Clínica");
        using var client = _factory.CreateAdministratorClient();

        var response = await client.PostAsJsonAsync(
            "/api/doctors",
            new DoctorModel.Request(name, "MP-101", speciality.Id));

        await AssertValidationError(response);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(100)]
    public async Task Create_AcceptsInclusiveNameBoundaries(int length)
    {
        var speciality = await CreateSpeciality("Traumatología");
        using var client = _factory.CreateAdministratorClient();

        var response = await client.PostAsJsonAsync(
            "/api/doctors",
            new DoctorModel.Request(new string('N', length), $"MP-{Guid.NewGuid():N}", speciality.Id));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_RejectsMissingOrWhitespaceLicenseNumber(string licenseNumber)
    {
        var speciality = await CreateSpeciality("Pediatría");
        using var client = _factory.CreateAdministratorClient();

        var response = await client.PostAsJsonAsync(
            "/api/doctors",
            new DoctorModel.Request("Nombre válido", licenseNumber, speciality.Id));

        await AssertValidationError(response);
    }

    [Fact]
    public async Task Create_RejectsEmptySpecialityId()
    {
        using var client = _factory.CreateAdministratorClient();

        var response = await client.PostAsJsonAsync(
            "/api/doctors",
            new DoctorModel.Request("Nombre válido", "MP-102", Guid.Empty));

        await AssertValidationError(response);
    }

    [Fact]
    public async Task Create_ReturnsNotFoundForMissingSpeciality()
    {
        using var client = _factory.CreateAdministratorClient();

        var response = await client.PostAsJsonAsync(
            "/api/doctors",
            new DoctorModel.Request("Nombre válido", "MP-103", Guid.NewGuid()));

        await AssertNotFound(response, "Especialidad");
    }

    [Fact]
    public async Task Create_ReturnsNotFoundForDeletedSpeciality()
    {
        var speciality = await CreateSpeciality("Especialidad eliminada", deleted: true);
        using var client = _factory.CreateAdministratorClient();

        var response = await client.PostAsJsonAsync(
            "/api/doctors",
            new DoctorModel.Request("Nombre válido", "MP-104", speciality.Id));

        await AssertNotFound(response, "Especialidad");
    }

    [Theory]
    [InlineData("?pageIndex=-1")]
    [InlineData("?pageIndex=1000001")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    public async Task GetAll_RejectsInvalidPagination(string query)
    {
        using var client = _factory.CreateAdministratorClient();

        var response = await client.GetAsync($"/api/doctors{query}");

        await AssertValidationError(response);
    }

    [Fact]
    public async Task GetAll_FiltersCountsAndPaginatesInStableZeroBasedOrder()
    {
        var speciality = await CreateSpeciality("Orden y paginación");
        using var client = _factory.CreateAdministratorClient();
        var marker = Guid.NewGuid().ToString("N")[..8];
        await CreateDoctor(client, $"Doctor {marker} C", speciality.Id);
        await CreateDoctor(client, $"Doctor {marker} A", speciality.Id);
        await CreateDoctor(client, $"Doctor {marker} B", speciality.Id);

        var first = await GetPage(client, $"?name=%20{marker}%20&pageSize=2&pageIndex=0");
        var second = await GetPage(client, $"?name={marker}&pageSize=2&pageIndex=1");
        var outside = await GetPage(client, $"?name={marker}&pageSize=2&pageIndex=5");

        Assert.Equal(3, first.Total);
        Assert.Equal(3, second.Total);
        Assert.Equal(2, first.Data.Count);
        Assert.Single(second.Data);
        Assert.Equal(
            [$"Doctor {marker} A", $"Doctor {marker} B"],
            first.Data.Select(doctor => doctor.Name));
        Assert.Equal($"Doctor {marker} C", second.Data[0].Name);
        Assert.Equal(5, outside.PageIndex);
        Assert.Equal(3, outside.Total);
        Assert.Empty(outside.Data);
    }

    [Fact]
    public async Task GetAll_ValidatesAndUsesTheNormalizedNameFilter()
    {
        var speciality = await CreateSpeciality("Filtro normalizado");
        using var client = _factory.CreateAdministratorClient();
        var marker = Guid.NewGuid().ToString("N")[..8];
        await CreateDoctor(client, $"Doctor {marker}", speciality.Id);

        var tooShort = await client.GetAsync("/api/doctors?name=%20a%20");
        var whitespace = await client.GetAsync("/api/doctors?name=%20%20%20");
        var normalized = await GetPage(client, $"?name=%20%20{marker}%20%20");

        await AssertValidationError(tooShort);
        await AssertValidationError(whitespace);
        Assert.Equal(1, normalized.Total);
        Assert.Single(normalized.Data);
        Assert.Contains(marker, normalized.Data[0].Name);
    }

    [Fact]
    public async Task GetAll_ExcludesDoctorWhoseSpecialityWasDeletedFromDataAndTotal()
    {
        var speciality = await CreateSpeciality("Especialidad eliminada después del alta");
        using var client = _factory.CreateAdministratorClient();
        var marker = Guid.NewGuid().ToString("N")[..8];
        await CreateDoctor(client, $"Doctor {marker}", speciality.Id);

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<Dsw2026TpiDbContext>();
            var persistedSpeciality = await context.Set<Speciality>()
                .SingleAsync(candidate => candidate.Id == speciality.Id);
            persistedSpeciality.MarkAsDeleted();
            await context.SaveChangesAsync();
        }

        var page = await GetPage(client, $"?name={marker}");

        Assert.Equal(0, page.Total);
        Assert.Empty(page.Data);
    }

    [Fact]
    public async Task Update_ReplacesNameLicenseAndSpeciality()
    {
        var originalSpeciality = await CreateSpeciality("Especialidad original");
        var replacementSpeciality = await CreateSpeciality("Especialidad nueva");
        using var client = _factory.CreateAdministratorClient();
        var created = await CreateDoctor(client, "Nombre original", originalSpeciality.Id);

        var response = await client.PutAsJsonAsync(
            $"/api/doctors/{created.Id}",
            new DoctorModel.Request("  Nombre actualizado  ", "  MP-NUEVA  ", replacementSpeciality.Id));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<Dsw2026TpiDbContext>();
        var updated = await context.Set<Doctor>()
            .AsNoTracking()
            .SingleAsync(doctor => doctor.Id == created.Id);
        Assert.Equal("Nombre actualizado", updated.Name);
        Assert.Equal("MP-NUEVA", updated.LicenseNumber);
        Assert.Equal(replacementSpeciality.Id, updated.SpecialityId);
        Assert.True(updated.IsActive);
    }

    [Fact]
    public async Task Update_ReturnsNotFoundForMissingDoctor()
    {
        var speciality = await CreateSpeciality("Especialidad para modificación");
        using var client = _factory.CreateAdministratorClient();

        var response = await client.PutAsJsonAsync(
            $"/api/doctors/{Guid.NewGuid()}",
            new DoctorModel.Request("Nombre válido", "MP-105", speciality.Id));

        await AssertNotFound(response, "Médico");
    }

    [Fact]
    public async Task Delete_DeactivatesPersistsAndExcludesDoctor()
    {
        var speciality = await CreateSpeciality("Especialidad para baja");
        using var client = _factory.CreateAdministratorClient();
        var marker = Guid.NewGuid().ToString("N")[..8];
        var created = await CreateDoctor(client, $"Doctor {marker}", speciality.Id);

        var deleteResponse = await client.DeleteAsync($"/api/doctors/{created.Id}");
        var page = await GetPage(client, $"?name={marker}");
        var repeatedDelete = await client.DeleteAsync($"/api/doctors/{created.Id}");
        var updateAfterDelete = await client.PutAsJsonAsync(
            $"/api/doctors/{created.Id}",
            new DoctorModel.Request("Nombre posterior", "MP-POST", speciality.Id));

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(0, page.Total);
        Assert.Empty(page.Data);
        await AssertNotFound(repeatedDelete, "Médico");
        await AssertNotFound(updateAfterDelete, "Médico");

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<Dsw2026TpiDbContext>();
        var persisted = await context.Set<Doctor>()
            .IgnoreQueryFilters()
            .SingleAsync(doctor => doctor.Id == created.Id);
        Assert.False(persisted.IsActive);
    }

    [Fact]
    public async Task DomainMigrations_ApplyFromAnEmptySqliteDatabase()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<Dsw2026TpiDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new Dsw2026TpiDbContext(options);

        await context.Database.MigrateAsync();

        await using var columnsCommand = connection.CreateCommand();
        columnsCommand.CommandText = "PRAGMA table_info('Doctors');";
        await using var columns = await columnsCommand.ExecuteReaderAsync();
        var specialityIdIsRequired = false;
        while (await columns.ReadAsync())
        {
            if (columns.GetString(1) == "SpecialityId")
            {
                specialityIdIsRequired = columns.GetInt32(3) == 1;
            }
        }

        Assert.True(specialityIdIsRequired);

        await using var foreignKeysCommand = connection.CreateCommand();
        foreignKeysCommand.CommandText = "PRAGMA foreign_key_list('Doctors');";
        await using var foreignKeys = await foreignKeysCommand.ExecuteReaderAsync();
        var requiredRestrictForeignKey = false;
        while (await foreignKeys.ReadAsync())
        {
            if (foreignKeys.GetString(3) == "SpecialityId")
            {
                requiredRestrictForeignKey =
                    foreignKeys.GetString(2) == "Specialities" &&
                    foreignKeys.GetString(6).Equals("RESTRICT", StringComparison.OrdinalIgnoreCase);
            }
        }

        Assert.True(requiredRestrictForeignKey);
    }

    private async Task<Speciality> CreateSpeciality(string prefix, bool deleted = false)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<Dsw2026TpiDbContext>();
        var speciality = new Speciality(
            $"{prefix} {Guid.NewGuid():N}",
            "Descripción válida para pruebas");
        if (deleted)
        {
            speciality.MarkAsDeleted();
        }

        context.Set<Speciality>().Add(speciality);
        await context.SaveChangesAsync();
        return speciality;
    }

    private static async Task<DoctorModel.Response> CreateDoctor(
        HttpClient client,
        string name,
        Guid specialityId)
    {
        var response = await client.PostAsJsonAsync(
            "/api/doctors",
            new DoctorModel.Request(name, $"MP-{Guid.NewGuid():N}", specialityId));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DoctorModel.Response>())!;
    }

    private static async Task<DoctorModel.PagedResponse> GetPage(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/doctors{query}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DoctorModel.PagedResponse>())!;
    }

    private static async Task AssertValidationError(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "VALIDATION_ERROR",
            payload.RootElement.GetProperty("errorCode").GetString());
        Assert.NotEmpty(payload.RootElement.GetProperty("details").EnumerateArray());
    }

    private static async Task AssertNotFound(HttpResponseMessage response, string entity)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "ENTITY_NOTFOUND",
            payload.RootElement.GetProperty("errorCode").GetString());
        Assert.Contains(entity, payload.RootElement.GetProperty("message").GetString());
    }
}

public sealed class DoctorsApiFactory : WebApplicationFactory<Program>
{
    private const string AuthenticationScheme = "DoctorsTest";
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly SqliteConnection _identityConnection = new("Data Source=:memory:");

    public DoctorsApiFactory()
    {
        _connection.Open();
        _identityConnection.Open();

        var options = new DbContextOptionsBuilder<AuthenticationDbContext>()
            .UseSqlite(_identityConnection)
            .Options;
        using var context = new AuthenticationDbContext(options);
        context.Database.EnsureCreated();
    }

    public HttpClient CreateAdministratorClient() => CreateClientForRole(Roles.Administrator);

    public HttpClient CreateClientForRole(string? role)
    {
        var client = CreateClient();
        if (role is not null)
        {
            client.DefaultRequestHeaders.Add(DoctorsAuthenticationHandler.RoleHeader, role);
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
        builder.UseSetting("InitialAdministrator:Email", "administrator@example.com");
        builder.UseSetting("InitialAdministrator:Password", "Admin1!x");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:",
                ["AllowedHosts"] = "*",
                ["Jwt:Key"] = "01234567890123456789012345678901",
                ["Jwt:Issuer"] = "test-issuer",
                ["Jwt:Audience"] = "test-audience",
                ["InitialAdministrator:Email"] = "administrator@example.com",
                ["InitialAdministrator:Password"] = "Admin1!x"
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
                .AddScheme<AuthenticationSchemeOptions, DoctorsAuthenticationHandler>(
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

public sealed class DoctorsAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string RoleHeader = "X-Test-Role";

    public DoctorsAuthenticationHandler(
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
                new Claim(ClaimTypes.NameIdentifier, "doctors-test-user"),
                new Claim(ClaimTypes.Role, role.ToString())
            ],
            Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
