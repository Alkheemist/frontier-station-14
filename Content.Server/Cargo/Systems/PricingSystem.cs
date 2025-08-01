using Content.Server.Administration;
using Content.Server.Body.Systems;
using Content.Server.Cargo.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Administration;
using Content.Shared.Body.Components;
using Content.Shared.Cargo;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Materials;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stacks;
using Robust.Shared.Console;
using Robust.Shared.Containers;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using System.Linq;
using Content.Shared.Research.Prototypes;
using Content.Server._NF.Cargo.Components; // Frontier
using Content.Server.Materials.Components; // Frontier
using Content.Shared.Cargo.Components; // Frontier

namespace Content.Server.Cargo.Systems;

/// <summary>
/// This handles calculating the price of items, and implements two basic methods of pricing materials.
/// </summary>
public sealed class PricingSystem : EntitySystem
{
    [Dependency] private readonly IConsoleHost _consoleHost = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IComponentFactory _factory = default!; // Frontier
    [Dependency] private readonly BodySystem _bodySystem = default!;
    [Dependency] private readonly MobStateSystem _mobStateSystem = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainerSystem = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<MobPriceComponent, PriceCalculationEvent>(CalculateMobPrice); // Frontier

        _consoleHost.RegisterCommand("appraisegrid",
            "Calculates the total value of the given grids.",
            "appraisegrid <grid Ids>", AppraiseGridCommand);
    }

    [AdminCommand(AdminFlags.Debug)]
    private void AppraiseGridCommand(IConsoleShell shell, string argstr, string[] args)
    {
        if (args.Length == 0)
        {
            shell.WriteError("Not enough arguments.");
            return;
        }

        foreach (var gid in args)
        {
            if (!EntityManager.TryParseNetEntity(gid, out var gridId) || !gridId.Value.IsValid())
            {
                shell.WriteError($"Invalid grid ID \"{gid}\".");
                continue;
            }

            if (!TryComp(gridId, out MapGridComponent? mapGrid))
            {
                shell.WriteError($"Grid \"{gridId}\" doesn't exist.");
                continue;
            }

            List<(double, EntityUid)> mostValuable = new();

            var value = AppraiseGrid(gridId.Value, null, (uid, price) =>
            {
                mostValuable.Add((price, uid));
                mostValuable.Sort((i1, i2) => i2.Item1.CompareTo(i1.Item1));
                if (mostValuable.Count > 5)
                    mostValuable.Pop();
            });

            shell.WriteLine($"Grid {gid} appraised to {value} spesos.");
            shell.WriteLine($"The top most valuable items were:");
            foreach (var (price, ent) in mostValuable)
            {
                shell.WriteLine($"- {ToPrettyString(ent)} @ {price} spesos");
            }
        }
    }

    private void CalculateMobPrice(EntityUid uid, MobPriceComponent component, ref PriceCalculationEvent args)
    {
        // TODO: Estimated pricing.
        if (args.Handled)
            return;

        if (!TryComp<BodyComponent>(uid, out var body) || !TryComp<MobStateComponent>(uid, out var state))
        {
            Log.Error($"Tried to get the mob price of {ToPrettyString(uid)}, which has no {nameof(BodyComponent)} and no {nameof(MobStateComponent)}.");
            return;
        }

        // TODO: Better handling of missing.
        var partList = _bodySystem.GetBodyChildren(uid, body).ToList();
        var totalPartsPresent = partList.Sum(_ => 1);
        var totalParts = partList.Count;

        var partRatio = totalPartsPresent / (double)totalParts;
        var partPenalty = component.Price * (1 - partRatio) * component.MissingBodyPartPenalty;

        args.Price += (component.Price - partPenalty) * (_mobStateSystem.IsAlive(uid, state) ? 1.0 : component.DeathPenalty) * (HasComp<LabGrownComponent>(uid) ? 1.0 : component.LabGrownPenalty); // Frontier - LabGrown
    }

    private double GetSolutionPrice(Entity<SolutionContainerManagerComponent> entity)
    {
        if (Comp<MetaDataComponent>(entity).EntityLifeStage < EntityLifeStage.MapInitialized)
            return GetSolutionPrice(entity.Comp);

        var price = 0.0;

        foreach (var (_, soln) in _solutionContainerSystem.EnumerateSolutions((entity.Owner, entity.Comp)))
        {
            var solution = soln.Comp.Solution;
            foreach (var (reagent, quantity) in solution.Contents)
            {
                if (!_prototypeManager.TryIndex<ReagentPrototype>(reagent.Prototype, out var reagentProto))
                    continue;

                // TODO check ReagentData for price information?
                price += (float)quantity * reagentProto.PricePerUnit;
            }
        }

        return price;
    }

    private double GetSolutionPrice(SolutionContainerManagerComponent component)
    {
        var price = 0.0;

        foreach (var (_, prototype) in _solutionContainerSystem.EnumerateSolutions(component))
        {
            foreach (var (reagent, quantity) in prototype.Contents)
            {
                if (!_prototypeManager.TryIndex<ReagentPrototype>(reagent.Prototype, out var reagentProto))
                    continue;

                // TODO check ReagentData for price information?
                price += (float)quantity * reagentProto.PricePerUnit;
            }
        }

        return price;
    }

    private double GetMaterialPrice(PhysicalCompositionComponent component)
    {
        double price = 0;
        foreach (var (id, quantity) in component.MaterialComposition)
        {
            price += _prototypeManager.Index<MaterialPrototype>(id).Price * quantity;
        }
        return price;
    }

    public double GetLatheRecipePrice(LatheRecipePrototype recipe)
    {
        var price = 0.0;

        if (recipe.Result is { } result)
        {
            price += GetEstimatedPrice(_prototypeManager.Index(result));
        }

        if (recipe.ResultReagents is { } resultReagents)
        {
            foreach (var (reagent, amount) in resultReagents)
            {
                price += (_prototypeManager.Index(reagent).PricePerUnit * amount).Double();
            }
        }

        return price;
    }

    /// <summary>
    /// Get a rough price for an entityprototype. Does not consider contained entities.
    /// </summary>
    public double GetEstimatedPrice(EntityPrototype prototype)
    {
        var ev = new EstimatedPriceCalculationEvent(prototype);

        RaiseLocalEvent(ref ev);

        if (ev.Handled)
            return ev.Price;

        var price = ev.Price;
        price += GetMaterialsPrice(prototype);
        price += GetSolutionsPrice(prototype);
        // Can't use static price with stackprice
        var oldPrice = price;
        price += GetStackPrice(prototype);

        if (oldPrice.Equals(price))
        {
            price += GetStaticPrice(prototype);
        }

        // TODO: Proper container support.

        return price;
    }

    /// <summary>
    /// Add a hardcoded price for an item to set how much it will cost to buy it from a vending machine, while allowing staticPrice to set its sell price.
    /// </summary>
    public double GetEstimatedVendPrice(EntityPrototype prototype)
    {
        var ev = new EstimatedPriceCalculationEvent()
        {
            Prototype = prototype,
        };

        RaiseLocalEvent(ref ev);

        if (ev.Handled)
            return ev.Price;

        var price = ev.Price;
        price += GetVendPrice(prototype);

        // TODO: Proper container support.

        return price;
    }

    /// <summary>
    /// Appraises an entity, returning it's price.
    /// </summary>
    /// <param name="uid">The entity to appraise.</param>
    /// <returns>The price of the entity.</returns>
    /// <remarks>
    /// This fires off an event to calculate the price.
    /// Calculating the price of an entity that somehow contains itself will likely hang.
    /// </remarks>
    public double GetPrice(EntityUid uid, bool includeContents = true, Func<EntityUid, bool>? predicate = null) // Frontier - Add optional predicate
    {
        if (predicate is not null && !predicate(uid)) // Frontier
            return 0.0;                               // Frontier

        var ev = new PriceCalculationEvent();
        ev.Price = 0; // Structs doesnt initialize doubles when called by constructor.
        RaiseLocalEvent(uid, ref ev);

        if (ev.Handled)
            return ev.Price;

        var price = ev.Price;
        //TODO: Add an OpaqueToAppraisal component or similar for blocking the recursive descent into containers, or preventing material pricing.
        // DO NOT FORGET TO UPDATE ESTIMATED PRICING
        price += GetMaterialsPrice(uid);
        price += GetSolutionsPrice(uid);

        // Can't use static price with stackprice
        var oldPrice = price;
        price += GetStackPrice(uid);

        if (oldPrice.Equals(price))
        {
            price += GetStaticPrice(uid);
        }

        if (includeContents && TryComp<ContainerManagerComponent>(uid, out var containers))
        {
            foreach (var container in containers.Containers.Values)
            {
                foreach (var ent in container.ContainedEntities)
                {
                    price += GetPrice(ent, includeContents, predicate); // Frontier - Add includeContents, predicate
                }
            }
        }

        return price;
    }

    private double GetMaterialsPrice(EntityUid uid)
    {
        double price = 0;

        if (HasComp<MaterialComponent>(uid) &&
            TryComp<PhysicalCompositionComponent>(uid, out var composition))
        {
            var matPrice = GetMaterialPrice(composition);
            if (TryComp<StackComponent>(uid, out var stack))
                matPrice *= stack.Count;

            price += matPrice;
        }

        return price;
    }

    private double GetMaterialsPrice(EntityPrototype prototype)
    {
        double price = 0;

        if (prototype.Components.ContainsKey(Factory.GetComponentName<MaterialComponent>()) &&
            prototype.Components.TryGetValue(Factory.GetComponentName<PhysicalCompositionComponent>(), out var composition))
        {
            var compositionComp = (PhysicalCompositionComponent)composition.Component;
            var matPrice = GetMaterialPrice(compositionComp);

            if (prototype.Components.TryGetValue(Factory.GetComponentName<StackComponent>(), out var stackProto))
            {
                matPrice *= ((StackComponent)stackProto.Component).Count;
            }

            price += matPrice;
        }

        return price;
    }

    private double GetSolutionsPrice(EntityUid uid)
    {
        var price = 0.0;

        if (TryComp<SolutionContainerManagerComponent>(uid, out var solComp))
        {
            price += GetSolutionPrice((uid, solComp));
        }

        return price;
    }

    private double GetSolutionsPrice(EntityPrototype prototype)
    {
        var price = 0.0;

        if (prototype.Components.TryGetValue(Factory.GetComponentName<SolutionContainerManagerComponent>(), out var solManager))
        {
            var solComp = (SolutionContainerManagerComponent)solManager.Component;
            price += GetSolutionPrice(solComp);
        }

        return price;
    }

    private double GetStackPrice(EntityUid uid)
    {
        var price = 0.0;

        if (TryComp<StackPriceComponent>(uid, out var stackPrice) &&
            TryComp<StackComponent>(uid, out var stack) &&
            !HasComp<MaterialComponent>(uid)) // don't double count material prices
        {
            price += stack.Count * stackPrice.Price;
        }

        return price;
    }

    private double GetStackPrice(EntityPrototype prototype)
    {
        var price = 0.0;

        if (prototype.Components.TryGetValue(Factory.GetComponentName<StackPriceComponent>(), out var stackpriceProto) &&
            prototype.Components.TryGetValue(Factory.GetComponentName<StackComponent>(), out var stackProto) &&
            !prototype.Components.ContainsKey(Factory.GetComponentName<MaterialComponent>()))
        {
            var stackPrice = (StackPriceComponent)stackpriceProto.Component;
            var stack = (StackComponent)stackProto.Component;
            price += stack.Count * stackPrice.Price;
        }

        return price;
    }

    private double GetStaticPrice(EntityUid uid)
    {
        var price = 0.0;

        if (TryComp<StaticPriceComponent>(uid, out var staticPrice))
        {
            price += staticPrice.Price;
        }

        return price;
    }

    private double GetStaticPrice(EntityPrototype prototype)
    {
        var price = 0.0;

        if (prototype.Components.TryGetValue(Factory.GetComponentName<StaticPriceComponent>(), out var staticProto))
        {
            var staticPrice = (StaticPriceComponent)staticProto.Component;
            price += staticPrice.Price;
        }

        return price;
    }

    // New Frontiers - Stack Vendor Prices - Gets overwrite values for vendor prices.
    // This code is licensed under AGPLv3. See AGPLv3.txt
    private double GetVendPrice(EntityPrototype prototype)
    {
        var price = 0.0;

        // Prefer static price to stack price component, take the first positive value read.
        if (prototype.Components.TryGetValue(_factory.GetComponentName(typeof(StaticPriceComponent)), out var staticProto))
        {
            var staticComp = (StaticPriceComponent)staticProto.Component;
            if (staticComp.VendPrice > 0.0)
                price += staticComp.VendPrice;
        }
        if (price == 0.0 && prototype.Components.TryGetValue(_factory.GetComponentName(typeof(StackPriceComponent)), out var stackProto))
        {
            var stackComp = (StackPriceComponent)stackProto.Component;
            if (stackComp.VendPrice > 0.0)
                price += stackComp.VendPrice;
        }

        return price;
    }
    // End of modified code

    /// <summary>
    /// Appraises a grid, this is mainly meant to be used by yarrs.
    /// </summary>
    /// <param name="grid">The grid to appraise.</param>
    /// <param name="predicate">An optional predicate that controls whether or not the entity is counted toward the total.</param>
    /// <param name="afterPredicate">An optional predicate to run after the price has been calculated. Useful for high scores or similar.</param>
    /// <returns>The total value of the grid.</returns>
    public double AppraiseGrid(EntityUid grid, Func<EntityUid, bool>? predicate = null, Action<EntityUid, double>? afterPredicate = null)
    {
        var xform = Transform(grid);
        var price = 0.0;
        var enumerator = xform.ChildEnumerator;
        while (enumerator.MoveNext(out var child))
        {
            if (predicate is null || predicate(child))
            {
                var subPrice = GetPrice(child, true, predicate); // Frontier: add true, predicate
                price += subPrice;
                afterPredicate?.Invoke(child, subPrice);
            }
        }

        return price;
    }
}
