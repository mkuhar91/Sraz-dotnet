namespace Sraz.Application.Rooms;

public sealed class RoomManagerOptions
{
    public TimeSpan InactivityTimeout { get; set; } = TimeSpan.FromHours(2);
    public TimeSpan QuestionTimeLimit { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxRooms { get; set; } = 500;
}
