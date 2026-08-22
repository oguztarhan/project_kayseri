using Game.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.EditorTools
{
    /// <summary>
    /// Sets every UI panel's landscape strategy, and rebuilds the premium store's scroll page into the
    /// two-column shape a landscape screen wants.
    ///
    /// The strategies themselves live in <see cref="LetterboxRoot"/> and are chosen per screen: which
    /// one a panel wants depends on how its content is arranged, and that is not something the component
    /// can work out for itself without guessing. This is a menu item rather than hand-edited prefabs so
    /// the choice is written down, reviewable, and can be re-run after any of these panels is re-exported.
    ///
    /// Safe to run more than once — the store rebuild checks for its own output first.
    /// </summary>
    public static class LandscapeLayoutTool
    {
        private const string Root = "Assets/Prefabs/UI/";

        /// <summary>
        /// Which panel gets which strategy. These are the same choices <see cref="LandscapeFit.Auto"/>
        /// arrives at by measuring, written down so a re-export cannot quietly change one, and so the
        /// Inspector says what a screen is doing without having to run it.
        /// </summary>
        private static readonly (string file, LandscapeFit fit)[] Panels =
        {
            // One tall stack — title, model, phase bar, upgrade tray — so it folds into two columns and
            // the tray lands beside the model instead of under it. This is the screen that drove all of this.
            ("UI_IstasyonEkrani", LandscapeFit.TwoColumn),

            // Title strip and close button to the left of the contract card, which is one solid block.
            ("UI_Kontrat", LandscapeFit.TwoColumn),

            // One edge-anchored scroll view. Nothing to letterbox: the sheet becomes the screen.
            ("UI_Magaza", LandscapeFit.Stretch),

            // The rest are a card holding a tall stack of rows. Folding the rows and rebuilding the card
            // around them is what actually buys the width — fitting the card alone only buys its margins.
            ("UI_Harita", LandscapeFit.InnerColumns),
            ("UI_Ayarlar", LandscapeFit.InnerColumns),
            ("UI_GunlukOdul", LandscapeFit.InnerColumns),
            ("UI_HosGeldin", LandscapeFit.InnerColumns),
            ("UI_Reklam", LandscapeFit.InnerColumns),
            ("UI_Teklif", LandscapeFit.InnerColumns),
        };

        // The store page, laid out sideways: two offer cards abreast, and the pack grids twice as wide.
        private const int StoreOfferColumns = 2;
        private const int StorePackColumns = 6;
        private const string OfferRowPrefix = "SatirTeklif";

        [MenuItem("Tools/Kayseri/UI/Apply Landscape Layout")]
        public static void Apply()
        {
            int done = 0;
            for (int i = 0; i < Panels.Length; i++)
            {
                string path = Root + Panels[i].file + ".prefab";
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                if (root == null) { Debug.LogWarning("Landscape: missing " + path); continue; }

                bool changed = SetFit(root, Panels[i].fit, Panels[i].file);
                if (Panels[i].file == "UI_Magaza") changed |= RebuildStorePage(root);

                if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
                if (changed) done++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log("Landscape layout applied to " + done + " of " + Panels.Length + " UI prefabs.");
        }

        private static bool SetFit(GameObject root, LandscapeFit fit, string label)
        {
            var letterbox = root.GetComponentInChildren<LetterboxRoot>(true);
            if (letterbox == null) { Debug.LogWarning("Landscape: no LetterboxRoot in " + label); return false; }

            var so = new SerializedObject(letterbox);
            so.FindProperty("landscapeFit").enumValueIndex = (int)fit;

            // Wired rather than left to the runtime fallback, so the Inspector says which nodes the
            // strategy is measuring and folding.
            Transform frame = FindSafeAreaChild(letterbox.transform) ?? letterbox.transform;
            so.FindProperty("content").objectReferenceValue = frame == letterbox.transform ? null : frame;
            so.FindProperty("card").objectReferenceValue = fit == LandscapeFit.InnerColumns ? FindCard(frame) : null;

            bool changed = so.ApplyModifiedPropertiesWithoutUndo();
            if (changed) EditorUtility.SetDirty(letterbox);
            return changed;
        }

        private static RectTransform FindSafeAreaChild(Transform letterbox)
        {
            for (int i = 0; i < letterbox.childCount; i++)
            {
                Transform child = letterbox.GetChild(i);
                if (child.GetComponent<SafeArea>() != null) return child as RectTransform;
            }
            return null;
        }

        /// <summary>
        /// The card holding the rows — the block with the most children. That is the panel body on every
        /// one of these screens, never the title strip, the glow or the close button.
        /// </summary>
        private static RectTransform FindCard(Transform frame)
        {
            RectTransform best = null;
            int most = 1;
            for (int i = 0; i < frame.childCount; i++)
            {
                var child = frame.GetChild(i) as RectTransform;
                if (child == null || child.childCount <= most) continue;
                if (child.anchorMin == Vector2.zero && child.anchorMax == Vector2.one) continue;
                most = child.childCount;
                best = child;
            }
            return best;
        }

        // ---------- store ----------

        /// <summary>
        /// Turns the store's single column of cards into a landscape page: the four offer cards pair off
        /// into two rows, the three pack grids go from three columns to six, and the scroll view is
        /// re-inset for a short wide screen instead of a tall narrow one.
        /// </summary>
        private static bool RebuildStorePage(GameObject root)
        {
            var scroll = root.GetComponentInChildren<ScrollRect>(true);
            if (scroll == null || scroll.content == null) { Debug.LogWarning("Landscape: store has no ScrollRect"); return false; }

            RectTransform content = scroll.content;
            if (content.Find(OfferRowPrefix + "0") != null) return false;   // already rebuilt

            float pageWidth = StorePackColumns * PackCell(content).x
                              + (StorePackColumns - 1) * PackSpacing(content).x;

            PairOffCards(content, pageWidth);
            WidenGrids(content, pageWidth);
            InsetScrollForLandscape(scroll.GetComponent<RectTransform>());
            return true;
        }

        private static Vector2 PackCell(RectTransform content)
        {
            var grid = content.GetComponentInChildren<GridLayoutGroup>(true);
            return grid != null ? grid.cellSize : new Vector2(280f, 360f);
        }

        private static Vector2 PackSpacing(RectTransform content)
        {
            var grid = content.GetComponentInChildren<GridLayoutGroup>(true);
            return grid != null ? grid.spacing : new Vector2(26f, 32f);
        }

        /// <summary>Puts the offer cards two abreast, in rows that keep their authored order.</summary>
        private static void PairOffCards(RectTransform content, float pageWidth)
        {
            var cards = new System.Collections.Generic.List<RectTransform>();
            for (int i = 0; i < content.childCount; i++)
            {
                var child = content.GetChild(i) as RectTransform;
                if (child != null && child.name.StartsWith("Teklif")) cards.Add(child);
            }
            if (cards.Count == 0) return;

            float cardWidth = cards[0].sizeDelta.x;
            float cardHeight = cards[0].sizeDelta.y;
            float gap = Mathf.Max(0f, (pageWidth - StoreOfferColumns * cardWidth) / (StoreOfferColumns - 1));

            int firstSibling = cards[0].GetSiblingIndex();
            for (int i = 0; i < cards.Count; i += StoreOfferColumns)
            {
                var row = new GameObject(OfferRowPrefix + (i / StoreOfferColumns), typeof(RectTransform));
                var rowRect = (RectTransform)row.transform;
                rowRect.SetParent(content, false);
                rowRect.anchorMin = rowRect.anchorMax = rowRect.pivot = new Vector2(0.5f, 0.5f);
                rowRect.sizeDelta = new Vector2(pageWidth, cardHeight);
                rowRect.SetSiblingIndex(firstSibling + i / StoreOfferColumns);

                var layout = row.AddComponent<HorizontalLayoutGroup>();
                layout.spacing = gap;
                layout.childAlignment = TextAnchor.MiddleCenter;
                layout.childControlWidth = false;
                layout.childControlHeight = false;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;

                for (int c = 0; c < StoreOfferColumns && i + c < cards.Count; c++)
                    cards[i + c].SetParent(rowRect, false);
            }
        }

        /// <summary>
        /// Widens the pack grids and their headers to the page, and shortens each grid by however many
        /// rows the extra columns saved. Row count is read back out of the authored height rather than
        /// assumed, so a grid that gains or loses packs stays right.
        /// </summary>
        private static void WidenGrids(RectTransform content, float pageWidth)
        {
            for (int i = 0; i < content.childCount; i++)
            {
                var child = content.GetChild(i) as RectTransform;
                if (child == null) continue;

                var grid = child.GetComponent<GridLayoutGroup>();
                if (grid == null)
                {
                    if (child.name.StartsWith("Baslik"))
                        child.sizeDelta = new Vector2(pageWidth, child.sizeDelta.y);
                    continue;
                }

                float step = grid.cellSize.y + grid.spacing.y;
                int wasRows = Mathf.Max(1, Mathf.RoundToInt((child.sizeDelta.y + grid.spacing.y) / step));
                int cells = wasRows * grid.constraintCount;

                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = StorePackColumns;

                int rows = Mathf.Max(1, Mathf.CeilToInt(cells / (float)StorePackColumns));
                child.sizeDelta = new Vector2(pageWidth, rows * grid.cellSize.y + (rows - 1) * grid.spacing.y);
            }
        }

        /// <summary>
        /// Re-insets the scroll view for a short wide screen. The authored insets left 400 units clear
        /// above the list for a portrait header; on a 1080-tall sheet that is more than a third of the
        /// screen given to nothing.
        /// </summary>
        private static void InsetScrollForLandscape(RectTransform view)
        {
            if (view == null) return;
            view.anchorMin = Vector2.zero;
            view.anchorMax = Vector2.one;
            view.offsetMin = new Vector2(84f, 30f);      // left, bottom
            view.offsetMax = new Vector2(-84f, -200f);   // right, top — clears the close button
        }
    }
}
