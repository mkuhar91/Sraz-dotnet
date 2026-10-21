using Microsoft.Extensions.DependencyInjection;
using Sraz.Application.Abstractions;
using Sraz.Domain.Models;

namespace Sraz.Infrastructure.Persistence;

/// <summary>Singleton-safe question provider: reads through a short-lived scope and caches the question bank.</summary>
public sealed class CachedQuestionProvider(IServiceScopeFactory scopeFactory) : IQuestionProvider
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyList<Question>? _cache;

    public async Task<IReadOnlyList<Question>> GetQuestionsAsync(CancellationToken cancellationToken = default)
    {
        if (_cache is { } cached)
        {
            return cached;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cache is null)
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var provider = new DbQuestionProvider(scope.ServiceProvider.GetRequiredService<SrazDbContext>());
                var questions = await provider.GetQuestionsAsync(cancellationToken);
                if (questions.Count == 0)
                {
                    return questions;
                }

                _cache = questions;
            }

            return _cache;
        }
        finally
        {
            _lock.Release();
        }
    }
}
