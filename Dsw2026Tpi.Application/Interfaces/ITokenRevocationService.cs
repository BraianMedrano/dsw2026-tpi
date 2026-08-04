namespace Dsw2026Tpi.Application.Interfaces;

public interface ITokenRevocationService
{
    Task RevokeAsync(string jti, DateTime expiresAtUtc);
    Task<bool> IsRevokedAsync(string jti, CancellationToken cancellationToken = default);
}
