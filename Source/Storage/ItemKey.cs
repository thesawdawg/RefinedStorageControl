using System;
using System.Linq;

namespace RefinedStorageControl.Storage
{
    // Opaque exact serialized item state. Two stacks merge on a disk only when
    // every serialized byte matches, so equality is byte-sequence equality.
    public readonly struct ItemKey : IEquatable<ItemKey>
    {
        private readonly byte[] _bytes;
        private readonly int _hash;

        public ItemKey(byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length == 0)
                throw new ArgumentException("Item key must not be empty.", nameof(bytes));
            _bytes = (byte[])bytes.Clone();
            _hash = ComputeHash(_bytes);
        }

        // Internal raw view for the serializer's deterministic ordering; never
        // mutated after construction.
        internal byte[] RawBytes => _bytes;

        public byte[] ToArray() => _bytes == null ? Array.Empty<byte>() : (byte[])_bytes.Clone();

        public bool Equals(ItemKey other)
        {
            if (ReferenceEquals(_bytes, other._bytes))
                return true;
            if (_bytes == null || other._bytes == null || _hash != other._hash)
                return false;
            return _bytes.SequenceEqual(other._bytes);
        }

        public override bool Equals(object obj) => obj is ItemKey other && Equals(other);

        public override int GetHashCode() => _hash;

        public static bool operator ==(ItemKey left, ItemKey right) => left.Equals(right);

        public static bool operator !=(ItemKey left, ItemKey right) => !left.Equals(right);

        private static int ComputeHash(byte[] bytes)
        {
            unchecked
            {
                int hash = (int)2166136261;
                foreach (var b in bytes)
                    hash = (hash ^ b) * 16777619;
                return hash;
            }
        }
    }
}
