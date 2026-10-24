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
