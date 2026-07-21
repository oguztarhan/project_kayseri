using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Rising smoke from the refinery chimney — the "it's working / heating" feedback (GDD §13). Builds a
    /// lightweight particle system in code (Sprites/Default material, always available under URP).
    /// </summary>
    public sealed class ChimneySmoke : MonoBehaviour
    {
        [SerializeField] private Vector3 localOffset = new Vector3(0f, 3.2f, 0f);
        [SerializeField] private Color color = new Color(0.55f, 0.55f, 0.58f, 0.6f);
        [SerializeField] private float rate = 10f;

        private void Start()
        {
            var go = new GameObject("Smoke");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localOffset;
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // cone points up

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startColor = color;
            main.startLifetime = 2.4f;
            main.startSpeed = 1.1f;
            main.startSize = 0.7f;
            main.maxParticles = 48;
            main.gravityModifier = -0.04f;

            var em = ps.emission; em.rateOverTime = rate;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 12f; sh.radius = 0.25f;

            var col = ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(0.6f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            go.GetComponent<ParticleSystemRenderer>().material = new Material(Shader.Find("Sprites/Default"));
            ps.Play();
        }
    }
}
