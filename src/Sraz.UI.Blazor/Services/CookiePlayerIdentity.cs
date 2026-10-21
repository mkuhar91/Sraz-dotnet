namespace Sraz.UI.Blazor.Services;

public sealed class CookiePlayerIdentity(IHttpContextAccessor httpContextAccessor) : IPlayerIdentity
{
    public const string CookieName = "sraz_player_id";
    private Guid? _playerId;

    public Guid GetOrCreatePlayerId()
    {
        if (_playerId is { } cached)
        {
            return cached;
        }

        var context = httpContextAccessor.HttpContext;
        if (context is not null && Guid.TryParse(context.Request.Cookies[CookieName], out var existing) && existing != Guid.Empty)
        {
            return (_playerId = existing).Value;
        }

        var created = Guid.NewGuid();
        // Cookies can only be written during an HTTP request, not once the circuit is running.
        if (context is not null && !context.Response.HasStarted)
        {
            context.Response.Cookies.Append(CookieName, created.ToString(), new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = context.Request.IsHttps,
                Expires = DateTimeOffset.UtcNow.AddYears(1)
            });
        }

        return (_playerId = created).Value;
    }
}
