using UnityEngine;

namespace Game.UI
{
    /// <summary>Shared, runtime-loaded Tycoon surfaces used by both upgrade flows.</summary>
    internal static class TycoonUpgradeArt
    {
        private const string Root = "UI/TycoonUpgrade/";
        private static Sprite _panel, _card, _buy, _close, _wallet, _title;
        private static Sprite[] _yardIcons;

        internal static Sprite Panel => _panel ?? (_panel = Resources.Load<Sprite>(Root + "upgrade_panel"));
        internal static Sprite Card => _card ?? (_card = LoadTrimmed("upgrade_card"));
        internal static Sprite Buy => _buy ?? (_buy = Resources.Load<Sprite>(Root + "upgrade_buy"));
        internal static Sprite Close => _close ?? (_close = Resources.Load<Sprite>(Root + "upgrade_close"));
        internal static Sprite Wallet => _wallet ?? (_wallet = LoadTrimmed("upgrade_wallet"));
        internal static Sprite Title => _title ?? (_title = Resources.Load<Sprite>(Root + "upgrade_title"));
        internal static Sprite CashIcon => Resources.Load<Sprite>(Root + "cash_icon");

        private static Sprite LoadTrimmed(string name)
        {
            Sprite source = Resources.Load<Sprite>(Root + name);
            if (source == null || source.texture == null) return source;
            Texture2D texture = source.texture;
            Rect rect = new Rect(texture.width * 0.05f, texture.height * 0.17f,
                                 texture.width * 0.90f, texture.height * 0.66f);
            Sprite trimmed = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), source.pixelsPerUnit,
                                           0u, SpriteMeshType.FullRect, Vector4.zero);
            trimmed.name = name + "_trimmed";
            return trimmed;
        }

        internal static Sprite YardIcon(int index)
        {
            if (_yardIcons == null)
            {
                _yardIcons = new[]
                {
                    Resources.Load<Sprite>(Root + "yard_storage"),
                    Resources.Load<Sprite>(Root + "yard_queue"),
                    Resources.Load<Sprite>(Root + "yard_carrier"),
                    Resources.Load<Sprite>(Root + "yard_sales"),
                    Resources.Load<Sprite>(Root + "yard_cashier"),
                    Resources.Load<Sprite>(Root + "yard_transport")
                };
            }
            return index >= 0 && index < _yardIcons.Length ? _yardIcons[index] : null;
        }
    }
}
