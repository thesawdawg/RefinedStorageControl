using System;
using System.Collections.Generic;
using System.Linq;

namespace RefinedStorageControl.Core
{
    // Topology for a Refined Storage network. Links form undirected components; a
    // component is usable only while it contains exactly one controller. Conflict
    // policy is intentionally open (F02): the registry reports conflicts but never
    // resolves them. Queries BFS over adjacency on demand; scale target ~30 nodes.
    public sealed class NetworkRegistry
    {
        private sealed class Node
        {
            public NodeKind Kind;
            public ControllerTier? Tier;
        }

        private readonly Dictionary<NodeId, Node> _nodes = new Dictionary<NodeId, Node>();
        private readonly Dictionary<NodeId, HashSet<NodeId>> _links = new Dictionary<NodeId, HashSet<NodeId>>();

        public int NodeCount => _nodes.Count;

        public void AddNode(NodeId id, NodeKind kind, ControllerTier? tier = null)
        {
            if (_nodes.ContainsKey(id))
                throw new InvalidOperationException("Node already registered: " + id);
            if (kind == NodeKind.Controller && tier == null)
                throw new ArgumentException("Controller nodes require a tier.", nameof(tier));
            if (kind != NodeKind.Controller && tier != null)
                throw new ArgumentException("Only controller nodes take a tier.", nameof(tier));
            _nodes.Add(id, new Node { Kind = kind, Tier = tier });
            _links.Add(id, new HashSet<NodeId>());
        }

        public void RemoveNode(NodeId id)
        {
            if (!_nodes.Remove(id))
                throw new InvalidOperationException("Unknown node: " + id);
            foreach (var neighbor in _links[id])
                _links[neighbor].Remove(id);
            _links.Remove(id);
        }

        public LinkCheck EvaluateLink(NodeId a, NodeId b)
        {
            if (!_nodes.ContainsKey(a) || !_nodes.ContainsKey(b))
                return LinkCheck.UnknownNode;
            if (a == b)
                return LinkCheck.SelfLink;
            if (_links[a].Contains(b))
                return LinkCheck.AlreadyLinked;
            var merged = ComponentOf(a);
            merged.UnionWith(ComponentOf(b));
            return CountControllers(merged) > 1 ? LinkCheck.ControllerConflict : LinkCheck.Ok;
        }

        // Placement preview (NET-04): would a new node of this kind linked to the
        // given existing nodes merge a component with more than one controller?
        public LinkCheck EvaluateNodeWithLinks(NodeKind kind, IEnumerable<NodeId> links)
        {
            var merged = new HashSet<NodeId>();
            if (links != null)
                foreach (var id in links)
                {
                    if (!_nodes.ContainsKey(id))
                        return LinkCheck.UnknownNode;
                    merged.UnionWith(ComponentOf(id));
                }
            int controllers = CountControllers(merged) + (kind == NodeKind.Controller ? 1 : 0);
            return controllers > 1 ? LinkCheck.ControllerConflict : LinkCheck.Ok;
        }

        public void Link(NodeId a, NodeId b)
        {
            var check = EvaluateLink(a, b);
            if (check != LinkCheck.Ok)
                throw new InvalidOperationException("Cannot link " + a + " -> " + b + ": " + check);
            _links[a].Add(b);
            _links[b].Add(a);
        }

        public void Unlink(NodeId a, NodeId b)
        {
            if (!_nodes.ContainsKey(a) || !_nodes.ContainsKey(b) || !_links[a].Contains(b))
                throw new InvalidOperationException("Nodes are not linked: " + a + " -> " + b);
            _links[a].Remove(b);
            _links[b].Remove(a);
        }

        // Save-restore path: bypasses link validation so a persisted Conflict state
        // can be reproduced instead of silently dropped.
        internal void LoadLink(NodeId a, NodeId b)
        {
            if (!_nodes.ContainsKey(a) || !_nodes.ContainsKey(b))
                throw new InvalidOperationException("Unknown node in saved link: " + a + " -> " + b);
            _links[a].Add(b);
            _links[b].Add(a);
        }

        public NodeStatus GetStatus(NodeId id)
        {
            if (!_nodes.ContainsKey(id))
                throw new InvalidOperationException("Unknown node: " + id);
            var controllers = ComponentOf(id).Where(n => _nodes[n].Kind == NodeKind.Controller).ToList();
            if (controllers.Count == 1)
                return new NodeStatus(NodeState.Connected, controllers[0]);
            if (controllers.Count == 0)
                return new NodeStatus(NodeState.Disconnected, null);
            return new NodeStatus(NodeState.Conflict, null);
        }

        // NET-05 host recheck: is member served by this controller right now?
        public bool IsConnected(NodeId member, NodeId controller)
        {
            if (!_nodes.ContainsKey(member))
                return false;
            var status = GetStatus(member);
            return status.State == NodeState.Connected && status.Controller == controller;
        }

        public ControllerTier? TierOf(NodeId controller)
        {
            return _nodes.TryGetValue(controller, out var node) && node.Kind == NodeKind.Controller
                ? node.Tier
                : (ControllerTier?)null;
        }

        public IReadOnlyList<NodeId> Members(NodeId controller)
        {
            if (!_nodes.ContainsKey(controller))
                return Array.Empty<NodeId>();
            var status = GetStatus(controller);
            if (status.State != NodeState.Connected || status.Controller != controller)
                return Array.Empty<NodeId>();
            var members = ComponentOf(controller).ToList();
            members.Sort();
            return members;
        }

        private int CountControllers(HashSet<NodeId> component)
        {
            int count = 0;
            foreach (var id in component)
                if (_nodes[id].Kind == NodeKind.Controller)
                    count++;
            return count;
        }

        private HashSet<NodeId> ComponentOf(NodeId start)
        {
            var visited = new HashSet<NodeId> { start };
            var queue = new Queue<NodeId>();
            queue.Enqueue(start);
            while (queue.Count > 0)
                foreach (var next in _links[queue.Dequeue()])
                    if (visited.Add(next))
                        queue.Enqueue(next);
            return visited;
        }
    }
}
