using Sraz.Domain.Models;
using Sraz.Infrastructure.Persistence;
using Xunit;

namespace Sraz.Tests;

public class SeedQuestionTests
{
    [Fact]
    public async Task SeedContainsAtLeastOneHundredValidChoiceQuestionsPerTheme()
    {
        var seedPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../data/questions.seed.json"));
        var questions = await new JsonQuestionProvider(seedPath).GetQuestionsAsync();
        var categoryByTheme = new Dictionary<QuestionTheme, string>
        {
            [QuestionTheme.Geography] = "Geography",
            [QuestionTheme.History] = "History",
            [QuestionTheme.Math] = "Math",
            [QuestionTheme.Science] = "Science",
            [QuestionTheme.Sports] = "Sports",
            [QuestionTheme.Art] = "Art",
            [QuestionTheme.Literature] = "Literature",
            [QuestionTheme.Entertainment] = "Entertainment"
        };

        foreach (var category in categoryByTheme.Values)
        {
            var themeQuestions = questions.Where(question => question.Category == category).ToList();
            Assert.True(themeQuestions.Count >= 100, category);
            Assert.All(themeQuestions, question =>
            {
                Assert.Equal(QuestionType.MultipleChoice, question.Type);
                Assert.Equal(4, question.Options.Count);
                Assert.InRange(question.CorrectOptionIndex, 0, question.Options.Count - 1);
                Assert.Equal(question.Options.Count, question.Options.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            });
            Assert.Equal(themeQuestions.Count, themeQuestions.Select(question => question.Text).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        Assert.Equal(800, questions.Count);
    }
}
