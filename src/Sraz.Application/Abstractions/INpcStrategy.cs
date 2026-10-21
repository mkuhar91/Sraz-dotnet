using Sraz.Domain.Models;

namespace Sraz.Application.Abstractions;

public interface INpcStrategy
{
    (Pawn Pawn, Direction Direction)? ChooseMove(IGameEngine engine);
    bool AnswerQuestionCorrectly();
}
