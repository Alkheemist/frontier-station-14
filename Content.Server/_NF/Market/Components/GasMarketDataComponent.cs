using Content.Shared._NF.Market;

namespace Content.Server._NF.Market.Components;

/// <summary>
/// Component that is put on the console's grid that will hold gases that have been sold, for that grid.
/// </summary>
[RegisterComponent]
public sealed partial class GasMarketDataComponent : Component
{
    [DataField]
    public List<GasMarketData> MarketDataList = [];
}
