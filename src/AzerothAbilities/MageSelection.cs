using Assets.Scripts.Actors.Player;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UObject = UnityEngine.Object;

namespace AzerothAbilities;

// A separate native uGUI panel. No IMGUI, asset decoding, custom shaders, or save writes.
// The class controls spells; the selected Megabonk character continues to own the appearance.
public sealed class MageSelectionBehaviour : MonoBehaviour
{
    public MageSelectionBehaviour(IntPtr pointer) : base(pointer) { }

    private MainMenu menu;
    private GameObject root;
    private TextMeshProUGUI status;
    private TextMeshProUGUI mageLabel;
    private Button mageButton;
    private Button nativeButton;
    private UnityAction chooseMage;
    private UnityAction chooseNative;
    private float nextRefresh = 1f;
    private bool failed;
    private bool lastVisible;
    private string lastSelection;

    private void Update()
    {
        if (failed || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.25f;
        try
        {
            if(ClassCatalogs.HasHeroRoster){SetVisible(false);return;}
            var plugin = Plugin.Current;
            if (plugin?.Catalog == null) return;
            if (IsRunStarted()) { SetVisible(false); return; }
            if (menu == null) menu = UObject.FindObjectOfType<MainMenu>();
            if (menu == null || !IsSelectionTab()) { SetVisible(false); return; }
            if (root == null)
            {
                TextMeshProUGUI template = null;
                // This game's Component.GetComponentInChildren<T>(bool) wrapper fails verification.
                // Resources<T> is used successfully by the existing laboratory probes.
                foreach (var candidate in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
                {
                    if (candidate == null || !candidate.gameObject.activeInHierarchy || candidate.font == null) continue;
                    template = candidate;
                    break;
                }
                if (template == null || template.font == null) return;
                Build(template);
            }
            SetVisible(true);
            RefreshSelection();
        }
        catch (Exception ex)
        {
            // Fail only this panel. Native menus and all native inventory cards stay available.
            failed = true;
            if (root != null) root.SetActive(false);
            Plugin.Current?.Log.LogError("Mage selection UI disabled; native menu retained: " + ex);
        }
    }

    [HideFromIl2Cpp]
    private static bool IsRunStarted()
    {
        var player = MyPlayer.Instance;
        return player != null && player.hasStarted;
    }

    [HideFromIl2Cpp]
    private bool IsSelectionTab()
    {
        if (!menu.isActiveAndEnabled) return false;
        if (IsActive(menu.settings) || IsActive(menu.credits) || IsActive(menu.quests)
            || IsActive(menu.leaderboardsFull) || IsActive(menu.blockRaycastOverlay)) return false;
        return IsActive(menu.tabMenu) || IsActive(menu.tabCharacters) || IsActive(menu.tabMaps);
    }

    [HideFromIl2Cpp]
    private static bool IsActive(GameObject value) => value != null && value.activeInHierarchy;

    [HideFromIl2Cpp]
    private static GameObject RectObject(string name, Transform parent)
    {
        var go = new GameObject(name, new[] { Il2CppType.Of<RectTransform>() });
        if (parent != null) go.transform.SetParent(parent, false);
        return go;
    }

    [HideFromIl2Cpp]
    private static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 position)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        rect.localScale = Vector3.one;
    }

    [HideFromIl2Cpp]
    private void Build(TextMeshProUGUI template)
    {
        root = RectObject("AzerothMageSelection", null);
        root.SetActive(false);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();

        var panel = RectObject("MageClassPanel", root.transform);
        var rect = panel.GetComponent<RectTransform>();
        // Header position leaves the native character confirm/back controls untouched.
        Place(rect, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(690, 205), new Vector2(0, -16));
        var background = panel.AddComponent<Image>();
        background.color = new Color(0.035f, 0.035f, 0.055f, 0.96f);
        background.raycastTarget = false;

        CreateText(panel.transform, template, "Title", "Azeroth: Маг", 28, new Vector2(20, -13), new Vector2(650, 35), new Color(1, 0.83f, 0.36f));
        status = CreateText(panel.transform, template, "SelectedStatus", "", 23, new Vector2(20, -49), new Vector2(650, 33), Color.white);
        CreateText(panel.transform, template, "AppearanceNotice", "Модель, анимации и заклинания — Azeroth.\nМувмент и появление врагов — Megabonk.", 19, new Vector2(20, -86), new Vector2(650, 55), new Color(0.86f, 0.88f, 0.94f));

        mageButton = CreateButton(panel.transform, template, "SelectMage", "Выбрать мага", new Vector2(20, -148), new Vector2(310, 43), out mageLabel);
        nativeButton = CreateButton(panel.transform, template, "SelectNative", "Обычный герой Megabonk", new Vector2(348, -148), new Vector2(322, 43), out _);
        chooseMage = (UnityAction)(Action)SelectMage;
        chooseNative = (UnityAction)(Action)SelectNative;
        mageButton.onClick.AddListener(chooseMage);
        nativeButton.onClick.AddListener(chooseNative);
        lastSelection = null;
        lastVisible = false;
        Plugin.Current.Log.LogInfo("Mage selection UI built: native Canvas/TMP; title=Azeroth: Маг; menu/character/map tabs only; original Mage appearance; no save or native book changes.");
    }

    [HideFromIl2Cpp]
    private static TextMeshProUGUI CreateText(Transform parent, TextMeshProUGUI template, string name, string value, float fontSize, Vector2 position, Vector2 size, Color color)
    {
        var go = RectObject(name, parent);
        Place(go.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(0, 1), size, position);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.font = template.font;
        text.fontSharedMaterial = template.fontSharedMaterial;
        text.fontSize = fontSize;
        text.enableAutoSizing = false;
        text.richText = false;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.color = color;
        text.raycastTarget = false;
        text.text = value;
        return text;
    }

    [HideFromIl2Cpp]
    private static Button CreateButton(Transform parent, TextMeshProUGUI template, string name, string label, Vector2 position, Vector2 size, out TextMeshProUGUI text)
    {
        var go = RectObject(name, parent);
        Place(go.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(0, 1), size, position);
        var image = go.AddComponent<Image>();
        image.color = new Color(0.19f, 0.23f, 0.34f, 1);
        image.raycastTarget = true;
        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        var navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;
        text = CreateText(go.transform, template, "Label", label, 20, new Vector2(9, 0), new Vector2(size.x - 18, size.y), Color.white);
        text.alignment = TextAlignmentOptions.Center;
        return button;
    }

    [HideFromIl2Cpp]
    private void SetVisible(bool visible)
    {
        if (root == null) return;
        if (root.activeSelf != visible) root.SetActive(visible);
        if (lastVisible != visible)
        {
            lastVisible = visible;
            Plugin.Current?.Log.LogInfo("Mage selection UI visible=" + visible);
        }
    }

    [HideFromIl2Cpp]
    private void RefreshSelection()
    {
        var selected = Plugin.Current.SelectedClass.Value;
        if (selected == lastSelection) return;
        lastSelection = selected;
        var mage = selected == "mage";
        status.text = mage ? "Маг Azeroth выбран" : "Маг не выбран — обычные способности Megabonk";
        status.color = mage ? new Color(0.5f, 1, 0.61f) : Color.white;
        mageLabel.text = mage ? "Маг выбран" : "Выбрать мага";
        mageButton.interactable = !mage;
        nativeButton.interactable = mage;
        Plugin.Current.Log.LogInfo("Mage selection UI state: " + selected + "; " + status.text);
    }

    [HideFromIl2Cpp]
    private void SelectMage() => SelectClass("mage");

    [HideFromIl2Cpp]
    private void SelectNative() => SelectClass("none");

    [HideFromIl2Cpp]
    private void SelectClass(string classId)
    {
        try
        {
            if (IsRunStarted() || !IsSelectionTab()) return;
            if (HudBridge.SelectClass(classId))
            {
                lastSelection = null;
                RefreshSelection();
                Plugin.Current.Log.LogInfo("Mage selection button applied: " + classId + "; original Mage appearance active.");
            }
        }
        catch (Exception ex) { Plugin.Current?.Log.LogError("Mage selection click failed: " + ex); }
    }

    private void OnDestroy()
    {
        try
        {
            if (mageButton != null && chooseMage != null) mageButton.onClick.RemoveListener(chooseMage);
            if (nativeButton != null && chooseNative != null) nativeButton.onClick.RemoveListener(chooseNative);
            if (root != null) UObject.Destroy(root);
        }
        catch (Exception ex) { Plugin.Current?.Log.LogWarning("Mage selection cleanup: " + ex.Message); }
        root = null;
        chooseMage = chooseNative = null;
    }
}

