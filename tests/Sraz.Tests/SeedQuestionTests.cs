using Sraz.Domain.Models;
using Sraz.Infrastructure.Persistence;
using Xunit;

namespace Sraz.Tests;

public class SeedQuestionTests
{
    [Fact]
    public async Task SeedContainsAtLeastTwoChoiceAndOneTextQuestionPerTheme()
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
            Assert.True(themeQuestions.Count(question => question.Type == QuestionType.MultipleChoice) >= 2, category);
            Assert.True(themeQuestions.Count(question => question.Type == QuestionType.TextInput) >= 1, category);
        }
    }
}
