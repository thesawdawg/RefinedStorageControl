using System;
using System.Collections.Generic;

namespace RefinedStorageControl.Routing
{
    // PROG-04 fairness: pending requests are served one per owner in turn, so a
    // player's bulk deposit cannot starve another player's requests. Each owner's
    // own requests stay in FIFO order.
    public sealed class RoundRobinQueue<TOwner, TItem>
    {
        private readonly Dictionary<TOwner, Queue<TItem>> _queues;
        private readonly LinkedList<TOwner> _turns = new LinkedList<TOwner>();

        public RoundRobinQueue(IEqualityComparer<TOwner> comparer = null)
        {
            _queues = new Dictionary<TOwner, Queue<TItem>>(comparer ?? EqualityComparer<TOwner>.Default);
        }

        public int Count { get; private set; }

        public void Enqueue(TOwner owner, TItem item)
        {
            if (!_queues.TryGetValue(owner, out var queue))
            {
                _queues[owner] = queue = new Queue<TItem>();
                _turns.AddLast(owner);
            }
            queue.Enqueue(item);
            Count++;
        }

        public bool TryDequeue(out TOwner owner, out TItem item)
        {
            owner = default;
            item = default;
            if (_turns.Count == 0)
                return false;
            owner = _turns.First.Value;
            _turns.RemoveFirst();
            var queue = _queues[owner];
            item = queue.Dequeue();
            Count--;
            if (queue.Count > 0)
                _turns.AddLast(owner);
            else
                _queues.Remove(owner);
            return true;
        }

        // Puts a request that could not run yet back at the head of the line, so
        // the same owner is served next and its order is unchanged.
        public void ReturnToFront(TOwner owner, TItem item)
        {
            if (_queues.TryGetValue(owner, out var queue))
            {
                var rest = queue.ToArray();
                queue.Clear();
                queue.Enqueue(item);
                foreach (var r in rest)
                    queue.Enqueue(r);
                _turns.Remove(owner);
            }
            else
            {
                _queues[owner] = queue = new Queue<TItem>();
                queue.Enqueue(item);
            }
            _turns.AddFirst(owner);
            Count++;
        }

        public int CountFor(TOwner owner) => _queues.TryGetValue(owner, out var queue) ? queue.Count : 0;

        public void Clear()
        {
            _queues.Clear();
            _turns.Clear();
            Count = 0;
        }
    }
}
