using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Kutlama konfetisi (faz atlayısı). Kağıt parçaları kodla sürülür: tek bir havuz Awake'te
    /// kurulur, <see cref="Play"/> her patlamada aynı parçaları yeniden fırlatır — patlama anında
    /// hiçbir şey ayrılmaz, ki o an zaten sahnenin en yüklü karesi.
    ///
    /// Parçaların sprite'ı yok; boş bir <see cref="Image"/> zaten dikdörtgen çiziyor ve konfeti
    /// dikdörtgen. Havuz kendi <see cref="Canvas"/>'ında durmalı (prefabda öyle bağlı): seksen
    /// nesne her karede kımıldadığı için aynı canvas'taki duran her şeyi de yeniden kurdururdu.
    ///
    /// Derinlik hilesi: parça z'de dönmüyor, localScale.x bir kosinüsle salınıyor. Kağıt havada
    /// kendi ekseninde takla atarken tam olarak böyle daralıp genişler — üç boyutlu bir şey
    /// çizmeden üç boyutlu okunmasının sebebi bu.
    /// </summary>
    public sealed class ConfettiBurst : MonoBehaviour
    {
        [Header("Patlama")]
        [Tooltip("Havuz boyu. Patlama başına fırlatılan parça sayısı da bu.")]
        [SerializeField] private int pieceCount = 80;
        [Tooltip("Bir parçanın havada kalma süresi (sn). Hepsi aynı anda inmesin diye ±%25 sapar.")]
        [SerializeField] private float lifeSeconds = 3.0f;
        [SerializeField] private Vector2 pieceSize = new Vector2(20f, 30f);
        [Tooltip("Boş bırakılırsa parçalar düz dikdörtgen kağıt olur. Bir sikke koyulursa aynı fizik "
                 + "para yağmuru oynatır: takla zaten madeni paranın kendi ekseninde dönüşü gibi okunuyor.")]
        [SerializeField] private Sprite pieceSprite;
        [SerializeField] private float speedMin = 900f;
        [SerializeField] private float speedMax = 2000f;
        [Tooltip("Yerçekimi (birim/sn²). Negatif = aşağı.")]
        [SerializeField] private float gravity = -1000f;
        [Tooltip("Bir saniye sonunda kalan hız oranı.\n\n" +
                 "Kağıdın son inme hızı yerçekimi / ln(1/buDeğer) çıkıyor; 0.11'de bu ~450 birim/sn, " +
                 "yani parça ekranı iki buçuk saniyede geçiyor. Daha yüksek bir değer konfetiyi taşa " +
                 "çevirip patlamayı görülmeden bitiriyor.")]
        [Range(0.02f, 0.9f)] [SerializeField] private float dragPerSecond = 0.11f;
        [Tooltip("Patlamanın çıktığı nokta, panel merkezine göre.")]
        [SerializeField] private Vector2 origin = new Vector2(0f, -120f);

        [SerializeField]
        private Color[] palette =
        {
            new Color(1.00f, 0.82f, 0.29f),   // altın
            new Color(0.97f, 0.59f, 0.20f),   // turuncu
            new Color(0.31f, 0.76f, 0.97f),   // gök
            new Color(0.23f, 0.44f, 0.88f),   // mavi
            new Color(0.31f, 0.82f, 0.48f),   // yeşil
            new Color(1.00f, 0.48f, 0.66f),   // pembe
            new Color(1.00f, 1.00f, 1.00f),   // beyaz
        };

        private struct Piece
        {
            public Vector2 pos;
            public Vector2 vel;
            public float life;      // kalan süre; <= 0 ise parça kapalı
            public float span;      // bu parçanın toplam ömrü
            public float spin;      // takla hızı
            public float phase;     // takla başlangıcı
            public float tilt;      // z dönüşü
            public float tiltRate;
        }

        private const float MaxStep = 0.05f;

        private RectTransform[] _rects;
        private Image[] _images;
        private Piece[] _pieces;
        private Color[] _colors;
        private bool _running;

        private void Awake()
        {
            if (pieceCount < 1) pieceCount = 1;
            _rects = new RectTransform[pieceCount];
            _images = new Image[pieceCount];
            _pieces = new Piece[pieceCount];
            _colors = new Color[pieceCount];

            for (int i = 0; i < pieceCount; i++)
            {
                GameObject go = new GameObject("Parca", typeof(RectTransform),
                                               typeof(CanvasRenderer), typeof(Image));
                RectTransform rt = (RectTransform)go.transform;
                rt.SetParent(transform, false);
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = pieceSize;

                Image im = go.GetComponent<Image>();
                im.raycastTarget = false;
                if (pieceSprite != null) im.sprite = pieceSprite;

                go.SetActive(false);
                _rects[i] = rt;
                _images[i] = im;
            }
        }

        /// <summary>Yeni bir patlama. Öncekini keser — üst üste binen iki kutlama olmaz.</summary>
        public void Play()
        {
            if (_pieces == null) return;

            for (int i = 0; i < _pieces.Length; i++)
            {
                // Yukarı yayılan bir yelpaze: yatayda tam serbest, aşağıya doğru çıkmıyor.
                // Aşağı fırlayan parça hemen ekrandan düşüyor ve patlamayı seyrek gösteriyor.
                float a = Random.Range(18f, 162f) * Mathf.Deg2Rad;
                float speed = Random.Range(speedMin, speedMax);
                float span = lifeSeconds * Random.Range(0.75f, 1.25f);

                _pieces[i].pos = origin + new Vector2(Random.Range(-40f, 40f), Random.Range(-30f, 30f));
                _pieces[i].vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * speed;
                _pieces[i].span = span;
                _pieces[i].life = span;
                _pieces[i].spin = Random.Range(6f, 17f) * (Random.value < 0.5f ? -1f : 1f);
                _pieces[i].phase = Random.Range(0f, Mathf.PI * 2f);
                _pieces[i].tilt = Random.Range(0f, 360f);
                _pieces[i].tiltRate = Random.Range(-260f, 260f);

                Color c = palette != null && palette.Length > 0
                    ? palette[Random.Range(0, palette.Length)]
                    : Color.white;
                _colors[i] = c;
                _images[i].color = c;
                _rects[i].anchoredPosition = _pieces[i].pos;
                _rects[i].gameObject.SetActive(true);
            }
            _running = true;
        }

        private void Update()
        {
            if (!_running) return;

            // Adımı sınırla. Tek bir uzun kare (sahne yüklemesi, GC duraklaması) kırpılmazsa
            // bütün kağıdı bir karede ekran dışına fırlatır ve kutlama hiç görünmez.
            float dt = Mathf.Min(Time.unscaledDeltaTime, MaxStep);
            float keep = Mathf.Pow(dragPerSecond, dt);
            bool any = false;

            for (int i = 0; i < _pieces.Length; i++)
            {
                if (_pieces[i].life <= 0f) continue;

                _pieces[i].life -= dt;
                if (_pieces[i].life <= 0f)
                {
                    _rects[i].gameObject.SetActive(false);
                    continue;
                }
                any = true;

                _pieces[i].vel.x *= keep;
                _pieces[i].vel.y = (_pieces[i].vel.y * keep) + gravity * dt;
                _pieces[i].pos += _pieces[i].vel * dt;
                _pieces[i].tilt += _pieces[i].tiltRate * dt;

                float age = _pieces[i].span - _pieces[i].life;
                float flip = Mathf.Cos(_pieces[i].phase + age * _pieces[i].spin);

                RectTransform rt = _rects[i];
                rt.anchoredPosition = _pieces[i].pos;
                rt.localRotation = Quaternion.Euler(0f, 0f, _pieces[i].tilt);
                rt.localScale = new Vector3(flip, 1f, 1f);

                // Son üçte birinde sönerek gider; yerde birden yok olan kağıt ucuz durur.
                float k = _pieces[i].life / _pieces[i].span;
                Color c = _colors[i];
                c.a = k < 0.34f ? k / 0.34f : 1f;
                _images[i].color = c;
            }

            if (!any) _running = false;
        }

        private void OnDisable()
        {
            if (_pieces == null) return;
            for (int i = 0; i < _pieces.Length; i++)
            {
                _pieces[i].life = 0f;
                _rects[i].gameObject.SetActive(false);
            }
            _running = false;
        }
    }
}
