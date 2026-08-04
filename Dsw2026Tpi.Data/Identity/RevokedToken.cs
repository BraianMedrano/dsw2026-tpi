namespace Dsw2026Tpi.Data.Identity;

public sealed class RevokedToken
{
    public string Jti { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }

#pragma warning disable CS8618
    private RevokedToken()
    {
    }
#pragma warning restore CS8618

    public RevokedToken(string jti, DateTime expiresAtUtc)
    {
        Jti = jti;
        ExpiresAtUtc = expiresAtUtc;
    }
}
