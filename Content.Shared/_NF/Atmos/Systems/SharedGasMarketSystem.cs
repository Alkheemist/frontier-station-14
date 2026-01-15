using Content.Shared._NF.Atmos.Components;
using Content.Shared.Atmos.Piping.Binary.Components;
using Content.Shared.Construction.Components;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Shared._NF.Atmos.Systems;

public abstract class SharedGasMarketSystem : EntitySystem
{
    [Dependency] protected readonly SharedUserInterfaceSystem UI = default!;
}