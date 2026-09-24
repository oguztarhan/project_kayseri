using System.Text;

namespace Game.Core
{
    /// <summary>
    /// The rules for the player's league profile: which avatars exist and what a name may be. The
    /// numbers and the character rules only — the saved choice is <c>Game.Systems.PlayerProfileService</c>,
    /// and the pictures are <c>Game.UI.AvatarArt</c>.
    ///
    /// AVATARS ARE THE FIFTEEN MASTERS, by atlas sprite name. The portraits already exist in the masters
    /// kit, so the profile needs no new art and no new atlas page. An avatar is saved as an INDEX into
    /// <see cref="AvatarSprites"/>, so this list may only ever be appended to — reordering it would
    /// swap every saved player's face.
    ///
    /// A NAME IS SHOWN ON A BOARD, so it is cleaned rather than trusted: no rich-text or format
    /// characters (a name reading "&lt;size=200&gt;" would restyle the row it sits in), no control
    /// characters, no runs of spaces. Letters in any script are allowed — the game ships in eleven
    /// languages and a Russian or Vietnamese player's name is not a typo.
    /// </summary>
    public static class PlayerProfiles
    {
        /// <summary>Sprite names in the masters atlas. Append only; see the class note.</summary>
        public static readonly string[] AvatarSprites =
        {
            "portre_bekir", "portre_cemil", "portre_fikri", "portre_hasan", "portre_hikmet",
            "portre_leyla", "portre_mahmut", "portre_nazmi", "portre_necla", "portre_rasim",
            "portre_riza", "portre_sedef", "portre_sema", "portre_sukru", "portre_zeki",
        };

        public static int AvatarCount => AvatarSprites.Length;

        /// <summary>Shortest and longest name a player may save, counted after cleaning.</summary>
        public const int MinNameLength = 3;
        public const int MaxNameLength = 16;

        /// <summary>A saved index that no longer points at an avatar reads as the first one rather than
        /// as nothing — a damaged save must still draw a face.</summary>
        public static int ClampAvatar(int index) => index >= 0 && index < AvatarCount ? index : 0;

        /// <summary>Whether one typed character may go into a name at all. The input field asks this per
        /// keystroke; <see cref="Sanitize"/> asks it again for anything pasted.</summary>
        public static bool IsAllowedChar(char c)
            => char.IsLetterOrDigit(c) || c == ' ' || c == '_' || c == '-' || c == '.';

        /// <summary>
        /// A name as it would be saved: disallowed characters dropped, runs of spaces collapsed, the ends
        /// trimmed, and cut to <see cref="MaxNameLength"/>. Never null.
        /// </summary>
        public static string Sanitize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            var clean = new StringBuilder(raw.Length);
            bool space = false;
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (char.IsWhiteSpace(c)) c = ' ';
                if (!IsAllowedChar(c)) continue;
                if (c == ' ')
                {
                    if (space || clean.Length == 0) continue;
                    space = true;
                }
                else space = false;
                clean.Append(c);
            }

            string name = clean.ToString().Trim();
            if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength).TrimEnd();
            return name;
        }

        /// <summary>Whether a cleaned name is long enough to save. Too long cannot happen after
        /// <see cref="Sanitize"/>; too short can.</summary>
        public static bool IsValidName(string clean)
            => clean != null && clean.Length >= MinNameLength && clean.Length <= MaxNameLength;
    }
}
