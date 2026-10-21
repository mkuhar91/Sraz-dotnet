using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Sraz.UI.Blazor.Services;

public sealed class PlayerCircuitHandler(IPlayerIdentity identity, PlayerConnectionTracker connections) : CircuitHandler
{
    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken) =>
        connections.SetConnectedAsync(identity.GetOrCreatePlayerId(), circuit.Id, true);

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken) =>
        connections.SetConnectedAsync(identity.GetOrCreatePlayerId(), circuit.Id, false);

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken) =>
        connections.SetConnectedAsync(identity.GetOrCreatePlayerId(), circuit.Id, false);
}
