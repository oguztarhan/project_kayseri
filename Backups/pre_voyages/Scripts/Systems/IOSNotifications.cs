#if UNITY_IOS
using System;
using Unity.Notifications.iOS;

namespace Game.Systems
{
    /// <summary>Local iOS counterpart of <see cref="AndroidNotifications"/>.</summary>
    public sealed class IOSNotifications : INotifications
    {
        private AuthorizationRequest _authorization;
        private QueryLastRespondedNotificationOp _openQuery;
        private string _consumedIdentifier;

        public IOSNotifications() => RefreshOpenedTarget();

        public void Schedule(LocalNotificationRequest request)
        {
            if (request.AfterSeconds <= 0) return;
            var notification = new iOSNotification
            {
                Identifier = string.IsNullOrEmpty(request.Id) ? Guid.NewGuid().ToString("N") : request.Id,
                Title = request.Title,
                Body = request.Message,
                Data = request.Target ?? string.Empty,
                ShowInForeground = false,
                SoundName = "default",
                Trigger = new iOSNotificationTimeIntervalTrigger
                {
                    TimeInterval = TimeSpan.FromSeconds(request.AfterSeconds),
                    Repeats = false
                }
            };
            iOSNotificationCenter.ScheduleNotification(notification);
        }

        public void CancelAll()
        {
            iOSNotificationCenter.RemoveAllScheduledNotifications();
            iOSNotificationCenter.RemoveAllDeliveredNotifications();
            iOSNotificationCenter.ApplicationBadge = 0;
        }

        public void RequestPermission()
        {
            if (_authorization != null) return;
            _authorization = new AuthorizationRequest(
                AuthorizationOption.Alert | AuthorizationOption.Sound, false);
        }

        public void RefreshOpenedTarget()
        {
            if (_openQuery == null) _openQuery = iOSNotificationCenter.QueryLastRespondedNotification();
        }

        public string PollOpenedTarget()
        {
            if (_authorization != null && _authorization.IsFinished)
            {
                _authorization.Dispose();
                _authorization = null;
            }

            if (_openQuery == null || _openQuery.keepWaiting) return null;
            iOSNotification notification = _openQuery.Notification;
            _openQuery = null;
            if (notification == null || notification.Identifier == _consumedIdentifier) return null;
            _consumedIdentifier = notification.Identifier;
            return string.IsNullOrEmpty(notification.Data) ? null : notification.Data;
        }
    }
}
#endif
