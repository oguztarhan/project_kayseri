using Game.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    /// <summary>
    /// The card prefabs are bound BY CHILD NAME, and a name that does not match is skipped rather than
    /// reported — that is what keeps a half-finished prefab from throwing on open, and it is also what
    /// would let a rename delete a line from every card in silence. This is the check that makes the
    /// contract real: rename "Nadirlik" in the prefab and this fails instead of the rarity quietly
    /// vanishing from fifteen cards.
    ///
    /// The names come from ForemanRosterUI.BindPrefabCard and CaptainRosterUI.BindPrefabRow.
    /// </summary>
    public sealed class RosterCardPrefabTests
    {
        private const string MasterPath = "Assets/Prefabs/UI/UI_UstaKarti.prefab";
        private const string CaptainPath = "Assets/Prefabs/UI/UI_KaptanKarti.prefab";

        [Test]
        public void TheMasterCardCarriesEveryPieceTheScreenBinds()
        {
            GameObject card = Load(MasterPath);
            Has<Image>(card, "Portre");
            Has<Text>(card, "Ad");
            Has<Text>(card, "Nadirlik");
            Has<Text>(card, "Beceri");
            Has<Text>(card, "Kartlar");
            Has<Image>(card, "Dolgu");
            Has<Image>(card, "Sirad");
            Has<Transform>(card, "Aktif");
            Has<Transform>(card, "Kilit");
            Button action = Has<Button>(card, "Dugme");
            Assert.That(action.GetComponentInChildren<Text>(true), Is.Not.Null,
                        "the action pill needs a label the screen can write into");

            for (int i = 0; i < Foremen.MaxStars; i++) Has<Image>(card, "Yildiz" + i);
        }

        [Test]
        public void TheCaptainCardCarriesEveryPieceTheScreenBinds()
        {
            GameObject card = Load(CaptainPath);
            Has<Image>(card, "Portre");
            Has<Text>(card, "Ad");
            Has<Text>(card, "Gorev");
            Has<Image>(card, "Derece");
            Has<Image>(card, "Dolgu");
            Button action = Has<Button>(card, "Yukselt");
            Assert.That(action.GetComponentInChildren<Text>(true), Is.Not.Null,
                        "the action pill needs a label the screen can write into");

            Has<Transform>(card, "Durum");
            for (int i = 0; i < Captains.MaxLevel; i++) Has<Image>(card, "Yildiz" + i);
        }

        /// <summary>
        /// Every state pill ships switched OFF and with a writable label. One that shipped on would
        /// mark every card as posted until the first refresh; one with no Text has nothing to say.
        /// </summary>
        [Test]
        public void StateBadgesShipHiddenAndWritable()
        {
            foreach (var pair in new[]
            {
                (MasterPath, new[] { "Aktif", "Kilit" }),
                (CaptainPath, new[] { "Durum" }),
            })
            {
                GameObject card = Load(pair.Item1);
                foreach (string name in pair.Item2)
                {
                    Transform badge = Has<Transform>(card, name);
                    Assert.That(badge.gameObject.activeSelf, Is.False,
                                pair.Item1 + " ships " + name + " switched on");
                    Assert.That(badge.GetComponentInChildren<Text>(true), Is.Not.Null,
                                pair.Item1 + " badge " + name + " has no label to write into");
                }
            }
        }

        [Test]
        public void BothCardsAreTappableAndFillTheirCell()
        {
            // The whole card opens the detail sheet, so the root has to be a raycast target. And the
            // screen re-anchors the root into a grid cell, which only works from a RectTransform.
            foreach (string path in new[] { MasterPath, CaptainPath })
            {
                GameObject card = Load(path);
                Assert.That(card.GetComponent<RectTransform>(), Is.Not.Null, path);
                var body = card.GetComponent<Image>();
                Assert.That(body, Is.Not.Null, path + " needs a body image to be tappable");
                Assert.That(body.raycastTarget, Is.True, path + " body must take taps");
            }
        }

        /// <summary>The star pips must not overlap, or five stars read as one smear.</summary>
        [Test]
        public void TheMasterStarPipsAreLaidOutSideBySide()
        {
            GameObject card = Load(MasterPath);
            float previousRight = float.NegativeInfinity;
            for (int i = 0; i < Foremen.MaxStars; i++)
            {
                var pip = (RectTransform)Has<Image>(card, "Yildiz" + i).transform;
                Assert.That(pip.anchorMin.x, Is.GreaterThanOrEqualTo(previousRight),
                            "Yildiz" + i + " overlaps the pip before it");
                Assert.That(pip.anchorMax.x, Is.GreaterThan(pip.anchorMin.x), "Yildiz" + i);
                previousRight = pip.anchorMax.x;
            }
        }

        private static GameObject Load(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(go, Is.Not.Null, "missing prefab: " + path
                        + " — rebuild with Tools/Kayseri/UI/Build Roster Card Prefabs");
            return go;
        }

        private static T Has<T>(GameObject root, string name) where T : Component
        {
            T[] all = root.GetComponentsInChildren<T>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i].name == name) return all[i];
            Assert.Fail("prefab " + root.name + " has no " + typeof(T).Name + " named '" + name
                        + "' — the screen binds that name and would skip it silently");
            return null;
        }
    }
}
