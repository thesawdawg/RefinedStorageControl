using System;
using System.Collections.Generic;

namespace RefinedStorageControl.Crafting
{
    // What the chooser needs to know about one connected station.
    public readonly struct StationCandidate
    {
        public StationCandidate(long sortKey, bool usable, int freeSlots, float queueSeconds)
        {
            SortKey = sortKey;
            Usable = usable;
            FreeSlots = freeSlots;
            QueueSeconds = queueSeconds;
        }

        public long SortKey { get; }

        // Compatible, accessible, has the tool and fuel it needs, not in use.
        public bool Usable { get; }
        public int FreeSlots { get; }

        // Estimated time left on everything already queued there.
        public float QueueSeconds { get; }
    }

    // CRAFT-05 / CRAFT-11 (F06): one station per request; among usable stations
    // with a free queue slot, the shortest estimated queue wins, ties in fixed
    // position order.
    public static class StationChooser
    {
        public static int Choose(IReadOnlyList<StationCandidate> candidates)
        {
            int best = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (!c.Usable || c.FreeSlots <= 0)
                    continue;
                if (best < 0)
                {
                    best = i;
                    continue;
                }
                var b = candidates[best];
                if (c.QueueSeconds < b.QueueSeconds || (c.QueueSeconds == b.QueueSeconds && c.SortKey < b.SortKey))
                    best = i;
            }
            return best;
        }
    }

    // A native station crafts from its last queue slot and moves later jobs up
    // toward it. A new job goes into the slot right behind the earliest-queued
    // job, so it never jumps ahead of anything already waiting (F06).
    public static class QueueSlots
    {
        // Free slots behind every queued job.
        public static int Free(IReadOnlyList<bool> occupied)
        {
            for (int i = 0; i < occupied.Count; i++)
                if (occupied[i])
                    return i;
            return occupied.Count;
        }

        // The slot to append into, or -1 when the queue is full.
        public static int AppendIndex(IReadOnlyList<bool> occupied) => Free(occupied) - 1;
    }

    // A land claim covers a square of `size` blocks centred on its block, at
    // every height (RUN-05).
    public static class ClaimArea
    {
        public static bool Contains(int centerX, int centerZ, int size, int x, int z)
        {
            int half = Math.Max(0, (size - 1) / 2);
            return Math.Abs(x - centerX) <= half && Math.Abs(z - centerZ) <= half;
        }

        // Chunk radius for an observer at the claim block that covers the whole
        // claim, including partly covered edge chunks.
        public static int ObserverRadius(int size) => Math.Max(0, (size - 1) / 2) / 16 + 2;
    }
}
