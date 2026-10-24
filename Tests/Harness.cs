using System;
using System.IO;

internal static class Harness
{
    public static int Count;

    public static void Check(bool condition, string message)
    {
        Count++;
        if (!condition)
            throw new Exception(message);
    }

    public static void Reject(Action action, string message) => Reject<InvalidOperationException>(action, message);

    public static void Reject<TException>(Action action, string message) where TException : Exception
    {
        bool rejected = false;
        try { action(); }
        catch (TException) { rejected = true; }
        Check(rejected, message);
    }
}
