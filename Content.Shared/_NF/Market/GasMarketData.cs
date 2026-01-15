using Robust.Shared.Serialization;
using Content.Shared.Atmos.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._NF.Market;

[Virtual, NetSerializable, Serializable]
public class GasMarketData
{
    [ViewVariables]
    public ProtoId<GasPrototype> GasType { get; set; }

    [ViewVariables]
    public int Quantity { get; set; }

    public GasMarketData(ProtoId<GasPrototype> gasType, int quantity, double price)
    {
        GasType = gasType;
        Quantity = quantity;
    }
}
