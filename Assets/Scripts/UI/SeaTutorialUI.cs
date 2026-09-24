using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay;
using Game.Systems;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The sea's lessons, taught out at sea: the first fight (DÜŞMAN ARA, then SAVAŞ! on the sighting),
    /// the first win's spoils, and the first boss. The sail button that leads here is taught on the
    /// island by <see cref="TutorialUI"/>; everything after it has to live in this scene, because the
    /// island — HUD, tutorial and all — is parked while the ship is out.
    ///
    /// Same guide as on the island (Max, his card, the ring), borrowed through
    /// <see cref="TutorialUI.CreateGuide"/>, and the same rules as the island's later lessons: only
    /// after the basics, once each, never blocking, and passed over for a save that shows the player
    /// has done it already. The ids are the ones the old sea hints wrote, so a hint seen before counts.
    /// </summary>
    public sealed class SeaTutorialUI : MonoBehaviour
    {
        [Tooltip("Denize çıkınca ilk kartın gelmesinden önceki bekleme: sahne otursun, oyuncu etrafa baksın.")]
        [SerializeField, Min(0f)] private float settleSeconds = 1.5f;
        [Tooltip("Oyuncu dokunmazsa DEVAM'ın çıkmasından önceki bekleme.")]
        [SerializeField, Min(3f)] private float patienceSeconds = 8f;
        [Tooltip("Ganimet kartının ekranda kalma süresi. DEVAM ile daha önce kapanır.")]
        [SerializeField, Min(2f)] private float rewardSeconds = 7f;
        [Tooltip("İki deniz kartı arasında en az bu kadar saniye geçer.")]
        [SerializeField, Min(0f)] private float gapSeconds = 4f;
        [Tooltip("Patron dersi, oyuncu bu kadar savaş kazandıktan sonra gelir: ilk seferde savaş, ganimet "
                 + "ve patron üst üste gelmesin, önce sıradan savaşları tanısın.")]
        [SerializeField, Min(0)] private int bossAfterWins = 3;

        private SeaFightUI _ui;
        private EncounterController _fights;
        private ExpeditionService _sea;
        private SaveData _data;
        private SaveService _save;
        private AudioService _audio;
        private HapticService _haptic;
        private TutorialPresenter _view;
        private TutorialProgress _progress;
        private List<string> _progressList;

        private bool _busy, _tapped, _offered;
        private float _next;
        private int _seenStamp;
        private bool _wonBefore, _rewardOwed;
        private RectTransform _target;
        private bool _hasTarget, _cardAway;
        private Coroutine _hiding;
        private string _title, _body;

        private bool _covered;
        private float _coverCheckAt;
        private PointerEventData _probe;
        private readonly List<RaycastResult> _hits = new List<RaycastResult>();

        public void Init(SeaFightUI ui, EncounterController fights)
        {
            _ui = ui;
            _fights = fights;
            _sea = ServiceLocator.Get<ExpeditionService>();
            _data = ServiceLocator.Get<SaveData>();
            _save = ServiceLocator.Get<SaveService>();
            _audio = ServiceLocator.Get<AudioService>();
            _haptic = ServiceLocator.Get<HapticService>();
            _seenStamp = _fights != null ? _fights.Stamp : 0;
            // Won out here before this visit: the spoils need no introduction.
            _wonBefore = _data != null && _data.seaFightsWon > 0;
            _next = settleSeconds;
        }

        private void Update()
        {
            if (_fights == null || _data == null) return;
            if (_fights.Stamp != _seenStamp)
            {
                _seenStamp = _fights.Stamp;
                if (_fights.LastWon) _rewardOwed = true;
            }
            if (_busy) return;
            _next -= Time.unscaledDeltaTime;
            if (_next > 0f) return;
            _next = 0.5f;

            if (_data.tutorialStep < TutorialProgress.StepDone || _sea == null || !_sea.Active) return;
            if (_fights.State != EncounterController.Phase.Idle || _ui.Toasting) return;

            if (Intro(TutorialProgress.SeaFightLesson, _sea.Energy > 0, _data.seaFightsWon > 0))
            {
                StartCoroutine(FightLesson());
                return;
            }
            if (_rewardOwed && Intro(TutorialProgress.SeaRewardLesson, true, _wonBefore))
            {
                StartCoroutine(RewardLesson());
                return;
            }
            RectTransform boss = _ui.BossRect(0);
            bool bossDue = _data.seaFightsWon >= bossAfterWins && _sea.Energy > 0 && _fights.BossAvailable(0) && !_fights.BossDefeated(0)
                           && boss != null && boss.gameObject.activeInHierarchy;
            if (Intro(TutorialProgress.SeaBossLesson, bossDue, BossBeaten()))
                StartCoroutine(BossLesson());
        }

        /// <summary>
        /// The island's rule: decided, and when it is to be shown, written down at once — leaving the
        /// sea mid-card does not owe it again. Not decided at all without a guide to show it with.
        /// </summary>
        private bool Intro(string id, bool relevant, bool used)
        {
            TutorialProgress progress = Progress();
            if (progress.Has(id)) return false;
            if (!used && relevant && !EnsureGuide()) return false;
            TutorialProgress.Intro decision = progress.DecideIntro(id, relevant, used);
            if (decision == TutorialProgress.Intro.Show)
            {
                progress.Complete(id);
                _save?.Save(_data);
                return true;
            }
            if (progress.Has(id)) _save?.Save(_data);
            return false;
        }

        private TutorialProgress Progress()
        {
            if (_data.tutorialTipsSeen == null) _data.tutorialTipsSeen = new List<string>();
            if (_progress == null || _progressList != _data.tutorialTipsSeen || _progress.Step != _data.tutorialStep)
            {
                _progressList = _data.tutorialTipsSeen;
                _progress = new TutorialProgress(_progressList, _data.tutorialStep);
            }
            return _progress;
        }

        private bool BossBeaten()
        {
            List<StageBossState> bosses = _data.stageBosses;
            if (bosses == null) return false;
            for (int i = 0; i < bosses.Count; i++)
            {
                StageBossState state = bosses[i];
                if (state != null && (state.firstDefeated || state.secondDefeated || state.legacyCleared)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ the lessons

        /// <summary>DÜŞMAN ARA, then SAVAŞ! once something is sighted. Over when a fight begins.</summary>
        private IEnumerator FightLesson()
        {
            Begin(TutorialProgress.SeaFightLesson);
            Point(_ui.SearchRect, true);
            Card();
            _tapped = false;
            float waited = 0f;
            while (true)
            {
                EncounterController.Phase phase = _fights.State;
                if (phase == EncounterController.Phase.Fight || phase == EncounterController.Phase.Sunk
                    || phase == EncounterController.Phase.Driven || phase == EncounterController.Phase.Loot)
                {
                    Done();
                    break;
                }
                if (_offered && _tapped) break;
                // The sighting's card is what the player decides on — the foe, the odds, the spoils.
                // Max steps off it and leaves the ring on SAVAŞ!; back on DÜŞMAN ARA he returns.
                bool sighted = phase == EncounterController.Phase.Found;
                Point(phase == EncounterController.Phase.Idle ? _ui.SearchRect : sighted ? _ui.FightRect : null,
                      !sighted);
                if (Hold()) { yield return null; continue; }
                Offer(ref waited);
                yield return null;
            }
            yield return End();
        }

        /// <summary>After the first win's banner and any drop decision: what the spoils are for.</summary>
        private IEnumerator RewardLesson()
        {
            Begin(TutorialProgress.SeaRewardLesson);
            _offered = true;
            Card();
            _tapped = false;
            float shown = 0f;
            while (shown < rewardSeconds && !_tapped && _fights.State == EncounterController.Phase.Idle)
            {
                shown += Time.unscaledDeltaTime;
                yield return null;
            }
            yield return End();
        }

        /// <summary>The first boss button. Over when the player starts anything — the boss or not.</summary>
        private IEnumerator BossLesson()
        {
            Begin(TutorialProgress.SeaBossLesson);
            Point(_ui.BossRect(0), true);
            Card();
            _tapped = false;
            float waited = 0f;
            while (true)
            {
                if (_fights.State != EncounterController.Phase.Idle)
                {
                    Done();
                    break;
                }
                if (_offered && _tapped) break;
                Point(_ui.BossRect(0), true);
                if (Hold()) { yield return null; continue; }
                Offer(ref waited);
                yield return null;
            }
            yield return End();
        }

        private void Begin(string id)
        {
            _busy = true;
            _offered = false;
            _hasTarget = false;
            _target = null;
            _cardAway = false;
            _title = Loc.T(TutorialProgress.TextKey(id, true));
            _body = Loc.T(TutorialProgress.TextKey(id, false));
            _view.SetVisible(true);
            _view.BlockInput(false);
            _view.SetShadeActive(false);
            _view.SetPips(0, 0);
            _view.ClearTarget();
            _view.SetRing(false, false);
            Sound(SoundId.PanelOpen);
        }

        private IEnumerator End()
        {
            yield return _view.HideCard();
            _view.Clear();
            _tapped = false;
            _offered = false;
            _next = gapSeconds;
            _busy = false;
        }

        private void Done()
        {
            Sound(SoundId.Tick);
            if (_haptic != null) _haptic.Light();
        }

        private void Offer(ref float waited)
        {
            if (_offered) return;
            waited += Time.unscaledDeltaTime;
            if (waited < patienceSeconds) return;
            _offered = true;
            _view.ShowContinue(true);
        }

        /// <summary>
        /// Rings the button the lesson is about. The card picks its side against the target it is
        /// shown with, so a new target puts it up again rather than leave it over the button — or,
        /// with <paramref name="card"/> false, takes it down and leaves the ring to speak alone.
        /// </summary>
        private void Point(RectTransform want, bool card)
        {
            if (want != null && !want.gameObject.activeInHierarchy) want = null;
            if (_hasTarget && want == _target) return;
            _hasTarget = true;
            _target = want;
            if (want != null) _view.TargetUi(want);
            else _view.ClearTarget();
            _view.SetRing(want != null, true);
            if (!card)
            {
                if (_view.CardShowing) _hiding = StartCoroutine(_view.HideCard());
                _cardAway = true;
            }
            else if (_view.CardShowing || _cardAway) Card();
            _coverCheckAt = 0f;
        }

        private void Card()
        {
            if (_hiding != null) StopCoroutine(_hiding);
            _hiding = null;
            _cardAway = false;
            _view.ShowCard(_title, _body, null, null, _offered, false);
        }

        /// <summary>
        /// Something the player opened lies over the button the lesson points at: the guide steps
        /// aside until it is gone. Asked of the raycasters at the button itself, five times a second.
        /// </summary>
        private bool Hold()
        {
            bool covered = Covered();
            _view.SetSuspended(covered);
            return covered;
        }

        private bool Covered()
        {
            if (_target == null) return false;
            if (Time.unscaledTime < _coverCheckAt) return _covered;
            _coverCheckAt = Time.unscaledTime + 0.2f;
            _covered = false;
            EventSystem events = EventSystem.current;
            if (events == null) return false;
            if (_probe == null) _probe = new PointerEventData(events);
            _probe.position = RectTransformUtility.WorldToScreenPoint(null, _target.TransformPoint(_target.rect.center));
            _hits.Clear();
            events.RaycastAll(_probe, _hits);
            Transform own = _view.transform;
            for (int i = 0; i < _hits.Count; i++)
            {
                GameObject hit = _hits[i].gameObject;
                if (hit == null || !(_hits[i].module is GraphicRaycaster)) continue;
                if (hit.transform.IsChildOf(own)) continue;
                _covered = !hit.transform.IsChildOf(_target);
                break;
            }
            return _covered;
        }

        private bool EnsureGuide()
        {
            if (_view != null) return true;
            _view = TutorialUI.CreateGuide();
            if (_view == null) return false;
            // Created in whichever scene is active; it belongs to the sea and goes when the sea does.
            SceneManager.MoveGameObjectToScene(_view.gameObject, gameObject.scene);
            _view.Continued += OnContinue;
            return true;
        }

        private void OnContinue()
        {
            if (!_offered) return;
            _tapped = true;
            Sound(SoundId.Tap);
            if (_haptic != null) _haptic.Light();
        }

        private void Sound(SoundId id)
        {
            if (_audio != null) _audio.Play(id);
        }

        private void OnDestroy()
        {
            if (_view != null) Destroy(_view.gameObject);
        }
    }
}
