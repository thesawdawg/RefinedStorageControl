using System;
using System.Collections.Generic;
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
    [Preserve]
    public class XUiC_RscTerminal : XUiController
    {
        public const string WindowGroup = "rscTerminal";
        private const float RefreshSeconds = 0.5f;
        private const float ClientRefreshSeconds = 1f;

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
            Button("btnNetNext", () => { if ((_netPage + 1) * _netCells.Length < _shown.Count) _netPage++; ShowNet(); });
            Button("btnBagPrev", () => { _bagPage = Math.Max(0, _bagPage - 1); ShowBag(); });
            Button("btnBagNext", () => { if ((_bagPage + 1) * _bagCells.Length < Bag.Grid.Length) _bagPage++; ShowBag(); });
            Button("btnDepositAll", () => StoreMany(matchingOnly: false));
            Button("btnDepositMatching", () => StoreMany(matchingOnly: true));
            Button("btnCategory", () =>
            {
                _category = TerminalView.NextCategory(_category, ViewItems(_backend?.Entries));
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
            Guard(Sync);
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

        private void ShowNet()
        {
            int start = _netPage * _netCells.Length;
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
                    value = _backend.Error ?? (pending > 0 ? "Storing... " + pending + " left" : _backend.Status ?? "");
                    return true;
                case "netpage":
                    value = (_netPage + 1) + "/" + Math.Max(1, (_shown.Count + _netCells.Length - 1) / Math.Max(1, _netCells.Length))
                        + "  (" + _shown.Count + " kinds)";
                    return true;
                case "category":
                    value = TerminalView.Find(_category).Label;
                    return true;
                case "sortmode":
                    value = "Sort: " + (_sort == SortMode.Name ? "Name" : _sort == SortMode.Count ? "Count" : "Quality");
                    return true;
                case "favmode":
                    value = _markFavorites ? "Marking favorites" : "Mark favorites";
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
