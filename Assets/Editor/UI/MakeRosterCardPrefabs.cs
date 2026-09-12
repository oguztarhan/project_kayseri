// Editor-only builder for the two roster card prefabs. Run once from
// Tools/Kayseri/UI/Build Roster Card Prefabs; the prefabs are the deliverable, not this file —
// but it stays so the layout can be regenerated instead of re-hand-wired.
using Game.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.EditorTools
{
    public static class MakeRosterCardPrefabs
    {
        private const string Folder = "Assets/Prefabs/UI";
        private const string MasterPath = Folder + "/UI_UstaKarti.prefab";
        private const string CaptainPath = Folder + "/UI_KaptanKarti.prefab";

        /// <summary>The star the pips are drawn with — the same one the chest reveal uses.</summary>
        private const string StarResource = "UI/Sea/yildiz";

        // The card is white art, so every label on it is ink rather than paper. Same values the
        // code-built cards use, so a wired prefab and an unwired one read alike.
        private static readonly Color Ink = new Color(0.09f, 0.14f, 0.24f, 1f);
        private static readonly Color InkSoft = new Color(0.36f, 0.42f, 0.52f, 1f);
        private static readonly Color InkFaint = new Color(0.58f, 0.63f, 0.71f, 1f);
        private static readonly Color Paper = new Color(0.96f, 0.97f, 1f, 1f);
        private static readonly Color Green = new Color(0.13f, 0.62f, 0.35f, 1f);
        private static readonly Color Slate = new Color(0.34f, 0.38f, 0.45f, 1f);

        [MenuItem("Tools/Kayseri/UI/Build Roster Card Prefabs")]
        public static void Build()
        {
            System.IO.Directory.CreateDirectory(Folder);
            Save(BuildMasterCard(), MasterPath);
            Save(BuildCaptainCard(), CaptainPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Roster card prefabs written:\n" + MasterPath + "\n" + CaptainPath);
        }

        private static void Save(GameObject go, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
        }

        // ------------------------------------------------------------------ master
        /// <summary>
        /// One master card. The cell is WIDE — fifteen cards three across a portrait sheet — so the
        /// face sits left and everything about him stacks right. Child names are the contract
        /// ForemanRosterUI.BindPrefabCard reads; renaming one silently drops that piece from the card.
        /// </summary>
        private static GameObject BuildMasterCard()
        {
            GameObject root = Card("UI_UstaKarti");
            RectTransform r = (RectTransform)root.transform;

            // The face in its frame down the left. The UstaKiti portraits and frames are both 2:3, so
            // the slot is as tall as the card and as wide as that allows. Portrait FIRST: the frame's
            // rim has to draw over the portrait's edges, and siblings draw in order.
            Image(r, "Portre", new Vector2(0.075f, 0.150f), new Vector2(0.385f, 0.800f), preserveAspect: true);
            Image(r, "Cerceve", new Vector2(0.010f, 0.020f), new Vector2(0.450f, 0.980f), preserveAspect: true);

            // Ready to star up: the kit's upgrade coin on the frame's shoulder. Ships off; the screen
            // shows it only while the cards on the bar already cover the next star.
            Image(r, "Hazir", new Vector2(0.330f, 0.780f), new Vector2(0.450f, 0.980f), preserveAspect: true)
                .gameObject.SetActive(false);

            // The two state pills share the foot of the frame and are mutually exclusive — posted, or
            // not found yet. The screen writes neither's text; the words are localised at runtime.
            Badge(r, "Aktif", Green, new Vector2(0.040f, 0.025f), new Vector2(0.420f, 0.165f), icon: true);
            Badge(r, "Kilit", Slate, new Vector2(0.040f, 0.025f), new Vector2(0.420f, 0.165f), icon: true);

            Label(r, "Ad", new Vector2(0.465f, 0.760f), new Vector2(0.975f, 0.965f),
                  26, TextAnchor.MiddleLeft, Ink);

            // Which station he works. Three masters share each one, and the card was the only place
            // that did not say which.
            Label(r, "Istasyon", new Vector2(0.465f, 0.630f), new Vector2(0.975f, 0.760f),
                  18, TextAnchor.MiddleLeft, InkSoft);

            // Five star pips across the middle band. Present here means the rarity label stops
            // spelling the stars out in ★ — see ForemanRosterUI.HasStarPips.
            Stars(r, Foremen.MaxStars, 0.465f, 0.500f, 0.725f, 0.625f);

            Label(r, "Nadirlik", new Vector2(0.730f, 0.495f), new Vector2(0.975f, 0.625f),
                  18, TextAnchor.MiddleRight, InkSoft);

            Flat(r, "Sirad", new Vector2(0.465f, 0.470f), new Vector2(0.725f, 0.484f), InkFaint);

            Label(r, "Beceri", new Vector2(0.465f, 0.295f), new Vector2(0.975f, 0.465f),
                  28, TextAnchor.MiddleLeft, Ink);

            Bar(r, new Vector2(0.465f, 0.205f), new Vector2(0.700f, 0.265f));

            Label(r, "Kartlar", new Vector2(0.465f, 0.030f), new Vector2(0.700f, 0.190f),
                  20, TextAnchor.MiddleLeft, InkFaint);

            // Wide and low: the pill art is a capsule whose caps are a share of its height, and a box
            // much under 2:1 draws it as an egg.
            Pill(r, "Dugme", new Vector2(0.715f, 0.060f), new Vector2(0.975f, 0.230f));
            return root;
        }

        // ----------------------------------------------------------------- captain
        /// <summary>
        /// One captain row — a wide strip, five down a portrait sheet. Child names are the contract
        /// CaptainRosterUI.BindPrefabRow reads.
        /// </summary>
        private static GameObject BuildCaptainCard()
        {
            GameObject root = Card("UI_KaptanKarti");
            RectTransform r = (RectTransform)root.transform;

            // The grade stripe down the left edge — the fastest read on the screen, and the one thing
            // a collection row has to answer before anything else.
            Flat(r, "Derece", new Vector2(0.014f, 0.120f), new Vector2(0.042f, 0.880f), InkFaint);

            Image(r, "Portre", new Vector2(0.055f, 0.090f), new Vector2(0.185f, 0.910f), preserveAspect: true);

            Label(r, "Ad", new Vector2(0.205f, 0.560f), new Vector2(0.700f, 0.930f),
                  26, TextAnchor.MiddleLeft, Ink);
            Label(r, "Gorev", new Vector2(0.205f, 0.300f), new Vector2(0.700f, 0.540f),
                  21, TextAnchor.MiddleLeft, InkSoft);

            Stars(r, Captains.MaxLevel, 0.705f, 0.560f, 0.965f, 0.930f);

            Bar(r, new Vector2(0.205f, 0.090f), new Vector2(0.700f, 0.260f));

            Pill(r, "Yukselt", new Vector2(0.715f, 0.130f), new Vector2(0.972f, 0.500f));

            // At the helm / at sea / locked. One pill, recoloured and reworded by the screen.
            Badge(r, "Durum", Green, new Vector2(0.715f, 0.560f), new Vector2(0.972f, 0.900f));
            return root;
        }

        // ------------------------------------------------------------------ pieces
        private static GameObject Card(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var r = (RectTransform)go.transform;
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;

            // The card's own body. White by default so the ink above reads; the screen tints it.
            var img = go.GetComponent<Image>();
            img.color = Color.white;
            img.raycastTarget = true;

            var b = go.GetComponent<Button>();
            b.targetGraphic = img;
            b.transition = Selectable.Transition.None;
            return go;
        }

        private static RectTransform Anchor(GameObject go, Vector2 min, Vector2 max)
        {
            var r = (RectTransform)go.transform;
            r.anchorMin = min;
            r.anchorMax = max;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
            return r;
        }

        private static Image Image(RectTransform parent, string name, Vector2 min, Vector2 max,
                                   bool preserveAspect)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Anchor(go, min, max);
            var img = go.GetComponent<Image>();
            img.preserveAspect = preserveAspect;
            img.raycastTarget = false;
            // No sprite yet: the screen assigns the portrait from its own array, and an Image with no
            // sprite draws a white box, so it starts disabled and is enabled when one arrives.
            img.enabled = false;
            return img;
        }

        private static Image Flat(RectTransform parent, string name, Vector2 min, Vector2 max, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Anchor(go, min, max);
            var img = go.GetComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        private static Text Label(RectTransform parent, string name, Vector2 min, Vector2 max,
                                  int size, TextAnchor align, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Anchor(go, min, max);
            var t = go.GetComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = size;
            t.alignment = align;
            t.color = colour;
            t.raycastTarget = false;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = Mathf.Max(8, size / 2);
            t.resizeTextMaxSize = size;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        /// <summary>
        /// Yildiz0..N-1, evenly spaced across the band and drawn with the kit's own star. Without a
        /// sprite an Image is a solid rectangle, so five "stars" would read as five grey blocks; the
        /// screens only ever set the pips' COLOUR, never their sprite, so the art has to be here.
        /// </summary>
        private static void Stars(RectTransform parent, int count, float left, float bottom,
                                  float right, float top)
        {
            Sprite star = Resources.Load<Sprite>(StarResource);
            if (star == null) Debug.LogWarning("Star sprite missing at Resources/" + StarResource
                                               + " — the pips will draw as plain blocks.");
            float pitch = (right - left) / count;
            for (int i = 0; i < count; i++)
            {
                float x0 = left + i * pitch;
                Image pip = Flat(parent, "Yildiz" + i,
                                 new Vector2(x0 + pitch * 0.08f, bottom),
                                 new Vector2(x0 + pitch * 0.92f, top), InkFaint);
                pip.sprite = star;
                pip.preserveAspect = true;
            }
        }

        /// <summary>The progress bar: a track named Cubuk holding a left-anchored fill named Dolgu.</summary>
        private static void Bar(RectTransform parent, Vector2 min, Vector2 max)
        {
            Image track = Flat(parent, "Cubuk", min, max, new Color(0.85f, 0.87f, 0.90f, 1f));
            var go = new GameObject("Dolgu", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(track.transform, false);
            // Driven by its RIGHT anchor — the screen sets anchorMax.x to the progress.
            Anchor(go, Vector2.zero, new Vector2(0f, 1f));
            var img = go.GetComponent<Image>();
            img.color = new Color(0.33f, 0.62f, 0.92f, 1f);
            img.raycastTarget = false;
        }

        /// <summary>
        /// A state pill: a filled rect named for the state, with a Text child the screen writes into.
        /// Ships switched off — the screen decides which one applies.
        /// </summary>
        private static void Badge(RectTransform parent, string name, Color fill, Vector2 min, Vector2 max,
                                  bool icon = false)
        {
            Image pill = Flat(parent, name, min, max, fill);
            Text label = Label((RectTransform)pill.transform, "Text",
                               new Vector2(icon ? 0.26f : 0f, 0f), Vector2.one,
                               20, TextAnchor.MiddleCenter, Paper);
            label.text = string.Empty;
            // "Ikon": the kit's state coin at the pill's left end. Spriteless like the portrait; the
            // screen hands it the sprite from its own Inspector.
            if (icon)
                Image((RectTransform)pill.transform, "Ikon", new Vector2(0.02f, 0.02f), new Vector2(0.26f, 0.98f),
                      preserveAspect: true);
            pill.gameObject.SetActive(false);
        }

        /// <summary>An action pill with its label, which the screens find via GetComponentInChildren.</summary>
        private static void Pill(RectTransform parent, string name, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            Anchor(go, min, max);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.24f, 0.68f, 0.36f, 1f);
            var b = go.GetComponent<Button>();
            b.targetGraphic = img;

            Text label = Label((RectTransform)go.transform, "Text", Vector2.zero, Vector2.one,
                               22, TextAnchor.MiddleCenter, Paper);
            label.text = string.Empty;
        }
    }
}
