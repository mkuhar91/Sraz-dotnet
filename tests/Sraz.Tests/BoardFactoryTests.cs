using Sraz.Application.Services;
using Sraz.Domain.Models;
using Xunit;

namespace Sraz.Tests;

public class BoardFactoryTests
{
    [Fact]
    public void Create_UsesTenByTenBoardByDefault()
    {
        var board = new BoardFactory().Create(seed: 1);

        Assert.Equal(10, board.Size);
        Assert.Equal(100, board.Cells.Length);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(0)]
    public void Create_RejectsBoardsOfSizeFiveOrLess(int size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoardFactory().Create(size));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(15)]
    public void Create_DistributesThemesEvenlyAndDeterministically(int size)
    {
        var factory = new BoardFactory();
        var first = factory.Create(size, 42);
        var second = factory.Create(size, 42);
        var counts = Enum.GetValues<QuestionTheme>()
            .Select(theme => first.Cells.Cast<BoardCell>().Count(cell => cell.Theme == theme))
            .ToArray();

        Assert.Equal(first.Cells.Cast<BoardCell>().Select(cell => cell.Theme),
            second.Cells.Cast<BoardCell>().Select(cell => cell.Theme));
        Assert.True(counts.Max() - counts.Min() <= 1);
    }

    [Fact]
    public void GetStartingCells_ReturnsCornerAndTwoAdjacentCells()
    {
        var cells = new BoardFactory().Create(6, 1).GetStartingCells(3);

        Assert.Equal(new[] { (5, 5), (5, 4), (4, 5) }, cells);
    }
}
