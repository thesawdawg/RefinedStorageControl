using System;
using System.Collections.Generic;
using System.Linq;
using RefinedStorageControl.Game.Persistence;
using RefinedStorageControl.Game.Providers;
using RefinedStorageControl.Routing;
using UnityEngine.Scripting;

namespace RefinedStorageControl.Game.UI
{
    // Interface settings window (ROUTE-02/05, host player): priority buttons,
    // a filter-mode toggle, and a filter list built from the held item.
    [Preserve]
    public class XUiC_RscInterface : XUiController
    {
        public const string WindowGroup = "rscInterface";

        private static Vector3i _pendingPos;

        private Vector3i _pos;
        private string _status = "";

        public static void Open(EntityPlayerLocal player, Vector3i pos)
        {
            _pendingPos = pos;
            LocalPlayerUI.GetUIForPlayer(player).windowManager.Open(WindowGroup, true);
        }

        public override void Init()
        {
            base.Init();
            Button("btnMinus10", () => AddPriority(-10));
            Button("btnMinus1", () => AddPriority(-1));
            Button("btnPlus1", () => AddPriority(1));
            Button("btnPlus10", () => AddPriority(10));
            Button("btnMode", CycleMode);
            Button("btnAddHeld", AddHeld);
            Button("btnClear", () => SetFilter(FilterMode.None, new string[0], "Filter cleared"));
        }

        public override void OnOpen()
        {
            base.OnOpen();
            _pos = _pendingPos;
            _status = "";
            RefreshBindings();
        }

        private long Key => Core.PositionKey.Pack(_pos.x, _pos.y, _pos.z);

        private NodeConfig Config => NodeConfigFile.Store.Get(Key);

        private void AddPriority(int delta)
        {
            int priority = Math.Max(ProviderRouting.MinPriority, Math.Min(ProviderRouting.MaxPriority, Config.Priority + delta));
            NodeConfigFile.Store.Set(Key, new NodeConfig(priority, Config.Filter));
            _status = "";
            RefreshBindings();
        }

        private void CycleMode()
        {
            var filter = Config.Filter;
            var next = filter.Mode == FilterMode.None ? FilterMode.Allow
                : filter.Mode == FilterMode.Allow ? FilterMode.Block : FilterMode.None;
            var names = next == FilterMode.None ? new string[0] : filter.Names.ToArray();
            SetFilter(next, names, next == FilterMode.Allow && names.Length == 0
                ? "Allow list is empty: nothing will be stored here until you add items" : "");
        }

        private void AddHeld()
        {
            var held = xui.playerUI.entityPlayer.inventory.holdingItemItemValue;
            if (held == null || held.IsEmpty() || held.ItemClass == null)
            {
                _status = "Hold the item you want to add";
                RefreshBindings();
                return;
            }
            var filter = Config.Filter;
            var mode = filter.Mode == FilterMode.None ? FilterMode.Allow : filter.Mode;
            var names = new List<string>(filter.Names);
            string name = held.ItemClass.GetItemName();
            if (!names.Contains(name))
                names.Add(name);
            SetFilter(mode, names, "Added " + held.ItemClass.GetLocalizedItemName());
        }

        private void SetFilter(FilterMode mode, IEnumerable<string> names, string status)
        {
            NodeConfigFile.Store.Set(Key, new NodeConfig(Config.Priority, new ItemFilter(mode, names)));
            _status = status;
            RefreshBindings();
        }

        public override bool GetBindingValueInternal(ref string value, string bindingName)
        {
            switch (bindingName)
            {
                case "title":
                    value = Localization.Get("rscInterfaceTitle");
                    return true;
                case "priority":
                    value = Config.Priority.ToString();
                    return true;
                case "filtermode":
                    var mode = Config.Filter.Mode;
                    value = mode == FilterMode.None ? "Accept everything"
                        : mode == FilterMode.Allow ? "Only listed items" : "Everything except listed";
                    return true;
                case "filteritems":
                    var names = Config.Filter.Names.Select(n => ItemClass.GetItemClass(n)?.GetLocalizedItemName() ?? n).ToList();
                    names.Sort(StringComparer.CurrentCultureIgnoreCase);
                    value = names.Count == 0 ? "(none)" : string.Join(", ", names);
                    return true;
                case "containers":
                    var world = GameManager.Instance.World;
                    int count = world == null ? 0 : TransferService.ContainersAround(world, _pos).Count;
                    value = count == 0 ? "No player-placed container is touching this interface"
                        : "Serves " + count + " container" + (count == 1 ? "" : "s");
                    return true;
                case "status":
                    value = _status;
                    return true;
            }
            return base.GetBindingValueInternal(ref value, bindingName);
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
