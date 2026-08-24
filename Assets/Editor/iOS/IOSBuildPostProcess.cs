using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
#if UNITY_IOS
using UnityEditor.iOS.Xcode;
#endif

namespace Game.EditorTools
{
    /// <summary>
    /// Unity'nin ve eklentilerin yazmadığı her şeyi Xcode projesine ekler. Bunlar elle yapılabilir
    /// ama her dışa aktarımda tekrar gerekir; unutulan tek anahtar bir ret turu demek.
    ///
    /// <see cref="callbackOrder"/> yüksek: Google'ın kendi plist işleyicisi (GADApplicationIdentifier,
    /// SKAdNetworkItems, NSUserTrackingUsageDescription) ve EDM4U'nun pod adımı önce çalışsın, biz
    /// yalnız eksik kalanı tamamlayalım.
    /// </summary>
    public sealed class IOSBuildPostProcess : IPostprocessBuildWithReport
    {
        /// <summary>
        /// ATT izin metninin dil tablosundaki anahtarı. Bu satır oyunda hiç gösterilmez; yalnız burada,
        /// derleme sırasında okunur. Yine de tabloda duruyor çünkü oyuncunun okuduğu her cümlenin tek
        /// bir yerde olması, çeviriye giden dosyanın da tek olması demek.
        /// </summary>
        private const string TrackingKey = "ios.att_izin";

        /// <summary>
        /// Tablo okunamazsa devreye giren son çare. ATT diyaloğunu açıklamasız göstermek App Review'da
        /// anında rettir, o yüzden burada mutlaka bir şey olmalı — ve İngilizce olmalı: temel dil
        /// eşleşmeyen her cihazın gördüğü metindir.
        /// </summary>
        private const string TrackingFallback =
            "Used to make the ads you see more relevant. If you decline, you will still see ads.";

        /// <summary>
        /// Eşleşme bulunamayan cihazın düştüğü dil. Reddin sebebi tam olarak buydu: temel metin
        /// Türkçeydi, yani İspanyol bir inceleme uzmanı Türkçe bir izin diyaloğu gördü.
        /// </summary>
        private const string BaseLanguage = "en";

        private const string PrivacyManifest = "PrivacyInfo.xcprivacy";

        public int callbackOrder => 100;

        public void OnPostprocessBuild(BuildReport report)
        {
#if UNITY_IOS
            if (report.summary.platform != BuildTarget.iOS) return;

            string root = report.summary.outputPath;
            Dictionary<string, string> tracking = TrackingStrings();
            PatchPlist(root, tracking);
            AddTrackingLocalizations(root, tracking);
            AddPrivacyManifest(root);
            LinkFrameworks(root);
#endif
        }

        /// <summary>
        /// Dil tablosundan ATT satırını çeker -> dil kodu (tr, en, es, ...) -> metin. Tablo, kodun
        /// değil verinin sahibi: yeni bir dil sütunu eklendiğinde burası da kendiliğinden o dili yazar.
        /// </summary>
        private static Dictionary<string, string> TrackingStrings()
        {
            var sonuc = new Dictionary<string, string>();
            string yol = Path.Combine(Application.dataPath, "Resources/Diller/metinler.txt");
            if (!File.Exists(yol))
            {
                Debug.LogError("[iOS] Dil tablosu bulunamadı: " + yol);
                return sonuc;
            }

            string[] satirlar = File.ReadAllLines(yol, Encoding.UTF8);
            string[] kodlar = null;
            for (int i = 0; i < satirlar.Length; i++)
            {
                string satir = satirlar[i];
                if (satir.Length == 0 || satir[0] == '#') continue;

                string[] hucre = satir.Split('\t');
                if (kodlar == null) { kodlar = hucre; continue; }   // ilk veri satırı başlıktır
                if (hucre[0] != TrackingKey) continue;

                for (int s = 1; s < kodlar.Length && s < hucre.Length; s++)
                    if (!string.IsNullOrEmpty(hucre[s])) sonuc[kodlar[s]] = hucre[s];
                break;
            }

            if (sonuc.Count == 0)
                Debug.LogError("[iOS] Dil tablosunda '" + TrackingKey + "' satırı yok; ATT metni " +
                               "yalnız İngilizce gidecek.");
            return sonuc;
        }

#if UNITY_IOS
        private static void PatchPlist(string root, Dictionary<string, string> tracking)
        {
            string path = Path.Combine(root, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(path);

            // İhracat beyanı: kütüphane şifrelemesi yalnız yerel kayıt dosyasını karıştırmak için
            // kullanılıyor, kitlesel pazar istisnası kapsamında. Bu anahtar olmadan App Store Connect
            // her yüklemede aynı soruyu tekrar sorar.
            plist.root.SetBoolean("ITSAppUsesNonExemptEncryption", false);

            // Bu anahtarın tek sahibi burasıdır. GoogleMobileAdsSettings'teki alan bilerek boş
            // bırakılıyor: orası tek dil yazabilir, oysa metnin on bir dili var ve hangisinin
            // gösterileceğine iOS karar veriyor. Buradaki değer yalnızca temel (eşleşmeyen) dil.
            string taban;
            if (!tracking.TryGetValue(BaseLanguage, out taban) || string.IsNullOrEmpty(taban))
                taban = TrackingFallback;
            plist.root.SetString("NSUserTrackingUsageDescription", taban);

            // Cihazın dili listede yoksa iOS geliştirme bölgesine düşer; orası İngilizce olmalı.
            plist.root.SetString("CFBundleDevelopmentRegion", BaseLanguage);

            // .lproj klasörleri paketi zaten yerelleştirilmiş sayar; bu liste App Store ürün
            // sayfasındaki "Diller" satırını da doldurur.
            if (tracking.Count > 0)
            {
                PlistElementArray diller = plist.root.CreateArray("CFBundleLocalizations");
                foreach (KeyValuePair<string, string> dil in tracking) diller.AddString(dil.Key);
            }

            plist.WriteToFile(path);
        }

        /// <summary>
        /// Her dil için bir <c>&lt;kod&gt;.lproj/InfoPlist.strings</c> yazar ve ana hedefe ekler.
        /// İzin diyaloğunun metnini dile göre değiştirmenin başka yolu yok: ATT penceresini iOS
        /// çiziyor, uygulamanın kendi çeviri katmanı oraya hiç ulaşmıyor.
        /// </summary>
        private static void AddTrackingLocalizations(string root, Dictionary<string, string> tracking)
        {
            if (tracking.Count == 0) return;

            string projectPath = PBXProject.GetPBXProjectPath(root);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            string target = project.GetUnityMainTargetGuid();

            var utf8 = new UTF8Encoding(false);
            foreach (KeyValuePair<string, string> dil in tracking)
            {
                string goreceli = dil.Key + ".lproj/InfoPlist.strings";
                string tam = Path.Combine(root, goreceli);
                Directory.CreateDirectory(Path.GetDirectoryName(tam));
                File.WriteAllText(tam,
                    "/* Otomatik üretildi: Assets/Resources/Diller/metinler.txt -> " + TrackingKey +
                    ". Elle düzenleme, sonraki derlemede üzerine yazılır. */\n" +
                    "\"NSUserTrackingUsageDescription\" = \"" + Kacir(dil.Value) + "\";\n", utf8);

                project.AddFileToBuild(target, project.AddFile(goreceli, goreceli));
            }

            project.WriteToFile(projectPath);
            Debug.Log("[iOS] ATT izin metni " + tracking.Count + " dile yazıldı.");
        }

        /// <summary>.strings biçimi C dizesi: tırnak ve ters bölü kaçırılmalı.</summary>
        private static string Kacir(string metin)
        {
            return metin.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static void AddPrivacyManifest(string root)
        {
            string source = Path.Combine(Application.dataPath, "Editor/iOS/" + PrivacyManifest);
            if (!File.Exists(source))
            {
                Debug.LogError("[iOS] " + PrivacyManifest + " bulunamadı: " + source);
                return;
            }

            File.Copy(source, Path.Combine(root, PrivacyManifest), true);

            string projectPath = PBXProject.GetPBXProjectPath(root);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);

            // Ana uygulama hedefi: manifest uygulamanın kendi veri toplama beyanı, UnityFramework'ün değil.
            string target = project.GetUnityMainTargetGuid();
            string file = project.AddFile(PrivacyManifest, PrivacyManifest);
            project.AddFileToBuild(target, file);
            project.WriteToFile(projectPath);
        }

        private static void LinkFrameworks(string root)
        {
            string projectPath = PBXProject.GetPBXProjectPath(root);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);

            // Plugins/iOS buraya derlenir, o yüzden .mm dosyalarının ihtiyacı olan çerçeveler burada.
            string framework = project.GetUnityFrameworkTargetGuid();
            project.AddFrameworkToProject(framework, "StoreKit.framework", false);
            // Weak: dağıtım hedefi 15.0 olsa da ATT yalnız iOS 14+ var ve köprü @available ile korunuyor.
            project.AddFrameworkToProject(framework, "AppTrackingTransparency.framework", true);
            project.WriteToFile(projectPath);
        }
#endif
    }
}
