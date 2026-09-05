using System.Collections.Generic;
using Game.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.EditorTools
{
    /// <summary>
    /// Undoes <see cref="LandscapeLayoutTool"/>'s store rebuild, for the portrait build.
    ///
    /// Everything else reverts on its own: <see cref="LetterboxRoot.Resolve"/> tests the aspect
    /// FIRST and returns <see cref="LandscapeFit.Uniform"/> for any portrait screen, before it
    /// ever looks at the serialised <c>landscapeFit</c>. So the nine panels the landscape tool
    /// tagged render at their authored 1080x2340 with no change at all, and those tags are left
    /// alone here so the landscape build still works if it is ever wanted back.
    ///
    /// The store is the exception. <c>RebuildStorePage</c> did not set a mode, it restructured the
    /// prefab: it wrapped the offer cards in two-abreast rows and widened the pack grids from
    /// three columns to six. On a 1080-wide portrait sheet six 280px cells plus spacing is 1810px
    /// of content in a 1080px sheet, so the page overflows badly until this is run.
    ///
    /// Safe to run more than once.
    /// </summary>
    public static class PortraitLayoutTool
    {
        private const string StorePath = "Assets/Prefabs/UI/UI_Magaza.prefab";
        private const string OfferRowPrefix = "SatirTeklif";

        // The authored portrait page: one offer card per row, three packs across.
        private const int PackColumns = 3;

        // Read out of the prefab as it stood at 0b2af2c^, the last commit before the landscape
        // rebuild. Only the top inset was changed (-400 -> -200); the other three were untouched,
        // and are restated here so the whole rect is set from one place.
        private static readonly Vector2 PortraitOffsetMin = new Vector2(84f, 30f);
        private static readonly Vector2 PortraitOffsetMax = new Vector2(-84f, -400f);

        [MenuItem("Tools/Kayseri/UI/Apply Portrait Layout")]
        public static void Apply()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(StorePath);
            if (root == null) { Debug.LogError("Portrait: missing " + StorePath); return; }

            bool changed = RebuildStorePage(root);
            if (changed) PrefabUtility.SaveAsPrefabAsset(root, StorePath);
            PrefabUtility.UnloadPrefabContents(root);

            AssetDatabase.SaveAssets();
            Debug.Log(changed
                ? "Portrait layout: store page rebuilt for a 1080-wide sheet."
                : "Portrait layout: store page was already portrait, nothing to do.");
        }

        private static bool RebuildStorePage(GameObject root)
        {
            var scroll = root.GetComponentInChildren<ScrollRect>(true);
            if (scroll == null || scroll.content == null)
            {
                Debug.LogWarning("Portrait: store has no ScrollRect");
                return false;
            }

            RectTransform content = scroll.content;
            bool paired = content.Find(OfferRowPrefix + "0") != null;
            bool widened = false;
            for (int i = 0; i < content.childCount && !widened; i++)
            {
                var g = content.GetChild(i).GetComponent<GridLayoutGroup>();
                if (g != null && g.constraintCount != PackColumns) widened = true;
            }
            if (!paired && !widened) return false;   // already portrait

            float pageWidth = PackColumns * PackCell(content).x
                              + (PackColumns - 1) * PackSpacing(content).x;

            UnpairOfferCards(content);
            NarrowGrids(content, pageWidth);
            InsetScrollForPortrait(scroll.GetComponent<RectTransform>());
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

        /// <summary>
        /// Lifts the offer cards back out of their two-abreast rows into the content column, in the
        /// order they were authored, and deletes the now-empty rows.
        /// </summary>
        private static void UnpairOfferCards(RectTransform content)
        {
            var rows = new List<RectTransform>();
            for (int i = 0; i < content.childCount; i++)
            {
                var child = content.GetChild(i) as RectTransform;
                if (child != null && child.name.StartsWith(OfferRowPrefix)) rows.Add(child);
            }
            if (rows.Count == 0) return;

            int at = rows[0].GetSiblingIndex();
            for (int r = 0; r < rows.Count; r++)
            {
                // ToArray first: SetParent mutates the child list being walked.
                var cards = new List<RectTransform>();
                for (int c = 0; c < rows[r].childCount; c++)
                    cards.Add(rows[r].GetChild(c) as RectTransform);

                for (int c = 0; c < cards.Count; c++)
                {
                    if (cards[c] == null) continue;
                    cards[c].SetParent(content, false);
                    cards[c].SetSiblingIndex(at);
                    at++;
                }
                Object.DestroyImmediate(rows[r].gameObject);
            }
        }

        /// <summary>
        /// Three columns again, with the row count re-derived from how many cells the grid actually
        /// holds — read back out of the current height the same way the landscape tool read it, so a
        /// grid that gained or lost packs since still comes out right.
        /// </summary>
        private static void NarrowGrids(RectTransform content, float pageWidth)
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
                grid.constraintCount = PackColumns;

                int rows = Mathf.Max(1, Mathf.CeilToInt(cells / (float)PackColumns));
                child.sizeDelta = new Vector2(
                    pageWidth, rows * grid.cellSize.y + (rows - 1) * grid.spacing.y);
            }
        }

        /// <summary>Puts back the 400 units of clear space the portrait header sits in.</summary>
        private static void InsetScrollForPortrait(RectTransform view)
        {
            if (view == null) return;
            view.anchorMin = Vector2.zero;
            view.anchorMax = Vector2.one;
            view.offsetMin = PortraitOffsetMin;
            view.offsetMax = PortraitOffsetMax;
        }
    }
}
