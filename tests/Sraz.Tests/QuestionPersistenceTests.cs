using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sraz.Domain.Models;
using Sraz.Infrastructure.Persistence;
using Xunit;

namespace Sraz.Tests;

public sealed class QuestionPersistenceTests
{
    private static readonly string SeedPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../data/questions.seed.json"));

    [Fact]
    public async Task ImporterIsIdempotentAndImportsOneHundredQuestionsPerTheme()
    {
        await using var dbContext = CreateContext();
        var importer = new QuestionSeedImporter(dbContext, new JsonQuestionProvider(SeedPath));

        Assert.Equal(800, await importer.ImportAsync());
        Assert.Equal(0, await importer.ImportAsync());
        Assert.Equal(800, await dbContext.Questions.CountAsync());

        var countsByTheme = await dbContext.Questions
            .GroupBy(question => question.Category)
            .Select(group => new { Theme = group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.Theme, group => group.Count);
        Assert.Equal(8, countsByTheme.Count);
        Assert.All(countsByTheme.Values, count => Assert.Equal(100, count));
    }

    [Fact]
    public async Task DbQuestionProviderReturnsOnlyQuestionsFromRequestedTheme()
    {
        await using var dbContext = CreateContext();
        await new QuestionSeedImporter(dbContext, new JsonQuestionProvider(SeedPath)).ImportAsync();

        var questions = await new DbQuestionProvider(dbContext).GetQuestionsAsync("Geography");

        Assert.Equal(100, questions.Count);
        Assert.All(questions, question =>
        {
            Assert.Equal("Geography", question.Category);
            Assert.InRange(question.CorrectOptionIndex, 0, question.Options.Count - 1);
            Assert.Equal(1, question.Options.Count(option => option == question.CorrectAnswerText));
        });
    }

    private static SrazDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SrazDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SrazDbContext(options);
    }
}
