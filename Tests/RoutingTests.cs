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

        var remaining = new TransferBudget(3);
        Check(remaining.Remaining(0.0) == 3, "A fresh budget must report its full rate.");
        remaining.TryConsume(0.0);
        Check(remaining.Remaining(0.5) == 2, "Remaining must drop by one per completed transfer.");
        Check(remaining.Remaining(1.0) == 3, "Remaining must recover when the window expires.");

        Check(TransferLimits.For(RefinedStorageControl.Core.ControllerTier.Simple) == 2
            && TransferLimits.For(RefinedStorageControl.Core.ControllerTier.Advanced) == 20,
            "Tier limits must be 2/s simple and 20/s advanced by default.");

        FilterTests();
        OrderTests();
    }

    private static void FilterTests()
    {
        Check(ItemFilter.None.Accepts("wood") && ItemFilter.None.Accepts(null), "No filter must accept everything.");
        var allow = new ItemFilter(FilterMode.Allow, new[] { "wood" });
        Check(allow.Accepts("wood") && !allow.Accepts("stone") && !allow.Accepts(null),
            "An allow list must accept only listed items.");
        var block = new ItemFilter(FilterMode.Block, new[] { "wood" });
        Check(!block.Accepts("wood") && block.Accepts("stone"), "A block list must reject only listed items.");
        Check(!allow.Accepts("Wood"), "Filters must match item names exactly.");
    }

    private static void OrderTests()
    {
        Reject<ArgumentOutOfRangeException>(() => new ProviderInfo(1, 101, null, false), "Priority above 100 must throw.");
        Reject<ArgumentOutOfRangeException>(() => new ProviderInfo(1, -1, null, false), "Negative priority must throw.");
        Check(new ProviderInfo(1, ProviderRouting.DefaultPriority, null, false).Priority == 50,
            "The default priority must be 50.");

        var wood = new ItemFilter(FilterMode.Allow, new[] { "wood" });
        var providers = new[]
        {
            new ProviderInfo(30, 50, null, false), // 0
            new ProviderInfo(10, 50, null, true),  // 1 holds item
            new ProviderInfo(20, 50, null, false), // 2
            new ProviderInfo(40, 10, null, false), // 3 preferred
            new ProviderInfo(50, 90, null, true),  // 4 overflow holding item
            new ProviderInfo(5, 0, wood, false),   // 5 wood only, top priority
        };
        Check(string.Join(",", ProviderRouting.DepositOrder(providers, "stone")) == "3,1,2,0,4",
            "Deposits: lowest number, then holding the item, then position; filtered providers omitted.");
        Check(string.Join(",", ProviderRouting.DepositOrder(providers, "wood")) == "5,3,1,2,0,4",
            "An allow-listed provider must receive its item first when its priority is lowest.");
        Check(string.Join(",", ProviderRouting.WithdrawOrder(providers)) == "4,1,2,0,3,5",
            "Withdrawals: highest number first, then position; filters ignored.");
        Check(ProviderRouting.DepositOrder(new ProviderInfo[0], "wood").Count == 0, "No providers must route nowhere.");
    }
}
