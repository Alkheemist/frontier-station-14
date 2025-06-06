using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared.VendingMachines;
using Content.Shared.Cargo.Components;
using Content.Shared.Stacks;
using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface.Controls;
using Content.Shared._NF.Bank; // Frontier
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Prototypes;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.Reagent;
using FancyWindow = Content.Client.UserInterface.Controls.FancyWindow;
using Robust.Client.UserInterface;
using Content.Shared.IdentityManagement;
using Robust.Client.Graphics;
using Robust.Shared.Utility;

namespace Content.Client.VendingMachines.UI
{
    [GenerateTypedNameReferences]
    public sealed partial class VendingMachineMenu : FancyWindow
    {
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly IComponentFactory _componentFactory = default!; // Frontier

        private readonly Dictionary<EntProtoId, EntityUid> _dummies = [];
        private readonly Dictionary<EntProtoId, (ListContainerButton Button, VendingMachineItem Item)> _listItems = new();
        private readonly Dictionary<EntProtoId, uint> _amounts = new();

        /// <summary>
        /// Whether the vending machine is able to be interacted with or not.
        /// </summary>
        private bool _enabled;

        public event Action<GUIBoundKeyEventArgs, ListData>? OnItemSelected;

        public VendingMachineMenu()
        {
            MinSize = SetSize = new Vector2(250, 150);
            RobustXamlLoader.Load(this);
            IoCManager.InjectDependencies(this);

            VendingContents.SearchBar = SearchBar;
            VendingContents.DataFilterCondition += DataFilterCondition;
            VendingContents.GenerateItem += GenerateButton;
            VendingContents.ItemKeyBindDown += (args, data) => OnItemSelected?.Invoke(args, data);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            // Don't clean up dummies during disposal or we'll just have to spawn them again
            if (!disposing)
                return;

            // Delete any dummy items we spawned
            foreach (var entity in _dummies.Values)
            {
                _entityManager.QueueDeleteEntity(entity);
            }
            _dummies.Clear();
        }

        private bool DataFilterCondition(string filter, ListData data)
        {
            if (data is not VendorItemsListData { ItemText: var text })
                return false;

            if (string.IsNullOrEmpty(filter))
                return true;

            return text.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
        }

        private void GenerateButton(ListData data, ListContainerButton button)
        {
            if (data is not VendorItemsListData { ItemProtoID: var protoID, ItemText: var text })
                return;

            var item = new VendingMachineItem(protoID, text);
            _listItems[protoID] = (button, item);
            button.AddChild(item);
            button.AddStyleClass("ButtonSquare");
            button.Disabled = !_enabled || _amounts[protoID] == 0;
        }

        /// <summary>
        /// Populates the list of available items on the vending machine interface
        /// and sets icons based on their prototypes
        /// </summary>
        public void Populate(List<VendingMachineInventoryEntry> inventory, bool enabled, float priceModifier, int balance, int? cashSlotBalance) // Frontier: add priceModifier, balance, cashSlotBalance
        {
            _enabled = enabled;
            _listItems.Clear();
            _amounts.Clear();

            UpdateBalance(balance); // Frontier
            UpdateCashSlotBalance(cashSlotBalance); // Frontier

            if (inventory.Count == 0 && VendingContents.Visible)
            {
                SearchBar.Visible = false;
                VendingContents.Visible = false;

                var outOfStockLabel = new Label()
                {
                    Text = Loc.GetString("vending-machine-component-try-eject-out-of-stock"),
                    Margin = new Thickness(4, 4),
                    HorizontalExpand = true,
                    VerticalAlignment = VAlignment.Stretch,
                    HorizontalAlignment = HAlignment.Center
                };

                MainContainer.AddChild(outOfStockLabel);

                SetSizeAfterUpdate(outOfStockLabel.Text.Length, 0);

                return;
            }

            var longestEntry = string.Empty;
            var listData = new List<VendorItemsListData>();

            for (var i = 0; i < inventory.Count; i++)
            {
                var entry = inventory[i];

                if (!_prototypeManager.TryIndex(entry.ID, out var prototype))
                {
                    _amounts[entry.ID] = 0;
                    continue;
                }

                if (!_dummies.TryGetValue(entry.ID, out var dummy))
                {
                    dummy = _entityManager.Spawn(entry.ID);
                    _dummies.Add(entry.ID, dummy);
                }

                var cost = GetPrototypePrice(prototype, priceModifier); // Frontier: item pricing

                var itemName = Identity.Name(dummy, _entityManager);

                // Frontier: unlimited vending
                string itemText;
                if (entry.Amount != uint.MaxValue)
                    itemText = $"[{BankSystemExtensions.ToSpesoString(cost)}] {itemName} [{entry.Amount}]";
                else
                    itemText = $"[{BankSystemExtensions.ToSpesoString(cost)}] {itemName}";
                // End Frontier: unlimited vending
                _amounts[entry.ID] = entry.Amount;

                if (itemText.Length > longestEntry.Length)
                    longestEntry = itemText;

                listData.Add(new VendorItemsListData(prototype!.ID, i) // Frontier: prototype<prototype!
                {
                    ItemText = itemText,
                });
            }

            VendingContents.PopulateList(listData);

            SetSizeAfterUpdate(longestEntry.Length, inventory.Count);
        }

        // Frontier
        public void UpdateBalance(int balance)
        {
            BalanceLabel.Text = BankSystemExtensions.ToSpesoString(balance);
        }

        public void UpdateCashSlotBalance(int? balance)
        {
            CashSlotControls.Visible = balance != null;
            if (balance != null)
                CashSlotLabel.Text = BankSystemExtensions.ToSpesoString(balance.Value);
        }
        // End Frontier

        /// <summary>
        /// Updates text entries for vending data in place without modifying the list controls.
        /// </summary>
        public void UpdateAmounts(List<VendingMachineInventoryEntry> cachedInventory, float priceModifier, bool enabled) // Frontier: add priceModifier
        {
            _enabled = enabled;

            foreach (var proto in _dummies.Keys)
            {
                if (!_listItems.TryGetValue(proto, out var button))
                    continue;

                var dummy = _dummies[proto];
                if (!cachedInventory.TryFirstOrDefault(o => o.ID == proto, out var entry))
                    continue;
                var amount = entry.Amount;
                // Could be better? Problem is all inventory entries get squashed.
                var text = GetItemText(dummy, amount, priceModifier);

                button.Item.SetText(text);
                button.Button.Disabled = !enabled || amount == 0;
            }
        }

        private string GetItemText(EntityUid dummy, uint amount, float priceModifier) // Frontier: add priceModifier
        {
            // Frontier: lookup price from entity, finite output
            var cost = (int)(20 * priceModifier);
            if (_entityManager.TryGetComponent(dummy, out MetaDataComponent? component) && component.EntityPrototype != null)
            {
                cost = GetPrototypePrice(component.EntityPrototype, priceModifier);
            }

            var itemName = Identity.Name(dummy, _entityManager);
            if (amount != uint.MaxValue)
                return $"[{BankSystemExtensions.ToSpesoString(cost)}] {itemName} [{amount}]";
            else
                return $"[{BankSystemExtensions.ToSpesoString(cost)}] {itemName}";
            // End Frontier
        }

        private void SetSizeAfterUpdate(int longestEntryLength, int contentCount)
        {
            SetSize = new Vector2(Math.Clamp((longestEntryLength + 2) * 12, 250, 400),
                Math.Clamp(contentCount * 50, 150, 350));
        }

        // Frontier: get item price
        private int GetPrototypePrice(EntityPrototype prototype, float priceModifier)
        {
            // Check for vending price - if anything sets it explicitly, use that number as-is.
            double vendPrice = 0;
            if (prototype.TryGetComponent<StaticPriceComponent>(out var staticComp, _componentFactory) && staticComp.VendPrice > 0.0)
            {
                vendPrice += staticComp.VendPrice;
            }
            else if (prototype.TryGetComponent<StackPriceComponent>(out var stackComp, _componentFactory) && stackComp.VendPrice > 0.0)
            {
                vendPrice += stackComp.VendPrice;
            }

            if (vendPrice > 0.0)
                return (int)vendPrice;

            // ok so we dont really have access to the pricing system so we are doing a quick price check
            // based on prototype info since the items inside a vending machine dont actually exist as entities
            // until they are spawned. So this little alg does the following:
            // first, checks for a staticprice component, and if it has one, checks to make sure its not 0 since
            // stacks and other items have 0 cost.
            // If the price is 0, then we check for both a stack price and a stack component, since if it has one
            // it should have the other too, and then calculates the price based on that.
            // If the price is still 0 or non-existant (this is the case for food and containers since their value is
            // determined dynamically by their contents/inventory), it then falls back to the default mystery
            // hardcoded value of 20xMarketModifier.
            double cost = 20;
            if (prototype.TryGetComponent<StaticPriceComponent>(out var priceComponent, _componentFactory))
            {
                if (priceComponent.Price != 0)
                {
                    cost = priceComponent.Price;
                }
                else
                {
                    if (prototype.TryGetComponent<StackPriceComponent>(out var stackPrice, _componentFactory)
                        && prototype.TryGetComponent<StackComponent>(out var stack, _componentFactory))
                    {
                        cost = stackPrice.Price * stack.Count;
                    }
                }
            }
            cost *= priceModifier;

            if (prototype.TryGetComponent<SolutionContainerManagerComponent>(out var priceSolutions, _componentFactory))
            {
                if (priceSolutions.Solutions != null)
                {
                    foreach (var solution in priceSolutions.Solutions.Values)
                    {
                        foreach (var (reagent, quantity) in solution.Contents)
                        {
                            if (!_prototypeManager.TryIndex<ReagentPrototype>(reagent.Prototype,
                                    out var reagentProto))
                                continue;

                            // TODO check ReagentData for price information?
                            var costReagent = quantity.Float() * reagentProto.PricePerUnit;
                            cost += costReagent * priceModifier;
                        }
                    }
                }
            }

            return (int)cost;
        }
    }
    // End Frontier

    public record VendorItemsListData(EntProtoId ItemProtoID, int ItemIndex) : ListData
    {
        public string ItemText = string.Empty;
    }
}
