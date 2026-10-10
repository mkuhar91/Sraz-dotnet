using Sraz.Application.Abstractions;
using Sraz.Application.Services;
using Sraz.Domain.Models;

namespace Sraz.Tests;

public class QuizRoundServiceTests
{
    [Fact]
    public async Task CreateRoundAsync_WhenCategoryAndDifficultyMatch_UsesFilters()
    {
        var provider = new FakeQuestionProvider(new[]
        {
            new Question
            {
                Category = "Science",
                Difficulty = 1,
                Text = "Q1",
                Options = new[] { "A", "B", "C" },
                CorrectOptionIndex = 0,
                Explanation = "exp1"
            },
            new Question
            {
                Category = "History",
                Difficulty = 1,
                Text = "Q2",
                Options = new[] { "A", "B", "C" },
                CorrectOptionIndex = 1,
                Explanation = "exp2"
            },
            new Question
            {
                Category = "Science",
                Difficulty = 2,
                Text = "Q3",
                Options = new[] { "A", "B", "C" },
                CorrectOptionIndex = 2,
                Explanation = "exp3"
            }
        });

        var sut = new QuizRoundService(provider);

        var round = await sut.CreateRoundAsync(10, "Science", "Easy");

        Assert.Single(round.Questions);
        Assert.Equal("Science", round.Questions[0].Category);
        Assert.Equal(1, round.Questions[0].Difficulty);
    }

    [Fact]
    public async Task CreateRoundAsync_WhenNoQuestionsMatch_ReturnsEmptyRound()
    {
        var provider = new FakeQuestionProvider(new[]
        {
            new Question
            {
                Category = "Science",
                Difficulty = 1,
                Text = "Q1",
                Options = new[] { "A", "B", "C" },
                CorrectOptionIndex = 0,
                Explanation = "exp1"
            }
        });

        var sut = new QuizRoundService(provider);

        var round = await sut.CreateRoundAsync(10, "History", "Hard");

        Assert.Empty(round.Questions);
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
