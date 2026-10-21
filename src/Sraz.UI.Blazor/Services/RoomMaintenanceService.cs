using Sraz.Application.Rooms;

namespace Sraz.UI.Blazor.Services;

public sealed class RoomMaintenanceService(IRoomManager roomManager) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await roomManager.RemoveInactiveAsync(stoppingToken);
        }
    }
}
