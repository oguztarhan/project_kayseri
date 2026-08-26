using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The "this one is you" dressing on the market player: a lit ring on the floor at their feet, a
    /// shadow inside it, and a marker bobbing over their head.
    ///
    /// The yard is a room full of people who are all built the same way. Customers, hired staff and the
    /// player are one capsule or one authored body apiece, told apart only by colour — and the player's
    /// colour was the ISLAND's ore, which on the coal island is very nearly black. So the one body in the
    /// room the player is actually driving was the hardest of the lot to pick out of a queue.
    ///
    /// Three pieces because they answer at three distances. The RING says which body is yours at a glance
    /// from anywhere in the yard. The SHADOW inside it plants that body on the floor instead of hovering
    /// over it, which is most of what makes a character feel like it is standing somewhere. The BEACON is
    /// what you find when the body itself is behind a heap or in a queue and you have lost it.
    ///
    /// Rides the feet pivot, so it follows the player up onto a pad without any code: the pivot is at the
    /// controller's contact point, and the ring is a couple of centimetres above whatever that is standing
    /// on. Colliders come off everything here — a collider at the player's feet fights the controller that
    /// put it there — and nothing casts a shadow, since one of the three IS the shadow.
    /// </summary>
    public sealed class PlayerMarker : MonoBehaviour
    {
        [Tooltip("Halkanın çapı, dünya birimi. Gövdeden belirgin şekilde geniş olmalı, yoksa halka " +
                 "ayakkabı gibi duruyor.")]
        [SerializeField, Min(0.5f)] private float ringDiameter = 3.9f;

        [Tooltip("Halkanın kalınlığı. İç daire bunun kadar küçük kalıyor ve arada kalan parlak şerit " +
                 "halkanın kendisi oluyor.")]
        [SerializeField, Min(0.1f)] private float ringThickness = 0.55f;

        [Tooltip("İşaretin başın üstünde durduğu yükseklik, gövde boyunun üstüne eklenir.\n\n" +
                 "Duvar ve çatı 4,6'da. Gövde 3,7 boyunda, işaret köşesi üstünde döndüğü için kendi " +
                 "boyunun 0,87 katı yer kaplıyor, süzülme de ekleniyor — üçünün toplamı 4,6'nın altında " +
                 "kalmalı, yoksa kapıdan geçerken komşu avlunun çatısını deliyor.")]
        [SerializeField] private float beaconLift = 0.22f;

        [Tooltip("İşaretin boyu.")]
        [SerializeField, Min(0.05f)] private float beaconSize = 0.58f;

        [Tooltip("Süzülme genliği ve bir turun süresi. Sıfır genlik işareti çiviler.")]
        [SerializeField] private float bobHeight = 0.18f;
        [SerializeField, Min(0.1f)] private float bobSeconds = 1.7f;

        [Tooltip("İşaretin kendi ekseninde dönme hızı, saniyedeki derece.")]
        [SerializeField] private float spinDegreesPerSecond = 90f;

        /// <summary>
        /// A blue nobody else in the yard wears. The staff are green, teal and olive, the customers are
        /// grey-blue and the ore is whatever the island digs — this has to be none of those on any island,
        /// which is why it is not tinted per yard like everything else in here.
        /// </summary>
        private static readonly Color Signal = new Color(0.40f, 0.86f, 1f);

        private Transform _beacon;
        private float _base;

        /// <summary>Builds the three pieces. <paramref name="bodyHeight"/> is the scaled height of the body.</summary>
        public void Build(float bodyHeight)
        {
            Disc("Halka", ringDiameter, 0.02f, Signal);
            // Sitting a hair above the ring so the pair reads as an annulus rather than two stacked discs.
            Disc("Golge", Mathf.Max(0.1f, ringDiameter - ringThickness * 2f), 0.035f,
                 new Color(0.11f, 0.09f, 0.08f));

            _base = bodyHeight + beaconLift;
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Isaret";
            Strip(cube);
            cube.transform.SetParent(transform, false);
            cube.transform.localPosition = new Vector3(0f, _base, 0f);
            // Turned onto a corner. There is no cone primitive, and a cube stood on its point is the
            // cheapest thing in the box that reads as a marker rather than as a crate.
            cube.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
            cube.transform.localScale = Vector3.one * beaconSize;
            Paint(cube, Signal);
            _beacon = cube.transform;
        }

        private void Disc(string name, float diameter, float lift, Color colour)
        {
            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = name;
            Strip(disc);
            disc.transform.SetParent(transform, false);
            disc.transform.localPosition = new Vector3(0f, lift, 0f);
            // Unity's cylinder is two units tall, so the y scale is half the thickness — and the thickness
            // wants to be nearly nothing. This is paint on a floor, not a plinth.
            disc.transform.localScale = new Vector3(diameter, 0.008f, diameter);
            Paint(disc, colour);
        }

        /// <summary>
        /// Colliders off, shadows off. The collider is the important half: a primitive arrives with one,
        /// and a capsule collider parked at the player's feet is something for his own CharacterController
        /// to climb.
        /// </summary>
        private static void Strip(GameObject go)
        {
            Collider collider = go.GetComponent<Collider>();
            if (collider == null) return;
            // Switched off first. Destroy is deferred to the end of the frame, and the frame this is built
            // in is one the player's own CharacterController also moves in — a flat capsule sitting inside
            // it for that one step is something for it to climb out of.
            collider.enabled = false;
            Destroy(collider);
        }

        private static void Paint(GameObject go, Color colour)
        {
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null) return;
            renderer.sharedMaterial = MarketYardBuild.Mat(colour);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void Update()
        {
            if (_beacon == null) return;
            // Unscaled: the marker is the one thing that should keep moving while a popup has the game
            // paused, because it is what the player looks for when the popup goes away.
            float cycle = Time.unscaledTime / bobSeconds * Mathf.PI * 2f;
            _beacon.localPosition = new Vector3(0f, _base + Mathf.Sin(cycle) * bobHeight, 0f);
            _beacon.localRotation = Quaternion.Euler(45f, Time.unscaledTime * spinDegreesPerSecond, 45f);
        }
    }
}
