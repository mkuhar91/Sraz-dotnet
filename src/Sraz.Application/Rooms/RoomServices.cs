using Sraz.Application.Abstractions;

namespace Sraz.Application.Rooms;

public sealed record RoomServices(
    IRoomStore Store,
    IQuestionProvider Questions,
    INpcStrategy Npc,
    TimeProvider Time,
    TimeSpan QuestionTimeLimit);
