using System.Net;
using Dsw2026Tpi.Api;
using Dsw2026Tpi.Data;
using Dsw2026Tpi.Data.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dsw2026Tpi.Tests.Integration.Cors;

// F03: el frontend corre en http://localhost:5173 (Vite). Estas pruebas usan el
// entorno Development para validar la lista real de appsettings.Development.json.
public sealed class CorsTests : IClassFixture<CorsTests.CorsApiFactory>
{
    private const string FrontendOrigin = "http://localhost:5173";
    private readonly CorsApiFactory _factory;

    public CorsTests(CorsApiFactory factory)
    {
        _factory = factory;
    }

    // El navegador envía un preflight (OPTIONS) antes de una petición con
    // Authorization. Si no recibe los encabezados CORS, bloquea la petición real.
    [Fact]
    public async Task PreflightFromFrontendOriginReceivesCorsHeaders()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/doctors");
        request.Headers.Add("Origin", FrontendOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            FrontendOrigin,
            Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    // Con UseCors antes de autenticación, un 401 también lleva el encabezado
    // CORS. Así el frontend puede leer el error y redirigir al login.
    [Fact]
    public async Task UnauthorizedResponseFromFrontendOriginKeepsCorsHeader()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/doctors");
        request.Headers.Add("Origin", FrontendOrigin);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(
            FrontendOrigin,
            Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    public sealed class CorsApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _databasePath =
            Path.Combine(Path.GetTempPath(), $"dsw2026-cors-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            // La clave real vive en User Secrets; en las pruebas se usa una fija.
            // UseSetting se aplica antes de que Program lea la configuración de JWT.
            builder.UseSetting("Jwt:Key", "01234567890123456789012345678901");
            var connectionString = $"Data Source={_databasePath};Foreign Keys=True;Pooling=False";
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = connectionString,
                    ["InitialAdministrator:Email"] = "cors-admin@example.com",
                    ["InitialAdministrator:Password"] = "Admin1!x"
                }));
            builder.ConfigureTestServices(services =>
            {
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
                var path = _databasePath + suffix;
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
