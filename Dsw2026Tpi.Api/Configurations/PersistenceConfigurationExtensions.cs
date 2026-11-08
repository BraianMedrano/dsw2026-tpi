using Dsw2026Tpi.Data;
using Dsw2026Tpi.Data.Extensions;
using Dsw2026Tpi.Data.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Dsw2026Tpi.Api.Configurations;

public static class PersistenceConfigurationExtensions
{
    public static IServiceCollection AddApplicationPersistence(this IServiceCollection services,
        IConfiguration configuration)
    {
        // Lee desde appsettings la cadena de conexión SQLite compartida.
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        // Path.Combine usa el separador correcto tanto en Windows como en Linux.
        var rolesDataSource = Path.Combine("Sources", "roles.json");

        // Mantiene aislados los modelos de dominio e Identity aunque usen una misma base local portable.
        services.AddDbContext<Dsw2026TpiDbContext>(options =>
        {
            options.UseSqlite(connectionString);
        });

        services.AddDbContext<AuthenticationDbContext>(options =>
        {
            options.UseSqlite(connectionString);
            options.UseSeeding((context, _) =>
            {
                context.Seedwork<IdentityRole>(rolesDataSource);
            });
            options.UseAsyncSeeding((context, _, cancellationToken) =>
                context.SeedworkAsync<IdentityRole>(
                    rolesDataSource,
                    cancellationToken));
        });
        return services;
    }
}
