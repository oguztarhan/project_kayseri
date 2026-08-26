using Game.Core;
using Game.Gameplay;
using Game.Systems;
using TMPro;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// Everything written on a market yard's walls: the name over the ramp, the sales sign above the
    /// counter, the live price board, the plates either side of the way through to the next market,
    /// and the banners.
    ///
    /// WHY THERE WAS NOTHING BEFORE. The yard already had wayfinding, and all of it was painted on the
    /// FLOOR — arrows and names lying flat, because the camera never rotates and anything standing in
    /// a doorway shows one of the two rooms its back. That reasoning is right about doorways and wrong
    /// about walls, and the difference is which walls. At pitch 52 and yaw 45 the camera looks
    /// north-east and down, so exactly two interior faces in the room are ever pointed at it: the
    /// north wall's and the east wall's. Those two are billboards nothing can turn away from, and the
    /// yard was leaving them blank. Everything here goes on one of them, or on something that is
    /// already facing that way — the ramp mouth, the back of the counter.
    ///
    /// The pad rank's wall, the west one, gets NOTHING for the same reason: its inner face points away
    /// from the camera and a sign on it would only ever be read edge-on. The rank keeps its painted
    /// floor prices and gets a free-standing pylon at the head of it instead, turned to face south.
    ///
    /// This lives in <c>Game.UI</c> rather than with the rest of the yard because it draws text, and
    /// <c>Game.Gameplay</c> cannot see TextMeshPro. The yard takes it back as a bare GameObject through
    /// <see cref="MarketYardScene.AddFitting"/>, which is enough for it to switch the whole lot off
    /// when it is parked — and it is worth switching off, because the price board reads the ledger
    /// twice a second.
    /// </summary>
    public sealed class MarketYardSigns : MonoBehaviour
    {
        /// <summary>What the signage holder is called under the yard root.</summary>
        public const string HolderName = "Tabelalar";

        private const string FrameResource = "Market/Models/SM_Market_SignFrame";

        /// <summary>
        /// The sign frame's own size, in the units it was modelled at. Everything below is placed
        /// against these and then multiplied by a per-sign scale, so one model serves a seven-unit
        /// header over the ramp and a three-unit plate beside a doorway.
        /// </summary>
        private const float FrameWidth = 3.2f, FrameHeight = 1.15f, FrameDepth = 0.153f;

        /// <summary>How far in front of the frame's own panel the lit face and then the words sit.</summary>
        private const float FaceOffset = 0.10f, InkOffset = 0.135f;

        private MarketService _market;
        private string _islandKey;
        private TextMeshPro _neonState;
        private Material _neonFace;
        private Color _neonLit, _neonDim;
        private TextMeshPro _priceValue;
        private Color _priceInk;
        private float _refresh;
        private double _lastPrice = -1d;

        // Where the two walls that face the camera actually are. Derived rather than copied: the wall
        // is a slab centred on the yard's edge, so its inner face is half a thickness in from it.
        private static float NorthFace => MarketYardBuild.Depth * 0.5f - MarketYardBuild.WallThickness * 0.5f;
        private static float EastFace => MarketYardBuild.Width * 0.5f - MarketYardBuild.WallThickness * 0.5f;

        /// <summary>
        /// Puts the yard's signs up and hands back the holder, for the yard to park with the rest of
        /// itself. <paramref name="accent"/> is the island's ore colour — the same one its roof is
        /// tinted with, so the sign over the ramp and the roof seen from the row agree about which
        /// shop this is.
        /// </summary>
        public static MarketYardSigns Build(Transform yardRoot, MarketService market, string islandKey,
                                            Color accent, MarketTheme.Palette theme, TMP_FontAsset font)
        {
            var holder = new GameObject(HolderName);
            holder.transform.SetParent(yardRoot, false);
            var signs = holder.AddComponent<MarketYardSigns>();
            signs._market = market;
            signs._islandKey = islandKey;
            signs.BuildAll(accent, theme, font);
            return signs;
        }

        private void BuildAll(Color accent, MarketTheme.Palette theme, TMP_FontAsset font)
        {
            // Pale enough to read words off, still recognisably the ore. The raw colour is no good for
            // this in either direction — coal's is nearly black and a sign lit in it is a dark patch,
            // diamond's is nearly white and the letters vanish into it.
            Color lit = Color.Lerp(accent, Color.white, 0.30f);
            Color ink = new Color(0.10f, 0.11f, 0.14f);

            // The name of the shop, on the mouth of the delivery ramp. The biggest flat surface in the
            // room that faces the camera and has nothing standing in front of it — the ramp itself is
            // three units tall and this sits above it. It is the yard's identity, so it is the ore's
            // own name and it is lit.
            //
            // Over the ramp rather than on the north wall behind it, and that is the ramp's fault: the
            // ramp covers that wall from the floor to 3.2 and the roofline beam starts at 4.14, which
            // leaves under a unit of wall to write in. The ramp mouth is the wall, effectively.
            Sign("Tabela_Rampa", new Vector3(0f, 4.5f, 13.5f), 180f, 2.0f,
                 theme.Metal, lit, 1.15f, Loc.Id("ada", _islandKey).ToUpperInvariant(), ink, font);

            // Over the counter, on two posts off the counter top. Faces the queue, which means it also
            // faces the camera, and it is the one sign the player is looking straight at while doing
            // the thing the yard is for.
            Post("Tezgah_Direk_Bati", new Vector3(-11.6f, 2.24f, -8f), 1.44f, theme.Metal);
            Post("Tezgah_Direk_Dogu", new Vector3(-6.4f, 2.24f, -8f), 1.44f, theme.Metal);
            Sign("Tabela_Tezgah", new Vector3(-9f, 3.7f, -8f), 180f, 1.3f,
                 theme.Metal, lit, 1.15f, Loc.T("market.tabela.satis"), ink, font);

            BuildPriceBoard(theme, accent, font);

            // A pylon at the head of the pad rank, because the rank's own wall points the wrong way.
            // Turned south, down the line of pads, so it is read on the walk toward them.
            Post("Yukseltme_Direk", new Vector3(-18f, 1.3f, 17.4f), 2.6f, theme.Metal);
            Sign("Tabela_Yukseltme", new Vector3(-18f, 3.05f, 17.4f), 180f, 0.9f,
                 theme.Metal, Color.Lerp(theme.Trim, Color.white, 0.25f), 0.55f,
                 Loc.T("market.tabela.yukseltmeler"), ink, font);

            // Two cloth banners on the north wall, in the two panels the ramp does not cover. Pure
            // decoration and the cheapest thing in here — one box apiece — but they are what stops the
            // top half of that wall being four blank metres of the same colour.
            Banner("Afis_Bati", new Vector3(-21.5f, 2.7f, NorthFace - 0.25f), accent);
            Banner("Afis_Dogu", new Vector3(20.5f, 2.7f, NorthFace - 0.25f), accent);
        }

        /// <summary>
        /// The price board on the east wall, above the stock pad — which is where it belongs rather
        /// than anywhere prettier. That slab is where the player decides whether another armful is
        /// worth the walk, and until now nothing in the game had ever told him what one bar is worth.
        /// Everywhere else the number has already been multiplied by however many went past.
        ///
        /// A dark plate with bright figures rather than a lit panel: this one is a readout, and the
        /// difference between a sign and a readout is that a readout changes.
        /// </summary>
        private void BuildPriceBoard(MarketTheme.Palette theme, Color accent, TMP_FontAsset font)
        {
            _priceInk = Color.Lerp(accent, Color.white, 0.55f);
            Color plate = Color.Lerp(theme.Slab, Color.black, 0.78f);

            // z = 12.5 ve bu bir zevk meselesi degil: dogu duvarinin ORTASI duvar degil, komsu
            // pazara acilan gecit. Pano once z = 3'te duruyordu, yani tam o bosluga asilmisti —
            // avlunun disinda, havada. Duvar iki parca: kuzeyde 5..20, guneyde -20..-5. Bu, kuzey
            // parcasinin tam ortasi, ve stok pedinin bittigi yer.
            Transform board = Sign("Tabela_Fiyat", new Vector3(EastFace, 3.42f, 12.5f), -90f, 1.1f,
                                   theme.Metal, plate, 0f, null, Color.white, font);

            // Two lines rather than one with rich text: the bottom one is rewritten twice a second and
            // the top one never is, and there is no reason to re-lay out the word "PRICE" every time
            // the figure under it moves.
            Ink(board, "Etiket", new Vector3(0f, FrameHeight * 0.26f, InkOffset),
                new Vector2(FrameWidth * 0.86f, FrameHeight * 0.34f),
                Loc.T("market.tabela.fiyat") + "  /  " + Loc.T("market.tabela.kulce"),
                new Color(0.62f, 0.65f, 0.72f), font, 2.0f, 3.4f);

            _priceValue = Ink(board, "Deger", new Vector3(0f, -FrameHeight * 0.16f, InkOffset),
                              new Vector2(FrameWidth * 0.86f, FrameHeight * 0.44f),
                              "—", _priceInk, font, 3.4f, 6.2f);
            Refresh();
        }

        /// <summary>
        /// The plates either side of the gap in the east wall, naming the market on the other side of
        /// it and pointing at the gap. The floor arrow says the same thing and stays: this is the half
        /// of it that can be read from across the room rather than from on top of it.
        ///
        /// Called from <see cref="MarketSceneBoot"/>, because which market is next door is a fact about
        /// the row and not about this yard. Nothing happens for the last yard in the row, which has no
        /// gap in that wall at all.
        ///
        /// ASCII chevrons, for the same reason the floor sign uses them: this is drawn in eleven
        /// languages and a glyph the font has not got is a box.
        /// </summary>
        public void SetGateSign(string neighbourKey, Color neighbourTint, MarketTheme.Palette theme,
                                TMP_FontAsset font)
        {
            if (string.IsNullOrEmpty(neighbourKey)) return;
            string name = Loc.Id("ada", neighbourKey).ToUpperInvariant();
            Color lit = Color.Lerp(neighbourTint, Color.white, 0.45f);
            Color ink = new Color(0.10f, 0.11f, 0.14f);

            // The gap is at z = 0. On a sign turned to face west, its own right hand points north — so
            // the plate standing north of the gap points back down at it and the one south points up.
            Sign("Tabela_Gecit_Kuzey", new Vector3(EastFace, 3.35f, 8.5f), -90f, 0.95f,
                 theme.Metal, lit, 0.75f, "<<  " + name, ink, font);
            Sign("Tabela_Gecit_Guney", new Vector3(EastFace, 3.35f, -8.5f), -90f, 0.95f,
                 theme.Metal, lit, 0.75f, name + "  >>", ink, font);
        }

        private void Update()
        {
            // Twice a second. The price only moves when an upgrade lands or a boost starts or stops,
            // so anything faster is a per-frame string allocation buying nothing.
            _refresh -= Time.deltaTime;
            if (_refresh > 0f) return;
            _refresh = 0.5f;
            Refresh();
        }

        private void Refresh()
        {
            if (_priceValue == null || _market == null) return;
            double price = _market.BarPrice(_islandKey);
            // Only when it actually changed. A board that rewrites the same string twice a second is
            // two allocations a second in a room with eight of them, seven of which are asleep.
            if (System.Math.Abs(price - _lastPrice) < 1e-6d) return;

            // Which way it moved, marked next to the figure. It is the one piece of the yard that
            // reacts to an upgrade somewhere else in the room, and an upgrade that visibly moves a
            // number on the wall is worth more than the same upgrade that quietly moves a rate.
            string trend = _lastPrice < 0d ? string.Empty
                         : price > _lastPrice ? "  ^"
                         : price < _lastPrice ? "  v" : string.Empty;
            _priceValue.color = _lastPrice >= 0d && price > _lastPrice
                ? new Color(0.45f, 0.90f, 0.55f) : _priceInk;
            _priceValue.text = NumberFormatter.Format(new BigDouble(price)) + trend;
            _lastPrice = price;
        }

        /// <summary>
        /// One sign: the frame, a face inside it, and the words on the face.
        ///
        /// The frame is a model and the face is a box, and the face is built either way. A missing
        /// model leaves a plain lit panel with the writing still on it, which is the same bargain
        /// <see cref="MarketYardDressing"/> makes with its props — a project without the art folder
        /// gets a plainer market, not a broken one. Hanging the text off a model that might not be
        /// there is how you end up with words floating in the middle of a room.
        /// </summary>
        private Transform Sign(string name, Vector3 at, float yaw, float scale,
                               Color frameColour, Color faceColour, float glow,
                               string line, Color ink, TMP_FontAsset font, Transform parent = null)
        {
            var holder = new GameObject(name).transform;
            // Everything hangs off this component's own holder except the neon, which has to survive a
            // yard being parked — see <see cref="BuildNeon"/>.
            holder.SetParent(parent != null ? parent : transform, false);
            holder.localPosition = at;
            holder.localRotation = Quaternion.Euler(0f, yaw, 0f);
            holder.localScale = Vector3.one * scale;

            var frame = Resources.Load<GameObject>(FrameResource);
            if (frame != null)
            {
                GameObject body = Instantiate(frame, holder, false);
                body.name = "Cerceve";
                Material metal = MarketSurfaces.Get(frameColour, MarketSurfaces.Finish.Metal);
                var parts = body.GetComponentsInChildren<MeshRenderer>(true);
                for (int i = 0; i < parts.Length; i++) parts[i].sharedMaterial = metal;
                // Scenery on a wall. Nothing in the yard should ever be stopped by a sign.
                var hazards = body.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < hazards.Length; i++) Destroy(hazards[i]);
            }

            var face = new GameObject("Yuzey").transform;
            face.SetParent(holder, false);
            face.localPosition = new Vector3(0f, 0f, FaceOffset);
            var filter = face.gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = MarketBoxMesh.Get(
                new Vector3(FrameWidth - 0.30f, FrameHeight - 0.30f, 0.03f), 0f);
            _lastFace = glow > 0f ? MarketSurfaces.Glow(faceColour, glow)
                                  : MarketSurfaces.Get(faceColour, MarketSurfaces.Finish.Plain);
            face.gameObject.AddComponent<MeshRenderer>().sharedMaterial = _lastFace;

            if (!string.IsNullOrEmpty(line))
                Ink(holder, "Yazi", new Vector3(0f, 0f, InkOffset),
                    new Vector2(FrameWidth * 0.86f, FrameHeight * 0.62f), line, ink, font, 3.5f, 7.5f);
            return holder;
        }

        /// <summary>The face material the last <see cref="Sign"/> built, for the one caller that keeps it.</summary>
        private Material _lastFace;

        /// <summary>
        /// The lit sign standing on this yard's roof, naming the shop and saying whether it is open.
        ///
        /// It is the only thing in this file NOT hung off the signage holder, and it must not be. The
        /// holder is switched off with the rest of a parked yard; this sign exists precisely to be read
        /// while the yard is parked. Standing on the roof it clears the wall the player is looking over,
        /// so from inside one market he can see the shopfronts of the ones either side of him — which
        /// is the difference between eight rooms in a row and a market with eight shops in it.
        ///
        /// "Open" means the player is standing in it, which is exactly what the roof already says: the
        /// live yard's roof comes off and the other seven keep theirs. The sign says the same thing in
        /// words, from a distance, with the shop's name attached.
        /// </summary>
        public void BuildNeon(Transform yardRoot, Color accent, MarketTheme.Palette theme,
                              TMP_FontAsset font)
        {
            _neonLit = Color.Lerp(accent, Color.white, 0.35f);
            // Shut, not switched off. A closed sign still has to be READ from the yard next door —
            // being able to see which shop is which down the row is the entire job of this thing, and
            // it does not stop being the job when the shop is shut. Pulled toward its own ore rather
            // than toward black, and left with enough emission to lift the lettering off it.
            _neonDim = Color.Lerp(accent, new Color(0.20f, 0.21f, 0.25f), 0.55f);

            // Standing ON the roof rather than in front of it: the slab tops out at 5.2 and the porch
            // roof is over at x = 9, so the west half of the south edge is the one clear line in the
            // whole silhouette.
            float ridge = MarketYardBuild.WallHeight + 1.7f;
            Transform neon = Sign("Tabela_Neon", new Vector3(-4f, ridge, -MarketYardBuild.Depth * 0.5f + 0.6f),
                                  180f, 1.6f, theme.Metal, _neonLit, 1.4f,
                                  Loc.Id("ada", _islandKey).ToUpperInvariant(),
                                  new Color(0.08f, 0.09f, 0.12f), font, yardRoot);
            _neonFace = _lastFace;

            // Two legs down onto the slab, or the whole thing is a name floating over a roof.
            for (int k = 0; k < 2; k++)
            {
                var leg = new GameObject("Neon_Ayak_" + k);
                leg.transform.SetParent(yardRoot, false);
                leg.transform.localPosition = new Vector3(-4f + (k == 0 ? -2.1f : 2.1f), ridge - 1.35f,
                                                          -MarketYardBuild.Depth * 0.5f + 0.6f);
                leg.AddComponent<MeshFilter>().sharedMesh = MarketBoxMesh.Get(
                    new Vector3(0.18f, 1.5f, 0.18f), MarketSurfaces.Tiles(MarketSurfaces.Finish.Metal));
                leg.AddComponent<MeshRenderer>().sharedMaterial =
                    MarketSurfaces.Get(theme.Metal, MarketSurfaces.Finish.Metal);
            }

            _neonState = Ink(neon, "Durum", new Vector3(0f, -FrameHeight * 0.62f, InkOffset),
                             new Vector2(FrameWidth * 0.7f, FrameHeight * 0.34f),
                             string.Empty, new Color(0.86f, 0.88f, 0.94f), font, 2.0f, 3.4f);
            SetOpen(false);
        }

        /// <summary>
        /// Turns this yard's shopfront on or off. Called as the player walks in and out, off the same
        /// decision that takes the roof away.
        ///
        /// Safe to call on a parked yard even though this component's own GameObject is switched off
        /// with it: the neon is parented to the yard root and stays awake, and a method on a disabled
        /// component still runs.
        /// </summary>
        public void SetOpen(bool open)
        {
            if (_neonState == null) return;
            _neonState.text = Loc.T(open ? "market.tabela.acik" : "market.tabela.kapali");
            _neonState.color = open ? new Color(0.92f, 0.96f, 1f) : new Color(0.72f, 0.74f, 0.80f);
            MarketSurfaces.SetGlow(_neonFace, open ? _neonLit : _neonDim, open ? 1.4f : 0.45f);
        }

        /// <summary>
        /// A line of text on a sign, sized to fit rather than sized once.
        ///
        /// Auto-sizing is not a nicety here: these words come out of the localisation table, and
        /// UPGRADES is nine characters in English, thirteen in Turkish and fourteen in Polish. A fixed
        /// point size that fits one of those runs off the end of the board in the other two.
        ///
        /// THE SIZES ARE POINTS, NOT WORLD UNITS. A 3D TextMeshPro's fontSize is roughly ten times the
        /// world height of a line — the floor signs next door run at 7.5 in a four-unit-tall box for
        /// exactly that reason. Written here because the first pass used world units and every sign in
        /// the market came out with a legible board and an unreadable smudge on it.
        /// </summary>
        private static TextMeshPro Ink(Transform parent, string name, Vector3 at, Vector2 area,
                                       string line, Color ink, TMP_FontAsset font,
                                       float minSize, float maxSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            // Turned to face the way the sign does, and this is not optional. A 3D TextMeshPro's
            // READABLE side is its local -Z, not +Z — put one on a board facing +Z and it comes out
            // mirrored, because the TMP shader does not cull backfaces and you end up reading the back
            // of the letters through them. Every sign in this file stands its words on the +Z face of a
            // board, so every one of them needs this half turn. The floor signs next door get away
            // without it only because their 90° pitch happens to land -Z pointing at the sky.
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var text = go.AddComponent<TextMeshPro>();
            if (font != null) text.font = font;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.rectTransform.sizeDelta = area;
            text.enableAutoSizing = true;
            text.fontSizeMin = minSize;
            text.fontSizeMax = maxSize;
            text.fontStyle = FontStyles.Bold;
            text.color = ink;
            text.text = line;
            return text;
        }

        /// <summary>A post holding a sign up, with no collider on it — see <see cref="Sign"/>.</summary>
        private void Post(string name, Vector3 at, float height, Color colour)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = at;
            go.AddComponent<MeshFilter>().sharedMesh =
                MarketBoxMesh.Get(new Vector3(0.20f, height, 0.20f), MarketSurfaces.Tiles(MarketSurfaces.Finish.Metal));
            go.AddComponent<MeshRenderer>().sharedMaterial =
                MarketSurfaces.Get(colour, MarketSurfaces.Finish.Metal);
        }

        /// <summary>
        /// A cloth banner hung flat on the north wall. One box, standing a hand's width proud of the
        /// bumper rail so it reads as hanging over it rather than being painted on it.
        /// </summary>
        private void Banner(string name, Vector3 at, Color accent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = at;
            go.AddComponent<MeshFilter>().sharedMesh =
                MarketBoxMesh.Get(new Vector3(2.6f, 2.6f, 0.06f), MarketSurfaces.Tiles(MarketSurfaces.Finish.Banner));
            go.AddComponent<MeshRenderer>().sharedMaterial =
                MarketSurfaces.Get(Color.Lerp(accent, Color.white, 0.25f), MarketSurfaces.Finish.Banner);
        }
    }
}
