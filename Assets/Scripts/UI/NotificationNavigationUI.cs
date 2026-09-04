using Game.Core;
using Game.Systems;
using UnityEngine;

namespace Game.UI
{
    /// <summary>Consumes platform notification taps after Main's UI is ready.</summary>
    public sealed class NotificationNavigationUI : MonoBehaviour
    {
        private NotificationService _notifications;
        private string _pending;

        private void Start()
        {
            _notifications = ServiceLocator.Get<NotificationService>();
            _notifications?.RefreshOpenedTarget();
        }

        private void Update()
        {
            if (_notifications == null) _notifications = ServiceLocator.Get<NotificationService>();
            if (_notifications == null) return;
            string opened = _notifications.PollOpenedTarget();
            if (!string.IsNullOrEmpty(opened)) _pending = opened;
            if (string.IsNullOrEmpty(_pending)) return;

            if (_pending == "contract")
            {
                ContractUI contracts = FindAnyObjectByType<ContractUI>(FindObjectsInactive.Include);
                if (contracts == null) return;
                contracts.Open();
                _pending = null;
                return;
            }

            if (_pending == "goals" || _pending.StartsWith("goals:", System.StringComparison.Ordinal))
            {
                GoalsUI goals = FindAnyObjectByType<GoalsUI>(FindObjectsInactive.Include);
                if (goals == null) return;
                goals.Show(_pending);
                _pending = null;
                return;
            }

            const string prefix = "island:";
            if (!_pending.StartsWith(prefix, System.StringComparison.Ordinal))
            {
                _pending = null;
                return;
            }
            IslandMapUI map = FindAnyObjectByType<IslandMapUI>(FindObjectsInactive.Include);
            if (map != null && map.TravelToIsland(_pending.Substring(prefix.Length))) _pending = null;
        }
    }
}
