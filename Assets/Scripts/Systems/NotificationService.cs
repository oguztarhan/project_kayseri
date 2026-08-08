using System;
using Game.Core;
using Game.Data;

namespace Game.Systems
{
    /// <summary>
    /// Turns <see cref="NotificationPlan"/>'s bare slots into the lines the player actually reads, and
    /// hands them to the platform. Called as the app goes to background and cancelled when it returns
    /// — see <see cref="GameBootstrap"/>.
    ///
    /// The money in the text is the player's WHOLE VAULT at the moment the line fires: what is in the
    /// wallet now, plus everything the mine will have earned by then. Not the offline haul on its own —
    /// early in a run the wallet is the larger half of it, so a figure that counted only the absence
    /// read as a smaller sum than the player knows they own.
    ///
    /// Neither half is a guess. Offline income is fully determined the moment the player leaves: the
    /// rate is frozen, the efficiency and cap are known, and the boost either has time left on it or
    /// does not. The accrued half comes from the same <see cref="OfflineEarnings.ComputeTotal"/> call
    /// the welcome-back screen pays out of, so the notification and the screen can never disagree.
    ///
    /// Everything is rebuilt from scratch on every background, which is also why nothing here has to
    /// care about the player changing language: the queue is only ever hours old, and it is written in
    /// whatever language was current when it was written.
    /// </summary>
    public sealed class NotificationService
    {
        private readonly SaveData _data;
        private readonly OfflineConfig _config;
        private readonly TimeService _time;
        private readonly INotifications _sink;
        private readonly int _testSpacing;
        private readonly NotificationSlot[] _slots = new NotificationSlot[NotificationPlan.MaxSlots];

        public NotificationService(SaveData data, OfflineConfig config, TimeService time,
                                   INotifications sink, int testSpacingSeconds = 0)
        {
            _data = data;
            _config = config;
            _time = time;
            _sink = sink;
            _testSpacing = testSpacingSeconds;
        }

        /// <summary>Queues the whole absence. Replaces anything already queued.</summary>
        public void ScheduleAway()
        {
            if (_sink == null) return;
            _sink.CancelAll();
            if (_data == null || _time == null) return;

            double efficiency = (_config != null ? _config.Efficiency : 0d) + _data.offlineEfficiencyBonus;
            if (efficiency > 1d) efficiency = 1d;
            long cap = (_config != null ? _config.CapSeconds : 0L) + _data.offlineCapBonusSeconds;

            // A brand-new player has no measured rate yet, and a build with offline earning switched off
            // has nothing to promise. Both fall back to lines that quote no figure at all, rather than
            // inviting the player back to collect $0.
            bool pays = _config != null && _config.Enabled && efficiency > 0d && _data.incomeRatePerSec > 0d;
            long boostLeft = _data.boostEndUnix - _time.NowUnix();

            int count = NotificationPlan.Build(DateTime.Now, cap, _slots, _testSpacing);
            if (_testSpacing > 0)
                UnityEngine.Debug.LogWarning($"[Bildirim] TEST MODU acik: {count} bildirim " +
                                             $"{_testSpacing} saniye arayla gidecek. Yayina cikmadan " +
                                             "GameBootstrap'taki test araligini 0 yap.");

            for (int i = 0; i < count; i++)
            {
                NotificationKind kind = _slots[i].Kind;

                string money = null;
                if (pays)
                {
                    // Sized from AwaySeconds, not from when it fires: under the test spacing those
                    // differ, and the figure the line quotes is the one the player would really be
                    // coming back to.
                    BigDouble total = OfflineEarnings.ComputeTotal(
                        new BigDouble(_data.incomeRatePerSec), _slots[i].AwaySeconds, efficiency, cap,
                        _data.boostMultiplier, boostLeft);
                    if (_data.wallet != null) total = total + _data.wallet.cash;
                    if (total.Mantissa > 0d) money = "$" + NumberFormatter.Format(total);
                }

                _sink.Schedule(Loc.T(TitleKey(kind)), Body(kind, money), _slots[i].AfterSeconds);
            }
        }

        /// <summary>Drops the queue. The absence it described is over.</summary>
        public void Cancel()
        {
            if (_sink != null) _sink.CancelAll();
        }

        /// <summary>Asks for the OS permission, once. Timing is the caller's call — see the interface.</summary>
        public void RequestPermission()
        {
            if (_sink != null) _sink.RequestPermission();
        }

        private static string TitleKey(NotificationKind kind)
        {
            switch (kind)
            {
                case NotificationKind.Filling: return "bildirim.dolduruyor_baslik";
                case NotificationKind.FillingLate: return "bildirim.dolduruyor_gec_baslik";
                case NotificationKind.Full: return "bildirim.doldu_baslik";
                case NotificationKind.Idle: return "bildirim.bosta_baslik";
                case NotificationKind.NewDay: return "bildirim.yenigun_baslik";
                default: return "bildirim.donus_baslik";
            }
        }

        private static string Body(NotificationKind kind, string money)
        {
            switch (kind)
            {
                case NotificationKind.Filling:
                    return money == null ? Loc.T("bildirim.dolduruyor_sade")
                                         : string.Format(Loc.T("bildirim.dolduruyor"), money);
                case NotificationKind.FillingLate:
                    return money == null ? Loc.T("bildirim.dolduruyor_gec_sade")
                                         : string.Format(Loc.T("bildirim.dolduruyor_gec"), money);
                case NotificationKind.Full:
                    return money == null ? Loc.T("bildirim.doldu_sade")
                                         : string.Format(Loc.T("bildirim.doldu"), money);
                case NotificationKind.Idle: return Loc.T("bildirim.bosta");
                case NotificationKind.NewDay: return Loc.T("bildirim.yenigun");
                default: return Loc.T("bildirim.donus");
            }
        }
    }
}
