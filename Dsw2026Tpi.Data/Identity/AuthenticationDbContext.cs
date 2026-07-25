using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Dsw2026Tpi.Data.Identity;

public class AuthenticationDbContext: IdentityDbContext
{
    public AuthenticationDbContext(DbContextOptions<AuthenticationDbContext> options)
            : base(options)
    {

    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(b =>
        {
            b.ToTable("ApplicationUsers");
            b.Property(user => user.Dni).HasMaxLength(8);
            // SQLite permite varios NULL en un índice único: los administradores no tienen DNI,
            // pero dos pacientes nunca pueden compartirlo.
            b.HasIndex(user => user.Dni).IsUnique();
        });
        builder.Entity<IdentityUser>(b => { b.ToTable("Users"); });
        builder.Entity<IdentityRole>(b => { b.ToTable("Roles"); });
        builder.Entity<IdentityUserRole<string>>(b => { b.ToTable("UsersRoles"); });
        builder.Entity<IdentityUserClaim<string>>(b => { b.ToTable("UsersClaims"); });
        builder.Entity<IdentityUserLogin<string>>(b => { b.ToTable("UsersLogins"); });
        builder.Entity<IdentityRoleClaim<string>>(b => { b.ToTable("RolesClaims"); });
        builder.Entity<IdentityUserToken<string>>(b => { b.ToTable("UsersTokens"); });
    }
}
