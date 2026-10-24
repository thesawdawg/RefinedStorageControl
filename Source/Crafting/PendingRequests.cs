using System;
using System.Collections.Generic;
using RefinedStorageControl.Storage;

namespace RefinedStorageControl.Crafting
{
    // A request in its five-second countdown (CRAFT-04). Nothing has moved yet:
    // it only reserves network items, station materials and one queue slot so
    // that competing requests cannot plan with the same things (CRAFT-06).
    public sealed class PendingRequest
    {
        public PendingRequest(int id, string requester, long controller, long station, double deadline,
            IDictionary<ItemKey, int> inputs, IDictionary<int, int> stationMaterials)
        {
            Id = id;
            Requester = requester ?? throw new ArgumentNullException(nameof(requester));
            Controller = controller;
            Station = station;
            Deadline = deadline;
            Inputs = new Dictionary<ItemKey, int>(inputs ?? new Dictionary<ItemKey, int>());
            StationMaterials = new Dictionary<int, int>(stationMaterials ?? new Dictionary<int, int>());
        }

        public int Id { get; }
        public string Requester { get; }
        public long Controller { get; }
        public long Station { get; }
        public double Deadline { get; }
        public IReadOnlyDictionary<ItemKey, int> Inputs { get; }
        public IReadOnlyDictionary<int, int> StationMaterials { get; }

        // Game-side details (recipe, amounts, positions) the planner never reads.
        public object Payload { get; set; }
    }

    public sealed class PendingRequests
    {
        public const double CountdownSeconds = 5.0;

        private readonly Dictionary<int, PendingRequest> _byId = new Dictionary<int, PendingRequest>();

        public int Count => _byId.Count;

        public IEnumerable<PendingRequest> All => _byId.Values;

        public void Add(PendingRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (_byId.ContainsKey(request.Id))
                throw new InvalidOperationException("Duplicate pending request " + request.Id);
            _byId.Add(request.Id, request);
        }

        public PendingRequest Get(int id) => _byId.TryGetValue(id, out var r) ? r : null;

        public bool Remove(int id) => _byId.Remove(id);

        public List<PendingRequest> For(string requester)
        {
            var list = new List<PendingRequest>();
            foreach (var r in _byId.Values)
                if (r.Requester == requester)
                    list.Add(r);
            list.Sort((a, b) => a.Id.CompareTo(b.Id));
            return list;
        }

        // Requests whose countdown has ended, oldest first, so dispatch is fair.
        public List<PendingRequest> Due(double now)
        {
            var due = new List<PendingRequest>();
            foreach (var r in _byId.Values)
                if (r.Deadline <= now)
                    due.Add(r);
            due.Sort((a, b) => a.Id.CompareTo(b.Id));
            return due;
        }

        // Network items reserved on one network, optionally ignoring one request
        // (its own reservation while it is re-planned at dispatch).
        public int ReservedInput(long controller, ItemKey key, int exceptId = -1)
        {
            int sum = 0;
            foreach (var r in _byId.Values)
                if (r.Id != exceptId && r.Controller == controller && r.Inputs.TryGetValue(key, out int n))
                    sum += n;
            return sum;
        }

        public int ReservedMaterial(long station, int type, int exceptId = -1)
        {
            int sum = 0;
            foreach (var r in _byId.Values)
                if (r.Id != exceptId && r.Station == station && r.StationMaterials.TryGetValue(type, out int n))
                    sum += n;
            return sum;
        }

        public int ReservedSlots(long station, int exceptId = -1)
        {
            int sum = 0;
            foreach (var r in _byId.Values)
                if (r.Id != exceptId && r.Station == station)
                    sum++;
            return sum;
        }

        public void Clear() => _byId.Clear();
    }
}
