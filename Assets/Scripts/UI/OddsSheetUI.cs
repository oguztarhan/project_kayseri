using System.Globalization;
using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The odds sheet, shared by the master chest and the captain crate. Both stores require the
    /// chance of a paid randomised pull to be readable BEFORE the purchase, and the master chest is
    /// paid for in gems, which are sold for money — so the ⓘ that opens this is not decoration.
    ///
    /// Every number printed here comes out of <see cref="Odds"/>, which derives it from the same
    /// tuning struct the roll reads. Nothing on this screen is typed in by hand, which is the only
    /// way an odds sheet stays true across a balance pass.
    ///
    /// A PLAIN CLASS, not a MonoBehaviour, and one per roster screen — the same shape as
    /// <see cref="RosterInspectPanel"/>, for the same reason: it is a layer inside a screen that is
    /// already built in code, and giving it its own component would mean giving it its own canvas.
    /// </summary>
    public sealed class OddsSheetUI
    {
        /// <summary>Five captain grades is the widest table either caller has.</summary>
        private const int MaxRows = 5;

        private static readonly Color Ink = new Color(0.09f, 0.14f, 0.24f, 1f);
        private static readonly Color InkSoft = new Color(0.36f, 0.42f, 0.52f, 1f);

        /// <summary>
        /// The game's decimal separator, not the handset's — the same rule the roster screens follow.
        /// A Turkish phone would otherwise draw "10,5%" here while the card beside it draws "10.5%".
        /// </summary>
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        private readonly RectTransform _overlay;
        private readonly Text _title;
        private readonly RectTransform[] _row = new RectTransform[MaxRows];
        private readonly Text[] _rowLabel = new Text[MaxRows];
        private readonly Text[] _rowValue = new Text[MaxRows];
        private readonly Text _note;
        private int _used;

        public OddsSheetUI(RectTransform parent)
        {
            _overlay = UiBuild.Flat(parent, "OranKarartma", new Color(0.02f, 0.03f, 0.06f, 0.88f),
                                    Vector2.zero, Vector2.one);
            var dismiss = _overlay.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(Hide);

            RectTransform sheet = UiBuild.Box(_overlay, "OranSayfasi", new Color(0.96f, 0.97f, 1f, 1f),
                                              new Vector2(0.20f, 0.12f), new Vector2(0.80f, 0.88f));
            // Stops a tap inside the sheet from reaching the dismiss layer under it.
            var blocker = sheet.gameObject.AddComponent<Button>();
            blocker.transition = Selectable.Transition.None;

            _title = Label(sheet, "Baslik", 34, TextAnchor.MiddleCenter,
                           new Vector2(0.06f, 0.885f), new Vector2(0.94f, 0.965f));

            // Six rows would not fit and five is the widest table either caller has; the band is split
            // evenly so a three-row sheet and a five-row sheet still look like the same screen.
            for (int i = 0; i < MaxRows; i++)
            {
                float top = 0.845f - i * 0.083f;
                _row[i] = Slot(sheet, "Satir" + i, new Vector2(0.06f, top - 0.072f),
                               new Vector2(0.94f, top));
                _rowLabel[i] = Label(_row[i], "Ad", 24, TextAnchor.MiddleLeft,
                                     new Vector2(0.02f, 0f), new Vector2(0.66f, 1f));
                _rowValue[i] = Label(_row[i], "Deger", 24, TextAnchor.MiddleRight,
                                     new Vector2(0.66f, 0f), new Vector2(0.98f, 1f));
            }

            _note = Label(sheet, "Not", 20, TextAnchor.UpperLeft,
                          new Vector2(0.06f, 0.150f), new Vector2(0.94f, 0.415f));
            _note.color = InkSoft;

            Button close = UiBuild.Btn(sheet, "Kapat", Loc.T("lig.kapat"), UiSkin.ButtonGrey,
                                       new Color(0.45f, 0.49f, 0.56f, 1f), 26, Hide);
            UiBuild.Anchor((RectTransform)close.transform,
                           new Vector2(0.34f, 0.035f), new Vector2(0.66f, 0.130f));
            PillFit.Wrap(close.GetComponent<Image>());
            Fit(close.GetComponentInChildren<Text>(), 14, 26);

            _overlay.gameObject.SetActive(false);
        }

        public bool Visible => _overlay != null && _overlay.gameObject.activeSelf;

        // ------------------------------------------------------------------ master chest
        /// <summary>
        /// What a master chest actually does. The directed card is listed apart from the rolled ones
        /// and never folded into a percentage: it is not chance, it always goes to whoever is furthest
        /// behind, and presenting it as a probability would overstate the randomness by a third.
        /// </summary>
        public void ShowMasterChest(in MasterChest.Tuning tuning)
        {
            Begin();
            int rolled = Odds.MasterRolledCards(tuning);
            Row(Loc.T("oran.usta.kart"), MasterChest.CardsFor(1, tuning).ToString(Culture));
            Row(Loc.T("oran.usta.yonlendirilen"), MasterChest.DirectedIn(tuning).ToString(Culture));
            Row(Loc.T("oran.usta.rastgele"), rolled.ToString(Culture));
            Row(Loc.T("oran.usta.her_usta"), Percent(Odds.MasterSlotChance()));

            _note.text = Loc.T("oran.usta.not") + "\n" + Loc.T("oran.not");
            Finish();
        }

        // ------------------------------------------------------------------ captain crate
        /// <summary>
        /// The crate's base table plus its three guarantees in words. Base means "with nothing owed" —
        /// the pity rules bend the numbers, so folding them in would print a percentage that is true
        /// of no particular pull.
        /// </summary>
        public void ShowCaptainCrate(in CaptainCrate.Tuning tuning)
        {
            Begin();
            for (int grade = 0; grade < Captains.GradeCount; grade++)
            {
                double chance = Odds.CaptainGradeChance(grade, tuning);
                // A grade nobody in the roster carries cannot be rolled, so it is not listed. Printing
                // it at 0% would read as a rate we are hiding rather than a rank that does not exist.
                if (chance <= 0d) continue;
                Row(Loc.T("kaptan.derece." + grade.ToString(Culture)), Percent(chance));
            }

            string note = string.Empty;
            if (tuning.EpicPity > 0)
                note += string.Format(Culture, Loc.T("oran.kaptan.garanti"),
                                      Loc.T("kaptan.derece.2"), tuning.EpicPity) + "\n";
            if (tuning.LegendaryPity > 0)
                note += string.Format(Culture, Loc.T("oran.kaptan.garanti"),
                                      Loc.T("kaptan.derece.3"), tuning.LegendaryPity) + "\n";
            if (tuning.SoftPityStart > 0 && tuning.SoftPityStep > 0d)
                note += string.Format(Culture, Loc.T("oran.kaptan.yumusak"),
                                      Loc.T("kaptan.derece.3"), tuning.SoftPityStart,
                                      (tuning.SoftPityStep * 100d).ToString("0.##", Culture)) + "\n";
            _note.text = note + Loc.T("oran.not");
            Finish();
        }

        public void Hide()
        {
            if (_overlay != null) _overlay.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ plumbing
        private void Begin()
        {
            _used = 0;
            _title.text = Loc.T("oran.baslik");
        }

        private void Row(string label, string value)
        {
            if (_used >= MaxRows) return;
            _rowLabel[_used].text = label;
            _rowValue[_used].text = value;
            _row[_used].gameObject.SetActive(true);
            _used++;
        }

        private void Finish()
        {
            for (int i = _used; i < MaxRows; i++) _row[i].gameObject.SetActive(false);
            _overlay.gameObject.SetActive(true);
            _overlay.SetAsLastSibling();
        }

        private static string Percent(double chance)
            => string.Format(Culture, "{0:0.##}%", chance * 100d);

        private static Text Label(RectTransform parent, string name, int size, TextAnchor anchor,
                                  Vector2 min, Vector2 max)
        {
            Text label = UiBuild.Label(Slot(parent, name, min, max), "Text", string.Empty, size, anchor);
            label.color = Ink;
            Fit(label, Mathf.Max(11, size / 2), size);
            return label;
        }

        private static void Fit(Text label, int min, int max)
        {
            AccessibilityConfig accessibility = ServiceLocator.Get<AccessibilityConfig>();
            float scale = accessibility != null ? accessibility.TextScale : 1f;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = Mathf.Max(1, Mathf.RoundToInt(min * scale));
            label.resizeTextMaxSize = Mathf.Max(label.resizeTextMinSize, Mathf.RoundToInt(max * scale));
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private static RectTransform Slot(RectTransform parent, string name, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return UiBuild.Anchor((RectTransform)go.transform, min, max);
        }
    }
}
