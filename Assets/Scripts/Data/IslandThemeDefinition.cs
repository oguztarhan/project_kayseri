using System;
using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// One chapter's look: a list of "draw THIS material instead of THAT one" over the island the
    /// player is already standing on.
    /// Create via: Assets &gt; Create &gt; Ore Empire &gt; Island Theme.
    ///
    /// WHY MATERIALS AND NOT OBJECTS. The map is 1,866 renderers built from 278 prefabs, but it is
    /// painted with only 37 materials — grassDark alone covers 249 slots. Re-skinning the island is
    /// therefore a 37-row table, not a tour of the hierarchy, and the geometry, the layout and every
    /// gameplay anchor stay exactly where they are.
    ///
    /// A ROW WITH NO REPLACEMENT KEEPS THE ORIGINAL. That is what makes a half-finished theme safe
    /// to ship: fill in the rocks this week and the roofs next week, and in between the island draws
    /// new rocks and its old roofs rather than turning into holes. It is also how a theme is switched
    /// off — empty every row — without deleting the asset.
    ///
    /// LAMPS ARE NOT THEMEABLE. <c>BuildingLights</c> and <c>StreetLamps</c> find their emissive
    /// materials BY NAME at startup (lamp_glow), so a replacement under a different name would put
    /// the island's night lights out with nothing logged. Leave the emissive materials out of the
    /// table, or give the replacement the identical asset name.
    /// </summary>
    [CreateAssetMenu(fileName = "IslandTheme", menuName = "Ore Empire/Island Theme", order = 26)]
    public sealed class IslandThemeDefinition : ScriptableObject
    {
        [Serializable]
        public struct MaterialSwap
        {
            [Tooltip("Adanın kendi malzemesi — haritada zaten kullanılan 37 taneden biri.")]
            public Material source;

            [Tooltip("Bu bölümde onun yerine çizilecek malzeme. BOŞ BIRAKILIRSA özgün malzeme " +
                     "kalır: yarım kalmış bir tema adayı bozmaz, sadece daha azını değiştirir.")]
            public Material replacement;
        }

        [Header("Kimlik")]
        [Tooltip("Kayda ve günlüğe yazılan sabit kimlik. Tema yeniden adlandırılsa da bu değişmez.")]
        [SerializeField] private string themeId = "";

        [Tooltip("Sadece Editor içindir; oyuncu bu metni görmez.")]
        [SerializeField] private string displayName = "";

        [Header("Kapsam")]
        [Tooltip("Bu temanın açıldığı bölüm, 0 tabanlı (0 = Bölüm 1). Tema, bir sonraki temanın " +
                 "açıldığı bölüme kadar yürürlükte kalır — yani sekiz tema 0..7 ise bölüm başına " +
                 "bir tema, üç tema 0/2/5 ise 1-2, 3-5, 6-8 aralıkları demektir.")]
        [SerializeField] private int fromChapter = 0;

        [Header("Malzemeler")]
        [Tooltip("Boş bırakılan satır özgün malzemeyi korur. Işık veren malzemeleri (lamp_glow) " +
                 "buraya EKLEME: gece ışıkları onları adlarıyla arıyor.")]
        [SerializeField] private MaterialSwap[] swaps = new MaterialSwap[0];

        public string ThemeId => themeId;
        public string DisplayName => displayName;
        public int FromChapter => fromChapter;
        public int SwapCount => swaps != null ? swaps.Length : 0;
        public MaterialSwap SwapAt(int index) => swaps[index];
    }
}
