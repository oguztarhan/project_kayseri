using Game.Core;
using UnityEngine;

namespace Game.Systems
{
    /// <summary>
    /// Who this device is, as far as the support desk is concerned: the id on the save, the build it is
    /// running, and the mail that carries both.
    ///
    /// A static helper rather than a service, on the precedent <see cref="StorePage"/> set — it holds no
    /// state of its own, every caller is a settings row, and registering it would put a fourth thing in
    /// the locator that only one screen ever asks for. What it does own is the ONE rule the UI must not
    /// be trusted with: the save is written before the id is shown.
    /// </summary>
    public static class PlayerIdentity
    {
        /// <summary>
        /// The id on this save, minted and persisted the first time anybody asks for it.
        ///
        /// MINTED ON DEMAND, NOT AT BOOT. Nothing in the game needs it until a settings screen shows it,
        /// and a boot-time mint would have to write a save file for a player who has not yet tapped
        /// anything.
        ///
        /// SAVED BEFORE IT IS RETURNED. The screen that receives it is about to put it on the clipboard
        /// and into a mail; an id that reached a support inbox but never reached the disk would come back
        /// as a different one on the next launch, and the ticket would be unmatchable. This is the same
        /// rule the contract claim runs under — the disk first, the screen second.
        ///
        /// Returns the empty string when there is no save to write to, which is a boot that failed
        /// earlier and louder than this.
        /// </summary>
        public static string Ensure(SaveData data, SaveService save)
        {
            if (data == null) return string.Empty;
            if (PlayerId.IsValid(data.playerId)) return data.playerId;

            data.playerId = PlayerId.From(System.Guid.NewGuid());
            // Suspended is the test-mode switch; honouring it here keeps a test session from writing over
            // a real save, and an id that is re-minted next launch is exactly what a test session wants.
            if (save != null) save.Save(data);
            return data.playerId;
        }

        /// <summary>
        /// The build, as one short string for the footer: <c>v1.4.2 · 8f3c1a02</c>.
        ///
        /// The second half is <see cref="Application.buildGUID"/>, not the Android version code. The
        /// version code is not exposed to a running player build without a JNI call into PackageManager,
        /// and the buildGUID answers the question the desk actually asks — "is this the build we shipped
        /// on Tuesday?" — which two players on the same store version cannot otherwise be told apart on.
        /// It is empty in the Editor, where the row simply shows the version.
        /// </summary>
        public static string VersionLine()
        {
            string build = Build();
            return build.Length > 0 ? "v" + Application.version + " · " + build : "v" + Application.version;
        }

        /// <summary>
        /// The first eight characters of the build guid — enough to name a build, short enough to sit
        /// under a version number without wrapping.
        ///
        /// AN ALL-ZERO GUID IS NOT A BUILD ID. Unity hands back thirty-two zeros rather than an empty
        /// string when there is no build to name — in the Editor, and in any player built without one —
        /// and the row was reading `v1.0 · 00000000` on screen because of it. A build id nobody can
        /// look up is worse than none: it invites a support desk to quote it back.
        /// </summary>
        public static string Build()
        {
            string guid = Application.buildGUID;
            if (string.IsNullOrEmpty(guid)) return string.Empty;

            for (int i = 0; i < guid.Length; i++)
                if (guid[i] != '0' && guid[i] != '-')
                    return guid.Length > 8 ? guid.Substring(0, 8) : guid;
            return string.Empty;
        }

        /// <summary>
        /// The support mail for this device, ready to open, or the empty string when
        /// <paramref name="address"/> is not one plain address (see <see cref="SupportTicket.Mailto"/>).
        /// </summary>
        public static string Mailto(string address, string playerId)
        {
            var loc = ServiceLocator.Get<LocalizationService>();
            string body = SupportTicket.Body(
                playerId,
                Application.version,
                Build(),
                Application.platform.ToString() + " " + SystemInfo.operatingSystem,
                SystemInfo.deviceModel,
                loc != null ? loc.Code : "?",
                SaveMigration.CurrentVersion,
                Loc.T("ayarlar.destek_mesaj"));

            return SupportTicket.Mailto(address, SupportTicket.Subject(Loc.T("ayarlar.destek_konu"), playerId), body);
        }

        /// <summary>
        /// Puts <paramref name="text"/> on the system clipboard. Wrapped here so the one platform quirk
        /// worth knowing has somewhere to live: on Android this is the only clipboard Unity offers, it is
        /// silent when it fails, and there is nothing to read back to confirm it — which is why the row
        /// that calls it says "copied" off its own bat rather than off a result.
        /// </summary>
        public static void Copy(string text)
        {
            if (!string.IsNullOrEmpty(text)) GUIUtility.systemCopyBuffer = text;
        }
    }
}
