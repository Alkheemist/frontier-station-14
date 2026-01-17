using System.Linq;
using Content.Shared._NF.Market;
using Robust.Shared.Prototypes;

namespace Content.Server._NF.Market.Extensions;

public static class GasMarketDataExtensions
{
    /// <summary>
    /// Update-or-insert the gas market data list or adds it new if it doesnt exist in there yet.
    /// </summary>
    /// <param name="gasPrototypeId">The gas prototype id to change the amount of.</param>
    /// <param name="increaseAmount">The change in mols</param>
    /// <param name="gasMarketDataList">The market data list to modify.</param>
    /// <remarks>
    /// For existing data, increaseAmount is not validated. Any update that would result in a non-positive quantity results in item removal.
    /// </remarks>
    public static void Upsert(this List<GasMarketData> gasMarketDataList, int gasID, float increaseAmount)
    {
        // Find the MarketData for the given EntityPrototype.
        var prototypeMarketData = gasMarketDataList.FirstOrDefault(md => md.GasType == gasID);

        if (prototypeMarketData != null)
        {
            prototypeMarketData.Quantity += increaseAmount;

            // Prune empty/negative quantities (overflow, emptying, or excessive withdrawal)
            if (prototypeMarketData.Quantity <= 0)
                gasMarketDataList.Remove(prototypeMarketData);
        }
        else if (increaseAmount > 0)
        {
            // If it doesn't exist, create a new MarketData and add it to the list.
            gasMarketDataList.Add(new GasMarketData(gasID, increaseAmount));
        }
    }

    /// <summary>
    /// Get the current maximum amount available for a particular prototype.
    /// </summary>
    /// <param name="marketDataList">the list to check in</param>
    /// <param name="gasID">the prototype to check for</param>
    /// <returns>The max quantity withdrawable</returns>
    public static float GetMaxQuantityToWithdraw(this List<GasMarketData> marketDataList, int gasID)
    {
        var marketData = marketDataList.FirstOrDefault(md => md.GasType == gasID);
        return marketData == null ? 0 : marketData.Quantity;
    }
}
