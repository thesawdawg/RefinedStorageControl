using System;

namespace RefinedStorageControl.Access
{
    [Flags]
    public enum DigitalPermission
    {
        None = 0,
        Deposit = 1,
        Withdraw = 2,
        Craft = 4,
        Configure = 8,
        All = Deposit | Withdraw | Craft | Configure
    }
}
