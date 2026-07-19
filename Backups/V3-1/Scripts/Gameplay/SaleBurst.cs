using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>Gold coin burst when the market makes a sale (GDD §13 juice). Listens to Market.Sold.</summary>
    public sealed class SaleBurst : MonoBehaviour
    {
        [SerializeField] private Market market;
        [SerializeField] private Vector3 localOffset = new Vector3(0f, 2.2f, 0f);

        private ParticleSystem _ps;
        private float _cooldown;

        private void Awake()
        {
            var go = new GameObject("SaleFX");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localOffset;

            _ps = go.AddComponent<ParticleSystem>();
            var main = _ps.main;
            main.startColor = new Color(1f, 0.85f, 0.2f, 1f);
            main.startLifetime = 0.8f;
            main.startSpeed = 3f;
            main.startSize = 0.35f;
            main.maxParticles = 60;
            main.gravityModifier = 1.2f;

            var em = _ps.emission; em.enabled = false; // manual bursts only
            var sh = _ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.2f;

            go.GetComponent<ParticleSystemRenderer>().material = new Material(Shader.Find("Sprites/Default"));
            _ps.Stop();
        }

        private void OnEnable() { if (market != null) market.Sold += OnSold; }
        private void OnDisable() { if (market != null) market.Sold -= OnSold; }

        private void OnSold(BigDouble revenue)
        {
            if (Time.time < _cooldown) return;
            _cooldown = Time.time + 0.3f;
            if (_ps != null) _ps.Emit(12);
        }
    }
}
