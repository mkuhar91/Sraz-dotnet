using Sraz.Domain.Models;
using Xunit;

namespace Sraz.Tests;

public class QuestionAnswerTests
{
    [Theory]
    [InlineData(" Zagreb ", true)]
    [InlineData("zagreb", true)]
    [InlineData("Split", false)]
    [InlineData("", false)]
    public void TextAnswersIgnoreCaseAndTrimWhitespace(string answer, bool expected)
    {
        var question = new Question
        {
            Type = QuestionType.TextInput,
            AcceptedAnswers = ["Zagreb"]
        };

        Assert.Equal(expected, question.IsTextAnswerCorrect(answer));
    }
}
