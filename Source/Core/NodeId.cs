using System;

namespace RefinedStorageControl.Core
{
    public readonly struct NodeId : IEquatable<NodeId>, IComparable<NodeId>
    {
        public NodeId(long value) => Value = value;
        public long Value { get; }
        public bool Equals(NodeId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is NodeId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(NodeId other) => Value.CompareTo(other.Value);
        public static bool operator ==(NodeId left, NodeId right) => left.Equals(right);
        public static bool operator !=(NodeId left, NodeId right) => !left.Equals(right);
        public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public enum NodeKind { Controller, Interface, Terminal, Hub, DriveCabinet }

    public enum ControllerTier { Simple, Advanced }

    public enum LinkCheck { Ok, UnknownNode, SelfLink, AlreadyLinked, ControllerConflict }

    public enum NodeState { Connected, Disconnected, Conflict }

    public readonly struct NodeStatus
    {
        public NodeStatus(NodeState state, NodeId? controller)
        {
            State = state;
            Controller = controller;
        }

        public NodeState State { get; }
        public NodeId? Controller { get; }
    }
}
