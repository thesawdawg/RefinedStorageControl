using System.IO;
using RefinedStorageControl.Access;
using RefinedStorageControl.Core;
using RefinedStorageControl.Game.Providers;
using UnityEngine.Scripting;

namespace RefinedStorageControl.Game.Net
{
    internal enum CabinetAction { Query, Insert, Remove, SetPermissions, Transfer }

    // Client -> server: read the cabinet or act on it. An Insert carries the disk
    // item, already removed from the client's backpack; if the server refuses it,
    // the item comes straight back in a NetPackageRscGive.
    [Preserve]
    public class NetPackageRscCabinet : NetPackage
    {
        private Vector3i _pos;
        private CabinetAction _action;
        private int _slot;
        private string _target = "";
        private DigitalPermission _perms;
        private ItemValue _item;

        public override NetPackageDirection PackageDirection => NetPackageDirection.ToServer;

        internal NetPackageRscCabinet Setup(Vector3i pos, CabinetAction action, int slot = -1, string target = null,
            DigitalPermission perms = DigitalPermission.None, ItemValue item = null)
        {
            _pos = pos;
            _action = action;
            _slot = slot;
            _target = target ?? "";
            _perms = perms;
            _item = item;
            return this;
        }

        public override void read(PooledBinaryReader _br)
        {
            _pos = StreamUtils.ReadVector3i(_br);
            _action = (CabinetAction)_br.ReadInt32();
            _slot = _br.ReadInt32();
            _target = _br.ReadString();
            _perms = (DigitalPermission)_br.ReadInt32();
            _item = null;
            if (_br.ReadBoolean())
            {
                _item = new ItemValue();
                _item.Read(_br);
            }
        }

        public override void write(PooledBinaryWriter _bw)
        {
            base.write(_bw);
            var w = (BinaryWriter)_bw;
            StreamUtils.Write(_bw, _pos);
            w.Write((int)_action);
            w.Write(_slot);
            w.Write(_target);
            w.Write((int)_perms);
            w.Write(_item != null);
            if (_item != null)
                _item.Write(_bw);
        }

        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            if (!ConnectionManager.Instance.IsServer)
                return;
            string message = null;
            ItemValue giveBack = null;
            bool authorized = RemoteGuard.TryAuthorize(Sender, _pos, NodeKind.DriveCabinet, out var user, out message);
            if (!authorized)
            {
                giveBack = _item; // never keep an item for a refused request
            }
            else
            {
                switch (_action)
                {
                    case CabinetAction.Insert:
                        message = _item == null ? "Nothing to insert" : CabinetService.InsertDisk(user, _pos, _slot, _item, out _);
                        if (message != null)
                            giveBack = _item;
                        break;
                    case CabinetAction.Remove:
                        message = CabinetService.RemoveDisk(user, _pos, _slot, out giveBack);
                        break;
                    case CabinetAction.SetPermissions:
                        message = CabinetService.SetPermissions(user, _pos, _slot, _target, _perms);
                        break;
                    case CabinetAction.Transfer:
                        message = CabinetService.TransferOwnership(user, _pos, _slot, _target);
                        break;
                }
            }
            if (giveBack != null)
                Sender.SendPackage(NetPackageManager.GetPackage<NetPackageRscGive>().Setup(giveBack, 1, null, -1, false));
            var view = authorized ? CabinetService.BuildView(user, _pos) : new CabinetView { Error = message };
            Sender.SendPackage(NetPackageManager.GetPackage<NetPackageRscCabinetState>().Setup(_pos, message, view));
        }
    }

    // Server -> client: cabinet contents for that player plus the result of the action.
    [Preserve]
    public class NetPackageRscCabinetState : NetPackage
    {
        private Vector3i _pos;
        private string _message;
        private CabinetView _view;

        public override NetPackageDirection PackageDirection => NetPackageDirection.ToClient;

        internal NetPackageRscCabinetState Setup(Vector3i pos, string message, CabinetView view)
        {
            _pos = pos;
            _message = message;
            _view = view;
            return this;
        }

        public override void read(PooledBinaryReader _br)
        {
            _pos = StreamUtils.ReadVector3i(_br);
            _message = _br.ReadBoolean() ? _br.ReadString() : null;
            _view = CabinetView.Read(_br);
        }

        public override void write(PooledBinaryWriter _bw)
        {
            base.write(_bw);
            var w = (BinaryWriter)_bw;
            StreamUtils.Write(_bw, _pos);
            w.Write(_message != null);
            if (_message != null)
                w.Write(_message);
            _view.Write(w);
        }

        public override void ProcessPackage(World _world, GameManager _callbacks) =>
            ClientSession.OnCabinetState(_pos, _message, _view);
    }
}
