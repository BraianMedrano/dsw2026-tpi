using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Dsw2026Tpi.Api.Middlewares;
using Dsw2026Tpi.Api.Services;
using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.Application.Services;
using Dsw2026Tpi.CrossCutting.Exceptions;
using Dsw2026Tpi.CrossCutting.Identity;
using Dsw2026Tpi.CrossCutting.Resources;
using Dsw2026Tpi.Data.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ApplicationAuthenticationService = Dsw2026Tpi.Application.Services.AuthenticationService;

namespace Dsw2026Tpi.Tests.Integration.Authentication;

public sealed class AdministratorLoginTests : IAsyncLifetime
{
    private const string AdministratorEmail = "administrator@example.com";
    private const string AdministratorPassword = "Admin1!x";
    private const string PatientEmail = "patient@example.com";
    private const string PatientPassword = "Patient123!";
    private const int TokenLifetimeMinutes = 30;

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private ServiceProvider _provider = null!;

    [Theory]
    [InlineData("", AdministratorPassword, nameof(LoginAdminModel.Request.Email))]
    [InlineData("not-an-email", AdministratorPassword, nameof(LoginAdminModel.Request.Email))]
    [InlineData(AdministratorEmail, "", nameof(LoginAdminModel.Request.Password))]
    [InlineData(AdministratorEmail, "short7", nameof(LoginAdminModel.Request.Password))]
    public void Request_RejectsInvalidCredentials(
        string email,
        string password,
        string expectedMember)
    {
        var request = new LoginAdminModel.Request(email, password);
        var results = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            results,
            validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(results, result => result.MemberNames.Contains(expectedMember));
    }

    [Theory]
    [InlineData("unknown@example.com", AdministratorPassword)]
    [InlineData(AdministratorEmail, "Wrong123!")]
    [InlineData(PatientEmail, PatientPassword)]
    public async Task LoginAdmin_ReturnsUniformUnauthorizedResponseForInvalidIdentity(
        string email,
        string password)
    {
        await using var scope = _provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ApplicationAuthenticationService>();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ExceptionHandlingMiddleware(
            async _ => await service.LoginAdmin(new LoginAdminModel.Request(email, password)),
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        var response = document.RootElement;
        Assert.Equal(
            nameof(ErrorCodes.AUTHENTICATION_FAILED),
            response.GetProperty("errorCode").GetString());
        Assert.Equal(
            ErrorCodes.AUTHENTICATION_FAILED,
            response.GetProperty("message").GetString());
        Assert.Empty(response.GetProperty("details").EnumerateArray());
    }

    [Fact]
    public async Task LoginAdmin_ReturnsCanonicalRoleAndAdministratorToken()
    {
        await using var scope = _provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ApplicationAuthenticationService>();
        var beforeLogin = DateTime.UtcNow;

        var response = await service.LoginAdmin(
            new LoginAdminModel.Request(AdministratorEmail, AdministratorPassword));

        Assert.False(string.IsNullOrWhiteSpace(response.Token));
        Assert.Equal("ADMINISTRADOR", response.Role);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(response.Token);
        Assert.Equal(
            Roles.Administrator,
            token.Claims.Single(claim => claim.Type == ClaimTypes.Role).Value);
        Assert.True(Guid.TryParse(token.Id, out _));
        Assert.InRange(
            token.ValidTo,
            beforeLogin.AddMinutes(TokenLifetimeMinutes).AddSeconds(-5),
            beforeLogin.AddMinutes(TokenLifetimeMinutes).AddSeconds(5));
    }

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "administrator-login-tests-signing-key-with-more-than-thirty-two-bytes",
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
            options => options.UseSqlite(_connection));
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
        Assert.True((await roleManager.CreateAsync(new IdentityRole(Roles.Administrator))).Succeeded);
        Assert.True((await roleManager.CreateAsync(new IdentityRole(Roles.Patient))).Succeeded);

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await CreateUser(userManager, AdministratorEmail, AdministratorPassword, Roles.Administrator);
        await CreateUser(userManager, PatientEmail, PatientPassword, Roles.Patient);
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static async Task CreateUser(
        UserManager<ApplicationUser> userManager,
        string email,
        string password,
        string role)
    {
        var now = DateTime.UtcNow;
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            CreatedAt = now,
            UpdatedAt = now
        };

        Assert.True((await userManager.CreateAsync(user, password)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(user, role)).Succeeded);
    }
}
