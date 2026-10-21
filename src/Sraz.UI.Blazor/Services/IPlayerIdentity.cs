namespace Sraz.UI.Blazor.Services;

public interface IPlayerIdentity
{
    /// <summary>Returns the anonymous PlayerId from the cookie, creating and storing it on first use.</summary>
    Guid GetOrCreatePlayerId();
}
