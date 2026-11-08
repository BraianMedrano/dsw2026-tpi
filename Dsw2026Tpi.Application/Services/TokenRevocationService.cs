using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.Data.Identity;
using Microsoft.EntityFrameworkCore;

namespace Dsw2026Tpi.Application.Services;

public sealed class TokenRevocationService(AuthenticationDbContext context)
    : ITokenRevocationService
{
    public async Task RevokeAsync(string jti, DateTime expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(jti))
        {
            throw new ArgumentException("A token identifier is required.", nameof(jti));
        }

        if (await context.Set<RevokedToken>().AnyAsync(token => token.Jti == jti))
        {
            return;
        }

        var revokedToken = new RevokedToken(jti, expiresAtUtc);
        context.Add(revokedToken);
        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            context.Entry(revokedToken).State = EntityState.Detached;
            if (await context.Set<RevokedToken>().AnyAsync(token => token.Jti == jti))
            {
                return;
            }

            throw;
        }
    }

    public Task<bool> IsRevokedAsync(
        string jti,
        CancellationToken cancellationToken = default) =>
        context.Set<RevokedToken>()
            .AsNoTracking()
            .AnyAsync(token => token.Jti == jti, cancellationToken);
}
