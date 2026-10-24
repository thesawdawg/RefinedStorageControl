using System.Collections.Generic;
using System.IO;
using RefinedStorageControl.Core;
using RefinedStorageControl.Crafting;
using RefinedStorageControl.Game.Crafting;
using UnityEngine.Scripting;

namespace RefinedStorageControl.Game.Net
{
    internal enum CraftAction
    {
        List,
        Request,
        Cancel,
        Status
    }

    // Client -> server: terminal crafting (list recipes, request, cancel, or
    // just the countdown status). Every request is validated on the server.
    [Preserve]
    public class NetPackageRscCraft : NetPackage
    {
        private Vector3i _pos;
        private CraftAction _action;
        private int _recipeIndex;
        private string _itemName = "";
        private int _crafts;
        private int _requestId;

        public override NetPackageDirection PackageDirection => NetPackageDirection.ToServer;

        internal NetPackageRscCraft Setup(Vector3i pos, CraftAction action, int recipeIndex = -1, string itemName = null,
            int crafts = 0, int requestId = 0)
        {
            _pos = pos;
            _action = action;
            _recipeIndex = recipeIndex;
            _itemName = itemName ?? "";
            _crafts = crafts;
            _requestId = requestId;
            return this;
        }

        public override void read(PooledBinaryReader _br)
        {
            _pos = StreamUtils.ReadVector3i(_br);
            _action = (CraftAction)_br.ReadInt32();
            _recipeIndex = _br.ReadInt32();
            _itemName = _br.ReadString();
            _crafts = _br.ReadInt32();
            _requestId = _br.ReadInt32();
        }

        public override void write(PooledBinaryWriter _bw)
        {
            base.write(_bw);
            StreamUtils.Write(_bw, _pos);
            ((BinaryWriter)_bw).Write((int)_action);
            ((BinaryWriter)_bw).Write(_recipeIndex);
            ((BinaryWriter)_bw).Write(_itemName);
            ((BinaryWriter)_bw).Write(_crafts);
            ((BinaryWriter)_bw).Write(_requestId);
        }

        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            if (!ConnectionManager.Instance.IsServer)
                return;
            string error = null;
            string message = null;
            List<CraftOption> options = null;
            if (!RemoteGuard.TryAuthorize(Sender, _pos, NodeKind.Terminal, out var user, out error))
            {
                Sender.SendPackage(NetPackageManager.GetPackage<NetPackageRscCraftState>().Setup(_pos, error, null, null, null));
                return;
            }
            var player = _world.GetEntity(Sender.entityId) as EntityPlayer;
            switch (_action)
            {
                case CraftAction.List:
                    if (!CraftService.List(user, player, _pos, out options, out error))
                        options = new List<CraftOption>();
                    break;
                case CraftAction.Request:
                    CraftService.Request(user, player, _pos, _recipeIndex, _itemName, _crafts, out message);
                    break;
                case CraftAction.Cancel:
                    CraftService.Cancel(user, _requestId, out message);
                    break;
            }
            Sender.SendPackage(NetPackageManager.GetPackage<NetPackageRscCraftState>()
                .Setup(_pos, error, message, options, CraftService.PendingFor(user)));
        }
    }

    // Server -> client: recipe options (when listed), the player's countdowns,
    // and a message. A message with no terminal position is a notice about a
    // request or job (dispatched, cancelled, stopped) shown as a tooltip.
    [Preserve]
    public class NetPackageRscCraftState : NetPackage
    {
        private Vector3i _pos;
        private string _error;
        private string _message;
        private List<CraftOption> _options;
        private List<CraftPendingView> _pending;

        public override NetPackageDirection PackageDirection => NetPackageDirection.ToClient;
        public override bool Compress => true;

        internal NetPackageRscCraftState Setup(Vector3i pos, string error, string message, List<CraftOption> options,
            List<CraftPendingView> pending)
        {
            _pos = pos;
            _error = error;
            _message = message;
            _options = options;
            _pending = pending;
            return this;
        }

        public override void read(PooledBinaryReader _br)
        {
            _pos = StreamUtils.ReadVector3i(_br);
            _error = _br.ReadBoolean() ? _br.ReadString() : null;
            _message = _br.ReadBoolean() ? _br.ReadString() : null;
            _options = null;
            if (_br.ReadBoolean())
            {
                int n = _br.ReadInt32();
                _options = new List<CraftOption>();
                for (int i = 0; i < n && i < 65536; i++)
                {
                    var o = new CraftOption { RecipeIndex = _br.ReadInt32(), Output = new ItemValue() };
                    o.Output.Read(_br);
                    o.PerCraft = _br.ReadInt32();
                    o.Max = _br.ReadInt32();
                    o.Info = _br.ReadString();
                    _options.Add(o);
                }
            }
            _pending = null;
            if (_br.ReadBoolean())
            {
                int n = _br.ReadInt32();
                _pending = new List<CraftPendingView>();
                for (int i = 0; i < n && i < 1024; i++)
                    _pending.Add(new CraftPendingView
                    {
                        Id = _br.ReadInt32(),
                        Item = _br.ReadString(),
                        Crafts = _br.ReadInt32(),
                        SecondsLeft = _br.ReadSingle()
                    });
            }
        }

        public override void write(PooledBinaryWriter _bw)
        {
            base.write(_bw);
            var w = (BinaryWriter)_bw;
            StreamUtils.Write(_bw, _pos);
            w.Write(_error != null);
            if (_error != null)
                w.Write(_error);
            w.Write(_message != null);
            if (_message != null)
                w.Write(_message);
            w.Write(_options != null);
            if (_options != null)
            {
                w.Write(_options.Count);
                foreach (var o in _options)
                {
                    w.Write(o.RecipeIndex);
                    o.Output.Write(_bw);
                    w.Write(o.PerCraft);
                    w.Write(o.Max);
                    w.Write(o.Info ?? "");
                }
            }
            w.Write(_pending != null);
            if (_pending != null)
            {
                w.Write(_pending.Count);
                foreach (var p in _pending)
                {
                    w.Write(p.Id);
                    w.Write(p.Item ?? "");
                    w.Write(p.Crafts);
                    w.Write(p.SecondsLeft);
                }
            }
        }

        public override void ProcessPackage(World _world, GameManager _callbacks) =>
            ClientSession.OnCraftState(_pos, _error, _message, _options, _pending);
    }

    // Server -> client: crafting XP from terminal jobs (CRAFT-10). XP is
    // granted on the player's own client, as at a station.
    [Preserve]
    public class NetPackageRscCraftXp : NetPackage
    {
        private List<XpGrant> _grants = new List<XpGrant>();

        public override NetPackageDirection PackageDirection => NetPackageDirection.ToClient;

        internal NetPackageRscCraftXp Setup(List<XpGrant> grants)
        {
            _grants = grants;
            return this;
        }

        public override void read(PooledBinaryReader _br)
        {
            int n = _br.ReadInt32();
            _grants = new List<XpGrant>();
            for (int i = 0; i < n && i < 4096; i++)
                _grants.Add(new XpGrant
                {
                    Recipe = _br.ReadString(),
                    Item = _br.ReadString(),
                    Exp = _br.ReadInt32(),
                    Count = _br.ReadInt32()
                });
        }

        public override void write(PooledBinaryWriter _bw)
        {
            base.write(_bw);
            var w = (BinaryWriter)_bw;
            w.Write(_grants.Count);
            foreach (var g in _grants)
            {
                w.Write(g.Recipe);
                w.Write(g.Item);
                w.Write(g.Exp);
                w.Write(g.Count);
            }
        }

        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            var player = _world?.GetPrimaryPlayer();
            if (player == null)
                return;
            foreach (var g in _grants)
                CraftService.GiveLocal(player, g);
        }
    }
}
