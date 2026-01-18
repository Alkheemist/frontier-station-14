using Content.Server._NF.Market.Systems;
using Content.Shared._NF.Market;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;

namespace Content.Server._NF.Market.Components;

/// <summary>
/// Component that belongs to the gas market computer
/// </summary>
[RegisterComponent]
[Access(typeof(MarketSystem))]
public sealed partial class GasPurchaseConsoleComponent : Component
{
    [DataField]
    public int PurchasePointDistance = 8;

    /// <summary>
    /// The cost of one transaction.
    /// </summary>
    [DataField]
    public int TransactionCost = 600;

    [DataField]
    public SoundSpecifier ErrorSound = new SoundPathSpecifier("/Audio/Effects/Cargo/buzz_sigh.ogg");

    [DataField]
    public SoundSpecifier SuccessSound = new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");
}
