using System.Collections;
using Game.Core;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>A receipt for rewards already paid at sea, shown after the island wakes.</summary>
    public sealed class ExpeditionSummaryUI : MonoBehaviour
    {
        private ExpeditionService _sea;
        private CraftingUI _workshop;
        private LocalizationService _loc;
        private Text _title, _wins, _rewards, _next, _workshopLabel, _closeLabel;

        public static void ShowAfterReturn(ExpeditionService sea)
        {
            if (sea == null || !sea.HasTripRewards) return;
            var go = new GameObject("SeferOzeti");
            DontDestroyOnLoad(go);
            var receipt = go.AddComponent<ExpeditionSummaryUI>();
            receipt._sea = sea;
            receipt.StartCoroutine(receipt.WaitForIsland());
        }

        private IEnumerator WaitForIsland()
        {
            while (SceneCurtain.Busy) yield return null;
            if (_sea.Active) { Destroy(gameObject); yield break; }
            _workshop = FindAnyObjectByType<CraftingUI>();
            Build();
            _loc = ServiceLocator.Get<LocalizationService>();
            if (_loc != null) _loc.Changed += Refresh;
            if (_sea.Crafting != null) _sea.Crafting.Changed += Refresh;
            Refresh();
        }

        private void Build()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "SeferOzetiKanvas", 180);
            RectTransform scrim = UiBuild.Flat(canvas, "Karartma", new Color(0.025f, 0.045f, 0.09f, 0.94f), Vector2.zero, Vector2.one);
            scrim.GetComponent<Image>().raycastTarget = true;
            RectTransform safe = UiBuild.Flat(scrim, "GuvenliAlan", Color.clear, Vector2.zero, Vector2.one);
            safe.gameObject.AddComponent<SafeArea>();
            RectTransform card = EtkinlikKit.Card(safe, "Ozet",
                new Vector2(0.08f, 0.18f), new Vector2(0.92f, 0.82f), 1.5f).rectTransform;
            _title = Label(card, "Baslik", 38, 0.85f, 0.97f);
            _title.color = new Color(1f, 0.8f, 0.3f);
            _wins = Label(card, "Zaferler", 26, 0.76f, 0.84f);
            _rewards = Label(card, "Kazanilan", 30, 0.35f, 0.75f);
            _rewards.lineSpacing = 1.6f;
            _next = Label(card, "YeniUretim", 24, 0.23f, 0.34f);
            var workshop = UiBuild.Btn(card, "AtolyeyeDon", string.Empty,
                AtolyeKit.Get("btn_yesil") ?? UiSkin.ButtonGreen, Color.white, 28, OpenWorkshop);
            UiBuild.Anchor((RectTransform)workshop.transform, new Vector2(0.08f, 0.13f), new Vector2(0.92f, 0.22f));
            _workshopLabel = workshop.GetComponentInChildren<Text>();
            PillFit.Wrap(workshop.GetComponent<Image>());
            workshop.interactable = _workshop != null;
            var close = UiBuild.Btn(card, "Devam", string.Empty,
                AtolyeKit.Get("btn_mavi") ?? UiSkin.ButtonBlue, Color.white, 26, Close);
            UiBuild.Anchor((RectTransform)close.transform, new Vector2(0.08f, 0.025f), new Vector2(0.92f, 0.115f));
            _closeLabel = close.GetComponentInChildren<Text>();
            PillFit.Wrap(close.GetComponent<Image>());
        }

        private static Text Label(Transform parent, string name, int size, float bottom, float top)
        {
            Text label = UiBuild.Label(parent, name, string.Empty, size, TextAnchor.MiddleCenter);
            UiBuild.Anchor(label.rectTransform, new Vector2(0.07f, bottom), new Vector2(0.93f, top));
            label.raycastTarget = false;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 14;
            label.resizeTextMaxSize = size;
            return label;
        }

        private void Refresh()
        {
            _title.text = Loc.T("sefer.ozet_baslik");
            _wins.text = string.Format(Loc.T("sefer.ozet_zafer"), _sea.TripWins);
            _rewards.text = CurrencyText.Gain(CurrencyId.CraftPoints, _sea.TripCraftPoints)
                + "\n" + CurrencyText.Gain(CurrencyId.Charts, _sea.TripCharts)
                + "\n" + CurrencyText.Gain(CurrencyId.Salvage, _sea.TripSalvage)
                + "\n" + CurrencyText.Gain(CurrencyId.Pearls, _sea.TripPearls)
                + "\n+" + NumberFormatter.Format(new BigDouble(_sea.TripCash), 0) + " " + CurrencyText.Name(CurrencyId.Cash);
            CraftingService craft = _sea.Crafting;
            _next.text = craft == null ? Loc.T("atolye.nereden") : craft.HasPending
                ? Loc.T("sefer.ozet_bekleyen")
                : string.Format(Loc.T("sefer.ozet_uretim"), craft.Points, craft.Tuning.CraftCost);
            _workshopLabel.text = Loc.T("sefer.ozet_atolye");
            _closeLabel.text = Loc.T("sefer.ozet_devam");
        }

        private void OpenWorkshop()
        {
            if (_workshop == null) return;
            _workshop.Show();
            Close();
        }

        private void Close() => Destroy(gameObject);

        private void OnDestroy()
        {
            if (_loc != null) _loc.Changed -= Refresh;
            if (_sea != null && _sea.Crafting != null) _sea.Crafting.Changed -= Refresh;
        }
    }
}
