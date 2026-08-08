#if UNITY_ANDROID
using Unity.Notifications.Android;
using UnityEngine;

namespace Game.Systems
{
    /// <summary>
    /// The real <see cref="INotifications"/>: local Android notifications through
    /// com.unity.mobile.notifications. Registered only in a device build — the editor keeps
    /// <see cref="StubNotifications"/>, the same split GooglePlayIAPService uses, so play mode never
    /// depends on a channel the editor has no notification manager to create.
    ///
    /// Everything here is LOCAL. There is no server, no push token and no network call: the OS is
    /// handed a list of times and lines as the app goes to background, and it posts them whether or
    /// not the game ever runs again.
    /// </summary>
    public sealed class AndroidNotifications : INotifications
    {
        /// <summary>
        /// One channel. Android 8+ lets the player mute channels individually, and splitting these
        /// nudges into several would only offer them more ways to switch off the same thing.
        /// Default importance rather than High on purpose: High posts a heads-up banner over whatever
        /// the player is doing, which is not a reasonable way to mention that some coal piled up.
        /// </summary>
        private const string ChannelId = "ocak";

        /// <summary>
        /// Icon ids as registered in Project Settings > Mobile Notifications, NOT file paths. The small
        /// one must be a white silhouette on transparency: Android discards its colour and paints the
        /// opaque area white, so a normal logo arrives as a solid white square.
        /// </summary>
        private const string SmallIcon = "ocak_kucuk";
        private const string LargeIcon = "ocak_buyuk";

        private readonly bool _ready;
        private bool _permissionAsked;

        public AndroidNotifications()
        {
            _ready = AndroidNotificationCenter.Initialize();
            if (!_ready) return;

            AndroidNotificationCenter.RegisterNotificationChannel(new AndroidNotificationChannel(
                ChannelId, Loc.T("bildirim.kanal_ad"), Loc.T("bildirim.kanal_aciklama"), Importance.Default)
            {
                EnableVibration = true,
                CanShowBadge = true,
                LockScreenVisibility = LockScreenVisibility.Public
            });
        }

        public void Schedule(string title, string message, int afterSeconds)
        {
            if (!_ready || afterSeconds <= 0) return;

            var n = new AndroidNotification(title, message, System.DateTime.Now.AddSeconds(afterSeconds))
            {
                SmallIcon = SmallIcon,
                LargeIcon = LargeIcon,
                ShouldAutoCancel = true,
                // The game cancels the whole queue when it comes to the foreground, but a notification
                // can still fire in the gap before that runs. Suppressing it there stops the player
                // being told to come back to a game they are looking at.
                ShowInForeground = false
            };
            AndroidNotificationCenter.SendNotification(n, ChannelId);
        }

        public void CancelAll()
        {
            if (!_ready) return;
            AndroidNotificationCenter.CancelAllNotifications();   // queued and already posted
        }

        /// <summary>
        /// Android 13 made posting a runtime permission, and the system stops showing the dialog after
        /// two refusals — permanently, for the life of the install. So this asks only when it has never
        /// been asked: a player who said no gets to change their mind in the OS settings rather than
        /// being asked again every time they open the game. Below API 33 the status reads Allowed and
        /// nothing happens.
        ///
        /// The request is deliberately not awaited. The answer decides nothing on this side — an
        /// unpermitted notification is dropped by the OS, not by us — so there is nothing for a
        /// coroutine to do but wait.
        /// </summary>
        public void RequestPermission()
        {
            if (!_ready || _permissionAsked) return;
            _permissionAsked = true;
            if (AndroidNotificationCenter.UserPermissionToPost != PermissionStatus.NotRequested) return;
            _ = new PermissionRequest();
        }
    }
}
#endif
