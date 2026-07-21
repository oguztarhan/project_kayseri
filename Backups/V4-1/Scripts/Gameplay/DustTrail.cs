using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>Dust kicked up behind a vehicle while it moves (GDD §13 juice). Emits only when moving.</summary>
    public sealed class DustTrail : MonoBehaviour
    {
        [SerializeField] private Color color = new Color(0.78f, 0.68f, 0.5f, 0.55f);

        private ParticleSystem _ps;
        private Vector3 _last;

        private void Start()
        {
            var go = new GameObject("Dust");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.05f, -0.6f);

            _ps = go.AddComponent<ParticleSystem>();
            var main = _ps.main;
            main.startColor = color;
            main.startLifetime = 0.6f;
            main.startSpeed = 0.4f;
            main.startSize = 0.4f;
            main.maxParticles = 30;
            main.gravityModifier = -0.02f;

            var em = _ps.emission; em.rateOverTime = 0f;
            var sh = _ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.25f;

            var col = _ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            go.GetComponent<ParticleSystemRenderer>().material = new Material(Shader.Find("Sprites/Default"));
            _last = transform.position;
            _ps.Play();
        }

        private void Update()
        {
            float moved = (transform.position - _last).magnitude;
            _last = transform.position;
            var em = _ps.emission;
            em.rateOverTime = moved > 0.002f ? 14f : 0f;
        }
    }
}
