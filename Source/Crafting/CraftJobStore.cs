using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RefinedStorageControl.Crafting
{
    // A dispatched request (CRAFT-08/09). Its queue entry at the station carries
    // the job's tag instead of a player id, which is how completions are
    // attributed to the job and never to someone's manual crafting.
    public sealed class CraftJob
    {
        public int Id;
        public string Requester = "";
        public int RequesterEntity;
        public long Station;
        public long Node;       // the interface the station touched at dispatch
        public long Controller;
        public long? Claim;     // land claim block keeping the area active (RUN-05)
        public string Recipe = "";
        public string OutputItem = "";
        public int OutputQuality;
        public int Crafts;      // requested
        public int Completed;   // crafts finished so far
        public int Owed;        // finished items still waiting in the station output
        public bool QueueDone;  // no queue entry with this job's tag remains
    }

    // Crafting XP waiting for its player (CRAFT-10).
    public sealed class XpGrant
    {
        public string Recipe = "";
        public string Item = "";
        public int Exp;
        public int Count;
    }

    // Queue entries store a crafter entity id. Jobs use negative ids far below
    // anything the game hands out (-1 means "nobody").
    public static class CraftTags
    {
        public const int Base = 1000000;
        public const int MaxId = 1000000000;

        public static int ToTag(int jobId)
        {
            if (jobId <= 0 || jobId >= MaxId)
                throw new ArgumentOutOfRangeException(nameof(jobId));
            return -(Base + jobId);
        }

        public static bool TryFromTag(int entityId, out int jobId)
        {
            jobId = 0;
            if (entityId > -Base - 1 || entityId < -(Base + MaxId - 1))
                return false;
            jobId = -entityId - Base;
            return true;
        }

        public static bool IsTag(int entityId) => TryFromTag(entityId, out _);
    }

    public sealed class CraftJobStore
    {
        private readonly Dictionary<int, CraftJob> _jobs = new Dictionary<int, CraftJob>();
        private readonly Dictionary<string, List<XpGrant>> _bank = new Dictionary<string, List<XpGrant>>();

        public int NextId { get; private set; } = 1;
        public bool Dirty { get; set; }

        public int Count => _jobs.Count;

        public IEnumerable<CraftJob> Jobs => _jobs.Values.OrderBy(j => j.Id);

        public IEnumerable<string> BankedPlayers => _bank.Keys.ToList();

        // Skips ids still in use when the counter wraps.
        public int NewId()
        {
            for (int tries = 0; tries < CraftTags.MaxId; tries++)
            {
                int id = NextId;
                NextId = NextId + 1 >= CraftTags.MaxId ? 1 : NextId + 1;
                if (!_jobs.ContainsKey(id))
                {
                    Dirty = true;
                    return id;
                }
            }
            throw new InvalidOperationException("No free job ids.");
        }

        public void Add(CraftJob job)
        {
            if (job == null)
                throw new ArgumentNullException(nameof(job));
            if (_jobs.ContainsKey(job.Id))
                throw new InvalidOperationException("Duplicate job " + job.Id);
            _jobs.Add(job.Id, job);
            Dirty = true;
        }

        public CraftJob Get(int id) => _jobs.TryGetValue(id, out var j) ? j : null;

        public bool Remove(int id)
        {
            bool removed = _jobs.Remove(id);
            Dirty |= removed;
            return removed;
        }

        // Matches the native station: completions of the same recipe and item
        // waiting for the same player merge into one grant.
        public void Bank(string player, XpGrant grant)
        {
            if (player == null)
                throw new ArgumentNullException(nameof(player));
            if (!_bank.TryGetValue(player, out var list))
                _bank[player] = list = new List<XpGrant>();
            var same = list.Find(g => g.Recipe == grant.Recipe && g.Item == grant.Item);
            if (same != null)
                same.Count += grant.Count;
            else
                list.Add(new XpGrant { Recipe = grant.Recipe, Item = grant.Item, Exp = grant.Exp, Count = grant.Count });
            Dirty = true;
        }

        public List<XpGrant> Banked(string player) =>
            _bank.TryGetValue(player, out var list) ? new List<XpGrant>(list) : new List<XpGrant>();

        public void ClearBank(string player)
        {
            if (_bank.Remove(player))
                Dirty = true;
        }

        internal void Restore(int nextId) => NextId = nextId;

        internal void RestoreBank(string player, List<XpGrant> grants) => _bank[player] = grants;
    }

    public static class CraftJobSerializer
    {
        public const int CurrentVersion = 1;

        private const int MaxJobs = 1 << 20;
        private const int MaxGrants = 1 << 16;
        private static readonly byte[] Magic = { (byte)'R', (byte)'S', (byte)'C', (byte)'J' };

        public static void Write(BinaryWriter writer, CraftJobStore store)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            if (store == null)
                throw new ArgumentNullException(nameof(store));
            writer.Write(Magic);
            writer.Write(CurrentVersion);
            writer.Write(store.NextId);
            var jobs = store.Jobs.ToList();
            writer.Write(jobs.Count);
            foreach (var j in jobs)
            {
                writer.Write(j.Id);
                writer.Write(j.Requester);
                writer.Write(j.RequesterEntity);
                writer.Write(j.Station);
                writer.Write(j.Node);
                writer.Write(j.Controller);
                writer.Write(j.Claim.HasValue);
                writer.Write(j.Claim ?? 0L);
                writer.Write(j.Recipe);
                writer.Write(j.OutputItem);
                writer.Write(j.OutputQuality);
                writer.Write(j.Crafts);
                writer.Write(j.Completed);
                writer.Write(j.Owed);
                writer.Write(j.QueueDone);
            }
            var players = store.BankedPlayers.OrderBy(p => p, StringComparer.Ordinal).ToList();
            writer.Write(players.Count);
            foreach (var p in players)
            {
                var grants = store.Banked(p);
                writer.Write(p);
                writer.Write(grants.Count);
                foreach (var g in grants)
                {
                    writer.Write(g.Recipe);
                    writer.Write(g.Item);
                    writer.Write(g.Exp);
                    writer.Write(g.Count);
                }
            }
        }

        public static CraftJobStore Read(BinaryReader reader)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));
            try
            {
                byte[] magic = reader.ReadBytes(Magic.Length);
                if (magic.Length != Magic.Length || !magic.SequenceEqual(Magic))
                    throw new InvalidDataException("Bad craft job magic.");
                int version = reader.ReadInt32();
                if (version != CurrentVersion)
                    throw new InvalidDataException("Unknown craft job version: " + version);
                var store = new CraftJobStore();
                int nextId = reader.ReadInt32();
                if (nextId <= 0 || nextId >= CraftTags.MaxId)
                    throw new InvalidDataException("Invalid next job id: " + nextId);
                store.Restore(nextId);
                int count = reader.ReadInt32();
                if (count < 0 || count > MaxJobs)
                    throw new InvalidDataException("Invalid job count: " + count);
                for (int i = 0; i < count; i++)
                {
                    var j = new CraftJob
                    {
                        Id = reader.ReadInt32(),
                        Requester = reader.ReadString(),
                        RequesterEntity = reader.ReadInt32(),
                        Station = reader.ReadInt64(),
                        Node = reader.ReadInt64(),
                        Controller = reader.ReadInt64()
                    };
                    bool hasClaim = reader.ReadBoolean();
                    long claim = reader.ReadInt64();
                    j.Claim = hasClaim ? claim : (long?)null;
                    j.Recipe = reader.ReadString();
                    j.OutputItem = reader.ReadString();
                    j.OutputQuality = reader.ReadInt32();
                    j.Crafts = reader.ReadInt32();
                    j.Completed = reader.ReadInt32();
                    j.Owed = reader.ReadInt32();
                    j.QueueDone = reader.ReadBoolean();
                    if (j.Id <= 0 || j.Id >= CraftTags.MaxId || j.Owed < 0 || j.Crafts < 0 || j.Completed < 0)
                        throw new InvalidDataException("Invalid job record " + j.Id);
                    if (store.Get(j.Id) != null)
                        throw new InvalidDataException("Duplicate job " + j.Id);
                    store.Add(j);
                }
                int players = reader.ReadInt32();
                if (players < 0 || players > MaxJobs)
                    throw new InvalidDataException("Invalid banked player count: " + players);
                for (int i = 0; i < players; i++)
                {
                    string player = reader.ReadString();
                    int n = reader.ReadInt32();
                    if (n < 0 || n > MaxGrants)
                        throw new InvalidDataException("Invalid grant count: " + n);
                    var grants = new List<XpGrant>(n);
                    for (int g = 0; g < n; g++)
                        grants.Add(new XpGrant
                        {
                            Recipe = reader.ReadString(),
                            Item = reader.ReadString(),
                            Exp = reader.ReadInt32(),
                            Count = reader.ReadInt32()
                        });
                    store.RestoreBank(player, grants);
                }
                if (reader.BaseStream.CanSeek && reader.BaseStream.Position != reader.BaseStream.Length)
                    throw new InvalidDataException("Trailing bytes after craft jobs.");
                store.Dirty = false;
                return store;
            }
            catch (EndOfStreamException e)
            {
                throw new InvalidDataException("Truncated craft job data.", e);
            }
        }
    }
}
