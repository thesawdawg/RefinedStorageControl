using System;
using RefinedStorageControl.Access;
using static Harness;

internal static class AccessTests
{
    public static void Run()
    {
        var open = new DigitalAcl(null);
        Check(!open.HasOwner && !open.Has("anyone", DigitalPermission.Deposit)
            && !open.Has("anyone", DigitalPermission.All),
            "Ownerless ACLs must deny everyone.");
        Reject(() => open.Set("anyone", "bob", DigitalPermission.Deposit),
            "Ownerless ACLs must reject allowlist edits.");

        var acl = new DigitalAcl("owner-1");
        Check(acl.HasOwner && acl.OwnerId == "owner-1", "Owner id must be retained verbatim.");
        Check(acl.Has("owner-1", DigitalPermission.All) && acl.Has("owner-1", DigitalPermission.Deposit),
            "The owner must have every permission.");
        Check(!acl.Has("stranger", DigitalPermission.Deposit), "Unlisted players must be denied by default.");
        Check(!acl.Has(null, DigitalPermission.Deposit) && !acl.Has("", DigitalPermission.Deposit),
            "Null and empty player ids must be denied.");

        acl.Set("owner-1", "alice", DigitalPermission.Deposit);
        Check(acl.Has("alice", DigitalPermission.Deposit), "Granted flags must apply.");
        Check(!acl.Has("alice", DigitalPermission.Withdraw) && !acl.Has("alice", DigitalPermission.Craft)
            && !acl.Has("alice", DigitalPermission.Configure),
            "Permissions must be independent.");
        Check(!acl.Has("alice", DigitalPermission.Deposit | DigitalPermission.Withdraw),
            "Combined requirements must need every flag.");
        acl.Set("owner-1", "alice", DigitalPermission.Deposit | DigitalPermission.Withdraw);
        Check(acl.Has("alice", DigitalPermission.Deposit | DigitalPermission.Withdraw)
            && acl.Has("alice", DigitalPermission.Deposit),
            "Combined grants must satisfy combined requirements.");

        acl.Set("owner-1", "carol", DigitalPermission.Configure);
        Reject(() => acl.Set("carol", "dave", DigitalPermission.Deposit),
            "Configure must not grant allowlist editing.");
        Reject(() => acl.Set("alice", "dave", DigitalPermission.Deposit),
            "Non-owners must not edit the allowlist.");
        Reject(() => acl.Set(null, "dave", DigitalPermission.Deposit),
            "Null actors must not edit the allowlist.");

        Reject(() => acl.Set("owner-1", "owner-1", DigitalPermission.Deposit),
            "The owner must not appear in the allowlist.");
        Reject<ArgumentException>(() => acl.Set("owner-1", "dave", (DigitalPermission)16),
            "Undefined permission bits must be rejected.");
        Reject<ArgumentException>(() => acl.Set("owner-1", "", DigitalPermission.Deposit),
            "Empty target ids must be rejected.");
        Reject<ArgumentException>(() => acl.Has("alice", DigitalPermission.None),
            "Checking for None must be rejected.");
        Reject<ArgumentException>(() => acl.Has("alice", (DigitalPermission)32),
            "Checking undefined flags must be rejected.");

        Check(acl.Entries.Count == 2 && acl.Entries["alice"] == (DigitalPermission.Deposit | DigitalPermission.Withdraw)
            && acl.Entries["carol"] == DigitalPermission.Configure,
            "Entries must reflect every explicit grant.");
        acl.Set("owner-1", "alice", DigitalPermission.None);
        Check(!acl.Entries.ContainsKey("alice") && !acl.Has("alice", DigitalPermission.Deposit),
            "Setting None must remove the entry.");

        var caseCheck = new DigitalAcl("Owner");
        caseCheck.Set("Owner", "Alice", DigitalPermission.Deposit);
        Check(!caseCheck.Has("owner", DigitalPermission.Deposit) && !caseCheck.Has("alice", DigitalPermission.Deposit),
            "Player ids must be ordinal and case-sensitive.");

        OwnershipTests();
    }

    private static void OwnershipTests()
    {
        Check(DigitalAcl.DefaultGrant == (DigitalPermission.Deposit | DigitalPermission.Withdraw),
            "New allowlist entries default to deposit + withdraw.");
        var acl = new DigitalAcl("ann");
        acl.Set("ann", "bob", DigitalPermission.Withdraw);
        Reject(() => acl.TransferOwnership("bob", "ann"), "Only the owner may transfer ownership.");
        Reject(() => acl.TransferOwnership("ann", "cat"), "Ownership must go to an allowlisted player.");
        acl.TransferOwnership("ann", "bob");
        Check(acl.OwnerId == "bob" && !acl.Entries.ContainsKey("bob") && acl.Has("bob", DigitalPermission.All),
            "The new owner must have full access and leave the allowlist.");
        Check(acl.Entries["ann"] == DigitalAcl.DefaultGrant && !acl.Has("ann", DigitalPermission.Configure),
            "The previous owner must keep the default grant only.");
        Reject(() => acl.Set("ann", "cat", DigitalPermission.Deposit), "The previous owner must lose allowlist control.");

        acl.ForceOwner("dan");
        Check(acl.OwnerId == "dan" && acl.Entries["bob"] == DigitalAcl.DefaultGrant,
            "A forced owner change must keep the previous owner with the default grant.");
        var ownerless = new DigitalAcl("");
        ownerless.ForceOwner("eve");
        Check(ownerless.HasOwner && ownerless.OwnerId == "eve" && ownerless.Entries.Count == 0,
            "Forcing an owner onto an ownerless disk must not add an empty entry.");
        Reject<ArgumentException>(() => ownerless.ForceOwner(""), "Forcing an empty owner must throw.");
    }
}
