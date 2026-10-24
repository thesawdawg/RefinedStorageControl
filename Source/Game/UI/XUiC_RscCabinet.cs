using System;
using System.Collections.Generic;
using System.Linq;
using RefinedStorageControl.Access;
using RefinedStorageControl.Game.Providers;
using UnityEngine.Scripting;

namespace RefinedStorageControl.Game.UI
{
    // Drive cabinet window (DISK-02/07, ACCESS-03/04). Top: the six disk slots;
    // click one to select it. Middle: the selected disk's owner and allowlist,
    // with per-player Deposit / Withdraw / Craft / Configure toggles, add and
    // remove, and "make owner". Bottom: the backpack; click a disk to insert it
    // into the selected slot (or the first free one).
    [Preserve]
    public class XUiC_RscCabinet : XUiController
    {
        public const string WindowGroup = "rscCabinet";
        private const float RefreshSeconds = 1f;

        private static Vector3i _pendingPos;

        private ICabinetBackend _backend;
        private XUiC_RscCell[] _diskCells = new XUiC_RscCell[0];
        private XUiC_RscCell[] _bagCells = new XUiC_RscCell[0];
        private int _seenVersion = -1;
        private int _selected;
        private int _entry;
        private int _candidate;
        private int _bagPage;
        private float _refreshIn;
        private bool _errorLogged;

        public static void Open(EntityPlayerLocal player, Vector3i pos)
        {
            _pendingPos = pos;
            LocalPlayerUI.GetUIForPlayer(player).windowManager.Open(WindowGroup, true);
        }

        public override void Init()
        {
            base.Init();
            _diskCells = CellsIn("cabGrid", (cell, button) => { _selected = cell.Index; _entry = 0; Redraw(); });
            _bagCells = CellsIn("bagGrid", BagClicked);
            Button("btnRemove", () => Guard(() => _backend.Remove(_selected)));
            Button("btnEntryPrev", () => { _entry--; Redraw(); });
            Button("btnEntryNext", () => { _entry++; Redraw(); });
            Button("btnPermDeposit", () => Toggle(DigitalPermission.Deposit));
            Button("btnPermWithdraw", () => Toggle(DigitalPermission.Withdraw));
            Button("btnPermCraft", () => Toggle(DigitalPermission.Craft));
            Button("btnPermConfigure", () => Toggle(DigitalPermission.Configure));
            Button("btnEntryRemove", () =>
            {
                var e = CurrentEntry;
                if (e != null)
                    Guard(() => _backend.SetPermissions(_selected, e.Id, DigitalPermission.None));
            });
            Button("btnMakeOwner", () =>
            {
                var e = CurrentEntry;
                if (e != null)
                    Guard(() => _backend.Transfer(_selected, e.Id));
            });
            Button("btnCandPrev", () => { _candidate--; RefreshBindings(); });
            Button("btnCandNext", () => { _candidate++; RefreshBindings(); });
            Button("btnAdd", () =>
            {
                var c = CurrentCandidate;
                if (c != null)
                    Guard(() => _backend.SetPermissions(_selected, c.Id, DigitalAcl.DefaultGrant));
            });
            Button("btnBagPrev", () => { _bagPage = Math.Max(0, _bagPage - 1); ShowBag(); });
            Button("btnBagNext", () =>
            {
                if ((_bagPage + 1) * _bagCells.Length < new BagEndpoint(Player).Grid.Length)
                    _bagPage++;
                ShowBag();
            });
        }

        public override void OnOpen()
        {
            base.OnOpen();
            _backend = ConnectionManager.Instance.IsServer
                ? (ICabinetBackend)new HostCabinetBackend(Player)
                : new ClientCabinetBackend(Player);
            _selected = 0;
            _entry = 0;
            _candidate = 0;
            _bagPage = 0;
            _seenVersion = -1;
            _refreshIn = RefreshSeconds;
            Guard(() => _backend.Open(_pendingPos));
        }

        public override void OnClose()
        {
            base.OnClose();
            _backend?.Close();
        }

        public override void Update(float _dt)
        {
            base.Update(_dt);
            if (_backend == null || xui?.playerUI?.windowManager?.IsWindowOpen(WindowGroup) != true)
                return;
            _refreshIn -= _dt;
            if (_refreshIn <= 0f)
            {
                _refreshIn = RefreshSeconds;
                Guard(_backend.Refresh);
            }
            if (_backend.Version != _seenVersion)
            {
                _seenVersion = _backend.Version;
                Redraw();
            }
            Guard(ShowBag);
            RefreshBindings();
        }

        private EntityPlayerLocal Player => xui.playerUI.entityPlayer;

        private CabinetView View => _backend?.View ?? new CabinetView();

        private CabinetView.Slot Selected => View.Slots[Math.Max(0, Math.Min(_selected, View.Slots.Length - 1))];

        private CabinetView.Entry CurrentEntry
        {
            get
            {
                var entries = Selected.Entries;
                if (!Selected.Present || entries.Count == 0)
                    return null;
                _entry = ((_entry % entries.Count) + entries.Count) % entries.Count;
                return entries[_entry];
            }
        }

        // Known players who are neither the owner nor already listed.
        private List<CabinetView.Entry> Candidates =>
            Selected.Present
                ? View.Candidates.Where(c => c.Id != Selected.OwnerId && Selected.Entries.All(e => e.Id != c.Id)).ToList()
                : new List<CabinetView.Entry>();

        private CabinetView.Entry CurrentCandidate
        {
            get
            {
                var list = Candidates;
                if (list.Count == 0)
                    return null;
                _candidate = ((_candidate % list.Count) + list.Count) % list.Count;
                return list[_candidate];
            }
        }

        private bool CanConfigure => Selected.Present && (Selected.MyPerms & DigitalPermission.Configure) != 0;

        private void Toggle(DigitalPermission flag)
        {
            var e = CurrentEntry;
            if (e == null)
                return;
            var perms = e.Perms ^ flag;
            // Clearing the last flag would drop the player; use "Remove from list" for that.
            if (perms == DigitalPermission.None)
                return;
            Guard(() => _backend.SetPermissions(_selected, e.Id, perms));
        }

        private void Redraw()
        {
            for (int i = 0; i < _diskCells.Length; i++)
            {
                var cell = _diskCells[i];
                cell.Index = i;
                var slot = i < View.Slots.Length ? View.Slots[i] : null;
                cell.Set(slot != null && slot.Present ? ItemClass.GetItem(DiskItems.ItemName(slot.Tier)) : null,
                    slot != null && slot.Present ? 1 : 0, false);
                cell.Favorite = i == _selected; // reuse the star as the selection marker
            }
            RefreshBindings();
        }

        private void ShowBag()
        {
            var bag = new BagEndpoint(Player);
            int start = _bagPage * _bagCells.Length;
            for (int i = 0; i < _bagCells.Length; i++)
            {
                int n = start + i;
                var cell = _bagCells[i];
                cell.Index = n;
                if (n < bag.Grid.Length)
                {
                    var stack = bag.Grid[n];
                    bool empty = GridEndpoint.IsEmpty(stack);
                    cell.Set(empty ? null : stack.itemValue, empty ? 0 : stack.count, bag.SlotLocked(n));
                }
                else
                    cell.Set(null, 0, false, visible: false);
            }
        }

        private void BagClicked(XUiC_RscCell cell, int button)
        {
            if (button != 0)
                return;
            var bag = new BagEndpoint(Player);
            if (cell.Index >= bag.Grid.Length || GridEndpoint.IsEmpty(bag.Grid[cell.Index]))
                return;
            Guard(() => _backend.Insert(cell.Index, _selected));
        }

        public override bool GetBindingValueInternal(ref string value, string bindingName)
        {
            var s = Selected;
            switch (bindingName)
            {
                case "title":
                    value = Localization.Get("rscCabinetTitle");
                    return true;
                case "slotinfo":
                    value = View.Error ?? (!s.Present
                        ? "Slot " + (_selected + 1) + " is empty. Click a disk in your backpack to insert it."
                        : "Slot " + (_selected + 1) + ": " + Localization.Get(DiskItems.ItemName(s.Tier))
                          + "  -  " + s.Used.ToString("N0") + " / " + s.Capacity.ToString("N0") + " items\nOwner: "
                          + s.OwnerName + "   Your access: " + Describe(s.IsOwner ? DigitalPermission.All : s.MyPerms, s.IsOwner));
                    return true;
                case "entryline":
                    var e = CurrentEntry;
                    value = !s.Present ? "" : e == null ? "Nobody else has access" : e.Name + " (" + (_entry + 1) + "/" + s.Entries.Count + ")";
                    return true;
                case "hasentry":
                    value = (CurrentEntry != null && CanConfigure).ToString();
                    return true;
                case "canconfigure":
                    value = CanConfigure.ToString();
                    return true;
                case "isowner":
                    value = (s.Present && s.IsOwner && CurrentEntry != null).ToString();
                    return true;
                case "permdeposit":
                    value = Flag("Deposit", DigitalPermission.Deposit);
                    return true;
                case "permwithdraw":
                    value = Flag("Withdraw", DigitalPermission.Withdraw);
                    return true;
                case "permcraft":
                    value = Flag("Craft", DigitalPermission.Craft);
                    return true;
                case "permconfigure":
                    value = Flag("Configure", DigitalPermission.Configure);
                    return true;
                case "candidate":
                    value = CurrentCandidate?.Name ?? "(no other known players)";
                    return true;
                case "status":
                    value = _backend?.Status ?? "";
                    return true;
                case "bagpage":
                    int bagLen = xui?.playerUI?.entityPlayer?.bag?.SlotCount ?? 0;
                    value = (_bagPage + 1) + "/" + Math.Max(1, (bagLen + _bagCells.Length - 1) / Math.Max(1, _bagCells.Length));
                    return true;
            }
            return base.GetBindingValueInternal(ref value, bindingName);
        }

        private string Flag(string label, DigitalPermission flag)
        {
            var e = CurrentEntry;
            return label + ": " + (e != null && (e.Perms & flag) != 0 ? "yes" : "no");
        }

        private static string Describe(DigitalPermission perms, bool owner)
        {
            if (owner)
                return "owner";
            if (perms == DigitalPermission.None)
                return "none";
            var parts = new List<string>();
            if ((perms & DigitalPermission.Deposit) != 0) parts.Add("deposit");
            if ((perms & DigitalPermission.Withdraw) != 0) parts.Add("withdraw");
            if ((perms & DigitalPermission.Craft) != 0) parts.Add("craft");
            if ((perms & DigitalPermission.Configure) != 0) parts.Add("configure");
            return string.Join(", ", parts);
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
                if (!_errorLogged)
                    RscLog.Error("cabinet window failed: " + e);
                _errorLogged = true;
            }
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
