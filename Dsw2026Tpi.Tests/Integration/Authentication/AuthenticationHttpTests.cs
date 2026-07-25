using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dsw2026Tpi.Api;
using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.CrossCutting.Resources;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Dsw2026Tpi.Tests.Integration.Authentication;

public sealed class AuthenticationHttpTests : IAsyncLifetime
{
    private const string AdministratorEmail = "administrator@example.com";
    private const string AdministratorPassword = "Admin1!x";

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"dsw2026-auth-http-{Guid.NewGuid():N}.db");
    private AuthenticationApiFactory _factory = null!;
    private HttpClient _client = null!;

    [Fact]
    public async Task PatientLogin_Returns400ForInvalidDtoAnd200ForFirstLogin()
    {
        var invalidResponse = await _client.PostAsJsonAsync(
            "/api/auth/patient/login",
            new LoginPatientModel.Request("not-an-email", "123"));

        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        await AssertErrorCode(invalidResponse, nameof(ErrorCodes.VALIDATION_ERROR));

        var validResponse = await _client.PostAsJsonAsync(
            "/api/auth/patient/login",
            new LoginPatientModel.Request("http-patient@example.com", "12345678"));

        Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);
        var login = await validResponse.Content.ReadFromJsonAsync<LoginPatientModel.Response>();
        Assert.NotNull(login);
        Assert.False(string.IsNullOrWhiteSpace(login.Token));
        Assert.Equal("PACIENTE", login.Role);
    }

    [Fact]
    public async Task PatientLogin_ReturnsUniform401ForAnInconsistentIdentity()
    {
        var firstResponse = await _client.PostAsJsonAsync(
            "/api/auth/patient/login",
            new LoginPatientModel.Request("identity@example.com", "23456789"));
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var mismatchResponse = await _client.PostAsJsonAsync(
            "/api/auth/patient/login",
            new LoginPatientModel.Request("other@example.com", "23456789"));

        Assert.Equal(HttpStatusCode.Unauthorized, mismatchResponse.StatusCode);
        await AssertErrorCode(
            mismatchResponse,
            nameof(ErrorCodes.AUTHENTICATION_FAILED));
    }

    [Fact]
    public async Task ProtectedEndpoints_Return401WithoutTokenAnd403ForWrongRole()
    {
        var anonymousDoctors = await _client.GetAsync("/api/doctors?pageSize=10&pageIndex=0");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousDoctors.StatusCode);
        await AssertErrorCode(
            anonymousDoctors,
            nameof(ErrorCodes.AUTHENTICATION_FAILED));

        var patientLogin = await LoginPatient("authorization@example.com", "34567890");
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", patientLogin.Token);

        var patientDoctors = await _client.GetAsync("/api/doctors?pageSize=10&pageIndex=0");

        Assert.Equal(HttpStatusCode.Forbidden, patientDoctors.StatusCode);
        await AssertErrorCode(
            patientDoctors,
            nameof(ErrorCodes.AUTHORIZATION_FAILED));
    }

    [Fact]
    public async Task AdministratorToken_AccessesAdminAndHealthEndpoints()
    {
        var administratorLogin = await LoginAdministrator();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", administratorLogin.Token);

        var doctors = await _client.GetAsync("/api/doctors?pageSize=10&pageIndex=0");
        var health = await _client.GetAsync("/health-check");

        Assert.Equal(HttpStatusCode.OK, doctors.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task HealthCheck_Returns401WithoutToken()
    {
        var response = await _client.GetAsync("/health-check");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertErrorCode(response, nameof(ErrorCodes.AUTHENTICATION_FAILED));
    }

    public Task InitializeAsync()
    {
        _factory = new AuthenticationApiFactory(_databasePath);
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();

        foreach (var suffix in new[] { string.Empty, "-shm", "-wal" })
        {
            var path = _databasePath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private async Task<LoginPatientModel.Response> LoginPatient(string email, string dni)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/patient/login",
            new LoginPatientModel.Request(email, dni));
        response.EnsureSuccessStatusCode();
        return Assert.IsType<LoginPatientModel.Response>(
            await response.Content.ReadFromJsonAsync<LoginPatientModel.Response>());
    }

    private async Task<LoginAdminModel.Response> LoginAdministrator()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/admin/login",
            new LoginAdminModel.Request(AdministratorEmail, AdministratorPassword));
        response.EnsureSuccessStatusCode();
        return Assert.IsType<LoginAdminModel.Response>(
            await response.Content.ReadFromJsonAsync<LoginAdminModel.Response>());
    }

    private static async Task AssertErrorCode(HttpResponseMessage response, string expected)
    {
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        Assert.Equal(expected, document.RootElement.GetProperty("errorCode").GetString());
    }

    private sealed class AuthenticationApiFactory(string databasePath)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] =
                        $"Data Source={databasePath};Foreign Keys=True;Pooling=False",
                    ["InitialAdministrator:Email"] = AdministratorEmail,
                    ["InitialAdministrator:Password"] = AdministratorPassword
                });
            });
        }
    }
}
