using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace RefinedStorageControl.Game.UI
{
    // One item cell in a terminal grid (XML: controller="RefinedStorageControl.Game.UI.RscCell,
    // RefinedStorageControl"). Display only; the owning window decides what a
    // click does through the Clicked event (button 0 = left, 1 = right).
    [Preserve]
    public class XUiC_RscCell : XUiController
    {
        private ItemValue _item;
        private int _count;
        private bool _locked;
        private bool _visible = true;

        public int Index { get; set; }

        public event Action<XUiC_RscCell, int> Clicked;

        public override void Init()
        {
            base.Init();
            OnPress += (sender, button) => Clicked?.Invoke(this, 0);
            OnRightPress += (sender, button) => Clicked?.Invoke(this, 1);
            IsDirty = true;
        }

        public void Set(ItemValue item, int count, bool locked, bool visible = true)
        {
            _item = item;
            _count = count;
            _locked = locked;
            if (_visible != visible)
            {
                _visible = visible;
                ViewComponent.IsVisible = visible;
            }
            IsDirty = true;
        }

        public override void Update(float _dt)
        {
            if (IsDirty)
            {
                RefreshBindings();
                IsDirty = false;
            }
            base.Update(_dt);
        }

        private bool HasItem => _item != null && !_item.IsEmpty() && _count > 0;

        public override bool GetBindingValueInternal(ref string value, string bindingName)
        {
            switch (bindingName)
            {
                case "hasitem":
                    value = HasItem.ToString();
                    return true;
                case "itemicon":
                    value = HasItem ? _item.ItemClass.GetIconName() : "";
                    return true;
                case "itemiconcolor":
                    value = HasItem ? Rgba(_item.ItemClass.GetIconTint(_item)) : "255,255,255,255";
                    return true;
                case "itemcount":
                    value = HasItem && _count > 1 ? Short(_count) : "";
                    return true;
                case "hasquality":
                    value = (HasItem && _item.HasQuality).ToString();
                    return true;
                case "qualitycolor":
                    value = HasItem && _item.HasQuality ? Rgba(QualityInfo.GetQualityColor(_item.Quality)) : "0,0,0,0";
                    return true;
                case "locked":
                    value = _locked.ToString();
                    return true;
                case "bgcolor":
                    value = _locked ? "60,45,30,255" : "40,40,40,255";
                    return true;
                case "tooltip":
                    value = HasItem
                        ? _item.ItemClass.GetLocalizedItemName() + " x" + _count
                          + (_item.HasQuality ? " (Q" + _item.Quality + ")" : "")
                        : "";
                    return true;
            }
            return base.GetBindingValueInternal(ref value, bindingName);
        }

        private static string Rgba(Color c)
        {
            Color32 c32 = c;
            return c32.r + "," + c32.g + "," + c32.b + "," + c32.a;
        }

        private static string Short(int count) =>
            count >= 100000 ? (count / 1000) + "k" : count.ToString();
    }
}
