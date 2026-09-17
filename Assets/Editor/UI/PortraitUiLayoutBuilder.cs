using Game.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class PortraitUiLayoutBuilder
{
    [MenuItem("Tools/UI/Apply Portrait Settings and HUD")]
    public static void Build()
    {
        string path = "Assets/Prefabs/UI/UI_Ayarlar.prefab";
        GameObject prefab = PrefabUtility.LoadPrefabContents(path);
        Settings(prefab);
        PrefabUtility.SaveAsPrefabAsset(prefab, path);
        PrefabUtility.UnloadPrefabContents(prefab);
        foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            var collection = root.GetComponent<Game.UI.CardCollectionUI>();
            if (collection != null)
            {
                var collectionSo = new SerializedObject(collection);
                collectionSo.FindProperty("_usePortraitArtwork").boolValue = true;
                collectionSo.ApplyModifiedPropertiesWithoutUndo();
            }
            if (root.name == "UI_Ayarlar") Settings(root);
            if (root.name == "UI_HUD") Hud(root);
            if (root.name == "UI_HUD" || root.name == "UI_Ayarlar")
                foreach (Component component in root.GetComponentsInChildren<Component>(true))
                {
                    if (component == null) continue;
                    EditorUtility.SetDirty(component);
                    if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                }
            if (root.name == "UI_HUD" || root.name == "UI_Ayarlar")
                foreach (Component component in root.GetComponentsInChildren<Component>(true))
                {
                    if (component == null) continue;
                    EditorUtility.SetDirty(component);
                    if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                }
        }
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
    }

    private static void Rect(RectTransform r, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        r.anchorMin = r.anchorMax = anchor; r.pivot = pivot; r.anchoredPosition = pos;
        r.sizeDelta = size; r.localScale = Vector3.one;
    }

    private static void Settings(GameObject root)
    {
        Transform letterbox = root.transform.Find("GuvenliAlan/Pencere/Letterbox");
        var fit = new SerializedObject(letterbox.GetComponent<LetterboxRoot>());
        fit.FindProperty("designSize").vector2Value = new Vector2(1080, 1920);
        fit.FindProperty("landscapeFit").enumValueIndex = 1;
        fit.ApplyModifiedPropertiesWithoutUndo();
        var panel = (RectTransform)letterbox.Find("Panel");
        Sprite panelSprite = PortraitUiArt.Get("settings-settings-panel");
        float panelHeight = 940 * panelSprite.rect.height / panelSprite.rect.width;
        Rect(panel, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -230), new Vector2(940, panelHeight));
        PortraitUiArt.Apply(panel.GetComponent<Image>(), panelSprite);
        var header = (RectTransform)letterbox.Find("Serit");
        Rect(header, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -155), new Vector2(750, 150));
        PortraitUiArt.Apply(header.GetComponent<Image>(), "general-title-plate");
        var title = header.Find("Baslik").GetComponent<TMP_Text>();
        UiBuild.Anchor((RectTransform)title.transform, new Vector2(0.14f, 0.22f), new Vector2(0.86f, 0.78f));
        title.fontSize = 52; title.color = new Color32(20, 47, 79, 255);
        var close = (RectTransform)letterbox.Find("BtnKapat");
        Rect(close, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(428, -232), new Vector2(112, 112));
        PortraitUiArt.Apply(close.GetComponent<Image>(), "general-close-button");

        Transform previous = panel.Find("SettingsViewport");
        RectTransform viewport;
        RectTransform content;
        if (previous == null)
        {
            var v = new GameObject("SettingsViewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            v.transform.SetParent(panel, false); viewport = (RectTransform)v.transform;
            v.GetComponent<Image>().color = new Color(0, 0, 0, 0.001f);
            var c = new GameObject("SettingsRows", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            c.transform.SetParent(viewport, false); content = (RectTransform)c.transform;
        }
        else { viewport = (RectTransform)previous; content = (RectTransform)viewport.Find("SettingsRows"); }
        Rect(viewport, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -110), new Vector2(800, panelHeight - 195));
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(0.5f, 1);
        content.anchoredPosition = Vector2.zero; content.sizeDelta = Vector2.zero;
        var group = content.GetComponent<VerticalLayoutGroup>();
        group.spacing = 14; group.padding = new RectOffset(0, 0, 10, 10);
        group.childControlWidth = group.childControlHeight = true; group.childForceExpandWidth = true; group.childForceExpandHeight = false;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = viewport.GetComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = content;
        scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40;
        string[] rows = { "SatirSes", "SatirMuzik", "SatirTitresim", "SatirDil", "SatirDegerlendir", "SatirGizlilik", "SatirGeriYukle", "SatirReklamTercihleri" };
        string[] icons = { "sound-icon", "music-icon", "vibration-icon", "language-icon", "rate-us-icon", "privacy-icon", "restore-purchases-icon", "privacy-icon" };
        for (int i = 0; i < rows.Length; i++)
        {
            Transform row = panel.Find(rows[i]); if (row == null) row = content.Find(rows[i]);
            row.SetParent(content, false); row.SetSiblingIndex(i);
            var le = row.GetComponent<LayoutElement>(); if (le == null) le = row.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = i < 2 ? 145 : 128; le.flexibleHeight = 0;
            var rowImage = row.GetComponent<Image>();
            rowImage.sprite = null; rowImage.color = new Color(0, 0, 0, 0); rowImage.raycastTarget = true;
            Transform medallion = row.Find("Madalyon"); if (medallion != null) medallion.gameObject.SetActive(false);
            var icon = row.Find("Ikon").GetComponent<Image>();
            Rect(icon.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(110, 110));
            PortraitUiArt.Apply(icon, "settings-" + icons[i]);
            var label = row.Find("Ad").GetComponent<TMP_Text>();
            Rect((RectTransform)label.transform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(145, i < 2 ? 34 : 0), new Vector2(i == 2 ? 395 : 550, 60));
            label.enableAutoSizing = true; label.fontSizeMin = 28; label.fontSizeMax = 36;
            label.color = new Color32(22, 57, 89, 255); label.textWrappingMode = TextWrappingModes.Normal;
            Transform arrow = row.Find("Ok");
            if (arrow != null) { Rect((RectTransform)arrow, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-4, 0), new Vector2(66, 66)); }
            if (i < 2)
            {
                var slider = row.Find("Slider").GetComponent<Slider>();
                Rect((RectTransform)slider.transform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(150, -28), new Vector2(620, 62));
                // Keep the functional fill and handle independent. The complete slider export is a reference, not a frozen value.
                var handle = slider.handleRect; handle.sizeDelta = new Vector2(70, 0);
                handle.GetComponent<Image>().preserveAspect = true;
            }
            if (i == 2)
            {
                var toggle = row.Find("Anahtar").GetComponent<Image>();
                Rect(toggle.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-5, 0), new Vector2(240, 100));
                PortraitUiArt.Apply(toggle, "settings-vibration-toggle-on");
            }
        }
        var settings = root.GetComponent<SettingsUI>();
        var so = new SerializedObject(settings);
        so.FindProperty("switchOn").objectReferenceValue = PortraitUiArt.Get("settings-vibration-toggle-on");
        so.FindProperty("switchOff").objectReferenceValue = PortraitUiArt.Get("settings-vibration-toggle-off");
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(root);
    }

    private static void Hud(GameObject root)
    {
        string[,] map = { {"BtnAyarlar","settings-icon"}, {"BtnMagaza","shop-icon"}, {"BtnGunluk","daily-reward-icon"},
            {"BtnKontrat","goals-icon"}, {"BtnBedava","rewarded-ad-icon"}, {"BtnTeklif","reward-chest"}, {"BtnYukselt","upgrade-button"} };
        foreach (Image im in root.GetComponentsInChildren<Image>(true))
            for (int i = 0; i < map.GetLength(0); i++) if (im.name == map[i, 0]) PortraitUiArt.Apply(im, "general-" + map[i, 1]);
        foreach (string name in new[] { "PillAltin", "PillElmas" })
        {
            var pill = root.transform.Find("GuvenliAlan/" + name).GetComponent<Image>();
            PortraitUiArt.Apply(pill, name == "PillAltin" ? "general-cash-pill-transparent" : "general-gem-pill");
            foreach (Transform child in pill.transform)
                if (child.name.StartsWith("Ikon") || child.name == "Arti") child.gameObject.SetActive(false);
            var text = pill.transform.Find("Deger").GetComponent<TMP_Text>();
            UiBuild.Anchor((RectTransform)text.transform, new Vector2(0.38f, 0.20f), new Vector2(0.87f, 0.82f));
            text.color = new Color32(19, 51, 84, 255); text.fontSizeMin = 24; text.fontSizeMax = 40;
        }
    }
}
