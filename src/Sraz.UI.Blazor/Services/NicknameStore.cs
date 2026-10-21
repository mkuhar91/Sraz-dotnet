using System.Collections.Concurrent;

namespace Sraz.UI.Blazor.Services;

/// <summary>Remembers the nickname chosen for an anonymous <c>PlayerId</c> (the cookie value).</summary>
public sealed class NicknameStore
{
    private const int MaxEntries = 10_000;
    private readonly ConcurrentDictionary<Guid, string> _names = new();

    public string? Get(Guid playerId) => _names.GetValueOrDefault(playerId);

    public void Set(Guid playerId, string nickname)
    {
        if (_names.Count >= MaxEntries)
        {
            _names.Clear();
        }

        _names[playerId] = nickname;
    }
}
