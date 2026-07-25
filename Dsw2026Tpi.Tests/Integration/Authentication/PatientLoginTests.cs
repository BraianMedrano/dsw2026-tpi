using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Dsw2026Tpi.Api.Services;
using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.Application.Services;
using Dsw2026Tpi.CrossCutting.Exceptions;
using Dsw2026Tpi.CrossCutting.Identity;
using Dsw2026Tpi.Data.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ApplicationAuthenticationService = Dsw2026Tpi.Application.Services.AuthenticationService;

namespace Dsw2026Tpi.Tests.Integration.Authentication;

public sealed class PatientLoginTests : IAsyncLifetime
{
    private const int TokenLifetimeMinutes = 30;

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"dsw2026-patient-login-{Guid.NewGuid():N}.db");
    private ServiceProvider _provider = null!;

    [Theory]
    [InlineData("", "12345678", nameof(LoginPatientModel.Request.Email))]
    [InlineData("not-an-email", "12345678", nameof(LoginPatientModel.Request.Email))]
    [InlineData("patient@example.com", "", nameof(LoginPatientModel.Request.Dni))]
    [InlineData("patient@example.com", "123456", nameof(LoginPatientModel.Request.Dni))]
    [InlineData("patient@example.com", "123456789", nameof(LoginPatientModel.Request.Dni))]
    [InlineData("patient@example.com", "1234567A", nameof(LoginPatientModel.Request.Dni))]
    public void Request_RejectsInvalidIdentity(
        string email,
        string dni,
        string expectedMember)
    {
        var request = new LoginPatientModel.Request(email, dni);
        var results = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            results,
            validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(results, result => result.MemberNames.Contains(expectedMember));
    }

    [Fact]
    public async Task LoginPatient_FirstLoginCreatesPatientAndReturnsPatientToken()
    {
        const string email = "first-login@example.com";
        const string dni = "12345678";
        var beforeLogin = DateTime.UtcNow;

        var response = await LoginPatient(email, dni);

        Assert.False(string.IsNullOrWhiteSpace(response.Token));
        Assert.Equal("PACIENTE", response.Role);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(response.Token);
        Assert.Equal(email, token.Subject);
        Assert.Equal(
            Roles.Patient,
            token.Claims.Single(claim => claim.Type == ClaimTypes.Role).Value);
        Assert.InRange(
            token.ValidTo,
            beforeLogin.AddMinutes(TokenLifetimeMinutes).AddSeconds(-5),
            beforeLogin.AddMinutes(TokenLifetimeMinutes).AddSeconds(5));

        await using var scope = _provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var patient = Assert.IsType<ApplicationUser>(await userManager.FindByEmailAsync(email));
        Assert.Equal(dni, patient.Dni);
        Assert.True(await userManager.IsInRoleAsync(patient, Roles.Patient));
        Assert.Null(patient.PasswordHash);
    }

    [Fact]
    public async Task LoginPatient_RepeatedLoginUsesTheExistingPatient()
    {
        const string email = "repeat-login@example.com";
        const string dni = "23456789";

        await LoginPatient(email, dni);
        await using var firstScope = _provider.CreateAsyncScope();
        var firstUserManager =
            firstScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var original = Assert.IsType<ApplicationUser>(
            await firstUserManager.FindByEmailAsync(email));

        var response = await LoginPatient(email, dni);

        await using var verificationScope = _provider.CreateAsyncScope();
        var verificationUserManager =
            verificationScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var stored = Assert.IsType<ApplicationUser>(
            await verificationUserManager.FindByEmailAsync(email));
        Assert.Equal(original.Id, stored.Id);
        Assert.Equal("PACIENTE", response.Role);
        Assert.Equal(1, await verificationUserManager.Users.CountAsync(user => user.Email == email));
    }

    [Fact]
    public async Task LoginPatient_RejectsEmailAndDniThatBelongToDifferentIdentities()
    {
        await LoginPatient("one@example.com", "34567890");
        await LoginPatient("two@example.com", "45678901");

        await Assert.ThrowsAsync<AuthenticationException>(
            () => LoginPatient("one@example.com", "45678901"));
        await Assert.ThrowsAsync<AuthenticationException>(
            () => LoginPatient("unknown@example.com", "34567890"));
        await Assert.ThrowsAsync<AuthenticationException>(
            () => LoginPatient("two@example.com", "56789012"));
    }

    [Fact]
    public async Task LoginPatient_ConcurrentFirstLoginsCreateOnePatient()
    {
        const string email = "concurrent@example.com";
        const string dni = "56789012";

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => LoginPatient(email, dni)));

        Assert.All(responses, response => Assert.Equal("PACIENTE", response.Role));
        await using var scope = _provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var patients = await userManager.Users
            .Where(user => user.Email == email || user.Dni == dni)
            .ToListAsync();
        Assert.Single(patients);
        Assert.True(await userManager.IsInRoleAsync(patients[0], Roles.Patient));
    }

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "patient-login-tests-signing-key-with-more-than-thirty-two-bytes",
                ["Jwt:Issuer"] = "Dsw2026Tpi.Tests",
                ["Jwt:Audience"] = "Dsw2026Tpi.Tests",
                ["Jwt:ExpiresInMinutes"] = TokenLifetimeMinutes.ToString()
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddAuthentication();
        services.AddDbContext<AuthenticationDbContext>(
            options => options.UseSqlite($"Data Source={_databasePath};Pooling=False"));
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AuthenticationDbContext>()
            .AddSignInManager();
        services.AddScoped<ISignInService, SignInService>();
        services.AddScoped<JwtService>();
        services.AddScoped<ApplicationAuthenticationService>();

        _provider = services.BuildServiceProvider();

        await using var scope = _provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        await context.Database.EnsureCreatedAsync();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        Assert.True((await roleManager.CreateAsync(new IdentityRole(Roles.Patient))).Succeeded);
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
    }

    private async Task<LoginPatientModel.Response> LoginPatient(string email, string dni)
    {
        await using var scope = _provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ApplicationAuthenticationService>();
        return await service.LoginPatient(new LoginPatientModel.Request(email, dni));
    }
}
