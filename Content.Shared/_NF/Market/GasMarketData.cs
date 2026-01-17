using Robust.Shared.Serialization;
using Content.Shared.Atmos.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._NF.Market;

[Virtual, NetSerializable, Serializable]
public class GasMarketData
{
    [ViewVariables]
    public int GasType { get; set; }

    [ViewVariables]
    public float Quantity { get; set; }

    public GasMarketData(int gasType, float quantity)
    {
        GasType = gasType;
        Quantity = quantity;
    }
}
