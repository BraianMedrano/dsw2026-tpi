using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace Dsw2026Tpi.Data;

public class Dsw2026TpiDbContext: DbContext
{
    // DI proporciona opciones como el proveedor y la cadena de conexión configurados para este contexto.
    public Dsw2026TpiDbContext(DbContextOptions<Dsw2026TpiDbContext> options):
        base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Aplica automáticamente las configuraciones IEntityTypeConfiguration<T> definidas en Data.
        // Esto permite trabajar con Set<T>() sin declarar un DbSet por cada entidad en este contexto.
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
