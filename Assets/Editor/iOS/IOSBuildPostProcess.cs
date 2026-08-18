using System.IO;
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
        /// GoogleMobileAdsSettings boş bırakılırsa devreye giren yedek metin. ATT diyaloğunu
        /// açıklamasız göstermek App Review'da anında rettir, o yüzden burada bir şey mutlaka olmalı.
        /// </summary>
        private const string TrackingUsage =
            "Reklamların sana daha uygun olması için kullanılır. İzin vermezsen reklamlar yine gösterilir.";

        private const string PrivacyManifest = "PrivacyInfo.xcprivacy";

        public int callbackOrder => 100;

        public void OnPostprocessBuild(BuildReport report)
        {
#if UNITY_IOS
            if (report.summary.platform != BuildTarget.iOS) return;

            string root = report.summary.outputPath;
            PatchPlist(root);
            AddPrivacyManifest(root);
            LinkFrameworks(root);
#endif
        }

#if UNITY_IOS
        private static void PatchPlist(string root)
        {
            string path = Path.Combine(root, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(path);

            // İhracat beyanı: kütüphane şifrelemesi yalnız yerel kayıt dosyasını karıştırmak için
            // kullanılıyor, kitlesel pazar istisnası kapsamında. Bu anahtar olmadan App Store Connect
            // her yüklemede aynı soruyu tekrar sorar.
            plist.root.SetBoolean("ITSAppUsesNonExemptEncryption", false);

            // Google'ın işleyicisi bunu GoogleMobileAdsSettings'ten yazar; orası boşsa burada dolar.
            if (!plist.root.values.ContainsKey("NSUserTrackingUsageDescription"))
            {
                Debug.LogWarning("[iOS] NSUserTrackingUsageDescription boştu, yedek metin yazıldı. " +
                                 "Kalıcı çözüm: GoogleMobileAdsSettings.asset içindeki alanı doldur.");
                plist.root.SetString("NSUserTrackingUsageDescription", TrackingUsage);
            }

            plist.WriteToFile(path);
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
