using System.Collections.Generic;
using System.IO;
using RefinedStorageControl.Core;
using RefinedStorageControl.Game.Persistence;
using RefinedStorageControl.Game.Providers;
using RefinedStorageControl.Game.Topology;
using RefinedStorageControl.Routing;
using RefinedStorageControl.Storage;
using UnityEngine;
using FilterMode = RefinedStorageControl.Routing.FilterMode;
using UnityEngine.Scripting;

namespace RefinedStorageControl.Game.Net
{
    // Co-op client support. A client's backpack is owned by the client, so items
    // travel inside packages: a store removes the stack from the client's bag
    // first and the server returns whatever the network did not accept; a take
    // removes items from the network and ships them to the client, which drops
    // anything that no longer fits. Every request is re-validated on the server.
    // Class names are unique because the game maps packages by type name.

    internal static class RemoteGuard
    {
        private const float MaxReach = 8f;

        public static bool TryAuthorize(ClientInfo sender, Vector3i pos, NodeKind kind, out PlatformUserIdentifierAbs user, out string reason)
        {
            user = sender?.InternalId;
            var world = GameManager.Instance.World;
            if (world == null || sender == null || user == null)
            {
                reason = "unknown player";
                return false;
            }
            if (!(world.GetEntity(sender.entityId) is EntityPlayer player))
            {
                reason = "player not found";
                return false;
            }
            if (Vector3.Distance(player.position, pos.ToVector3() + Vector3.one * 0.5f) > MaxReach)
            {
                reason = "too far from the " + (kind == NodeKind.Terminal ? "terminal" : "interface");
                return false;
            }
            if (!RscBlocks.TryGetRole(world.GetBlock(pos).Block, out var actual, out _) || actual != kind)
            {
                reason = "the block is gone";
                return false;
            }
            reason = null;
            return true;
        }

        public static void WriteKey(BinaryWriter bw, ItemKey key)
        {
            var bytes = key.ToArray();
            bw.Write(bytes.Length);
            bw.Write(bytes);
        }

        public static ItemKey? ReadKey(PooledBinaryReader br)
        {
            int length = br.ReadInt32();
            if (length <= 0 || length > 1 << 20)
                return null;
            return new ItemKey(br.ReadBytes(length));
        }
    }

    // Client -> server: send me what this terminal shows for me.
    [Preserve]
    public class NetPackageRscTerminalQuery : NetPackage
    {
        private Vector3i _pos;

        public override NetPackageDirection PackageDirection => NetPackageDirection.ToServer;

        public NetPackageRscTerminalQuery Setup(Vector3i pos)
        {
            _pos = pos;
            return this;
        }

        public override void read(PooledBinaryReader _br) => _pos = StreamUtils.ReadVector3i(_br);

        public override void write(PooledBinaryWriter _bw)
        {
            base.write(_bw);
            StreamUtils.Write(_bw, _pos);
        }

        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            if (!ConnectionManager.Instance.IsServer)
                return;
            var entries = new List<TransferService.Entry>();
            if (RemoteGuard.TryAuthorize(Sender, _pos, NodeKind.Terminal, out var user, out string reason)
                && TransferService.TryList(user, _pos, out var list, out reason))
                entries = list;
            Sender.SendPackage(NetPackageManager.GetPackage<NetPackageRscTerminalList>().Setup(_pos, reason, entries));
        }
    }

    // Server -> client: the terminal contents for that player.
    [Preserve]
    public class NetPackageRscTerminalList : NetPackage
    {
        private Vector3i _pos;
        private string _error;
        private List<TransferService.Entry> _entries;

        public override NetPackageDirection PackageDirection => NetPackageDirection.ToClient;
        public override bool Compress => true;

        internal NetPackageRscTerminalList Setup(Vector3i pos, string error, List<TransferService.Entry> entries)
        {
            _pos = pos;
            _error = error;
            _entries = entries;
            return this;
        }

        public override void read(PooledBinaryReader _br)
        {
            _pos = StreamUtils.ReadVector3i(_br);
            _error = _br.ReadBoolean() ? _br.ReadString() : null;
            int count = _br.ReadInt32();
            _entries = new List<TransferService.Entry>(count);
            for (int i = 0; i < count; i++)
            {
                var key = RemoteGuard.ReadKey(_br);
                var item = new ItemValue();
                item.Read(_br);
                int amount = _br.ReadInt32();
                if (key.HasValue)
                    _entries.Add(new TransferService.Entry { Key = key.Value, Sample = item, Count = amount });
            }
        }

        public override void write(PooledBinaryWriter _bw)
        {
            base.write(_bw);
            StreamUtils.Write(_bw, _pos);
            ((BinaryWriter)_bw).Write(_error != null);
            if (_error != null)
                ((BinaryWriter)_bw).Write(_error);
            ((BinaryWriter)_bw).Write(_entries.Count);
            foreach (var e in _entries)
            {
                RemoteGuard.WriteKey(_bw, e.Key);
                e.Sample.Write(_bw);
                ((BinaryWriter)_bw).Write(e.Count);
            }
        }

        public override void ProcessPackage(World _world, GameManager _callbacks) =>
            ClientSession.OnTerminalList(_pos, _error, _entries);
    }

    // Client -> server: take up to `count` of this exact item for me.
    [Preserve]
    public class NetPackageRscTake : NetPackage
    {
        private Vector3i _pos;
        private ItemKey? _key;
        private int _count;

        public override NetPackageDirection PackageDirection => NetPackageDirection.ToServer;

        internal NetPackageRscTake Setup(Vector3i pos, ItemKey key, int count)
        {
            _pos = pos;
            _key = key;
            _count = count;
            return this;
        }

        public override void read(PooledBinaryReader _br)
        {
            _pos = StreamUtils.ReadVector3i(_br);
            _key = RemoteGuard.ReadKey(_br);
            _count = _br.ReadInt32();
        }

        public override void write(PooledBinaryWriter _bw)
        {
            base.write(_bw);
            StreamUtils.Write(_bw, _pos);
            RemoteGuard.WriteKey(_bw, _key.Value);
            ((BinaryWriter)_bw).Write(_count);
        }

        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            if (!ConnectionManager.Instance.IsServer)
                return;
            ItemValue taken = null;
            int moved = 0;
            string message;
            if (!_key.HasValue)
                message = "bad request";
            else if (RemoteGuard.TryAuthorize(Sender, _pos, NodeKind.Terminal, out var user, out message))
            {
                var result = TransferService.TakeItem(user, _pos, _key.Value, _count, out taken);
                moved = result.Ok ? result.Moved : 0;
                message = result.Ok
                    ? "Took " + moved + " " + taken.ItemClass.GetLocalizedItemName() + (result.Reason != null ? " (" + result.Reason + ")" : "")
                    : result.Reason;
            }
            Sender.SendPackage(NetPackageManager.GetPackage<NetPackageRscGive>().Setup(taken, moved, message, -1, false));
        }
    }

    // Client -> server: store this stack (already removed from my backpack).
    [Preserve]
    public class NetPackageRscStore : NetPackage
    {
        private Vector3i _pos;
        private int _requestId;
        private ItemValue _item;
        private int _count;

        public override NetPackageDirection PackageDirection => NetPackageDirection.ToServer;

        internal NetPackageRscStore Setup(Vector3i pos, int requestId, ItemValue item, int count)
        {
            _pos = pos;
            _requestId = requestId;
            _item = item;
            _count = count;
            return this;
        }

        public override void read(PooledBinaryReader _br)
        {
            _pos = StreamUtils.ReadVector3i(_br);
            _requestId = _br.ReadInt32();
            _item = new ItemValue();
            _item.Read(_br);
            _count = _br.ReadInt32();
        }

        public override void write(PooledBinaryWriter _bw)
        {
            base.write(_bw);
            StreamUtils.Write(_bw, _pos);
            ((BinaryWriter)_bw).Write(_requestId);
            _item.Write(_bw);
            ((BinaryWriter)_bw).Write(_count);
        }

        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            if (!ConnectionManager.Instance.IsServer)
                return;
            int accepted = 0;
            bool budget = false;
            string message;
            if (RemoteGuard.TryAuthorize(Sender, _pos, NodeKind.Terminal, out var user, out message))
            {
                var result = TransferService.DepositItem(user, _pos, _item, _count);
                accepted = result.Ok ? result.Moved : 0;
                budget = result.BudgetLimited;
                message = result.Reason;
            }
            // Everything not accepted goes straight back to the client.
            Sender.SendPackage(NetPackageManager.GetPackage<NetPackageRscGive>()
                .Setup(_item, _count - accepted, message, _requestId, budget));
        }
    }

    // Server -> client: items for your backpack (a take, or the part of a store
    // the network did not accept), plus a status line.
    [Preserve]
    public class NetPackageRscGive : NetPackage
    {
        private ItemValue _item;
        private int _count;
        private string _message;
        private int _requestId; // -1 for takes
        private bool _budgetLimited;

        public override NetPackageDirection PackageDirection => NetPackageDirection.ToClient;

        internal NetPackageRscGive Setup(ItemValue item, int count, string message, int requestId, bool budgetLimited)
        {
            _item = item;
            _count = item == null ? 0 : count;
            _message = message;
            _requestId = requestId;
            _budgetLimited = budgetLimited;
            return this;
        }

        public override void read(PooledBinaryReader _br)
        {
            _count = _br.ReadInt32();
            _item = null;
            if (_count > 0)
            {
                _item = new ItemValue();
                _item.Read(_br);
            }
            _message = _br.ReadBoolean() ? _br.ReadString() : null;
            _requestId = _br.ReadInt32();
            _budgetLimited = _br.ReadBoolean();
        }

        public override void write(PooledBinaryWriter _bw)
        {
            base.write(_bw);
            ((BinaryWriter)_bw).Write(_count);
            if (_count > 0)
                _item.Write(_bw);
            ((BinaryWriter)_bw).Write(_message != null);
            if (_message != null)
                ((BinaryWriter)_bw).Write(_message);
            ((BinaryWriter)_bw).Write(_requestId);
            ((BinaryWriter)_bw).Write(_budgetLimited);
        }

        public override void ProcessPackage(World _world, GameManager _callbacks) =>
            ClientSession.OnGive(_item, _count, _message, _requestId, _budgetLimited);
    }

    // Client -> server: read (set = false) or change (set = true) interface settings.
    [Preserve]
    public class NetPackageRscInterface : NetPackage
    {
        private Vector3i _pos;
        private bool _set;
        private int _priority;
        private FilterMode _mode;
        private List<string> _names = new List<string>();

        public override NetPackageDirection PackageDirection => NetPackageDirection.ToServer;

        internal NetPackageRscInterface Setup(Vector3i pos, NodeConfig change)
        {
            _pos = pos;
            _set = change != null;
            if (change != null)
            {
                _priority = change.Priority;
                _mode = change.Filter.Mode;
                _names = new List<string>(change.Filter.Names);
            }
            return this;
        }

        public override void read(PooledBinaryReader _br)
        {
            _pos = StreamUtils.ReadVector3i(_br);
            _set = _br.ReadBoolean();
            _priority = _br.ReadInt32();
            _mode = (FilterMode)_br.ReadInt32();
            int n = _br.ReadInt32();
            _names = new List<string>();
            for (int i = 0; i < n && i < 4096; i++)
                _names.Add(_br.ReadString());
        }

        public override void write(PooledBinaryWriter _bw)
        {
            base.write(_bw);
            StreamUtils.Write(_bw, _pos);
            ((BinaryWriter)_bw).Write(_set);
            ((BinaryWriter)_bw).Write(_priority);
            ((BinaryWriter)_bw).Write((int)_mode);
            ((BinaryWriter)_bw).Write(_names.Count);
            foreach (var name in _names)
                ((BinaryWriter)_bw).Write(name);
        }

        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            if (!ConnectionManager.Instance.IsServer)
                return;
            if (!RemoteGuard.TryAuthorize(Sender, _pos, NodeKind.Interface, out _, out string reason))
            {
                Sender.SendPackage(NetPackageManager.GetPackage<NetPackageRscInterfaceState>().Setup(_pos, null, 0, reason));
                return;
            }
            long key = PositionKey.Pack(_pos.x, _pos.y, _pos.z);
            if (_set && _priority >= ProviderRouting.MinPriority && _priority <= ProviderRouting.MaxPriority
                && System.Enum.IsDefined(typeof(FilterMode), _mode))
            {
                _names.RemoveAll(n => ItemClass.GetItemClass(n) == null);
                NodeConfigFile.Store.Set(key, new NodeConfig(_priority, new ItemFilter(_mode, _names)));
            }
            int containers = TransferService.ContainersAround(_world, _pos).Count;
            Sender.SendPackage(NetPackageManager.GetPackage<NetPackageRscInterfaceState>()
                .Setup(_pos, NodeConfigFile.Store.Get(key), containers, null));
        }
    }

    // Server -> client: current interface settings.
    [Preserve]
    public class NetPackageRscInterfaceState : NetPackage
    {
        private Vector3i _pos;
        private NodeConfig _config;
        private int _containers;
        private string _error;

        public override NetPackageDirection PackageDirection => NetPackageDirection.ToClient;

        internal NetPackageRscInterfaceState Setup(Vector3i pos, NodeConfig config, int containers, string error)
        {
            _pos = pos;
            _config = config;
            _containers = containers;
            _error = error;
            return this;
        }

        public override void read(PooledBinaryReader _br)
        {
            _pos = StreamUtils.ReadVector3i(_br);
            _error = _br.ReadBoolean() ? _br.ReadString() : null;
            _containers = _br.ReadInt32();
            int priority = _br.ReadInt32();
            var mode = (FilterMode)_br.ReadInt32();
            int n = _br.ReadInt32();
            var names = new List<string>();
            for (int i = 0; i < n && i < 4096; i++)
                names.Add(_br.ReadString());
            _config = _error != null ? null
                : new NodeConfig(Mathf.Clamp(priority, ProviderRouting.MinPriority, ProviderRouting.MaxPriority),
                    new ItemFilter(mode, names));
        }

        public override void write(PooledBinaryWriter _bw)
        {
            base.write(_bw);
            StreamUtils.Write(_bw, _pos);
            ((BinaryWriter)_bw).Write(_error != null);
            if (_error != null)
                ((BinaryWriter)_bw).Write(_error);
            ((BinaryWriter)_bw).Write(_containers);
            var config = _config ?? NodeConfig.Default;
            ((BinaryWriter)_bw).Write(config.Priority);
            ((BinaryWriter)_bw).Write((int)config.Filter.Mode);
            ((BinaryWriter)_bw).Write(config.Filter.Names.Count);
            foreach (var name in config.Filter.Names)
                ((BinaryWriter)_bw).Write(name);
        }

        public override void ProcessPackage(World _world, GameManager _callbacks) =>
            ClientSession.OnInterfaceState(_pos, _config, _containers, _error);
    }
}
