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
        ///
        /// The id changed from "ocak" when the chime was added, and it HAD to: Android freezes a
        /// channel's sound at creation and ignores every later attempt to change it, so an install that
        /// already had the silent "ocak" channel would have kept the default sound forever. A new id is
        /// a new channel, which is the only way to hand the OS a different sound.
        /// </summary>
        private const string ChannelId = "ocak_zil";

        /// <summary>The silent channel this replaced. Deleted on sight so it stops showing up as a
        /// second, dead entry in the app's notification settings.</summary>
        private const string RetiredChannelId = "ocak";

        /// <summary>
        /// Base name of the file in <c>Assets/Plugins/Android/bildirim.androidlib/res/raw/</c>, without
        /// the extension — Android addresses raw resources by name, not by path.
        /// </summary>
        private const string SoundResource = "bildirim_sesi";

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

            string name = Loc.T("bildirim.kanal_ad");
            string description = Loc.T("bildirim.kanal_aciklama");

            AndroidNotificationCenter.DeleteNotificationChannel(RetiredChannelId);

            // Order matters and is the whole trick. The JNI call below is what actually creates the
            // channel, because it is the only path that can attach a sound. Unity's own registration
            // runs afterwards so its bookkeeping knows the channel exists, and it cannot undo the sound:
            // createNotificationChannel on an id that already exists updates the name and description
            // and leaves everything else — sound included — exactly as it was created.
            //
            // If the JNI path fails for any reason we still register through Unity, which gives a
            // working channel with the system default sound rather than no notifications at all.
            CreateChannelWithSound(ChannelId, name, description);

            AndroidNotificationCenter.RegisterNotificationChannel(new AndroidNotificationChannel(
                ChannelId, name, description, Importance.Default)
            {
                EnableVibration = true,
                CanShowBadge = true,
                LockScreenVisibility = LockScreenVisibility.Public
            });
        }

        /// <summary>
        /// Creates the channel straight through the Android SDK so it can be given a sound.
        /// com.unity.mobile.notifications 2.4.3 exposes no sound field on either
        /// <see cref="AndroidNotificationChannel"/> or <see cref="AndroidNotification"/>, so there is no
        /// C# API for this — the channel has to be built the way Java would build it.
        ///
        /// Below API 26 there are no channels at all and this does nothing; the notification carries the
        /// device's default sound, which is the platform behaviour of that era.
        /// </summary>
        private static void CreateChannelWithSound(string id, string name, string description)
        {
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                    if (version.GetStatic<int>("SDK_INT") < 26) return;

                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    string package = activity.Call<string>("getPackageName");
                    using (var manager = activity.Call<AndroidJavaObject>("getSystemService", "notification"))
                    using (var uris = new AndroidJavaClass("android.net.Uri"))
                    using (var uri = uris.CallStatic<AndroidJavaObject>(
                               "parse", "android.resource://" + package + "/raw/" + SoundResource))
                    using (var builder = new AndroidJavaObject("android.media.AudioAttributes$Builder"))
                    // 3 = NotificationManager.IMPORTANCE_DEFAULT — matches Importance.Default above.
                    using (var channel = new AndroidJavaObject("android.app.NotificationChannel", id, name, 3))
                    {
                        // 4 = AudioAttributes.CONTENT_TYPE_SONIFICATION, 5 = USAGE_NOTIFICATION. Without
                        // these the chime is routed as media, so it plays at media volume and keeps
                        // playing when the player has notifications muted but music running.
                        using (builder.Call<AndroidJavaObject>("setContentType", 4)) { }
                        using (builder.Call<AndroidJavaObject>("setUsage", 5)) { }
                        using (var attributes = builder.Call<AndroidJavaObject>("build"))
                            channel.Call("setSound", uri, attributes);

                        channel.Call("setDescription", description);
                        channel.Call("enableVibration", true);
                        channel.Call("setShowBadge", true);
                        channel.Call("setLockscreenVisibility", 1);   // Notification.VISIBILITY_PUBLIC
                        manager.Call("createNotificationChannel", channel);
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Bildirim] Kanal sesle kurulamadi, varsayilan sese dusuluyor: " + e);
            }
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
