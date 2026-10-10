using Sraz.Application.Abstractions;
using Sraz.Application.Services;
using Sraz.Domain.Models;

namespace Sraz.Tests;

public class GameEngineTests
{
    [Fact]
    public void StartNewSession_WithEmptyName_Throws()
    {
        var sut = new GameEngine();

        Assert.Throws<ArgumentException>(() => sut.StartNewSession(""));
    }

    [Fact]
    public async Task StartNewSessionAsync_WithoutProvider_Throws()
    {
        var sut = new GameEngine();

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.StartNewSessionAsync("Alice"));
    }

    [Fact]
    public async Task StartNewSessionAsync_WhenQuestionsExist_ReturnsSessionSummary()
    {
        var provider = new FakeQuestionProvider(new[]
        {
            new Question
            {
                Category = "Science",
                Difficulty = 1,
                Text = "What planet is known as the Red Planet?",
                Options = new[] { "Mars", "Earth", "Venus" },
                CorrectOptionIndex = 0,
                Explanation = "Mars is called the Red Planet."
            },
            new Question
            {
                Category = "Science",
                Difficulty = 2,
                Text = "Which gas do plants absorb?",
                Options = new[] { "Oxygen", "Carbon dioxide", "Nitrogen" },
                CorrectOptionIndex = 1,
                Explanation = "Plants absorb carbon dioxide."
            }
        });

        var sut = new GameEngine(provider);

        var session = await sut.StartNewSessionAsync("Alice", 2, "Science", "Easy");

        Assert.Equal("Alice", session.PlayerName);
        Assert.Equal(1, session.TotalQuestions);
        Assert.Equal(1, session.FinalScore);
        Assert.NotNull(sut.CurrentSession);
    }

    private sealed class FakeQuestionProvider : IQuestionProvider
    {
        private readonly IReadOnlyList<Question> _questions;

        public FakeQuestionProvider(IEnumerable<Question> questions)
        {
            _questions = questions.ToList();
        }

        public Task<IReadOnlyList<Question>> GetQuestionsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_questions);
        }
    }
}
