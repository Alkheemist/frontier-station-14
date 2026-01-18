using Content.Shared._NF.Market;
using Content.Shared.Atmos;

namespace Content.Server._NF.Market.Components;

[RegisterComponent, Access(typeof(SharedGasMarketSystem))]
public sealed partial class GasPurchasePointComponent : Component
{
    [DataField]
    public string OutletPipePortName = "outlet";

    // An unlimited internal gas storage, tracking how much gas has been put into the entity.
    [ViewVariables]
    public GasMixture GasStorage = new();
}
