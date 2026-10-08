using Sraz.Application.Services;

namespace Sraz.Tests;

public class GameEngineTests
{
    [Fact]
    public void StartNewSession_WithEmptyName_Throws()
    {
        var sut = new GameEngine();

        Assert.Throws<ArgumentException>(() => sut.StartNewSession(""));
    }
}
