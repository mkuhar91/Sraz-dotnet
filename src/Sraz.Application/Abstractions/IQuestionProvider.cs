using Sraz.Domain.Models;

namespace Sraz.Application.Abstractions;

public interface IQuestionProvider
{
    Task<IReadOnlyList<Question>> GetQuestionsAsync(CancellationToken cancellationToken = default);
}
