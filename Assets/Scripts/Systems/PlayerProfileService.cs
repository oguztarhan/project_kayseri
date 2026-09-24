using System;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// The player's league profile: the name and avatar the board shows for them. Reads and writes
    /// <see cref="SaveData.profile"/> and nothing else. The rules for what a valid name or avatar is
    /// live in <see cref="PlayerProfiles"/>.
    ///
    /// A NAME IS SAVED CLEAN OR NOT AT ALL. <see cref="TrySet"/> cleans the typed text and refuses a
    /// result that is too short, so a save can only ever hold a name that passed the rules. Reads
    /// clean the value again anyway, because a save file is text and can be edited by hand.
    /// </summary>
    public sealed class PlayerProfileService
    {
        private readonly SaveData _data;
        private readonly SaveService _save;

        public event Action Changed;

        public PlayerProfileService(SaveData data, SaveService save)
        {
            _data = data;
            _save = save;
        }

        private PlayerProfileData Profile
        {
            get
            {
                if (_data == null) return null;
                if (_data.profile == null) _data.profile = new PlayerProfileData();
                return _data.profile;
            }
        }

        /// <summary>The saved name, cleaned; empty when the player has not made a profile yet.</summary>
        public string Name
        {
            get
            {
                PlayerProfileData p = Profile;
                if (p == null) return string.Empty;
                string clean = PlayerProfiles.Sanitize(p.name);
                return PlayerProfiles.IsValidName(clean) ? clean : string.Empty;
            }
        }

        public bool HasName => Name.Length > 0;

        public int Avatar
        {
            get
            {
                PlayerProfileData p = Profile;
                return PlayerProfiles.ClampAvatar(p != null ? p.avatar : 0);
            }
        }

        /// <summary>Whether the editor has already opened by itself once. After that it opens only when
        /// the player asks for it.</summary>
        public bool Prompted
        {
            get
            {
                PlayerProfileData p = Profile;
                return p == null || p.prompted;
            }
        }

        /// <summary>
        /// Saves a new name and avatar. False, and nothing written, when the cleaned name is too short
        /// or the avatar does not exist. A save that changes nothing still counts as seen, so the
        /// editor does not open by itself again.
        /// </summary>
        public bool TrySet(string rawName, int avatar)
        {
            PlayerProfileData p = Profile;
            if (p == null) return false;

            string clean = PlayerProfiles.Sanitize(rawName);
            if (!PlayerProfiles.IsValidName(clean)) return false;
            if (avatar < 0 || avatar >= PlayerProfiles.AvatarCount) return false;

            bool moved = !string.Equals(p.name, clean, StringComparison.Ordinal) || p.avatar != avatar;
            p.name = clean;
            p.avatar = avatar;
            p.prompted = true;
            Commit();
            if (moved) Changed?.Invoke();
            return true;
        }

        /// <summary>The editor was closed without saving. It still counts as having been shown.</summary>
        public void MarkPrompted()
        {
            PlayerProfileData p = Profile;
            if (p == null || p.prompted) return;
            p.prompted = true;
            Commit();
        }

        private void Commit()
        {
            if (_save != null && _data != null) _save.Save(_data);
        }
    }
}
