using Game.Core;
using Game.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// The masters standing on the island. A plain class rather than a MonoBehaviour, so it can be
    /// driven straight from a test: the three delegates are the whole of its input.
    /// </summary>
    public sealed class StationForemenTests
    {
        private GameObject _parent;
        private GameObject[] _pack;

        [SetUp]
        public void SetUp()
        {
            _parent = new GameObject("IslandRoot");
            // Four distinguishable "bodies". The real pack is eighteen rigged prefabs; what matters
            // here is only that Instantiate produces something whose source can be told apart — hence
            // the marker child, since StationForemen renames the clone itself.
            _pack = new GameObject[4];
            for (int i = 0; i < _pack.Length; i++)
            {
                _pack[i] = new GameObject("Body" + i);
                var mark = new GameObject(MarkerPrefix + i);
                mark.transform.SetParent(_pack[i].transform, false);
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (_parent != null) Object.DestroyImmediate(_parent);
            if (_pack != null)
                for (int i = 0; i < _pack.Length; i++)
                    if (_pack[i] != null) Object.DestroyImmediate(_pack[i]);
        }

        private StationForemen Build(int stations = 8)
            => new StationForemen(_parent.transform, _pack, stations, 1f);

        /// <summary>Only the five stations a master runs get a body; the other three stay empty.</summary>
        [Test]
        public void OnlyStaffedStationsStandABodyUp()
        {
            StationForemen bosses = Build();
            bosses.Hired = s => Foremen.RosterStationOf(s) >= 0;
            bosses.Post = Post;
            bosses.Rank = s => 0;
            bosses.Refresh();

            Assert.That(ActiveBodies(), Is.EqualTo(Foremen.StationCount),
                        "one body per station a master actually runs");
        }

        [Test]
        public void AnUnstaffedStationShowsNobody()
        {
            StationForemen bosses = Build();
            bosses.Hired = s => false;
            bosses.Post = Post;
            bosses.Rank = s => 0;
            bosses.Refresh();

            Assert.That(ActiveBodies(), Is.Zero);
        }

        /// <summary>
        /// A station whose anchor cannot be resolved yet — the island is still being built — must
        /// leave the post empty rather than dropping a master at the origin.
        /// </summary>
        [Test]
        public void AStationWithNoAnchorLeavesThePostEmpty()
        {
            StationForemen bosses = Build();
            bosses.Hired = s => true;
            bosses.Post = NoAnchor;
            bosses.Rank = s => 0;
            bosses.Refresh();

            Assert.That(ActiveBodies(), Is.Zero);
        }

        /// <summary>
        /// THE POINT OF Who. Three masters share a station, so posting a different one has to change
        /// the man standing there — not just the plinth under him.
        /// </summary>
        [Test]
        public void PostingADifferentMasterSwapsTheBody()
        {
            int posted = Foremen.IndexOf(Foremen.Mine, Foremen.Rarity.Common);
            int mineSlot = Foremen.EconomyStation[Foremen.Mine];

            StationForemen bosses = Build();
            bosses.Hired = s => s == mineSlot;
            bosses.Post = Post;
            bosses.Rank = s => 0;
            bosses.Who = s => s == mineSlot ? posted : -1;
            bosses.Refresh();

            Transform before = ActiveBody();
            Assert.That(before, Is.Not.Null, "the premise: somebody is posted");
            string firstBody = SourceOf(before);

            posted = Foremen.IndexOf(Foremen.Mine, Foremen.Rarity.Legendary);
            bosses.Refresh();

            Transform after = ActiveBody();
            Assert.That(after, Is.Not.Null, "the replacement must be standing there");
            Assert.That(SourceOf(after), Is.Not.EqualTo(firstBody),
                        "a different master must be a different man, not the same body re-tinted");
            Assert.That(ActiveBodies(), Is.EqualTo(1), "the man he replaced must not still be there");
        }

        [Test]
        public void PostingTheSameMasterAgainDoesNotRebuildHim()
        {
            int mineSlot = Foremen.EconomyStation[Foremen.Mine];
            int posted = Foremen.IndexOf(Foremen.Mine, Foremen.Rarity.Rare);

            StationForemen bosses = Build();
            bosses.Hired = s => s == mineSlot;
            bosses.Post = Post;
            bosses.Rank = s => 0;
            bosses.Who = s => s == mineSlot ? posted : -1;
            bosses.Refresh();

            int id = ActiveBody().gameObject.GetInstanceID();
            bosses.Refresh();
            bosses.Refresh();

            Assert.That(ActiveBody().gameObject.GetInstanceID(), Is.EqualTo(id),
                        "an unchanged posting must not destroy and rebuild the body every refresh");
        }

        /// <summary>No pack wired is a no-op, not a null dereference.</summary>
        [Test]
        public void NoBodiesWiredIsHarmless()
        {
            var bosses = new StationForemen(_parent.transform, new GameObject[0], 8, 1f);
            bosses.Hired = s => true;
            bosses.Post = Post;
            bosses.Rank = s => 0;
            bosses.Who = s => 0;
            Assert.DoesNotThrow(() => bosses.Refresh());
            Assert.DoesNotThrow(() => bosses.Tick(0.1f));
        }

        // ------------------------------------------------------------------ helpers
        private static bool Post(int station, out Vector3 ground, out Vector3 lookAt)
        {
            ground = new Vector3(station * 10f, 0f, 0f);
            lookAt = ground + Vector3.forward;
            return true;
        }

        private static bool NoAnchor(int station, out Vector3 ground, out Vector3 lookAt)
        {
            ground = Vector3.zero;
            lookAt = Vector3.zero;
            return false;
        }

        private int ActiveBodies()
        {
            int n = 0;
            foreach (Transform child in _parent.transform)
                if (child.gameObject.activeSelf) n++;
            return n;
        }

        private Transform ActiveBody()
        {
            foreach (Transform child in _parent.transform)
                if (child.gameObject.activeSelf) return child;
            return null;
        }

        private const string MarkerPrefix = "PackEntry";

        /// <summary>
        /// Which pack entry a body was cloned from. StationForemen renames the clone to OpForeman_N,
        /// so the name is no help; the marker child comes along with the clone and does identify it.
        /// </summary>
        private static string SourceOf(Transform body)
        {
            Transform[] all = body.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i].name.StartsWith(MarkerPrefix)) return all[i].name;
            Assert.Fail("body " + body.name + " carries no pack marker");
            return null;
        }
    }
}
