using System;
using System.Collections.Generic;

namespace RefinedStorageControl.Routing
{
    // PROG-04/06: one sliding 1-second window of completed transfers, shared by
    // all users of a controller. One transfer = one move out of one source slot.
    public sealed class TransferBudget
    {
        private readonly Queue<double> _completions = new Queue<double>();
        private double _last = double.NegativeInfinity;

        public TransferBudget(int transfersPerSecond = 2)
        {
            if (transfersPerSecond <= 0)
                throw new ArgumentOutOfRangeException(nameof(transfersPerSecond), transfersPerSecond, "Rate must be positive.");
            TransfersPerSecond = transfersPerSecond;
        }

        public int TransfersPerSecond { get; }

        public bool TryConsume(double nowSeconds)
        {
            double now = Clamp(nowSeconds);
            Prune(now);
            if (_completions.Count >= TransfersPerSecond)
                return false;
            _completions.Enqueue(now);
            return true;
        }

        // Records transfers that already happened as one step (a craft
        // dispatch taking several sources at once). It may go past the rate;
        // later requests then wait until the window has room again.
        public void Charge(double nowSeconds, int transfers)
        {
            double now = Clamp(nowSeconds);
            Prune(now);
            for (int i = 0; i < transfers; i++)
                _completions.Enqueue(now);
        }

        public int Remaining(double nowSeconds)
        {
            double now = Clamp(nowSeconds);
            Prune(now);
            return TransfersPerSecond - _completions.Count;
        }

        public double NextAvailableAt(double nowSeconds)
        {
            double now = Clamp(nowSeconds);
            Prune(now);
            return _completions.Count < TransfersPerSecond ? now : _completions.Peek() + 1.0;
        }

        // Clock-backwards guard: a timestamp earlier than anything already seen is
        // clamped up to the latest seen time, never granting extra transfers.
        private double Clamp(double nowSeconds) => _last = Math.Max(_last, nowSeconds);

        private void Prune(double now)
        {
            while (_completions.Count > 0 && _completions.Peek() <= now - 1.0)
                _completions.Dequeue();
        }
    }
}
