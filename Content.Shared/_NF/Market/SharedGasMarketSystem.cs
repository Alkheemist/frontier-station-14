namespace Content.Shared._NF.Market;

public abstract class SharedGasMarketSystem : EntitySystem
{
    [Dependency] protected readonly SharedUserInterfaceSystem UI = default!;
}