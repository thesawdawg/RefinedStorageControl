using System.Collections.Generic;
using RefinedStorageControl.Game.Providers;
using RefinedStorageControl.Routing;
using RefinedStorageControl.Storage;
using UnityEngine;

namespace RefinedStorageControl.Game.Net
{
    // Client-side state for the open terminal or interface window. Stores run
    // one stack at a time: the stack leaves the backpack, the server answers
    // with whatever it did not accept, and a budget answer puts the slot back at
    // the front of the line for a short retry. Returned items that no longer
    // fit are dropped at the player's feet rather than lost.
    internal static class ClientSession
    {
        private const float RetryDelay = 0.3f;

        private sealed class InFlight
        {
            public int Id;
            public int Slot;
        }

        // Terminal
        public static Vector3i? TerminalPos { get; private set; }
        public static List<TransferService.Entry> Entries { get; private set; } = new List<TransferService.Entry>();
        public static string Error { get; private set; }
        public static string Status { get; set; } = "";
        public static int Version { get; private set; }

        private static readonly LinkedList<(int slot, ItemKey key)> StoreQueue = new LinkedList<(int, ItemKey)>();
        private static InFlight _inFlight;
        private static int _nextId = 1;
        private static float _nextAttempt;

        public static int Pending => StoreQueue.Count + (_inFlight != null ? 1 : 0);

        // Interface
        public static Vector3i? InterfacePos { get; private set; }
        public static NodeConfig InterfaceConfig { get; private set; } = NodeConfig.Default;
        public static int InterfaceContainers { get; private set; }
        public static string InterfaceError { get; private set; }
        public static int InterfaceVersion { get; private set; }

        private static EntityPlayerLocal Player => GameManager.Instance.World?.GetPrimaryPlayer();

        public static void OpenTerminal(Vector3i pos)
        {
            TerminalPos = pos;
            Entries = new List<TransferService.Entry>();
            Error = "Loading...";
            Status = "";
            Version++;
            RequestList();
        }

        public static void CloseTerminal()
        {
            TerminalPos = null;
            StoreQueue.Clear(); // a stack already in flight still comes back through OnGive
        }

        public static void RequestList()
        {
            if (TerminalPos.HasValue)
                ConnectionManager.Instance.SendToServer(
                    NetPackageManager.GetPackage<NetPackageRscTerminalQuery>().Setup(TerminalPos.Value));
        }

        public static void Take(TransferService.Entry entry, int count)
        {
            var player = Player;
            if (!TerminalPos.HasValue || player == null)
                return;
            int room = new BagEndpoint(player).RoomFor(entry.Key, entry.Sample.ItemClass.MaxCount);
            if (room <= 0)
            {
                Status = "Backpack is full";
                return;
            }
            ConnectionManager.Instance.SendToServer(NetPackageManager.GetPackage<NetPackageRscTake>()
                .Setup(TerminalPos.Value, entry.Key, Mathf.Min(count, room)));
        }

        public static void Store(IEnumerable<(int slot, ItemKey key)> slots)
        {
            foreach (var s in slots)
                StoreQueue.AddLast(s);
        }

        // Runs every frame on clients (GameManager.Update hook).
        public static void Tick()
        {
            if (_inFlight != null || StoreQueue.Count == 0 || !TerminalPos.HasValue || Time.time < _nextAttempt)
                return;
            var player = Player;
            if (player == null)
                return;
            var bag = new BagEndpoint(player);
            while (StoreQueue.Count > 0)
            {
                var (slot, key) = StoreQueue.First.Value;
                StoreQueue.RemoveFirst();
                if (slot >= bag.Grid.Length || bag.SlotLocked(slot))
                    continue;
                var current = bag.KeyAt(slot);
                if (!current.HasValue || !current.Value.Equals(key))
                    continue; // the player moved or used it meanwhile
                var stack = bag.Grid[slot];
                var item = stack.itemValue.Clone();
                int count = stack.count;
                bag.Take(slot, count);
                bag.MarkModified();
                _inFlight = new InFlight { Id = _nextId++, Slot = slot };
                ConnectionManager.Instance.SendToServer(NetPackageManager.GetPackage<NetPackageRscStore>()
                    .Setup(TerminalPos.Value, _inFlight.Id, item, count));
                return;
            }
        }

        public static void OnTerminalList(Vector3i pos, string error, List<TransferService.Entry> entries)
        {
            if (TerminalPos != pos)
                return;
            Entries = entries;
            Error = error;
            Version++;
        }

        public static void OnGive(ItemValue item, int count, string message, int requestId, bool budgetLimited)
        {
            var player = Player;
            bool isStoreReply = requestId >= 0 && _inFlight != null && _inFlight.Id == requestId;
            if (player != null && item != null && count > 0)
            {
                var bag = new BagEndpoint(player);
                int left = count;
                // A returned store goes back to its own slot when that is still free.
                if (isStoreReply && _inFlight.Slot < bag.Grid.Length && GridEndpoint.IsEmpty(bag.Grid[_inFlight.Slot])
                    && !bag.SlotLocked(_inFlight.Slot))
                {
                    int put = Mathf.Min(left, item.ItemClass?.MaxCount ?? left);
                    bag.Grid[_inFlight.Slot] = new ItemStack(item.Clone(), put);
                    bag.MarkModified();
                    left -= put;
                }
                if (left > 0)
                    left = bag.Insert(item, left);
                if (left > 0)
                {
                    GameManager.Instance.ItemDropServer(new ItemStack(item.Clone(), left), player.GetPosition(),
                        Vector3.zero, player.entityId);
                    message = (message != null ? message + "; " : "") + left + " dropped (backpack full)";
                }
            }
            if (isStoreReply)
            {
                if (budgetLimited && TerminalPos.HasValue && player != null)
                {
                    var key = new BagEndpoint(player).KeyAt(_inFlight.Slot);
                    if (key.HasValue)
                        StoreQueue.AddFirst((_inFlight.Slot, key.Value));
                    _nextAttempt = Time.time + RetryDelay;
                    message = null; // waiting on the budget is normal, not an error
                }
                _inFlight = null;
            }
            if (message != null)
                Status = message;
            else if (isStoreReply && !budgetLimited)
                Status = "Stored";
            RequestList();
        }

        public static void OpenInterface(Vector3i pos)
        {
            InterfacePos = pos;
            InterfaceConfig = NodeConfig.Default;
            InterfaceError = "Loading...";
            InterfaceVersion++;
            ConnectionManager.Instance.SendToServer(NetPackageManager.GetPackage<NetPackageRscInterface>().Setup(pos, null));
        }

        public static void SetInterface(NodeConfig config)
        {
            if (!InterfacePos.HasValue)
                return;
            InterfaceConfig = config; // optimistic; the server's answer replaces it
            InterfaceVersion++;
            ConnectionManager.Instance.SendToServer(
                NetPackageManager.GetPackage<NetPackageRscInterface>().Setup(InterfacePos.Value, config));
        }

        public static void OnInterfaceState(Vector3i pos, NodeConfig config, int containers, string error)
        {
            if (InterfacePos != pos)
                return;
            InterfaceError = error;
            if (config != null)
                InterfaceConfig = config;
            InterfaceContainers = containers;
            InterfaceVersion++;
        }

        public static void Reset()
        {
            TerminalPos = null;
            InterfacePos = null;
            StoreQueue.Clear();
            _inFlight = null;
            Entries = new List<TransferService.Entry>();
        }
    }
}
