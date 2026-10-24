using HarmonyLib;
using RefinedStorageControl.Routing;
using RefinedStorageControl.Storage;

namespace RefinedStorageControl.Game.Providers
{
    // Bulk deposits (deposit all / deposit matching) queue one request per
    // source slot. The host drains the queue every frame, players taking turns
    // (PROG-04), at whatever rate each network's transfer budget allows.
    internal static class TransferQueue
    {
        private sealed class Request
        {
            public PlatformUserIdentifierAbs User;
            public Vector3i NodePos;
            public GridEndpoint Source;
            public int Slot;
            public ItemKey Expected; // skip if the slot changed while queued
        }

        private const int MaxAttemptsPerFrame = 16;

        private static readonly RoundRobinQueue<string, Request> Pending = new RoundRobinQueue<string, Request>();

        public static int PendingFor(PlatformUserIdentifierAbs user) => Pending.CountFor(user.CombinedString);

        public static void EnqueueDeposit(PlatformUserIdentifierAbs user, Vector3i nodePos, GridEndpoint source, int slot, ItemKey expected)
        {
            Pending.Enqueue(user.CombinedString, new Request
            {
                User = user, NodePos = nodePos, Source = source, Slot = slot, Expected = expected
            });
        }

        public static void Reset() => Pending.Clear();

        public static void Tick()
        {
            if (Pending.Count == 0 || !ConnectionManager.Instance.IsServer)
                return;
            for (int i = 0; i < MaxAttemptsPerFrame && Pending.TryDequeue(out var owner, out var request); i++)
            {
                var current = request.Source.KeyAt(request.Slot);
                if (!current.HasValue || !current.Value.Equals(request.Expected))
                    continue; // the player moved or used the item; nothing to do
                var result = TransferService.Deposit(request.User, request.NodePos, request.Source, request.Slot);
                if (result.BudgetLimited)
                {
                    Pending.ReturnToFront(owner, request);
                    return; // wait for the budget window; retry next frame
                }
                if (!result.Ok)
                    RscLog.Out("queued deposit skipped: " + result.Reason);
            }
        }
    }

    [HarmonyPatch(typeof(GameManager), "Update")]
    internal static class GameManagerUpdatePatch
    {
        private static void Postfix()
        {
            try
            {
                if (ConnectionManager.Instance.IsServer)
                {
                    TransferQueue.Tick();
                    Crafting.CraftService.Tick();
                    RscJournal.Tick();
                }
                else
                    Net.ClientSession.Tick();
            }
            catch (System.Exception e)
            {
                RscLog.Error("transfer queue tick failed: " + e);
                TransferQueue.Reset();
            }
        }
    }
}
