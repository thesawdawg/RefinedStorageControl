using System;
using System.Collections.Generic;
using System.Linq;
using RefinedStorageControl.Core;
using RefinedStorageControl.Crafting;
using RefinedStorageControl.Game.Net;
using RefinedStorageControl.Game.Persistence;
using RefinedStorageControl.Game.Providers;
using RefinedStorageControl.Game.Topology;
using RefinedStorageControl.Routing;
using RefinedStorageControl.Storage;
using UnityEngine;

namespace RefinedStorageControl.Game.Crafting
{
    // What the terminal shows for one recipe.
    internal sealed class CraftOption
    {
        public int RecipeIndex;
        public ItemValue Output;
        public int PerCraft;
        public int Max;      // whole crafts possible right now
        public string Info;  // ingredients and station, or what is missing
    }

    internal sealed class CraftPendingView
    {
        public int Id;
        public string Item;
        public int Crafts;
        public float SecondsLeft;
    }

    // Host-side manual crafting (CRAFT-01..11, PWR-05, RUN-01..05). A request
    // is validated and reserves what it will use, counts down for five seconds,
    // then is re-validated and dispatched once into one real station's queue.
    // The station does the work under its native rules; this service collects
    // the job's finished items into storage, passes the XP to the requester and
    // keeps the claimed area active while the job is still queued.
    internal static class CraftService
    {
        public static int MaxCraftsPerRequest => Config.RscSettings.Current.MaxCraftsPerRequest;
        private static double CountdownSeconds => Config.RscSettings.Current.CraftCountdownSeconds;
        private const double PendingCheckSeconds = 0.25;
        private const double JobCheckSeconds = 1.0;

        private sealed class Details
        {
            public Vector3i Terminal;
            public Vector3i StationPos;
            public Vector3i Node;
            public int RecipeIndex;
            public string ItemName;
            public int Crafts;
            public int Entity;
        }

        // A recipe with the requester's perks applied (CRAFT-10).
        private sealed class Effective
        {
            public Recipe Recipe;
            public int Tier;
            public float Time;
            public int PerCraft;
            public readonly List<Need> Needs = new List<Need>();
            public readonly List<ItemStack> Stacks = new List<ItemStack>();
        }

        private sealed class Candidate
        {
            public Station Station;
            public Vector3i Iface;
            public string Problem;
            public Dictionary<ItemKey, int> Plan;      // network items
            public Dictionary<int, int> Materials;     // station input by item type
            public StationCandidate Info;
        }

        private static readonly PendingRequests Pending = new PendingRequests();
        private static readonly Dictionary<long, ChunkManager.ChunkObserver> Observers =
            new Dictionary<long, ChunkManager.ChunkObserver>();
        private static readonly Dictionary<string, List<Recipe>> StationRecipes = new Dictionary<string, List<Recipe>>();
        private static readonly Dictionary<string, HashSet<Recipe>> StationRecipeSets = new Dictionary<string, HashSet<Recipe>>();
        private static Dictionary<Recipe, int> _recipeIndex;
        private static int _nextRequestId = 1;
        private static double _nextPendingCheck;
        private static double _nextJobCheck;

        private static double Now => Time.realtimeSinceStartupAsDouble;

        private static CraftJobStore Jobs => CraftFile.Store;

        public static void Reset()
        {
            var chunkManager = GameManager.Instance?.World?.m_ChunkManager;
            foreach (var observer in Observers.Values)
                chunkManager?.RemoveChunkObserver(observer);
            Observers.Clear();
            Pending.Clear();
            StationRecipes.Clear();
            StationRecipeSets.Clear();
            _recipeIndex = null;
        }

        // ---- terminal ----

        public static bool List(PlatformUserIdentifierAbs user, EntityPlayer player, Vector3i terminal,
            out List<CraftOption> options, out string reason)
        {
            options = new List<CraftOption>();
            if (Jobs == null || player == null)
            {
                reason = "crafting is not ready";
                return false;
            }
            if (!TransferService.TryResolveNetwork(terminal, out var net, out reason))
                return false;
            var world = GameManager.Instance.World;
            var stations = Station.Around(world, net.Interfaces);
            if (stations.Count == 0)
            {
                reason = "no workbench, forge or campfire touches an interface";
                return false;
            }
            long controller = net.Controller.Value;
            var supplies = NetworkSupplies(net, user);
            var index = RecipeIndex();
            var seen = new HashSet<Recipe>();
            foreach (var (station, _) in stations)
                foreach (var recipe in RecipesFor(station.BlockName))
                {
                    if (!seen.Add(recipe) || !index.TryGetValue(recipe, out int recipeIndex) || !recipe.IsUnlocked(player))
                        continue;
                    var eff = Resolve(recipe, player, timing: false);
                    int max = 0;
                    string where = null;
                    string problem = null;
                    bool anyUsable = false;
                    foreach (var (s, _) in stations)
                    {
                        if (!Supports(s, recipe))
                            continue;
                        where = where ?? s.Label;
                        string p = StationProblem(s, recipe);
                        if (p != null)
                        {
                            if (!anyUsable)
                                problem = problem ?? p;
                            continue;
                        }
                        anyUsable = true;
                        int m = s.DrawsFromStation
                            ? IngredientPlanner.MaxCrafts(eff.Needs, s.InputSupplies(), k => 0, MaxCraftsPerRequest)
                            : IngredientPlanner.MaxCrafts(eff.Needs, supplies, k => Pending.ReservedInput(controller, k), MaxCraftsPerRequest);
                        if (m > max)
                        {
                            max = m;
                            where = s.Label;
                        }
                        problem = null;
                    }
                    options.Add(new CraftOption
                    {
                        RecipeIndex = recipeIndex,
                        Output = new ItemValue(recipe.itemValueType),
                        PerCraft = recipe.count,
                        Max = max,
                        Info = problem ?? IngredientText(eff) + " (" + where + ")"
                    });
                }
            reason = null;
            return true;
        }

        public static bool Request(PlatformUserIdentifierAbs user, EntityPlayer player, Vector3i terminal, int recipeIndex,
            string itemName, int crafts, out string message)
        {
            if (Jobs == null || player == null || user == null)
            {
                message = "crafting is not ready";
                return false;
            }
            if (!TransferService.TryResolveNetwork(terminal, out var net, out message))
                return false;
            if (!TryRecipe(recipeIndex, itemName, out var recipe))
            {
                message = "unknown recipe";
                return false;
            }
            if (!recipe.IsUnlocked(player))
            {
                message = "you have not learned this recipe";
                return false;
            }
            crafts = Mathf.Clamp(crafts, 1, MaxCraftsPerRequest);
            var eff = Resolve(recipe, player, timing: false);
            var candidates = Evaluate(GameManager.Instance.World, net, user, eff, crafts);
            if (candidates.Count == 0)
            {
                message = "no connected station can craft this";
                return false;
            }
            int pick = StationChooser.Choose(candidates.Select(c => c.Info).ToList());
            if (pick < 0)
            {
                message = candidates[0].Problem;
                return false;
            }
            var chosen = candidates[pick];
            int id = _nextRequestId++;
            Pending.Add(new PendingRequest(id, user.CombinedString, net.Controller.Value, chosen.Station.SortKey,
                Now + CountdownSeconds, chosen.Plan, chosen.Materials)
            {
                Payload = new Details
                {
                    Terminal = terminal, StationPos = chosen.Station.Position, Node = chosen.Iface,
                    RecipeIndex = recipeIndex, ItemName = itemName, Crafts = crafts, Entity = player.entityId
                }
            });
            message = "Crafting " + crafts + "x " + Name(recipe) + " at the " + chosen.Station.Label + " in "
                + Math.Ceiling(CountdownSeconds) + "s";
            RscLog.Event("craft", "craft request " + id + ": " + crafts + "x '" + recipe.GetName() + "' at " + chosen.Station.Position
                + " for " + user.CombinedString);
            return true;
        }

        // CRAFT-04: cancelling during the countdown releases everything; nothing was taken.
        public static bool Cancel(PlatformUserIdentifierAbs user, int requestId, out string message)
        {
            var r = requestId > 0 ? Pending.Get(requestId) : Pending.For(user?.CombinedString ?? "").LastOrDefault();
            if (r == null || user == null || r.Requester != user.CombinedString)
            {
                message = "nothing to cancel";
                return false;
            }
            Pending.Remove(r.Id);
            message = "Cancelled " + Describe(r);
            RscLog.Event("craft", "craft request " + r.Id + " cancelled by its player");
            return true;
        }

        public static List<CraftPendingView> PendingFor(PlatformUserIdentifierAbs user)
        {
            var views = new List<CraftPendingView>();
            if (user == null)
                return views;
            double now = Now;
            foreach (var r in Pending.For(user.CombinedString))
            {
                var d = (Details)r.Payload;
                views.Add(new CraftPendingView
                {
                    Id = r.Id,
                    Item = ItemLabel(d.ItemName),
                    Crafts = d.Crafts,
                    SecondsLeft = (float)Math.Max(0, r.Deadline - now)
                });
            }
            return views;
        }

        public static List<string> Report()
        {
            var lines = new List<string>();
            double now = Now;
            lines.Add(Pending.Count + " countdowns, " + Jobs.Count + " jobs, keepalive on " + Observers.Count + " claims");
            foreach (var r in Pending.All.OrderBy(r => r.Id))
                lines.Add("  countdown " + r.Id + ": " + Describe(r) + " for " + r.Requester + " at "
                    + CraftCommands.Where(r.Station) + ", " + Math.Max(0, r.Deadline - now).ToString("F1") + "s left");
            foreach (var j in Jobs.Jobs)
                lines.Add("  job " + j.Id + ": " + j.Crafts + "x " + j.Recipe + " at " + CraftCommands.Where(j.Station)
                    + " for " + j.Requester + "; " + j.Completed + " made, " + j.Owed + " waiting in the station, "
                    + (j.QueueDone ? "queue done" : "queued")
                    + (j.Claim.HasValue ? ", claim " + CraftCommands.Where(j.Claim.Value) : ", no claim"));
            foreach (string p in Jobs.BankedPlayers)
                lines.Add("  banked XP for " + p + ": " + Jobs.Banked(p).Count + " grants");
            return lines;
        }

        // ---- host tick ----

        private static bool _tickFailed;

        public static void Tick()
        {
            if (Jobs == null)
                return;
            try
            {
                TickCore();
                _tickFailed = false;
            }
            catch (Exception e)
            {
                // Log once per run of failures, not every frame.
                if (!_tickFailed)
                    RscLog.Error("crafting tick failed: " + e);
                _tickFailed = true;
            }
        }

        private static void TickCore()
        {
            double now = Now;
            if (now >= _nextPendingCheck)
            {
                _nextPendingCheck = now + PendingCheckSeconds;
                CheckPending(now);
            }
            if (now >= _nextJobCheck)
            {
                _nextJobCheck = now + JobCheckSeconds;
                TrackJobs();
                SyncKeepalive();
                GrantXp();
            }
        }

        private static void CheckPending(double now)
        {
            if (Pending.Count == 0)
                return;
            var world = GameManager.Instance.World;
            var nets = new Dictionary<int, TransferService.Network>();
            foreach (var r in Pending.All.OrderBy(r => r.Id).ToList())
            {
                var d = (Details)r.Payload;
                // F06: a requester who left loses the request; nothing was taken.
                if (!(world.GetEntity(d.Entity) is EntityPlayer))
                {
                    Drop(r, "you left the game");
                    continue;
                }
                // F07 / PWR-05: no power (or no network) cancels the countdown.
                if (!TransferService.TryResolveNetwork(d.Terminal, out var net, out string reason)
                    || net.Controller.Value != r.Controller)
                {
                    Drop(r, reason ?? "the network changed");
                    continue;
                }
                nets[r.Id] = net;
            }
            foreach (var r in Pending.Due(now))
                if (nets.TryGetValue(r.Id, out var net))
                    Dispatch(r, net);
        }

        private static void Drop(PendingRequest r, string reason)
        {
            Pending.Remove(r.Id);
            RscLog.Event("craft", "craft request " + r.Id + " cancelled: " + reason);
            Notify(r.Requester, "Crafting cancelled (" + reason + "): " + Describe(r));
        }

        // CRAFT-06: everything is checked again; then inputs are taken and the
        // queue entry written in one main-thread step, or nothing happens.
        private static void Dispatch(PendingRequest r, TransferService.Network net)
        {
            Pending.Remove(r.Id);
            var d = (Details)r.Payload;
            string fail = TryDispatch(r, d, net);
            if (fail == null)
                return;
            RscLog.Event("craft", "craft request " + r.Id + " not dispatched: " + fail);
            Notify(r.Requester, "Crafting cancelled (" + fail + "): " + Describe(r));
        }

        private static string TryDispatch(PendingRequest r, Details d, TransferService.Network net)
        {
            var world = GameManager.Instance.World;
            if (!(world.GetEntity(d.Entity) is EntityPlayer player))
                return "you left the game";
            var user = PlatformUserIdentifierAbs.FromCombinedString(r.Requester, false);
            if (user == null)
                return "unknown player";
            if (!TryRecipe(d.RecipeIndex, d.ItemName, out var recipe))
                return "the recipe is gone";
            if (!recipe.IsUnlocked(player))
                return "you have not learned this recipe";
            var eff = Resolve(recipe, player, timing: true); // F05: frozen at dispatch
            var c = Evaluate(world, net, user, eff, d.Crafts).Find(x => x.Station.Position == d.StationPos);
            if (c == null)
                return "the station is gone or no longer connected";
            if (c.Problem != null)
                return c.Problem;

            if (c.Plan != null && !TakeFromNetwork(net, user, c.Plan))
                return "storage changed; try again";
            if (c.Materials != null)
                foreach (var m in c.Materials)
                    c.Station.TakeInput(m.Key, m.Value);

            var copy = new Recipe
            {
                itemValueType = recipe.itemValueType,
                count = eff.PerCraft,
                craftingArea = recipe.craftingArea,
                craftExpGain = recipe.craftExpGain,
                craftingTime = eff.Time,
                craftingToolType = recipe.craftingToolType,
                craftingTier = eff.Tier,
                tags = recipe.tags
            };
            // Per-craft inputs, which the station refunds if someone cancels the
            // entry at the station by hand.
            copy.AddIngredients(eff.Stacks.Select(s => s.Clone()).ToList());
            int jobId = Jobs.NewId();
            c.Station.Append(new RecipeQueueItem
            {
                Recipe = copy,
                Multiplier = (short)d.Crafts,
                CraftingTimeLeft = eff.Time,
                OneItemCraftTime = eff.Time,
                Quality = (byte)eff.Tier,
                StartingEntityId = CraftTags.ToTag(jobId)
            });
            var output = recipe.GetOutputItemClass();
            var controllerPos = RscBlocks.FromNodeId(net.Controller);
            var job = new CraftJob
            {
                Id = jobId,
                Requester = r.Requester,
                RequesterEntity = player.entityId,
                Station = c.Station.SortKey,
                Node = PositionKey.Pack(d.Node.x, d.Node.y, d.Node.z),
                Controller = r.Controller,
                Claim = FindClaim(controllerPos, c.Station.Position),
                Recipe = recipe.GetName(),
                OutputItem = output.GetItemName(),
                OutputQuality = output.HasQuality ? eff.Tier : 0,
                Crafts = d.Crafts
            };
            Jobs.Add(job);
            RscLog.Event("craft", "craft job " + jobId + ": " + d.Crafts + "x '" + job.Recipe + "' queued at " + c.Station.Position
                + (job.Claim.HasValue ? " (claim keeps the area active)" : " (no claim: no keepalive)"));
            Notify(r.Requester, "Crafting " + d.Crafts + "x " + Name(recipe) + " at the " + c.Station.Label);
            return null;
        }

        // All or nothing: every source is found first (withdraw order, so the
        // highest-priority storage empties first), then taken.
        private static bool TakeFromNetwork(TransferService.Network net, PlatformUserIdentifierAbs user, Dictionary<ItemKey, int> plan)
        {
            var takes = new List<(StorageProvider provider, ItemKey key, int handle, int count)>();
            foreach (var need in plan)
            {
                int left = need.Value;
                var candidates = TransferService.Eligible(net, null, p => p.CanCraft(user), need.Key, false, out var infos);
                foreach (int i in ProviderRouting.WithdrawOrder(infos))
                {
                    foreach (var (handle, available) in candidates[i].Sources(need.Key))
                    {
                        if (left == 0)
                            break;
                        int take = Math.Min(left, available);
                        takes.Add((candidates[i], need.Key, handle, take));
                        left -= take;
                    }
                    if (left == 0)
                        break;
                }
                if (left > 0)
                    return false; // e.g. part of it sits in a locked container slot
            }
            var touched = new HashSet<StorageProvider>();
            foreach (var t in takes)
            {
                t.provider.Take(t.handle, t.key, t.count);
                touched.Add(t.provider);
            }
            foreach (var p in touched)
                p.MarkModified();
            // PROG-04: each source taken is one transfer on the network budget.
            TransferService.Charge(net, takes.Count);
            return true;
        }

        // ---- jobs ----

        private static void TrackJobs()
        {
            var world = GameManager.Instance.World;
            if (world == null)
                return;
            foreach (var job in Jobs.Jobs.ToList())
            {
                int tag = CraftTags.ToTag(job.Id);
                var stationPos = Unpack(job.Station);
                if (!Loaded(world, stationPos))
                    continue; // unloaded: nothing can change there until it loads
                if (!Station.TryResolve(world, stationPos, out var station))
                {
                    Finish(job, "the station is gone");
                    continue;
                }
                if (Detached(world, job, out string why))
                {
                    station.Retag(tag, job.RequesterEntity);
                    Finish(job, why);
                    continue;
                }
                if (station.InUse)
                    continue; // the player's client owns the station until they close it

                foreach (var done in station.Harvest(tag))
                {
                    int n = done.CraftedItemStack?.count ?? 0;
                    job.Completed += n;
                    job.Owed += n;
                    Jobs.Bank(job.Requester, new XpGrant
                    {
                        Recipe = done.RecipeName,
                        Item = done.CraftedItemStack?.itemValue?.ItemClass?.GetItemName() ?? job.OutputItem,
                        Exp = done.CraftExpGain,
                        Count = n
                    });
                }
                bool queueDone = !station.HasTag(tag);
                if (queueDone != job.QueueDone)
                {
                    job.QueueDone = queueDone;
                    Jobs.Dirty = true;
                }
                if (job.Owed > 0)
                    Collect(job, station);
                if (job.QueueDone)
                {
                    // Items a player took from the station by hand are theirs.
                    int present = Matching(station, job);
                    if (present < job.Owed)
                    {
                        job.Owed = present;
                        Jobs.Dirty = true;
                    }
                    if (job.Owed == 0)
                        Finish(job, null);
                }
            }
        }

        // CRAFT-07: into storage the requester may deposit to, at the network's
        // transfer rate; whatever does not fit waits in the station output.
        private static void Collect(CraftJob job, Station station)
        {
            if (!TransferService.TryResolveNetwork(Unpack(job.Node), out var net, out _) || net.Controller.Value != job.Controller)
                return; // PWR-02: waits for power and connection
            var user = PlatformUserIdentifierAbs.FromCombinedString(job.Requester, false);
            var outClass = ItemClass.GetItemClass(job.OutputItem);
            if (user == null || outClass == null)
                return;
            var output = station.Te.output;
            bool changed = false;
            for (int i = 0; i < output.Length && job.Owed > 0; i++)
            {
                var stack = output[i];
                if (!Matches(stack, outClass, job))
                    continue;
                int n = Math.Min(stack.count, job.Owed);
                var result = TransferService.DepositCore(net, user, null, stack.itemValue, n,
                    "the " + station.Label + " at " + station.Position);
                if (!result.Ok || result.Moved == 0)
                    break;
                stack.count -= result.Moved;
                if (stack.count <= 0)
                    output[i] = ItemStack.Empty.Clone();
                job.Owed -= result.Moved;
                changed = true;
                if (result.Moved < n)
                    break;
            }
            if (changed)
            {
                station.Te.SetModified();
                Jobs.Dirty = true;
            }
        }

        private static int Matching(Station station, CraftJob job)
        {
            var outClass = ItemClass.GetItemClass(job.OutputItem);
            int n = 0;
            if (outClass != null)
                foreach (var stack in station.Te.output)
                    if (Matches(stack, outClass, job))
                        n += stack.count;
            return n;
        }

        private static bool Matches(ItemStack stack, ItemClass outClass, CraftJob job) =>
            !GridEndpoint.IsEmpty(stack) && stack.itemValue.type == outClass.Id
            && (!stack.itemValue.HasQuality || stack.itemValue.Quality == job.OutputQuality);

        // RUN-05: removing the controller or the claim ends RSC's part.
        private static bool Detached(World world, CraftJob job, out string why)
        {
            var controllerPos = Unpack(job.Controller);
            if (Loaded(world, controllerPos)
                && !(RscBlocks.TryGetRole(world.GetBlock(controllerPos).Block, out var kind, out _) && kind == NodeKind.Controller))
            {
                why = "the controller was removed";
                return true;
            }
            if (job.Claim.HasValue && !ClaimExists(Unpack(job.Claim.Value)))
            {
                why = "the land claim was removed";
                return true;
            }
            why = null;
            return false;
        }

        private static void Finish(CraftJob job, string reason)
        {
            Jobs.Remove(job.Id);
            RscLog.Event("craft", "craft job " + job.Id + (reason == null ? " finished" : " stopped: " + reason)
                + " (" + job.Completed + " made, " + job.Owed + " left in the station)");
            if (reason != null)
                Notify(job.Requester, "Crafting " + ItemLabel(job.OutputItem) + " is no longer tracked (" + reason
                    + "); finished items stay in the station");
        }

        // ---- keepalive (RUN-01/02/04) ----

        private static void SyncKeepalive()
        {
            var chunkManager = GameManager.Instance.World?.m_ChunkManager;
            if (chunkManager == null)
                return;
            var wanted = new HashSet<long>();
            foreach (var job in Jobs.Jobs)
                if (job.Claim.HasValue && !job.QueueDone && ClaimExists(Unpack(job.Claim.Value)))
                    wanted.Add(job.Claim.Value);
            foreach (var claim in Observers.Keys.Where(k => !wanted.Contains(k)).ToList())
            {
                chunkManager.RemoveChunkObserver(Observers[claim]);
                Observers.Remove(claim);
                RscLog.Event("craft", "keepalive released for the claim at " + Unpack(claim));
            }
            int size = GameStats.GetInt(EnumGameStats.LandClaimSize);
            foreach (var claim in wanted)
                if (!Observers.ContainsKey(claim))
                {
                    var pos = Unpack(claim);
                    Observers[claim] = chunkManager.AddChunkObserver(new Vector3(pos.x, pos.y, pos.z), false,
                        ClaimArea.ObserverRadius(size), -1);
                    RscLog.Event("craft", "keepalive started for the claim at " + pos);
                }
        }

        // The claim block whose area holds the controller; only a station in that
        // same claim is kept active.
        private static long? FindClaim(Vector3i controller, Vector3i station)
        {
            var map = GameManager.Instance.persistentPlayers?.m_lpBlockMap;
            if (map == null)
                return null;
            int size = GameStats.GetInt(EnumGameStats.LandClaimSize);
            long? best = null;
            foreach (var pos in map.Keys)
            {
                if (!ClaimArea.Contains(pos.x, pos.z, size, controller.x, controller.z)
                    || !ClaimArea.Contains(pos.x, pos.z, size, station.x, station.z))
                    continue;
                long key = PositionKey.Pack(pos.x, pos.y, pos.z);
                if (!best.HasValue || key < best.Value)
                    best = key;
            }
            return best;
        }

        private static bool ClaimExists(Vector3i pos) =>
            GameManager.Instance.persistentPlayers?.m_lpBlockMap?.ContainsKey(pos) == true;

        // ---- XP (CRAFT-10) ----

        private static void GrantXp()
        {
            var world = GameManager.Instance.World;
            foreach (string player in Jobs.BankedPlayers)
            {
                var user = PlatformUserIdentifierAbs.FromCombinedString(player, false);
                if (user == null)
                    continue;
                var grants = Jobs.Banked(player);
                var local = world.GetPrimaryPlayer();
                if (local != null && user.Equals(Platform.PlatformManager.InternalLocalUserIdentifier))
                {
                    foreach (var g in grants)
                        GiveLocal(local, g);
                }
                else
                {
                    var client = ConnectionManager.Instance.Clients?.ForUserId(user);
                    if (client == null || !(world.GetEntity(client.entityId) is EntityPlayer))
                        continue; // offline: stays banked until they are back
                    client.SendPackage(NetPackageManager.GetPackage<NetPackageRscCraftXp>().Setup(grants));
                }
                Jobs.ClearBank(player);
            }
        }

        public static void GiveLocal(EntityPlayerLocal player, XpGrant grant)
        {
            var item = ItemClass.GetItem(grant.Item);
            player.GiveExp(new CraftCompleteData(player.entityId, new ItemStack(item, grant.Count), grant.Recipe, "",
                grant.Exp, 1));
        }

        // ---- messages ----

        private static void Notify(string requester, string message)
        {
            var user = PlatformUserIdentifierAbs.FromCombinedString(requester, false);
            if (user == null)
                return;
            var world = GameManager.Instance.World;
            var local = world?.GetPrimaryPlayer();
            if (local != null && user.Equals(Platform.PlatformManager.InternalLocalUserIdentifier))
            {
                GameManager.ShowTooltip(local, message);
                return;
            }
            ConnectionManager.Instance.Clients?.ForUserId(user)?.SendPackage(
                NetPackageManager.GetPackage<NetPackageRscCraftState>().Setup(Vector3i.zero, null, message, null, null));
        }

        // ---- recipe helpers ----

        private static List<Candidate> Evaluate(World world, TransferService.Network net, PlatformUserIdentifierAbs user,
            Effective eff, int crafts)
        {
            var needs = IngredientPlanner.Scale(eff.Needs, crafts);
            long controller = net.Controller.Value;
            bool netPlanned = false;
            Dictionary<ItemKey, int> netPlan = null;
            string netShort = null;
            var result = new List<Candidate>();
            foreach (var (s, iface) in Station.Around(world, net.Interfaces))
            {
                if (!Supports(s, eff.Recipe))
                    continue;
                var c = new Candidate { Station = s, Iface = iface };
                int free = QueueSlots.Free(s.Occupied()) - Pending.ReservedSlots(s.SortKey);
                c.Problem = StationProblem(s, eff.Recipe);
                if (c.Problem == null && s.InUse)
                    c.Problem = "the " + s.Label + " is in use";
                else if (c.Problem == null && free <= 0)
                    c.Problem = "the " + s.Label + " queue is full";
                else if (c.Problem == null && s.DrawsFromStation)
                {
                    var supplies = s.InputSupplies();
                    var typeOf = new Dictionary<ItemKey, int>();
                    foreach (var x in supplies)
                        typeOf[x.Key] = x.Type;
                    var plan = IngredientPlanner.Plan(needs, supplies,
                        k => Pending.ReservedMaterial(s.SortKey, typeOf[k]), out int t, out int by);
                    if (plan == null)
                        c.Problem = Missing(t, by) + " in the " + s.Label;
                    else
                    {
                        c.Materials = new Dictionary<int, int>();
                        foreach (var p in plan)
                        {
                            c.Materials.TryGetValue(typeOf[p.Key], out int n);
                            c.Materials[typeOf[p.Key]] = n + p.Value;
                        }
                    }
                }
                else if (c.Problem == null)
                {
                    if (!netPlanned)
                    {
                        netPlan = IngredientPlanner.Plan(needs, NetworkSupplies(net, user),
                            k => Pending.ReservedInput(controller, k), out int t, out int by);
                        netShort = netPlan == null ? Missing(t, by) : null;
                        netPlanned = true;
                    }
                    if (netPlan == null)
                        c.Problem = netShort;
                    else
                        c.Plan = netPlan;
                }
                c.Info = new StationCandidate(s.SortKey, c.Problem == null, Math.Max(0, free), s.QueueSeconds());
                result.Add(c);
            }
            return result;
        }

        private static string StationProblem(Station s, Recipe recipe)
        {
            if (!s.HasTool(recipe))
                return "needs " + ItemLabel(ItemClass.GetForId(recipe.craftingToolType)?.GetItemName()) + " in the " + s.Label;
            if (s.UsesFuel && !s.HasFuel)
                return "the " + s.Label + " has no fuel";
            return null;
        }

        // Items the requester may spend: containers by native access, disks by
        // their craft permission. Modded items are skipped, as at a station.
        private static List<Supply> NetworkSupplies(TransferService.Network net, PlatformUserIdentifierAbs user)
        {
            var byKey = new Dictionary<ItemKey, TransferService.Entry>();
            foreach (var p in net.Providers)
                if (!p.InUse && p.CanCraft(user))
                    p.AddEntries(byKey);
            var supplies = new List<Supply>();
            foreach (var e in byKey.Values)
                if (!(e.Sample.HasModSlots && e.Sample.HasMods()))
                    supplies.Add(new Supply(e.Key, e.Sample.type, e.Count));
            return supplies;
        }

        // The crafting window's own formulas, with the requester's stats.
        private static Effective Resolve(Recipe recipe, EntityPlayer player, bool timing)
        {
            var eff = new Effective { Recipe = recipe };
            eff.Tier = Mathf.Clamp(recipe.GetCraftingTier(player), 1, Math.Max(1, XUiM_Recipes.CraftingMaxTier));
            foreach (var ingredient in recipe.ingredients)
            {
                int n = ingredient.count;
                if (recipe.UseIngredientModifier)
                {
                    n = (int)EffectManager.GetValue(PassiveEffects.CraftingIngredientCount, null, ingredient.count, player, recipe,
                        FastTags<TagGroup.Global>.Parse(ingredient.itemValue.ItemClass.GetItemName()),
                        calcEquipment: true, calcHoldingItem: true, calcProgression: true, calcBuffs: true,
                        calcChallenges: true, craftingTier: eff.Tier);
                    if (n > 0)
                    {
                        n = (int)(n * XUiM_Recipes.GetCraftingInputModifier(recipe));
                        if (XUiM_Recipes.CraftingInputModifier > 0f)
                            n = Math.Max(1, n);
                    }
                }
                if (n <= 0)
                    continue;
                eff.Needs.Add(new Need(ingredient.itemValue.type, n));
                eff.Stacks.Add(new ItemStack(ingredient.itemValue.Clone(), n));
            }
            if (timing)
            {
                eff.Time = Math.Max(0f, EffectManager.GetValue(PassiveEffects.CraftingTime, null, recipe.craftingTime, player,
                    recipe, recipe.tags) * XUiM_Recipes.CraftingTimeModifier);
                float outMod = recipe.tags.Test_AnySet(XUiM_Recipes.SandboxIgnoreTag) ? 1f : XUiM_Recipes.CraftingOutputModifier;
                eff.PerCraft = (int)Mathf.Max(EffectManager.GetValue(PassiveEffects.CraftingOutputCount, null, recipe.count,
                    player, recipe, recipe.tags) * outMod, 1f);
            }
            return eff;
        }

        private static bool Supports(Station s, Recipe recipe)
        {
            RecipesFor(s.BlockName);
            return StationRecipeSets[s.BlockName].Contains(recipe);
        }

        private static List<Recipe> RecipesFor(string blockName)
        {
            if (!StationRecipes.TryGetValue(blockName, out var list))
            {
                StationRecipes[blockName] = list = XUiM_Recipes.FilterRecipesByWorkstation(blockName, XUiM_Recipes.GetRecipes());
                StationRecipeSets[blockName] = new HashSet<Recipe>(list);
            }
            return list;
        }

        private static Dictionary<Recipe, int> RecipeIndex()
        {
            var all = CraftingManager.GetAllRecipes();
            if (_recipeIndex == null || _recipeIndex.Count != all.Count)
            {
                _recipeIndex = new Dictionary<Recipe, int>();
                for (int i = 0; i < all.Count; i++)
                    if (all[i] != null && !_recipeIndex.ContainsKey(all[i]))
                        _recipeIndex[all[i]] = i;
            }
            return _recipeIndex;
        }

        // Clients name a recipe by its index in the shared recipe list plus its
        // output, so a mismatched list is refused instead of crafting the wrong thing.
        private static bool TryRecipe(int index, string itemName, out Recipe recipe)
        {
            var all = CraftingManager.GetAllRecipes();
            recipe = index >= 0 && index < all.Count ? all[index] : null;
            return recipe != null && !recipe.IsScrap && recipe.GetName() == itemName;
        }

        private static string IngredientText(Effective eff)
        {
            if (eff.Stacks.Count == 0)
                return "no ingredients";
            return string.Join(", ", eff.Stacks.Select(s => s.count + " " + s.itemValue.ItemClass.GetLocalizedItemName()));
        }

        private static string Missing(int type, int by) =>
            "missing " + by + " " + (ItemClass.GetForId(type)?.GetLocalizedItemName() ?? "?");

        private static string Name(Recipe recipe) => recipe.GetOutputItemClass()?.GetLocalizedItemName() ?? recipe.GetName();

        private static string ItemLabel(string itemName) =>
            ItemClass.GetItemClass(itemName)?.GetLocalizedItemName() ?? itemName ?? "?";

        private static string Describe(PendingRequest r)
        {
            var d = (Details)r.Payload;
            return d.Crafts + "x " + ItemLabel(d.ItemName);
        }

        private static bool Loaded(World world, Vector3i pos) => world.GetChunkFromWorldPos(pos) != null;

        private static Vector3i Unpack(long key)
        {
            PositionKey.Unpack(key, out int x, out int y, out int z);
            return new Vector3i(x, y, z);
        }
    }
}
