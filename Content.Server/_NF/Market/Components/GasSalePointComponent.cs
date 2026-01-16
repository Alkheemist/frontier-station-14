using Content.Shared._NF.Market;
using Content.Shared.Atmos;

namespace Content.Server._NF.Market.Components;

[RegisterComponent, Access(typeof(SharedGasMarketSystem))]
public sealed partial class GasSalePointComponent : Component
{
    [DataField]
    public string InletPipePortName = "inlet";

    // An unlimited internal gas storage, tracking how much gas has been put into the entity.
    [ViewVariables]
    public GasMixture GasStorage = new();
}
