using System;
using System.Collections.Generic;
using System.Linq;
using RefinedStorageControl.Routing;
using UnityEngine.Scripting;

namespace RefinedStorageControl.Game.UI
{
    // Interface settings window (ROUTE-02/05): priority buttons, a filter-mode
    // toggle, and a filter list built from the held item. The host edits the
    // settings store directly; co-op clients go through network packages.
    [Preserve]
    public class XUiC_RscInterface : XUiController
    {
        public const string WindowGroup = "rscInterface";

        private static Vector3i _pendingPos;

        private IInterfaceBackend _backend;
        private int _seenVersion = -1;
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
            _backend = ConnectionManager.Instance.IsServer
                ? (IInterfaceBackend)new HostInterfaceBackend()
                : new ClientInterfaceBackend();
            _backend.Open(_pendingPos);
            _status = "";
            _seenVersion = -1;
            RefreshBindings();
        }

        public override void Update(float _dt)
        {
            base.Update(_dt);
            if (_backend != null && _backend.Version != _seenVersion)
            {
                _seenVersion = _backend.Version;
                RefreshBindings();
            }
        }

        private NodeConfig Config => _backend?.Config ?? NodeConfig.Default;

        private bool IsCabinet =>
            Topology.RscBlocks.TryGetRole(GameManager.Instance.World?.GetBlock(_pendingPos).Block, out var kind, out _)
            && kind == Core.NodeKind.DriveCabinet;

        private void AddPriority(int delta)
        {
            int priority = Math.Max(ProviderRouting.MinPriority, Math.Min(ProviderRouting.MaxPriority, Config.Priority + delta));
            _status = "";
            _backend.Apply(new NodeConfig(priority, Config.Filter));
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
            _status = status;
            _backend.Apply(new NodeConfig(Config.Priority, new ItemFilter(mode, names)));
            RefreshBindings();
        }

        public override bool GetBindingValueInternal(ref string value, string bindingName)
        {
            switch (bindingName)
            {
                case "title":
                    value = Localization.Get(IsCabinet ? "rscCabinetSettingsTitle" : "rscInterfaceTitle");
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
                    if (IsCabinet)
                    {
                        value = _backend?.Error ?? "Drive cabinet: these settings apply to all of its disks";
                        return true;
                    }
                    int count = _backend?.Containers ?? 0;
                    value = _backend?.Error ?? (count == 0 ? "No player-placed container is touching this interface"
                        : "Serves " + count + " container" + (count == 1 ? "" : "s"));
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
