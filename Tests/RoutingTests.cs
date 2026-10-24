using System;
using RefinedStorageControl.Routing;
using static Harness;

internal static class RoutingTests
{
    public static void Run()
    {
        Reject<ArgumentException>(() => new TransferBudget(0), "Zero rates must be rejected.");
        Reject<ArgumentException>(() => new TransferBudget(-2), "Negative rates must be rejected.");

        var budget = new TransferBudget();
        Check(budget.TransfersPerSecond == 2, "The default budget must be two transfers per second.");
        Check(budget.TryConsume(0.0) && budget.TryConsume(0.0), "Two transfers inside one second must pass.");
        Check(!budget.TryConsume(0.0), "A third transfer inside one second must wait.");
        Check(budget.NextAvailableAt(0.0) == 1.0, "The next slot must open when the oldest completion expires.");
        Check(budget.TryConsume(1.0), "Completions exactly one second old must expire.");

        var fast = new TransferBudget(5);
        for (int i = 0; i < 5; i++)
            Check(fast.TryConsume(10.0), "Configured rates must allow that many transfers per second.");
        Check(!fast.TryConsume(10.0), "Configured rates must still cap the window.");
        Check(!fast.TryConsume(10.5), "Window must remain full mid-second.");
        Check(fast.NextAvailableAt(10.5) == 11.0, "Next slot must be oldest completion plus one second.");

        var clock = new TransferBudget();
        Check(clock.TryConsume(0.0) && clock.TryConsume(0.0), "Baseline fill before clock tests.");
        Check(!clock.TryConsume(-5.0), "A backwards clock must not expire the window.");
        Check(clock.NextAvailableAt(-5.0) == 1.0, "A backwards clock must report the clamped availability.");
        Check(clock.TryConsume(1.0) && clock.TryConsume(1.0), "New completions must be tracked at the latest time.");
        Check(!clock.TryConsume(0.5), "Backwards timestamps must clamp to the last seen time, not reopen the window.");
    }
}
