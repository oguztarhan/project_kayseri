using System;
using UnityEngine;

namespace Game.Systems
{
    /// <summary>
    /// Builds the public store page for the platform the player is running. This is deliberately a
    /// normal store link rather than the quota-controlled in-app review prompt: the Settings row is a
    /// user-initiated action and must always lead somewhere visible.
    /// </summary>
    public static class StorePage
    {
        private const string GooglePlayBase = "https://play.google.com/store/apps/details?id=";
        private const string AppStoreBase = "https://apps.apple.com/app/id";
        private const string AppStoreSearch = "https://apps.apple.com/us/search?term=";

        public static string GooglePlayUrl(string packageName)
        {
            return GooglePlayBase + Uri.EscapeDataString(packageName ?? string.Empty);
        }

        /// <summary>
        /// Apple product links require the numeric App Store Connect ID; the bundle identifier is not
        /// accepted in its place. Before that ID exists, keep the row useful by opening an App Store
        /// search for the product name instead of sending the player to a broken placeholder URL.
        /// </summary>
        public static string AppStoreUrl(string appStoreId, string productName)
        {
            string id = NumericId(appStoreId);
            return id.Length > 0
                ? AppStoreBase + id
                : AppStoreSearch + Uri.EscapeDataString(productName ?? string.Empty);
        }

        public static void Open(string appStoreId)
        {
#if UNITY_IOS && !UNITY_EDITOR
            string url = AppStoreUrl(appStoreId, Application.productName);
#else
            // The Editor follows the Android route as well, so the authored button can be exercised
            // without producing an App Store search every time Play Mode is used on Windows.
            string url = GooglePlayUrl(Application.identifier);
#endif
            Application.OpenURL(url);
        }

        private static string NumericId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            string trimmed = value.Trim();
            if (trimmed.StartsWith("id", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed.Substring(2);
            if (trimmed.Length == 0) return string.Empty;

            for (int i = 0; i < trimmed.Length; i++)
                if (!char.IsDigit(trimmed[i])) return string.Empty;
            return trimmed;
        }
    }
}
