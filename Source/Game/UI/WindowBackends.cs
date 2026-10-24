using System.Collections.Generic;
using RefinedStorageControl.Game.Net;
using RefinedStorageControl.Game.Persistence;
using RefinedStorageControl.Game.Providers;
using RefinedStorageControl.Routing;
using RefinedStorageControl.Storage;

namespace RefinedStorageControl.Game.UI
{
    // The windows talk to one of these. The host calls the transfer service
    // directly; a co-op client goes through ClientSession and network packages.
    internal interface ITerminalBackend
    {
        void Open(Vector3i pos);
        void Close();
        void Refresh();
        int Version { get; }
        List<TransferService.Entry> Entries { get; }
        string Error { get; }
        string Status { get; }
        int Pending { get; }
        void Take(TransferService.Entry entry, int count);
        void Store(List<(int slot, ItemKey key)> slots);
    }

    internal sealed class HostTerminalBackend : ITerminalBackend
    {
        private readonly EntityPlayerLocal _player;
        private Vector3i _pos;

        public HostTerminalBackend(EntityPlayerLocal player) => _player = player;

        private static PlatformUserIdentifierAbs User => Platform.PlatformManager.InternalLocalUserIdentifier;

        public int Version { get; private set; }
        public List<TransferService.Entry> Entries { get; private set; } = new List<TransferService.Entry>();
        public string Error { get; private set; }
        public string Status { get; private set; } = "";
        public int Pending => TransferQueue.PendingFor(User);

        public void Open(Vector3i pos)
        {
            _pos = pos;
            Status = "";
            Refresh();
        }

        public void Close()
        {
        }

        public void Refresh()
        {
            if (TransferService.TryList(User, _pos, out var entries, out string reason))
            {
                Entries = entries;
                Error = null;
            }
            else
            {
                Entries = new List<TransferService.Entry>();
                Error = reason;
            }
            Version++;
        }

        public void Take(TransferService.Entry entry, int count)
        {
            var result = TransferService.Withdraw(User, _pos, entry.Key, count, new BagEndpoint(_player));
            Status = result.Ok
                ? "Took " + result.Moved + " " + entry.Sample.ItemClass.GetLocalizedItemName()
                  + (result.Reason != null ? " (" + result.Reason + ")" : "")
                : result.Reason;
            Refresh();
        }

        public void Store(List<(int slot, ItemKey key)> slots)
        {
            var bag = new BagEndpoint(_player);
            foreach (var (slot, key) in slots)
                TransferQueue.EnqueueDeposit(User, _pos, bag, slot, key);
            Status = slots.Count == 0 ? "Nothing to store" : "Storing...";
        }
    }

    internal sealed class ClientTerminalBackend : ITerminalBackend
    {
        public int Version => ClientSession.Version;
        public List<TransferService.Entry> Entries => ClientSession.Entries;
        public string Error => ClientSession.Error;
        public string Status => ClientSession.Status;
        public int Pending => ClientSession.Pending;

        public void Open(Vector3i pos) => ClientSession.OpenTerminal(pos);
        public void Close() => ClientSession.CloseTerminal();
        public void Refresh() => ClientSession.RequestList();
        public void Take(TransferService.Entry entry, int count) => ClientSession.Take(entry, count);

        public void Store(List<(int slot, ItemKey key)> slots)
        {
            ClientSession.Store(slots);
            ClientSession.Status = slots.Count == 0 ? "Nothing to store" : "Storing...";
        }
    }

    internal interface ICabinetBackend
    {
        void Open(Vector3i pos);
        void Close();
        void Refresh();
        CabinetView View { get; }
        string Status { get; }
        int Version { get; }
        void Insert(int bagSlot, int preferredSlot);
        void Remove(int slot);
        void SetPermissions(int slot, string playerId, Access.DigitalPermission perms);
        void Transfer(int slot, string playerId);
    }

    internal sealed class HostCabinetBackend : ICabinetBackend
    {
        private readonly EntityPlayerLocal _player;
        private Vector3i _pos;

        public HostCabinetBackend(EntityPlayerLocal player) => _player = player;

        private static PlatformUserIdentifierAbs User => Platform.PlatformManager.InternalLocalUserIdentifier;

        public CabinetView View { get; private set; } = new CabinetView();
        public string Status { get; private set; } = "";
        public int Version { get; private set; }

        public void Open(Vector3i pos)
        {
            _pos = pos;
            Status = "";
            Refresh();
        }

        public void Close()
        {
        }

        public void Refresh()
        {
            View = CabinetService.BuildView(User, _pos);
            Version++;
        }

        public void Insert(int bagSlot, int preferredSlot)
        {
            var bag = new BagEndpoint(_player);
            if (bagSlot < 0 || bagSlot >= bag.Grid.Length || GridEndpoint.IsEmpty(bag.Grid[bagSlot]))
                return;
            var item = bag.Grid[bagSlot].itemValue.Clone();
            // Validate before touching the backpack; the service only fails before
            // it changes anything, so the item stays put on refusal.
            string error = CabinetService.InsertDisk(User, _pos, preferredSlot, item, out int slot);
            if (error == null)
            {
                bag.Take(bagSlot, 1);
                bag.MarkModified();
            }
            Finish(error ?? "Disk inserted in slot " + (slot + 1));
        }

        public void Remove(int slot)
        {
            string error = CabinetService.RemoveDisk(User, _pos, slot, out var item);
            if (error == null)
            {
                int left = new BagEndpoint(_player).Insert(item, 1);
                if (left > 0)
                    Drops.Drop(item, left, _player.GetPosition(), _player.entityId);
            }
            Finish(error ?? "Disk removed");
        }

        public void SetPermissions(int slot, string playerId, Access.DigitalPermission perms) =>
            Finish(CabinetService.SetPermissions(User, _pos, slot, playerId, perms) ?? "");

        public void Transfer(int slot, string playerId) =>
            Finish(CabinetService.TransferOwnership(User, _pos, slot, playerId) ?? "Ownership transferred");

        private void Finish(string status)
        {
            Status = status;
            Refresh();
        }
    }

    internal sealed class ClientCabinetBackend : ICabinetBackend
    {
        private readonly EntityPlayerLocal _player;

        public ClientCabinetBackend(EntityPlayerLocal player) => _player = player;

        public CabinetView View => ClientSession.Cabinet;
        public string Status => ClientSession.CabinetStatus;
        public int Version => ClientSession.CabinetVersion;

        public void Open(Vector3i pos) => ClientSession.OpenCabinet(pos);
        public void Close() => ClientSession.CloseCabinet();
        public void Refresh() => ClientSession.SendCabinet(CabinetAction.Query);

        public void Insert(int bagSlot, int preferredSlot)
        {
            var bag = new BagEndpoint(_player);
            if (bagSlot < 0 || bagSlot >= bag.Grid.Length || GridEndpoint.IsEmpty(bag.Grid[bagSlot]))
                return;
            if (!DiskItems.TryGetTier(bag.Grid[bagSlot].itemValue, out _))
            {
                ClientSession.CabinetStatus = "That is not a storage disk";
                return;
            }
            // The disk travels to the server; a refusal sends it straight back.
            var item = bag.Grid[bagSlot].itemValue.Clone();
            bag.Take(bagSlot, 1);
            bag.MarkModified();
            ClientSession.CabinetStatus = "";
            ClientSession.SendCabinet(CabinetAction.Insert, preferredSlot, item: item);
        }

        public void Remove(int slot) => ClientSession.SendCabinet(CabinetAction.Remove, slot);

        public void SetPermissions(int slot, string playerId, Access.DigitalPermission perms) =>
            ClientSession.SendCabinet(CabinetAction.SetPermissions, slot, playerId, perms);

        public void Transfer(int slot, string playerId) =>
            ClientSession.SendCabinet(CabinetAction.Transfer, slot, playerId);
    }

    internal interface IInterfaceBackend
    {
        void Open(Vector3i pos);
        NodeConfig Config { get; }
        int Containers { get; }
        string Error { get; }
        int Version { get; }
        void Apply(NodeConfig config);
    }

    internal sealed class HostInterfaceBackend : IInterfaceBackend
    {
        private Vector3i _pos;

        private long Key => Core.PositionKey.Pack(_pos.x, _pos.y, _pos.z);

        public void Open(Vector3i pos)
        {
            _pos = pos;
            Version++;
        }

        public NodeConfig Config => NodeConfigFile.Store.Get(Key);

        public int Containers
        {
            get
            {
                var world = GameManager.Instance.World;
                return world == null ? 0 : TransferService.ContainersAround(world, _pos).Count;
            }
        }

        public string Error => null;
        public int Version { get; private set; }

        public void Apply(NodeConfig config)
        {
            NodeConfigFile.Store.Set(Key, config);
            Version++;
        }
    }

    internal sealed class ClientInterfaceBackend : IInterfaceBackend
    {
        public void Open(Vector3i pos) => ClientSession.OpenInterface(pos);
        public NodeConfig Config => ClientSession.InterfaceConfig;
        public int Containers => ClientSession.InterfaceContainers;
        public string Error => ClientSession.InterfaceError;
        public int Version => ClientSession.InterfaceVersion;
        public void Apply(NodeConfig config) => ClientSession.SetInterface(config);
    }
}
