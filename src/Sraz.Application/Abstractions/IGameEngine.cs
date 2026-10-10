using Sraz.Domain.Models;

namespace Sraz.Application.Abstractions;

public interface IGameEngine
{
    void StartNewSession(string playerName);
}
