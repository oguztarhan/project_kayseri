using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// The event schedule. One asset holds every event the build knows about; the service reads it
    /// once and asks <see cref="Game.Core.LiveEvents"/> what is running.
    /// Create via: Assets &gt; Create &gt; Ore Empire &gt; Live Event Config.
    ///
    /// DATES ARE AUTHORED AS DATES. The runtime wants unix seconds, but nobody can proof-read a
    /// ten-digit number in an Inspector, and a mistyped digit is an event that opens in 1974. So a row
    /// carries a UTC date string and a length in days, and this file does the arithmetic. A row whose
    /// date will not parse is DROPPED with a warning rather than clamped to something plausible —
    /// silently shipping an event on the wrong day is worse than shipping none.
    ///
    /// UTC, ALWAYS. The window is the same second everywhere on earth. A local-time schedule would
    /// open the event eleven hours apart for two players and make every screenshot of a countdown
    /// unfalsifiable.
    ///
    /// The game runs without this asset — no asset is no events, which is the correct behaviour for a
    /// build made before any event was authored, and the same fallback the crate and the bench keep.
    /// </summary>
    [CreateAssetMenu(fileName = "LiveEventConfig", menuName = "Ore Empire/Live Event Config", order = 24)]
    public sealed class LiveEventConfig : ScriptableObject
    {
        /// <summary>The date format every row is authored in. Fixed and invariant: the parse must not
        /// depend on the editor machine's locale, or a Turkish Windows and an English one disagree
        /// about what the config says.</summary>
        public const string DateFormat = "yyyy-MM-dd HH:mm";

        [Serializable]
        private sealed class Row
        {
            [Tooltip("Kalıcı kimlik. Kayıt satırı buna bağlı — adı değişen etkinlik YENİ bir kimliktir, " +
                     "düzenlenmiş bir kimlik değil. Yeniden kullanmak eski ilerlemeyi yeni içeriğe bağlar.")]
            public string id = "";

            [Tooltip("İçeriğin sahibi olan modül. Bu dosya etkinlikleri zamanlar, ne yaptıklarını bilmez.")]
            public int kind;

            [Tooltip("Başlangıç, UTC. Biçim: 2026-10-01 00:00")]
            public string startUtc = "";

            [Tooltip("Pencerenin uzunluğu, gün. 7 = bir hafta.")]
            public double durationDays = 7d;

            [Tooltip("İçerik yayınlandıktan SONRA değiştirildiğinde artırılır. Artırmak, o sürümde " +
                     "kazanılmış ilerlemeyi düşürür; alınmış ödülleri ASLA düşürmez.")]
            public int configVersion = 1;

            [Tooltip("Bu etkinliğin taşıdığı ödül yuvası sayısı — kilometre taşları, görev satırları.")]
            [Min(1)] public int slots = 1;

            [Tooltip("Etkinliğin görünmesi için gereken ada sayısı. 0 = herkese açık.")]
            [Min(0)] public int minIslands;

            [Tooltip("Etkinliğin görünmesi için tamamlanmış olması gereken bölüm sayısı. " +
                     "0 = bölüm şartı yok, 1 = Bölüm 1 tamamlanmış.")]
            [Min(0)] public int minCompletedChapters;
        }

        [Header("Etkinlikler")]
        [SerializeField] private Row[] events = new Row[0];

        /// <summary>
        /// The well-formed definitions, in config order. Malformed rows and duplicate ids are dropped
        /// here rather than at the call site, so the service never holds a definition it has to
        /// re-check. Both drops log: a config mistake that produces no event and no message is a
        /// bug report that says "the event did not start" and nothing else.
        /// </summary>
        public List<Game.Core.LiveEvents.Definition> Definitions()
        {
            var list = new List<Game.Core.LiveEvents.Definition>(events != null ? events.Length : 0);
            if (events == null) return list;

            for (int i = 0; i < events.Length; i++)
            {
                Row r = events[i];
                if (r == null) continue;

                if (!TryParseUtc(r.startUtc, out long start))
                {
                    Debug.LogWarning("[LiveEvents] '" + r.id + "' atlandı: tarih okunamadı ('" +
                                     r.startUtc + "'), beklenen biçim " + DateFormat + ".");
                    continue;
                }

                var d = new Game.Core.LiveEvents.Definition
                {
                    Id            = r.id,
                    Kind          = r.kind,
                    StartUnix     = start,
                    EndUnix       = start + (long)Math.Round(r.durationDays * 86400d),
                    ConfigVersion = r.configVersion,
                    Slots         = r.slots,
                    MinIslands    = r.minIslands,
                    MinCompletedChapters = r.minCompletedChapters,
                };

                if (!Game.Core.LiveEvents.IsWellFormed(d))
                {
                    Debug.LogWarning("[LiveEvents] '" + r.id + "' atlandı: tanım geçersiz (kimlik boş, " +
                                     "yuva sayısı aralık dışı ya da süre sıfır).");
                    continue;
                }

                bool duplicate = false;
                for (int j = 0; j < list.Count; j++)
                    if (string.Equals(list[j].Id, d.Id, StringComparison.Ordinal)) { duplicate = true; break; }

                if (duplicate)
                {
                    Debug.LogWarning("[LiveEvents] '" + d.Id + "' atlandı: bu kimlik zaten tanımlı. " +
                                     "İki satır aynı kayıt satırını paylaşamaz.");
                    continue;
                }

                list.Add(d);
            }

            return list;
        }

        /// <summary>Exact-format UTC parse. Exact rather than lenient on purpose: a permissive parse
        /// reads "01-10-2026" as a date too, just not the one that was meant.</summary>
        public static bool TryParseUtc(string text, out long unixSeconds)
        {
            unixSeconds = 0L;
            if (string.IsNullOrEmpty(text)) return false;

            if (!DateTime.TryParseExact(text.Trim(), DateFormat, CultureInfo.InvariantCulture,
                                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                                        out DateTime parsed))
                return false;

            unixSeconds = new DateTimeOffset(parsed, TimeSpan.Zero).ToUnixTimeSeconds();
            return unixSeconds > 0L;
        }
    }
}
