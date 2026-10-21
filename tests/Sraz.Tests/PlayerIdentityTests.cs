using Microsoft.AspNetCore.Http;
using Sraz.UI.Blazor.Services;
using Xunit;

namespace Sraz.Tests;

public sealed class PlayerIdentityTests
{
    [Fact]
    public void CreatesCookieOnceAndReusesExistingValue()
    {
        var context = new DefaultHttpContext();
        var identity = new CookiePlayerIdentity(new HttpContextAccessor { HttpContext = context });

        var id = identity.GetOrCreatePlayerId();

        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal(id, identity.GetOrCreatePlayerId());
        var setCookie = context.Response.Headers.SetCookie.ToString();
        Assert.Contains($"{CookiePlayerIdentity.CookieName}={id}", setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);

        var returning = new DefaultHttpContext();
        returning.Request.Headers.Cookie = $"{CookiePlayerIdentity.CookieName}={id}";
        Assert.Equal(id, new CookiePlayerIdentity(new HttpContextAccessor { HttpContext = returning }).GetOrCreatePlayerId());
        Assert.Equal(0, returning.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public void InvalidCookieIsReplaced()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{CookiePlayerIdentity.CookieName}=not-a-guid";

        var id = new CookiePlayerIdentity(new HttpContextAccessor { HttpContext = context }).GetOrCreatePlayerId();

        Assert.NotEqual(Guid.Empty, id);
        Assert.Contains(id.ToString(), context.Response.Headers.SetCookie.ToString());
    }
}
