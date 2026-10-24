using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace RefinedStorageControl.Access
{
    // ACCESS-03: an owner plus an explicit allowlist with independent
    // deposit/withdraw/craft/configure grants. Default deny. Player ids are
    // ordinal, case-sensitive strings.
    public sealed class DigitalAcl
    {
        private readonly Dictionary<string, DigitalPermission> _entries =
            new Dictionary<string, DigitalPermission>(StringComparer.Ordinal);

        public DigitalAcl(string ownerId)
        {
            OwnerId = ownerId ?? string.Empty;
            Entries = new ReadOnlyDictionary<string, DigitalPermission>(_entries);
        }

        public string OwnerId { get; private set; }
        public bool HasOwner => OwnerId.Length > 0;
        public IReadOnlyDictionary<string, DigitalPermission> Entries { get; }

        public bool Has(string playerId, DigitalPermission required)
        {
            if (required == DigitalPermission.None)
                throw new ArgumentException("A permission check must require at least one flag.", nameof(required));
            if ((required & ~DigitalPermission.All) != 0)
                throw new ArgumentException("Required permission contains undefined flags.", nameof(required));
            if (string.IsNullOrEmpty(playerId) || !HasOwner)
                return false;
            if (string.Equals(playerId, OwnerId, StringComparison.Ordinal))
                return true;
            return _entries.TryGetValue(playerId, out var granted) && (granted & required) == required;
        }

        public void Set(string actingPlayerId, string targetPlayerId, DigitalPermission perms)
        {
            if (!HasOwner || !string.Equals(actingPlayerId, OwnerId, StringComparison.Ordinal))
                throw new InvalidOperationException("Only the owner may change the allowlist.");
            if (string.IsNullOrEmpty(targetPlayerId))
                throw new ArgumentException("Target player id must not be empty.", nameof(targetPlayerId));
            if (string.Equals(targetPlayerId, OwnerId, StringComparison.Ordinal))
                throw new InvalidOperationException("The owner always has full access and cannot be listed.");
            if ((perms & ~DigitalPermission.All) != 0)
                throw new ArgumentException("Permissions contain undefined flags.", nameof(perms));
            if (perms == DigitalPermission.None)
                _entries.Remove(targetPlayerId);
            else
                _entries[targetPlayerId] = perms;
        }

        // ACCESS-04: the owner hands the disk to a player already on the
        // allowlist. The new owner leaves the allowlist (owners are implicit);
        // the previous owner stays on it with the default deposit + withdraw.
        public void TransferOwnership(string actingPlayerId, string newOwnerId)
        {
            if (!HasOwner || !string.Equals(actingPlayerId, OwnerId, StringComparison.Ordinal))
                throw new InvalidOperationException("Only the owner may transfer ownership.");
            if (string.IsNullOrEmpty(newOwnerId) || !_entries.ContainsKey(newOwnerId))
                throw new InvalidOperationException("Ownership can only go to a player on the allowlist.");
            string previous = OwnerId;
            _entries.Remove(newOwnerId);
            OwnerId = newOwnerId;
            _entries[previous] = DefaultGrant;
        }

        // ACCESS-04 host override (console), e.g. for a player who left for good.
        // The previous owner, if any, keeps the default grant.
        public void ForceOwner(string newOwnerId)
        {
            if (string.IsNullOrEmpty(newOwnerId))
                throw new ArgumentException("New owner id must not be empty.", nameof(newOwnerId));
            if (string.Equals(newOwnerId, OwnerId, StringComparison.Ordinal))
                return;
            string previous = OwnerId;
            _entries.Remove(newOwnerId);
            OwnerId = newOwnerId;
            if (previous.Length > 0)
                _entries[previous] = DefaultGrant;
        }

        // ACCESS-04: what a newly added allowlist entry gets.
        public const DigitalPermission DefaultGrant = DigitalPermission.Deposit | DigitalPermission.Withdraw;

        // Save-restore path used by the disk serializer after the on-disk format
        // has already been validated; bypasses the owner-only mutation rule.
        internal void RestoreEntry(string playerId, DigitalPermission perms)
        {
            if (string.IsNullOrEmpty(playerId))
                throw new ArgumentException("Entry player id must not be empty.", nameof(playerId));
            if (string.Equals(playerId, OwnerId, StringComparison.Ordinal))
                throw new ArgumentException("The owner cannot appear in the allowlist.", nameof(playerId));
            if (perms == DigitalPermission.None || (perms & ~DigitalPermission.All) != 0)
                throw new ArgumentException("Permissions contain undefined flags.", nameof(perms));
            _entries[playerId] = perms;
        }
    }
}
