using System;
using System.Collections.Generic;
using RefinedStorageControl.Game.Crafting;
using RefinedStorageControl.Game.Providers;
using RefinedStorageControl.Storage;
using RefinedStorageControl.View;
using UnityEngine.Scripting;

namespace RefinedStorageControl.Game.UI
{
    // Storage terminal window (UI-01/02/03). Top grid: everything this player
    // can reach through the network, one cell per exact item state; left click
    // takes a stack, right click takes one. Bottom grid: the player's backpack;
    // left click stores that slot. The host calls the transfer service directly;
    // co-op clients go through network packages (see ITerminalBackend).
    // Craft mode (CRAFT-01/04): the top grid lists recipes the connected
    // stations can make, each cell showing how many crafts are possible now;
    // pick one, set the amount, Craft, and a five-second countdown can be
    // cancelled before anything is taken.
    [Preserve]
    public class XUiC_RscTerminal : XUiController
    {
        public const string WindowGroup = "rscTerminal";
        private const float RefreshSeconds = 0.5f;
        private const float ClientRefreshSeconds = 1f;
        private const float CraftListSeconds = 5f;

        private static Vector3i _pendingPos;

        private ITerminalBackend _backend;
        private XUiC_RscCell[] _netCells = new XUiC_RscCell[0];
        private XUiC_RscCell[] _bagCells = new XUiC_RscCell[0];
        private XUiC_TextInput _search;
        private readonly List<TransferService.Entry> _shown = new List<TransferService.Entry>();
        private int _seenVersion = -1;
        private int _netPage;
        private int _bagPage;
        private float _refreshIn;
        private bool _errorLogged;

        // Kept across openings so the terminal remembers the player's view.
        private static string _category = TerminalView.AllKey;
        private static SortMode _sort = SortMode.Name;
        private bool _markFavorites;

        private static bool _crafting;
        private ICraftBackend _craft;
        private readonly List<CraftOption> _shownOptions = new List<CraftOption>();
        private int _seenCraftVersion = -1;
        private int _selectedRecipe = -1;
        private int _amount = 1;
        private float _craftListIn;
        private float _craftPollIn;
        private int _lastPending;

        public static void Open(EntityPlayerLocal player, Vector3i pos)
        {
            _pendingPos = pos;
            LocalPlayerUI.GetUIForPlayer(player).windowManager.Open(WindowGroup, true);
        }

        public override void Init()
        {
            base.Init();
            _netCells = CellsIn("netGrid", NetClicked);
            _bagCells = CellsIn("bagGrid", BagClicked);
            _search = GetChildById("search") as XUiC_TextInput;
            if (_search != null)
                _search.OnChangeHandler += (sender, text, changeFromCode) =>
                {
                    _netPage = 0;
                    ApplySearch();
                };
            Button("btnNetPrev", () => { _netPage = Math.Max(0, _netPage - 1); ShowNet(); });
            Button("btnNetNext", () => { if ((_netPage + 1) * _netCells.Length < ShownCount) _netPage++; ShowNet(); });
            Button("btnBagPrev", () => { _bagPage = Math.Max(0, _bagPage - 1); ShowBag(); });
            Button("btnBagNext", () => { if ((_bagPage + 1) * _bagCells.Length < Bag.Grid.Length) _bagPage++; ShowBag(); });
            Button("btnDepositAll", () => StoreMany(matchingOnly: false));
            Button("btnDepositMatching", () => StoreMany(matchingOnly: true));
            Button("btnCategory", () =>
            {
                _category = TerminalView.NextCategory(_category,
                    _crafting ? OptionItems(_craft?.Options) : ViewItems(_backend?.Entries));
                _netPage = 0;
                ApplySearch();
            });
            Button("btnSort", () =>
            {
                _sort = TerminalView.NextSort(_sort);
                ApplySearch();
            });
            Button("btnFavorites", () =>
            {
                _markFavorites = !_markFavorites;
                RefreshBindings();
            });
            Button("btnMode", () =>
            {
                _crafting = !_crafting;
                _netPage = 0;
                if (_crafting)
                    Guard(ListRecipes);
                ApplySearch();
                RefreshBindings();
            });
            Button("btnCraftLess", () => SetAmount(_amount - 1));
            Button("btnCraftMore", () => SetAmount(_amount + 1));
            Button("btnCraftMax", () => SetAmount(Math.Max(1, Selected()?.Max ?? 1)));
            Button("btnCraft", () =>
            {
                var option = Selected();
                if (option != null)
                    Guard(() => _craft.Request(option, _amount));
                _craftListIn = 0.3f;
            });
            Button("btnCraftCancel", () =>
            {
                var pending = _craft?.Pending;
                if (pending != null && pending.Count > 0)
                    Guard(() => _craft.Cancel(pending[pending.Count - 1].Id));
                _craftListIn = 0.3f;
            });
        }

        public override void OnOpen()
        {
            base.OnOpen();
            _backend = ConnectionManager.Instance.IsServer
                ? (ITerminalBackend)new HostTerminalBackend(Player)
                : new ClientTerminalBackend();
            _netPage = 0;
            _bagPage = 0;
            _seenVersion = -1;
            _markFavorites = false;
            _refreshIn = RefreshInterval;
            Guard(() => _backend.Open(_pendingPos));
            _craft = ConnectionManager.Instance.IsServer
                ? (ICraftBackend)new HostCraftBackend(Player, _pendingPos)
                : new ClientCraftBackend();
            _seenCraftVersion = -1;
            _selectedRecipe = -1;
            _amount = 1;
            _lastPending = 0;
            if (_crafting)
                Guard(ListRecipes);
            Guard(Sync);
        }

        public override void OnClose()
        {
            base.OnClose();
            _backend?.Close();
        }

        public override void Update(float _dt)
        {
            base.Update(_dt);
            if (_backend == null || !IsOpen)
                return;
            _refreshIn -= _dt;
            if (_refreshIn <= 0f)
            {
                _refreshIn = RefreshInterval;
                Guard(_backend.Refresh);
            }
            if (_crafting)
                Guard(() => TickCrafting(_dt));
            Guard(Sync);
        }

        // Recipe counts change as storage and requests change: list again every
        // few seconds and soon after a countdown ends; clients also poll the
        // countdown while one runs.
        private void TickCrafting(float dt)
        {
            var pending = _craft.Pending;
            if (pending.Count != _lastPending)
            {
                _lastPending = pending.Count;
                _craftListIn = Math.Min(_craftListIn, 0.5f);
            }
            _craftListIn -= dt;
            if (_craftListIn <= 0f)
                ListRecipes();
            _craftPollIn -= dt;
            if (pending.Count > 0 && _craftPollIn <= 0f)
            {
                _craftPollIn = 1f;
                _craft.Poll();
            }
        }

        private void ListRecipes()
        {
            _craftListIn = CraftListSeconds;
            _craft.List();
        }

        private CraftOption Selected() => _craft?.Options?.Find(o => o.RecipeIndex == _selectedRecipe);

        private void SetAmount(int amount)
        {
            _amount = Math.Max(1, Math.Min(CraftService.MaxCraftsPerRequest, amount));
            RefreshBindings();
        }

        private float RefreshInterval => ConnectionManager.Instance.IsServer ? RefreshSeconds : ClientRefreshSeconds;

        private bool IsOpen => xui?.playerUI?.windowManager?.IsWindowOpen(WindowGroup) == true;

        private EntityPlayerLocal Player => xui.playerUI.entityPlayer;

        private BagEndpoint Bag => new BagEndpoint(Player);

        // Runs every frame: network cells change only on new backend data; backpack
        // cells skip their redraw unless the slot changed.
        private void Sync()
        {
            if (_backend.Version != _seenVersion)
            {
                _seenVersion = _backend.Version;
                if (!_crafting)
                    ApplySearch();
            }
            if (_crafting && _craft.Version != _seenCraftVersion)
            {
                _seenCraftVersion = _craft.Version;
                ApplySearch();
            }
            ShowBag();
            RefreshBindings();
        }

        private void Guard(Action action)
        {
            try
            {
                action();
                _errorLogged = false;
            }
            catch (Exception e)
            {
                // Keep the window usable and log once, not on every frame.
                if (!_errorLogged)
                    RscLog.Error("terminal window failed: " + e);
                _errorLogged = true;
            }
        }

        private void ApplySearch()
        {
            if (_crafting)
            {
                var options = _craft.Options;
                _shownOptions.Clear();
                foreach (int i in TerminalView.Arrange(OptionItems(options), _search?.Text, _category, _sort, Favorites.Names))
                    _shownOptions.Add(options[i]);
                int craftPages = Math.Max(1, (_shownOptions.Count + _netCells.Length - 1) / Math.Max(1, _netCells.Length));
                _netPage = Math.Min(_netPage, craftPages - 1);
                ShowNet();
                return;
            }
            var entries = _backend.Entries;
            _shown.Clear();
            foreach (int i in TerminalView.Arrange(ViewItems(entries), _search?.Text, _category, _sort, Favorites.Names))
                _shown.Add(entries[i]);
            int pages = Math.Max(1, (_shown.Count + _netCells.Length - 1) / Math.Max(1, _netCells.Length));
            _netPage = Math.Min(_netPage, pages - 1);
            ShowNet();
        }

        private static List<ViewItem> ViewItems(List<TransferService.Entry> entries)
        {
            var items = new List<ViewItem>(entries?.Count ?? 0);
            if (entries != null)
                foreach (var e in entries)
                {
                    var cls = e.Sample.ItemClass;
                    items.Add(new ViewItem(cls.GetLocalizedItemName(), cls.GetItemName(), e.Count,
                        e.Sample.HasQuality ? e.Sample.Quality : 0, cls.Groups));
                }
            return items;
        }

        private static List<ViewItem> OptionItems(List<CraftOption> options)
        {
            var items = new List<ViewItem>(options?.Count ?? 0);
            if (options != null)
                foreach (var o in options)
                {
                    var cls = o.Output.ItemClass;
                    items.Add(new ViewItem(cls.GetLocalizedItemName(), cls.GetItemName(), o.Max, 0, cls.Groups));
                }
            return items;
        }

        private int ShownCount => _crafting ? _shownOptions.Count : _shown.Count;

        private void ShowNet()
        {
            int start = _netPage * _netCells.Length;
            if (_crafting)
            {
                for (int i = 0; i < _netCells.Length; i++)
                {
                    int n = start + i;
                    var cell = _netCells[i];
                    cell.Index = n;
                    if (n < _shownOptions.Count)
                    {
                        var o = _shownOptions[n];
                        cell.Set(o.Output, o.Max, false, recipe: true, selected: o.RecipeIndex == _selectedRecipe);
                        cell.Favorite = Favorites.Names.Contains(o.Output.ItemClass.GetItemName());
                    }
                    else
                    {
                        cell.Set(null, 0, false);
                        cell.Favorite = false;
                    }
                }
                return;
            }
            for (int i = 0; i < _netCells.Length; i++)
            {
                int n = start + i;
                var cell = _netCells[i];
                cell.Index = n;
                if (n < _shown.Count)
                {
                    cell.Set(_shown[n].Sample, _shown[n].Count, false);
                    cell.Favorite = Favorites.Names.Contains(_shown[n].Sample.ItemClass.GetItemName());
                }
                else
                {
                    cell.Set(null, 0, false);
                    cell.Favorite = false;
                }
            }
        }

        private void ShowBag()
        {
            var bag = Bag;
            var grid = bag.Grid;
            int start = _bagPage * _bagCells.Length;
            for (int i = 0; i < _bagCells.Length; i++)
            {
                int n = start + i;
                var cell = _bagCells[i];
                cell.Index = n;
                if (n < grid.Length)
                {
                    var stack = grid[n];
                    bool empty = GridEndpoint.IsEmpty(stack);
                    cell.Set(empty ? null : stack.itemValue, empty ? 0 : stack.count, bag.SlotLocked(n));
                }
                else
                    cell.Set(null, 0, false, visible: false);
            }
        }

        private void NetClicked(XUiC_RscCell cell, int button)
        {
            if (_crafting)
            {
                if (cell.Index >= _shownOptions.Count)
                    return;
                var option = _shownOptions[cell.Index];
                if (_markFavorites)
                    Favorites.Toggle(option.Output.ItemClass.GetItemName());
                else if (option.RecipeIndex != _selectedRecipe)
                {
                    _selectedRecipe = option.RecipeIndex;
                    _amount = 1;
                }
                ApplySearch();
                RefreshBindings();
                return;
            }
            if (cell.Index >= _shown.Count)
                return;
            var entry = _shown[cell.Index];
            if (_markFavorites)
            {
                Favorites.Toggle(entry.Sample.ItemClass.GetItemName());
                ApplySearch();
                return;
            }
            Guard(() => _backend.Take(entry, button == 0 ? entry.Sample.ItemClass.MaxCount : 1));
            _refreshIn = 0.1f;
        }

        private void BagClicked(XUiC_RscCell cell, int button)
        {
            var bag = Bag;
            if (button != 0 || cell.Index >= bag.Grid.Length || bag.SlotLocked(cell.Index))
                return;
            var key = bag.KeyAt(cell.Index);
            if (key.HasValue)
                Guard(() => _backend.Store(new List<(int, ItemKey)> { (cell.Index, key.Value) }));
        }

        private void StoreMany(bool matchingOnly)
        {
            var bag = Bag;
            var stored = new HashSet<ItemKey>();
            foreach (var e in _backend.Entries)
                stored.Add(e.Key);
            var slots = new List<(int, ItemKey)>();
            for (int i = 0; i < bag.Grid.Length; i++)
            {
                if (bag.SlotLocked(i))
                    continue;
                var key = bag.KeyAt(i);
                if (key.HasValue && (!matchingOnly || stored.Contains(key.Value)))
                    slots.Add((i, key.Value));
            }
            Guard(() => _backend.Store(slots));
        }

        public override bool GetBindingValueInternal(ref string value, string bindingName)
        {
            switch (bindingName)
            {
                case "title":
                    value = Localization.Get("rscTerminalTitle");
                    return true;
                case "status":
                    if (_backend == null)
                    {
                        value = "";
                        return true;
                    }
                    int pending = _backend.Pending;
                    if (_crafting && _craft != null)
                        value = _craft.Error ?? _craft.Status ?? _backend.Status ?? "";
                    else
                        value = _backend.Error ?? (pending > 0 ? "Storing... " + pending + " left" : _backend.Status ?? "");
                    return true;
                case "netpage":
                    value = (_netPage + 1) + "/" + Math.Max(1, (ShownCount + _netCells.Length - 1) / Math.Max(1, _netCells.Length))
                        + "  (" + ShownCount + (_crafting ? " recipes)" : " kinds)");
                    return true;
                case "modebtn":
                    value = Localization.Get(_crafting ? "rscModeStorage" : "rscModeCraft");
                    return true;
                case "craftmode":
                    value = _crafting.ToString();
                    return true;
                case "craftinfo":
                    var selected = _crafting ? Selected() : null;
                    value = selected == null
                        ? Localization.Get("rscCraftPick")
                        : selected.Output.ItemClass.GetLocalizedItemName()
                          + (selected.PerCraft > 1 ? " (x" + selected.PerCraft + " each)" : "") + ": " + selected.Info;
                    return true;
                case "craftamount":
                    value = _amount.ToString();
                    return true;
                case "craftpending":
                    var waiting = _crafting ? _craft?.Pending : null;
                    if (waiting == null || waiting.Count == 0)
                        value = "";
                    else
                    {
                        var last = waiting[waiting.Count - 1];
                        value = last.Crafts + "x " + last.Item + " in " + Math.Ceiling(last.SecondsLeft) + "s"
                            + (waiting.Count > 1 ? " (+" + (waiting.Count - 1) + ")" : "");
                    }
                    return true;
                case "haspending":
                    value = (_crafting && _craft != null && _craft.Pending.Count > 0).ToString();
                    return true;
                case "category":
                    value = TerminalView.Find(_category).Label;
                    return true;
                case "sortmode":
                    value = "Sort: " + (_sort == SortMode.Name ? "Name" : _sort == SortMode.Count ? "Count" : "Quality");
                    return true;
                case "favmode":
                    value = Localization.Get(_markFavorites ? "rscFavMarking" : "rscFavMark");
                    return true;
                case "bagpage":
                    int bagLen = xui?.playerUI?.entityPlayer?.bag?.SlotCount ?? 0;
                    value = (_bagPage + 1) + "/" + Math.Max(1, (bagLen + _bagCells.Length - 1) / Math.Max(1, _bagCells.Length));
                    return true;
            }
            return base.GetBindingValueInternal(ref value, bindingName);
        }

        private XUiC_RscCell[] CellsIn(string gridId, Action<XUiC_RscCell, int> onClick)
        {
            var grid = GetChildById(gridId);
            var cells = grid?.GetChildrenByType<XUiC_RscCell>() ?? new XUiC_RscCell[0];
            foreach (var c in cells)
                c.Clicked += onClick;
            return cells;
        }

        private void Button(string id, Action onPress)
        {
            var child = GetChildById(id);
            if (child is XUiC_SimpleButton simple)
                simple.OnPressed += (sender, button) => onPress();
            else if (child != null)
                child.OnPress += (sender, button) => onPress();
        }
    }
}
