using Sraz.Domain.Models;

namespace Sraz.Application.Abstractions;

public interface IBoardFactory
{
    Board Create(int size = Board.DefaultSize, int? seed = null);
}
