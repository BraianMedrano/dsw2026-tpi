using System.Data.Common;
using Dsw2026Tpi.Data;
using Dsw2026Tpi.Data.Identity;
using Microsoft.EntityFrameworkCore;

namespace Dsw2026Tpi.Api.Services;

public sealed class ApplicationStartupInitializer(
    Dsw2026TpiDbContext domainContext,
    AuthenticationDbContext authenticationContext,
    InitialAdministratorBootstrapper administratorBootstrapper,
    IHostEnvironment environment,
    ILogger<ApplicationStartupInitializer> logger)
{
    public async Task InitializeAsync()
    {
        if (environment.IsDevelopment())
        {
            logger.LogInformation("Aplicando migraciones de dominio e Identity para Development");
            await domainContext.Database.MigrateAsync();
            await authenticationContext.Database.MigrateAsync();
        }
        else
        {
            logger.LogInformation(
                "Las migraciones deben aplicarse explícitamente antes de iniciar en {Environment}",
                environment.EnvironmentName);
        }

        try
        {
            await administratorBootstrapper.InitializeAsync();
        }
        catch (DbException exception) when (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"El esquema de base de datos debe estar provisionado antes de iniciar en " +
                $"{environment.EnvironmentName}.",
                exception);
        }
    }
}
