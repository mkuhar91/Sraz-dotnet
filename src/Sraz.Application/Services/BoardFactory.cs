using Sraz.Application.Abstractions;
using Sraz.Domain.Models;

namespace Sraz.Application.Services;

public sealed class BoardFactory : IBoardFactory
{
    private static readonly QuestionTheme[] Themes = Enum.GetValues<QuestionTheme>();

    public Board Create(int size = Board.DefaultSize, int? seed = null)
    {
        if (size <= 5)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Board size must be greater than 5.");
        }

        var themes = Enumerable.Range(0, size * size)
            .Select(index => Themes[index % Themes.Length])
            .ToArray();
        var random = seed.HasValue ? new Random(seed.Value) : Random.Shared;

        for (var i = themes.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (themes[i], themes[j]) = (themes[j], themes[i]);
        }

        return new Board(size, themes);
    }
}
