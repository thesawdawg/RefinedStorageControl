using System;

internal static class Program
{
    private static int Main()
    {
        try
        {
            StorageTests.Run();
            AccessTests.Run();
            RoutingTests.Run();
            StackPlacementTests.Run();
            CoreTests.Run();
            Console.WriteLine("PASS: " + Harness.Count + " checks passed.");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return 1;
        }
    }
}
