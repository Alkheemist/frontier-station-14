using Content.Server.Administration.Logs;
using Content.Server.Body.Systems;
using Content.Server.Construction;
using Content.Server.Explosion.EntitySystems;
using Content.Server.DeviceLinking.Systems;
using Content.Server.Hands.Systems;
using Content.Server.Kitchen.Components;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Construction.EntitySystems;
using Content.Shared.Database;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Destructible;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Robust.Shared.Random;
using Robust.Shared.Audio;
using Content.Server.Lightning;
using Content.Shared.Item;
using Content.Shared.Kitchen;
using Content.Shared.Kitchen.Components;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Tag;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using System.Linq;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Content.Shared.Stacks;
using Content.Server.Construction.Components;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Robust.Shared.Utility;
using Content.Shared._NF.Kitchen.Components; // Frontier

namespace Content.Server.Kitchen.EntitySystems
{
    public sealed partial class MicrowaveSystem : EntitySystem // Frontier: add partial
    {
        [Dependency] private readonly BodySystem _bodySystem = default!;
        [Dependency] private readonly DeviceLinkSystem _deviceLink = default!;
        [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
        [Dependency] private readonly PowerReceiverSystem _power = default!;
        [Dependency] private readonly RecipeManager _recipeManager = default!;
        [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
        [Dependency] private readonly SharedAudioSystem _audio = default!;
        [Dependency] private readonly LightningSystem _lightning = default!;
        [Dependency] private readonly IRobustRandom _random = default!;
        [Dependency] private readonly IGameTiming _gameTiming = default!;
        [Dependency] private readonly ExplosionSystem _explosion = default!;
        [Dependency] private readonly SharedContainerSystem _container = default!;
        [Dependency] private readonly SharedSolutionContainerSystem _solutionContainer = default!;
        [Dependency] private readonly TagSystem _tag = default!;
        [Dependency] private readonly TemperatureSystem _temperature = default!;
        [Dependency] private readonly UserInterfaceSystem _userInterface = default!;
        [Dependency] private readonly HandsSystem _handsSystem = default!;
        [Dependency] private readonly SharedItemSystem _item = default!;
        [Dependency] private readonly SharedStackSystem _stack = default!;
        [Dependency] private readonly IPrototypeManager _prototype = default!;
        [Dependency] private readonly IAdminLogManager _adminLogger = default!;
        [Dependency] private readonly SharedSuicideSystem _suicide = default!;

        [ValidatePrototypeId<EntityPrototype>]
        private const string MalfunctionSpark = "Spark";

        private static readonly ProtoId<TagPrototype> MetalTag = "Metal";
        private static readonly ProtoId<TagPrototype> PlasticTag = "Plastic";

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<MicrowaveComponent, ComponentInit>(OnInit);
            SubscribeLocalEvent<MicrowaveComponent, MapInitEvent>(OnMapInit);
            SubscribeLocalEvent<MicrowaveComponent, SolutionContainerChangedEvent>(OnSolutionChange);
            SubscribeLocalEvent<MicrowaveComponent, EntInsertedIntoContainerMessage>(OnContentUpdate);
            SubscribeLocalEvent<MicrowaveComponent, EntRemovedFromContainerMessage>(OnContentUpdate);
            SubscribeLocalEvent<MicrowaveComponent, InteractUsingEvent>(OnInteractUsing, after: new[] { typeof(AnchorableSystem) });
            SubscribeLocalEvent<MicrowaveComponent, ContainerIsInsertingAttemptEvent>(OnInsertAttempt);
            SubscribeLocalEvent<MicrowaveComponent, BreakageEventArgs>(OnBreak);
            SubscribeLocalEvent<MicrowaveComponent, PowerChangedEvent>(OnPowerChanged);
            SubscribeLocalEvent<MicrowaveComponent, AnchorStateChangedEvent>(OnAnchorChanged);

            SubscribeLocalEvent<MicrowaveComponent, SuicideByEnvironmentEvent>(OnSuicideByEnvironment);

            SubscribeLocalEvent<MicrowaveComponent, SignalReceivedEvent>(OnSignalReceived);

            SubscribeLocalEvent<MicrowaveComponent, MicrowaveStartCookMessage>((u, c, m) => Wzhzhzh(u, c, m.Actor));
            SubscribeLocalEvent<MicrowaveComponent, MicrowaveEjectMessage>(OnEjectMessage);
            SubscribeLocalEvent<MicrowaveComponent, MicrowaveEjectSolidIndexedMessage>(OnEjectIndex);
            SubscribeLocalEvent<MicrowaveComponent, MicrowaveSelectCookTimeMessage>(OnSelectTime);

            SubscribeLocalEvent<ActiveMicrowaveComponent, ComponentStartup>(OnCookStart);
            SubscribeLocalEvent<ActiveMicrowaveComponent, ComponentShutdown>(OnCookStop);
            SubscribeLocalEvent<ActiveMicrowaveComponent, EntInsertedIntoContainerMessage>(OnActiveMicrowaveInsert);
            SubscribeLocalEvent<ActiveMicrowaveComponent, EntRemovedFromContainerMessage>(OnActiveMicrowaveRemove);

            SubscribeLocalEvent<ActivelyMicrowavedComponent, OnConstructionTemperatureEvent>(OnConstructionTemp);
            SubscribeLocalEvent<ActivelyMicrowavedComponent, SolutionRelayEvent<ReactionAttemptEvent>>(OnReactionAttempt);

            SubscribeLocalEvent<FoodRecipeProviderComponent, GetSecretRecipesEvent>(OnGetSecretRecipes);

            SubscribeLocalEvent<MicrowaveComponent, RefreshPartsEvent>(OnRefreshParts); // Frontier
            SubscribeLocalEvent<MicrowaveComponent, UpgradeExamineEvent>(OnUpgradeExamine); // Frontier

            SubscribeLocalEvent<MicrowaveComponent, AssemblerStartCookMessage>(TryStartAssembly); // Frontier
        }

        private void OnCookStart(Entity<ActiveMicrowaveComponent> ent, ref ComponentStartup args)
        {
            if (!TryComp<MicrowaveComponent>(ent, out var microwaveComponent))
                return;
            SetAppearance(ent.Owner, MicrowaveVisualState.Cooking, microwaveComponent);

            microwaveComponent.PlayingStream =
                _audio.PlayPvs(microwaveComponent.LoopingSound, ent, AudioParams.Default.WithLoop(true).WithMaxDistance(5))?.Entity;
        }

        private void OnCookStop(Entity<ActiveMicrowaveComponent> ent, ref ComponentShutdown args)
        {
            if (!TryComp<MicrowaveComponent>(ent, out var microwaveComponent))
                return;

            SetAppearance(ent.Owner, MicrowaveVisualState.Idle, microwaveComponent);
            microwaveComponent.PlayingStream = _audio.Stop(microwaveComponent.PlayingStream);
        }

        private void OnActiveMicrowaveInsert(Entity<ActiveMicrowaveComponent> ent, ref EntInsertedIntoContainerMessage args)
        {
            var microwavedComp = AddComp<ActivelyMicrowavedComponent>(args.Entity);
            microwavedComp.Microwave = ent.Owner;
        }

        private void OnActiveMicrowaveRemove(Entity<ActiveMicrowaveComponent> ent, ref EntRemovedFromContainerMessage args)
        {
            EntityManager.RemoveComponentDeferred<ActivelyMicrowavedComponent>(args.Entity);
        }

        // Stop items from transforming through constructiongraphs while being microwaved.
        // They might be reserved for a microwave recipe.
        private void OnConstructionTemp(Entity<ActivelyMicrowavedComponent> ent, ref OnConstructionTemperatureEvent args)
        {
            args.Result = HandleResult.False;
        }

        // Stop reagents from reacting if they are currently reserved for a microwave recipe.
        // For example Egg would cook into EggCooked, causing it to not being removed once we are done microwaving.
        private void OnReactionAttempt(Entity<ActivelyMicrowavedComponent> ent, ref SolutionRelayEvent<ReactionAttemptEvent> args)
        {
            if (!TryComp<ActiveMicrowaveComponent>(ent.Comp.Microwave, out var activeMicrowaveComp))
                return;

            if (activeMicrowaveComp.PortionedRecipe.Item1 == null) // no recipe selected
                return;

            var recipeReagents = activeMicrowaveComp.PortionedRecipe.Item1.IngredientsReagents.Keys;

            foreach (var reagent in recipeReagents)
            {
                if (args.Event.Reaction.Reactants.ContainsKey(reagent))
                {
                    args.Event.Cancelled = true;
                    return;
                }
            }
        }

        /// <summary>
        ///     Adds temperature to every item in the microwave,
        ///     based on the time it took to microwave.
        /// </summary>
        /// <param name="component">The microwave that is heating up.</param>
        /// <param name="time">The time on the microwave, in seconds.</param>
        private void AddTemperature(MicrowaveComponent component, float time)
        {
            // Frontier: temperature requires heat or irradiation
            if (!component.CanHeat && !component.CanIrradiate)
                return;
            // End Frontier

            var heatToAdd = time * component.BaseHeatMultiplier;
            foreach (var entity in component.Storage.ContainedEntities)
            {
                if (TryComp<TemperatureComponent>(entity, out var tempComp))
                    _temperature.ChangeHeat(entity, heatToAdd * component.ObjectHeatMultiplier, false, tempComp);

                if (!TryComp<SolutionContainerManagerComponent>(entity, out var solutions))
                    continue;
                foreach (var (_, soln) in _solutionContainer.EnumerateSolutions((entity, solutions)))
                {
                    var solution = soln.Comp.Solution;
                    if (solution.Temperature > component.TemperatureUpperThreshold)
                        continue;

                    _solutionContainer.AddThermalEnergy(soln, heatToAdd);
                }
            }
        }

        private void SubtractContents(MicrowaveComponent component, FoodRecipePrototype recipe)
        {
            // TODO Turn recipe.IngredientsReagents into a ReagentQuantity[]

            var totalReagentsToRemove = new Dictionary<string, FixedPoint2>(recipe.IngredientsReagents);

            // this is spaghetti ngl
            foreach (var item in component.Storage.ContainedEntities)
            {
                // use the same reagents as when we selected the recipe
                if (!_solutionContainer.TryGetDrainableSolution(item, out var solutionEntity, out var solution))
                    continue;

                foreach (var (reagent, _) in recipe.IngredientsReagents)
                {
                    // removed everything
                    if (!totalReagentsToRemove.ContainsKey(reagent))
                        continue;

                    var quant = solution.GetTotalPrototypeQuantity(reagent);

                    if (quant >= totalReagentsToRemove[reagent])
                    {
                        quant = totalReagentsToRemove[reagent];
                        totalReagentsToRemove.Remove(reagent);
                    }
                    else
                    {
                        totalReagentsToRemove[reagent] -= quant;
                    }

                    _solutionContainer.RemoveReagent(solutionEntity.Value, reagent, quant);
                }
            }

            foreach (var recipeSolid in recipe.IngredientsSolids)
            {
                for (var i = 0; i < recipeSolid.Value; i++)
                {
                    foreach (var item in component.Storage.ContainedEntities)
                    {
                        string? itemID = null;

                        // If an entity has a stack component, use the stacktype instead of prototype id
                        if (TryComp<StackComponent>(item, out var stackComp))
                        {
                            itemID = _prototype.Index<StackPrototype>(stackComp.StackTypeId).Spawn;
                        }
                        else
                        {
                            var metaData = MetaData(item);
                            if (metaData.EntityPrototype == null)
                            {
                                continue;
                            }
                            itemID = metaData.EntityPrototype.ID;
                        }

                        if (itemID != recipeSolid.Key)
                        {
                            continue;
                        }

                        if (stackComp is not null)
                        {
                            if (stackComp.Count == 1)
                            {
                                _container.Remove(item, component.Storage);
                            }
                            _stack.Use(item, 1, stackComp);
                            break;
                        }
                        else
                        {
                            _container.Remove(item, component.Storage);
                            Del(item);
                            break;
                        }
                    }
                }
            }
        }

        private void OnInit(Entity<MicrowaveComponent> ent, ref ComponentInit args)
        {
            // this really does have to be in ComponentInit
            ent.Comp.Storage = _container.EnsureContainer<Container>(ent, ent.Comp.ContainerId);
            ent.Comp.FinalCookTimeMultiplier = ent.Comp.CookTimeMultiplier; // Frontier: initial cook time consistency (assumes stock components)
        }

        private void OnMapInit(Entity<MicrowaveComponent> ent, ref MapInitEvent args)
        {
            _deviceLink.EnsureSinkPorts(ent, ent.Comp.OnPort);
        }

        /// <summary>
        /// Kills the user by microwaving their head
        /// TODO: Make this not awful, it keeps any items attached to your head still on and you can revive someone and cogni them so you have some dumb headless fuck running around. I've seen it happen.
        /// </summary>
        private void OnSuicideByEnvironment(Entity<MicrowaveComponent> ent, ref SuicideByEnvironmentEvent args)
        {
            if (args.Handled)
                return;

            // The act of getting your head microwaved doesn't actually kill you
            if (!TryComp<DamageableComponent>(args.Victim, out var damageableComponent))
                return;

            // Frontier: suicide requires heat or irradiation
            if (!ent.Comp.CanHeat && !ent.Comp.CanIrradiate)
                return;
            // Frontier

            // The application of lethal damage is what kills you...
            _suicide.ApplyLethalDamage((args.Victim, damageableComponent), "Heat");

            var victim = args.Victim;
            var headCount = 0;

            if (TryComp<BodyComponent>(victim, out var body))
            {
                var headSlots = _bodySystem.GetBodyChildrenOfType(victim, BodyPartType.Head, body);

                foreach (var part in headSlots)
                {
                    _container.Insert(part.Id, ent.Comp.Storage);
                    headCount++;
                }
            }

            var othersMessage = headCount > 1
                ? Loc.GetString("microwave-component-suicide-multi-head-others-message", ("victim", victim))
                : Loc.GetString("microwave-component-suicide-others-message", ("victim", victim));

            var selfMessage = headCount > 1
                ? Loc.GetString("microwave-component-suicide-multi-head-message")
                : Loc.GetString("microwave-component-suicide-message");

            _popupSystem.PopupEntity(othersMessage, victim, Filter.PvsExcept(victim), true);
            _popupSystem.PopupEntity(selfMessage, victim, victim);

            _audio.PlayPvs(ent.Comp.ClickSound, ent.Owner, AudioParams.Default.WithVolume(-2));
            ent.Comp.CurrentCookTimerTime = 10;
            Wzhzhzh(ent.Owner, ent.Comp, args.Victim);
            UpdateUserInterfaceState(ent.Owner, ent.Comp);
            args.Handled = true;
        }

        private void OnSolutionChange(Entity<MicrowaveComponent> ent, ref SolutionContainerChangedEvent args)
        {
            UpdateUserInterfaceState(ent, ent.Comp);
        }

        private void OnContentUpdate(EntityUid uid, MicrowaveComponent component, ContainerModifiedMessage args) // For some reason ContainerModifiedMessage just can't be used at all with Entity<T>. TODO: replace with Entity<T> syntax once that's possible
        {
            if (component.Storage != args.Container)
                return;

            UpdateUserInterfaceState(uid, component);
        }

        private void OnInsertAttempt(Entity<MicrowaveComponent> ent, ref ContainerIsInsertingAttemptEvent args)
        {
            if (args.Container.ID != ent.Comp.ContainerId)
                return;

            if (ent.Comp.Broken)
            {
                args.Cancel();
                return;
            }

            if (TryComp<ItemComponent>(args.EntityUid, out var item))
            {
                if (_item.GetSizePrototype(item.Size) > _item.GetSizePrototype(ent.Comp.MaxItemSize))
                {
                    args.Cancel();
                    return;
                }
            }
            else
            {
                args.Cancel();
                return;
            }

            if (ent.Comp.Storage.Count >= ent.Comp.Capacity)
                args.Cancel();
        }

        private void OnInteractUsing(Entity<MicrowaveComponent> ent, ref InteractUsingEvent args)
        {
            if (args.Handled)
                return;
            if (!(TryComp<ApcPowerReceiverComponent>(ent, out var apc) && apc.Powered))
            {
                _popupSystem.PopupEntity(Loc.GetString("microwave-component-interact-using-no-power"), ent, args.User);
                return;
            }

            if (ent.Comp.Broken)
            {
                _popupSystem.PopupEntity(Loc.GetString("microwave-component-interact-using-broken"), ent, args.User);
                return;
            }

            if (TryComp<ItemComponent>(args.Used, out var item))
            {
                // check if size of an item you're trying to put in is too big
                if (_item.GetSizePrototype(item.Size) > _item.GetSizePrototype(ent.Comp.MaxItemSize))
                {
                    _popupSystem.PopupEntity(Loc.GetString(ent.Comp.TooBigPopup, ("item", args.Used)), ent, args.User); // Frontier: "microwave-component-interact-item-too-big"<ent.Comp.TooBigPopup
                    return;
                }
            }
            else
            {
                // check if thing you're trying to put in isn't an item
                _popupSystem.PopupEntity(Loc.GetString("microwave-component-interact-using-transfer-fail"), ent, args.User);
                return;
            }

            if (ent.Comp.Storage.Count >= ent.Comp.Capacity)
            {
                _popupSystem.PopupEntity(Loc.GetString("microwave-component-interact-full"), ent, args.User);
                return;
            }

            args.Handled = true;
            _handsSystem.TryDropIntoContainer(args.User, args.Used, ent.Comp.Storage);
            UpdateUserInterfaceState(ent, ent.Comp);
        }

        private void OnBreak(Entity<MicrowaveComponent> ent, ref BreakageEventArgs args)
        {
            ent.Comp.Broken = true;
            SetAppearance(ent, MicrowaveVisualState.Broken, ent.Comp);
            StopCooking(ent);
            _container.EmptyContainer(ent.Comp.Storage);
            UpdateUserInterfaceState(ent, ent.Comp);
        }

        private void OnPowerChanged(Entity<MicrowaveComponent> ent, ref PowerChangedEvent args)
        {
            if (!args.Powered)
            {
                SetAppearance(ent, MicrowaveVisualState.Idle, ent.Comp);
                StopCooking(ent);
            }
            UpdateUserInterfaceState(ent, ent.Comp);
        }

        private void OnAnchorChanged(EntityUid uid, MicrowaveComponent component, ref AnchorStateChangedEvent args)
        {
            if (!args.Anchored)
                _container.EmptyContainer(component.Storage);
        }

        private void OnRefreshParts(Entity<MicrowaveComponent> ent, ref RefreshPartsEvent args)
        {
            var cookRating = args.PartRatings[ent.Comp.MachinePartCookTimeMultiplier];
            ent.Comp.FinalCookTimeMultiplier = ent.Comp.CookTimeMultiplier * MathF.Pow(ent.Comp.CookTimeScalingConstant, cookRating - 1); // Frontier: apply base cooktimemultiplier as a coefficient (syndie microwave)
        }

        private void OnUpgradeExamine(Entity<MicrowaveComponent> ent, ref UpgradeExamineEvent args)
        {
            args.AddPercentageUpgrade("microwave-component-upgrade-cook-time", ent.Comp.FinalCookTimeMultiplier);
        }

        private void OnSignalReceived(Entity<MicrowaveComponent> ent, ref SignalReceivedEvent args)
        {
            if (args.Port != ent.Comp.OnPort)
                return;

            if (ent.Comp.Broken || !_power.IsPowered(ent))
                return;

            Wzhzhzh(ent.Owner, ent.Comp, null);
        }

        public void UpdateUserInterfaceState(EntityUid uid, MicrowaveComponent component)
        {
            _userInterface.SetUiState(uid, component.Key, new MicrowaveUpdateUserInterfaceState(
                GetNetEntityArray(component.Storage.ContainedEntities.ToArray()),
                HasComp<ActiveMicrowaveComponent>(uid),
                component.CurrentCookTimeButtonIndex,
                component.CurrentCookTimerTime,
                component.CurrentCookTimeEnd
            ));
        }

        public void SetAppearance(EntityUid uid, MicrowaveVisualState state, MicrowaveComponent? component = null, AppearanceComponent? appearanceComponent = null)
        {
            if (!Resolve(uid, ref component, ref appearanceComponent, false))
                return;
            var display = component.Broken ? MicrowaveVisualState.Broken : state;
            _appearance.SetData(uid, PowerDeviceVisuals.VisualState, display, appearanceComponent);
        }

        public static bool HasContents(MicrowaveComponent component)
        {
            return component.Storage.ContainedEntities.Any();
        }

        /// <summary>
        /// Explodes the microwave internally, turning it into a broken state, destroying its board, and spitting out its machine parts
        /// </summary>
        /// <param name="ent"></param>
        public void Explode(Entity<MicrowaveComponent> ent)
        {
            ent.Comp.Broken = true; // Make broken so we stop processing stuff
            _explosion.TriggerExplosive(ent);
            if (TryComp<MachineComponent>(ent, out var machine))
            {
                _container.CleanContainer(machine.BoardContainer);
                _container.EmptyContainer(machine.PartContainer);
            }

            _adminLogger.Add(LogType.Action, LogImpact.Medium,
                $"{ToPrettyString(ent)} exploded from unsafe cooking!");
        }
        /// <summary>
        /// Handles the attempted cooking of unsafe objects
        /// </summary>
        /// <remarks>
        /// Returns false if the microwave didn't explode, true if it exploded.
        /// </remarks>
        private void RollMalfunction(Entity<ActiveMicrowaveComponent, MicrowaveComponent> ent)
        {
            if (ent.Comp1.MalfunctionTime == TimeSpan.Zero)
                return;

            if (ent.Comp1.MalfunctionTime > _gameTiming.CurTime)
                return;

            ent.Comp1.MalfunctionTime = _gameTiming.CurTime + TimeSpan.FromSeconds(ent.Comp2.MalfunctionInterval);
            if (_random.Prob(ent.Comp2.ExplosionChance))
            {
                Explode((ent, ent.Comp2));
                return;  // microwave is fucked, stop the cooking.
            }

            if (_random.Prob(ent.Comp2.LightningChance))
                _lightning.ShootRandomLightnings(ent, 1.0f, 2, MalfunctionSpark, triggerLightningEvents: false);
        }

        /// <summary>
        /// Starts Cooking
        /// </summary>
        /// <remarks>
        /// It does not make a "wzhzhzh" sound, it makes a "mmmmmmmm" sound!
        /// -emo
        /// </remarks>
        public void Wzhzhzh(EntityUid uid, MicrowaveComponent component, EntityUid? user)
        {
            if (!HasContents(component) || HasComp<ActiveMicrowaveComponent>(uid) || !(TryComp<ApcPowerReceiverComponent>(uid, out var apc) && apc.Powered))
                return;

            var solidsDict = new Dictionary<string, int>();
            var reagentDict = new Dictionary<string, FixedPoint2>();
            var malfunctioning = false;
            // TODO use lists of Reagent quantities instead of reagent prototype ids.
            foreach (var item in component.Storage.ContainedEntities.ToArray())
            {
                // special behavior when being microwaved ;)
                var ev = new BeingMicrowavedEvent(uid, user, component.CanHeat, component.CanIrradiate); // Frontier: add CanHeat, CanIrradiate
                RaiseLocalEvent(item, ev);

                // TODO MICROWAVE SPARKS & EFFECTS
                // Various microwaveable entities should probably spawn a spark, play a sound, and generate a pop=up.
                // This should probably be handled by the microwave system, with fields in BeingMicrowavedEvent.

                if (ev.Handled)
                {
                    UpdateUserInterfaceState(uid, component);
                    return;
                }

                if (_tag.HasTag(item, MetalTag) && component.CanIrradiate) // Frontier: add && !component.DisableMetalMalfunctions
                {
                    malfunctioning = true;
                }

                if (_tag.HasTag(item, PlasticTag) && (component.CanHeat || component.CanIrradiate)) // Frontier: add && !component.DisableRuiningPlastic
                {
                    var junk = Spawn(component.BadRecipeEntityId, Transform(uid).Coordinates);
                    _container.Insert(junk, component.Storage);
                    Del(item);
                    continue;
                }

                var microwavedComp = AddComp<ActivelyMicrowavedComponent>(item);
                microwavedComp.Microwave = uid;

                string? solidID = null;
                int amountToAdd = 1;

                // If a microwave recipe uses a stacked item, use the default stack prototype id instead of prototype id
                if (TryComp<StackComponent>(item, out var stackComp))
                {
                    solidID = _prototype.Index<StackPrototype>(stackComp.StackTypeId).Spawn;
                    amountToAdd = stackComp.Count;
                }
                else
                {
                    var metaData = MetaData(item); //this simply begs for cooking refactor
                    if (metaData.EntityPrototype is not null)
                        solidID = metaData.EntityPrototype.ID;
                }

                if (solidID is null)
                    continue;

                if (!solidsDict.TryAdd(solidID, amountToAdd))
                    solidsDict[solidID] += amountToAdd;

                // only use reagents we have access to
                // you have to break the eggs before we can use them!
                if (!_solutionContainer.TryGetDrainableSolution(item, out var _, out var solution))
                    continue;

                foreach (var (reagent, quantity) in solution.Contents)
                {
                    if (!reagentDict.TryAdd(reagent.Prototype, quantity))
                        reagentDict[reagent.Prototype] += quantity;
                }
            }

            // Check recipes
            var getRecipesEv = new GetSecretRecipesEvent();
            RaiseLocalEvent(uid, ref getRecipesEv);

            List<FoodRecipePrototype> recipes = getRecipesEv.Recipes;
            recipes.AddRange(_recipeManager.Recipes);
            var portionedRecipe = recipes.Select(r =>
                CanSatisfyRecipe(component, r, solidsDict, reagentDict)).FirstOrDefault(r => r.Item2 > 0);

            _audio.PlayPvs(component.StartCookingSound, uid);
            var activeComp = AddComp<ActiveMicrowaveComponent>(uid); //microwave is now cooking
            activeComp.CookTimeRemaining = component.CurrentCookTimerTime * component.FinalCookTimeMultiplier; // Frontier: CookTimeMultiplier<FinalCookTimeMultiplier
            activeComp.TotalTime = component.CurrentCookTimerTime; //this doesn't scale so that we can have the "actual" time
            activeComp.PortionedRecipe = portionedRecipe;
            //Scale tiems with cook times
            component.CurrentCookTimeEnd = _gameTiming.CurTime + TimeSpan.FromSeconds(component.CurrentCookTimerTime * component.FinalCookTimeMultiplier); // Frontier: CookTimeMultiplier<FinalCookTimeMultiplier
            if (malfunctioning)
                activeComp.MalfunctionTime = _gameTiming.CurTime + TimeSpan.FromSeconds(component.MalfunctionInterval);
            UpdateUserInterfaceState(uid, component);
        }

        private void StopCooking(Entity<MicrowaveComponent> ent)
        {
            RemCompDeferred<ActiveMicrowaveComponent>(ent);
            foreach (var solid in ent.Comp.Storage.ContainedEntities)
            {
                RemCompDeferred<ActivelyMicrowavedComponent>(solid);
            }
        }

        public static (FoodRecipePrototype, int) CanSatisfyRecipe(MicrowaveComponent component, FoodRecipePrototype recipe, Dictionary<string, int> solids, Dictionary<string, FixedPoint2> reagents)
        {
            var portions = 0;

            if (component.CurrentCookTimerTime % recipe.CookTime != 0)
            {
                //can't be a multiple of this recipe
                return (recipe, 0);
            }

            // Frontier: microwave recipe machine types
            if ((recipe.RecipeType & component.ValidRecipeTypes) == 0)
            {
                return (recipe, 0);
            }
            // End Frontier

            foreach (var solid in recipe.IngredientsSolids)
            {
                if (!solids.ContainsKey(solid.Key))
                    return (recipe, 0);

                if (solids[solid.Key] < solid.Value)
                    return (recipe, 0);

                portions = portions == 0
                    ? solids[solid.Key] / solid.Value.Int()
                    : Math.Min(portions, solids[solid.Key] / solid.Value.Int());
            }

            foreach (var reagent in recipe.IngredientsReagents)
            {
                // TODO Turn recipe.IngredientsReagents into a ReagentQuantity[]
                if (!reagents.ContainsKey(reagent.Key))
                    return (recipe, 0);

                if (reagents[reagent.Key] < reagent.Value)
                    return (recipe, 0);

                portions = portions == 0
                    ? reagents[reagent.Key].Int() / reagent.Value.Int()
                    : Math.Min(portions, reagents[reagent.Key].Int() / reagent.Value.Int());
            }

            //cook only as many of those portions as time allows
            return (recipe, (int) Math.Min(portions, component.CurrentCookTimerTime / recipe.CookTime));
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            var query = EntityQueryEnumerator<ActiveMicrowaveComponent, MicrowaveComponent>();
            while (query.MoveNext(out var uid, out var active, out var microwave))
            {

                active.CookTimeRemaining -= frameTime;

                RollMalfunction((uid, active, microwave));

                //check if there's still cook time left
                if (active.CookTimeRemaining > 0)
                {
                    AddTemperature(microwave, frameTime);
                    continue;
                }

                //this means the microwave has finished cooking.
                AddTemperature(microwave, Math.Max(frameTime + active.CookTimeRemaining, 0)); //Though there's still a little bit more heat to pump out

                if (active.PortionedRecipe.Item1 != null)
                {
                    var coords = Transform(uid).Coordinates;
                    for (var i = 0; i < active.PortionedRecipe.Item2; i++)
                    {
                        SubtractContents(microwave, active.PortionedRecipe.Item1);
                        // Frontier: ResultCount - support multiple results per recipe
                        for (var r = 0; r < active.PortionedRecipe.Item1.ResultCount; r++)
                        {
                            Spawn(active.PortionedRecipe.Item1.Result, coords);
                        }
                        // End Frontier
                    }
                }

                _container.EmptyContainer(microwave.Storage);
                microwave.CurrentCookTimeEnd = TimeSpan.Zero;
                UpdateUserInterfaceState(uid, microwave);
                _audio.PlayPvs(microwave.FoodDoneSound, uid);
                StopCooking((uid, microwave));
            }
        }

        /// <summary>
        /// This event tries to get secret recipes that the microwave might be capable of.
        /// Currently, we only check the microwave itself, but in the future, the user might be able to learn recipes.
        /// </summary>
        private void OnGetSecretRecipes(Entity<FoodRecipeProviderComponent> ent, ref GetSecretRecipesEvent args)
        {
            foreach (ProtoId<FoodRecipePrototype> recipeId in ent.Comp.ProvidedRecipes)
            {
                if (_prototype.TryIndex(recipeId, out var recipeProto))
                {
                    args.Recipes.Add(recipeProto);
                }
            }
        }

        #region ui
        private void OnEjectMessage(Entity<MicrowaveComponent> ent, ref MicrowaveEjectMessage args)
        {
            if (!HasContents(ent.Comp) || HasComp<ActiveMicrowaveComponent>(ent))
                return;

            _container.EmptyContainer(ent.Comp.Storage);
            _audio.PlayPvs(ent.Comp.ClickSound, ent, AudioParams.Default.WithVolume(-2));
            UpdateUserInterfaceState(ent, ent.Comp);
        }

        private void OnEjectIndex(Entity<MicrowaveComponent> ent, ref MicrowaveEjectSolidIndexedMessage args)
        {
            if (!HasContents(ent.Comp) || HasComp<ActiveMicrowaveComponent>(ent))
                return;

            _container.Remove(EntityManager.GetEntity(args.EntityID), ent.Comp.Storage);
            UpdateUserInterfaceState(ent, ent.Comp);
        }

        private void OnSelectTime(Entity<MicrowaveComponent> ent, ref MicrowaveSelectCookTimeMessage args)
        {
            if (!HasContents(ent.Comp) || HasComp<ActiveMicrowaveComponent>(ent) || !(TryComp<ApcPowerReceiverComponent>(ent, out var apc) && apc.Powered))
                return;

            // some validation to prevent trollage
            if (args.NewCookTime % 5 != 0 || args.NewCookTime > ent.Comp.MaxCookTime)
                return;

            ent.Comp.CurrentCookTimeButtonIndex = args.ButtonIndex;
            ent.Comp.CurrentCookTimerTime = args.NewCookTime;
            ent.Comp.CurrentCookTimeEnd = TimeSpan.Zero;
            _audio.PlayPvs(ent.Comp.ClickSound, ent, AudioParams.Default.WithVolume(-2));
            UpdateUserInterfaceState(ent, ent.Comp);
        }
        #endregion
    }
}
