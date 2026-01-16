using System.Numerics;
using Content.Server.Administration.Logs;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Server.Audio;
using Content.Server.Hands.Systems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Server.Stack;
using Content.Server._NF.Market.Components;
using Content.Shared.Atmos;
using Content.Shared.Coordinates;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Market;
using Content.Shared._NF.Market.BUI;
using Content.Shared._NF.Market.Events;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._NF.Market;

/// <summary>
/// System for handling sale and purchase of bulk gases
/// </summary>
public sealed class GasMarketSystem : SharedGasMarketSystem
{
    [Dependency] private readonly AmbientSoundSystem _ambientSound = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly HandsSystem _hands = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;
    [Dependency] private readonly StackSystem _stack = default!;
    [Dependency] private readonly TransformSystem _transform = default!;

    /// <summary>
    /// The maximum distance to check for nearby gas sale points when selling gas.
    /// </summary>
    private const double DefaultMaxSalePointDistance = 8.0;


    /// <inheritdoc />
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GasSalePointComponent, AtmosDeviceUpdateEvent>(OnSalePointUpdate);

        SubscribeLocalEvent<GasSaleConsoleComponent, BoundUIOpenedEvent>(OnConsoleUiOpened);
        SubscribeLocalEvent<GasSaleConsoleComponent, GasSaleSellMessage>(OnConsoleSell);
        SubscribeLocalEvent<GasSaleConsoleComponent, GasSaleRefreshMessage>(OnConsoleRefresh);
    }

    // Atmos update: take any gas from the connecting network and push it into the pump.
    private void OnSalePointUpdate(Entity<GasSalePointComponent> ent, ref AtmosDeviceUpdateEvent args)
    {
        if (TryComp<ApcPowerReceiverComponent>(ent, out var power) && !power.Powered
            || !_nodeContainer.TryGetNode(ent.Owner, ent.Comp.InletPipePortName, out PipeNode? port))
            return;

        if (port.Air.TotalMoles > 0)
        {
            _atmosphere.Merge(ent.Comp.GasStorage, port.Air);
            port.Air.Clear();
        }
    }

    private void OnConsoleUiOpened(Entity<GasSaleConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateConsoleInterface(ent);
    }

    private void OnConsoleRefresh(Entity<GasSaleConsoleComponent> ent, ref GasSaleRefreshMessage args)
    {
        UpdateConsoleInterface(ent);
    }

    private void OnConsoleSell(Entity<GasSaleConsoleComponent> ent, ref GasSaleSellMessage args)
    {
        var xform = Transform(ent);
        if (xform.GridUid is not { } gridUid)
        {
            UI.SetUiState(ent.Owner,
                GasSaleConsoleUiKey.Key,
                new GasSaleConsoleBoundUserInterfaceState(0, new GasMixture(), false));
            return;
        }

        var amount = 0.0;
        foreach (var salePoint in GetNearbySalePoints(ent, gridUid))
        {
            amount += _atmosphere.GetPrice(salePoint.Comp.GasStorage, true);
            salePoint.Comp.GasStorage.Clear();
        }

        if (TryComp<MarketModifierComponent>(ent, out var priceMod))
            amount *= priceMod.Mod;

        var stackPrototype = _prototype.Index(ent.Comp.CashType);
        var stackUid = _stack.Spawn((int)amount, stackPrototype, args.Actor.ToCoordinates());
        if (!_hands.TryPickupAnyHand(args.Actor, stackUid))
            _transform.SetLocalRotation(stackUid, Angle.Zero); // Orient these to grid north instead of map north
        _audio.PlayPvs(ent.Comp.ApproveSound, ent);
        UI.SetUiState(ent.Owner,
            GasSaleConsoleUiKey.Key,
            new GasSaleConsoleBoundUserInterfaceState(0, new GasMixture(), false));
    }

    private void UpdateConsoleInterface(Entity<GasSaleConsoleComponent> ent)
    {
        if (Transform(ent).GridUid is not { } gridUid)
        {
            UI.SetUiState(ent.Owner,
                GasSaleConsoleUiKey.Key,
                new GasSaleConsoleBoundUserInterfaceState(0, new GasMixture(), false));
            return;
        }

        GetNearbyMixtures(ent, gridUid, out var mixture, out var amount);
        if (TryComp<MarketModifierComponent>(ent, out var priceMod))
            amount *= priceMod.Mod;

        UI.SetUiState(ent.Owner,
            GasSaleConsoleUiKey.Key,
            new GasSaleConsoleBoundUserInterfaceState((int)amount, mixture, mixture.TotalMoles > 0));
    }

    private void GetNearbyMixtures(EntityUid consoleUid, EntityUid gridUid, out GasMixture mixture, out double value)
    {
        mixture = new GasMixture();
        value = 0.0;

        foreach (var salePoint in GetNearbySalePoints(consoleUid, gridUid))
        {
            _atmosphere.Merge(mixture, salePoint.Comp.GasStorage);
            value += _atmosphere.GetPrice(salePoint.Comp.GasStorage, true);
        }
    }

    private List<Entity<GasSalePointComponent>> GetNearbySalePoints(EntityUid consoleUid, EntityUid gridUid)
    {
        List<Entity<GasSalePointComponent>> ret = new();

        var query = AllEntityQuery<GasSalePointComponent, TransformComponent>();

        var consolePosition = Transform(consoleUid).Coordinates.Position;
        var maxSalePointDistance = DefaultMaxSalePointDistance;

        // Get the mapped checking distance from the console
        if (TryComp<GasSaleConsoleComponent>(consoleUid, out var cargoShuttleComponent))
            maxSalePointDistance = cargoShuttleComponent.SellPointDistance;

        while (query.MoveNext(out var uid, out var comp, out var compXform))
        {
            if (compXform.ParentUid != gridUid
                || !compXform.Anchored
                || Vector2.Distance(consolePosition, compXform.Coordinates.Position) > maxSalePointDistance)
                continue;

            ret.Add((uid, comp));
        }

        return ret;
    }
}
