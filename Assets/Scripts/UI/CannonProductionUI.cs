using System;
using System.Collections.Generic;
using System.Text;
using Game.Core;
using Game.Data;
using Game.Gameplay;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Small portrait-world card for the first Cannon loop. It is created under the existing
    /// systems canvas at runtime so the scene keeps its authored UI prefab contract; every button
    /// delegates to CannonProductionWorldBridge and therefore to the real save/economy services.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public sealed class CannonProductionUI : MonoBehaviour
    {
        [SerializeField] private CannonProductionWorldBridge bridge;
        [SerializeField] private Color panelColor = new Color(0.035f, 0.055f, 0.09f, 0.96f);
        [SerializeField] private Color buttonColor = new Color(0.11f, 0.25f, 0.38f, 1f);
        [SerializeField] private Color accentColor = new Color(0.95f, 0.74f, 0.28f, 1f);

        private Canvas _canvas;
        private GameObject _panel;
        private RectTransform _machineTabs;
        private readonly List<Button> _machineButtons = new List<Button>();
        private bool _tabsBuilt;
        private int _tabMachineCount = -1;
        private string _selectedMachineId = ShipyardProgression.CannonMachineId;
        private string _selectedRecipeId;
        private Text _title;
        private Text _recipe;
        private Text _status;
        private Text _input;
        private Text _output;
        private Button _open;
        private Button _start;
        private Button _sell;
        private Button _equip;
        private Button _store;
        private Button _salvage;
        private Button _fulfill;
        private float _refreshTimer;
        private Font _font;
        private Rect _lastSafeArea = new Rect(-1f, -1f, -1f, -1f);
        private Vector2Int _lastScreenSize;

        private void Awake()
        {
            if (!ShipyardFeatureSwitch.IsEnabled(ServiceLocator.Get<SaveData>()))
            {
                enabled = false;
                return;
            }
            if (bridge == null) bridge = FindAnyObjectByType<CannonProductionWorldBridge>();
            Build();
            ApplySafeArea();
            _panel.SetActive(false);
        }

        private void Update()
        {
            ApplySafeArea();
            if (bridge == null)
            {
                _refreshTimer -= Time.unscaledDeltaTime;
                if (_refreshTimer > 0f) return;
                _refreshTimer = 1f;
                bridge = FindAnyObjectByType<CannonProductionWorldBridge>();
                if (bridge == null) return;
            }

            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer > 0f) return;
            _refreshTimer = 0.2f;
            Refresh();
        }

        private void Build()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _canvas = gameObject.GetComponent<Canvas>();
            if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = 40;
            if (gameObject.GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            _open = MakeButton(transform, Loc.T("shipyard.open"), 170f, 64f,
                               new Vector2(-24f, 72f), TogglePanel);
            SetBottomRight(_open.transform as RectTransform, 170f, 64f, -24f, 72f);

            _panel = new GameObject("CannonProductionCard", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(transform, false);
            RectTransform panelRect = _panel.transform as RectTransform;
            SetBottomRight(panelRect, 440f, 720f, -24f, 150f);
            _panel.GetComponent<Image>().color = panelColor;

            VerticalLayoutGroup layout = _panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 22, 22);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;

            _title = MakeLabel(_panel.transform, "", 28, accentColor, 46f);
            _title.alignment = TextAnchor.MiddleCenter;
            GameObject tabObject = new GameObject("BuiltMachineTabs", typeof(RectTransform), typeof(LayoutElement));
            tabObject.transform.SetParent(_panel.transform, false);
            _machineTabs = tabObject.transform as RectTransform;
            HorizontalLayoutGroup tabs = _machineTabs.gameObject.AddComponent<HorizontalLayoutGroup>();
            tabs.spacing = 6f;
            tabs.childControlWidth = true;
            tabs.childControlHeight = true;
            tabs.childForceExpandWidth = true;
            tabs.childForceExpandHeight = true;
            _machineTabs.GetComponent<LayoutElement>().preferredHeight = 48f;
            _recipe = MakeLabel(_panel.transform, "", 18, accentColor, 52f);
            _input = MakeLabel(_panel.transform, "", 21, Color.white, 82f);
            _status = MakeLabel(_panel.transform, "", 19, new Color(0.78f, 0.86f, 0.94f), 112f);
            _output = MakeLabel(_panel.transform, "", 19, Color.white, 82f);

            _start = MakeButton(_panel.transform as RectTransform, Loc.T("shipyard.action.start"), 0f, 58f, Vector2.zero, StartSelectedMachine);
            _start.GetComponent<LayoutElement>().preferredHeight = 58f;

            GameObject decisions = new GameObject("ItemDecisions", typeof(RectTransform));
            decisions.transform.SetParent(_panel.transform, false);
            GridLayoutGroup grid = decisions.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(190f, 52f);
            grid.spacing = new Vector2(10f, 8f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            LayoutElement gridLayout = decisions.AddComponent<LayoutElement>();
            gridLayout.preferredHeight = 112f;
            _sell = MakeButton(decisions.transform as RectTransform, Loc.T("shipyard.action.sell"), 0f, 52f, Vector2.zero, Sell);
            _equip = MakeButton(decisions.transform as RectTransform, Loc.T("shipyard.action.equip"), 0f, 52f, Vector2.zero, Equip);
            _store = MakeButton(decisions.transform as RectTransform, Loc.T("shipyard.action.store"), 0f, 52f, Vector2.zero, Store);
            _salvage = MakeButton(decisions.transform as RectTransform, Loc.T("shipyard.action.salvage"), 0f, 52f, Vector2.zero, Salvage);

            _fulfill = MakeButton(_panel.transform as RectTransform, Loc.T("shipyard.action.fulfill"), 0f, 58f, Vector2.zero, Fulfill);
            _fulfill.GetComponent<LayoutElement>().preferredHeight = 58f;
        }

        private void Refresh()
        {
            CannonProductionService production = bridge != null ? bridge.Production : null;
            if (production == null) return;

            RebuildMachineTabs(production);
            ShipyardMachineState machine = string.IsNullOrEmpty(_selectedMachineId)
                ? null : production.MachineFor(_selectedMachineId);
            IReadOnlyList<ShipyardRecipeDefinition> discovered = machine == null
                ? Array.Empty<ShipyardRecipeDefinition>()
                : production.DiscoveredRecipesFor(_selectedMachineId);
            ShipyardRecipeDefinition recipe = discovered.Count > 0 ? discovered[discovered.Count - 1] : null;
            _selectedRecipeId = recipe != null ? recipe.RecipeId : null;
            string machineName = MachineName(_selectedMachineId);
            _title.text = machine == null ? Loc.T("shipyard.recipe.none") : machineName;
            _recipe.text = recipe == null
                ? Loc.T("shipyard.recipe.none")
                : string.Format(Loc.T("shipyard.recipe.selected"), Loc.T(recipe.LocalizationKey),
                                recipe.ProductionDurationSeconds.ToString("0.#"));
            _input.text = recipe == null ? Loc.T("shipyard.input.none") : IngredientSummary(production, recipe);

            if (machine != null && !string.IsNullOrEmpty(machine.activeRecipeId))
            {
                long left = machine.queueFinishUnix - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                _status.text = string.Format(Loc.T("shipyard.status.running"), machineName, Math.Max(0L, left));
            }
            else if (machine != null && production.HasFinishedOutputFor(_selectedMachineId))
            {
                _status.text = Loc.T("shipyard.status.ready");
            }
            else
            {
                _status.text = recipe == null ? Loc.T("shipyard.status.locked") : Loc.T("shipyard.status.waiting");
            }

            ShipyardFinishedItemState output = machine == null
                ? null : production.FinishedOutputAt(_selectedMachineId, 0);
            _output.text = output == null ? Loc.T("shipyard.output.empty")
                : string.Format(Loc.T("shipyard.output.ready"), recipe != null ? Loc.T(recipe.LocalizationKey) : output.itemId);
            bool hasOutput = output != null;
            SetOutputButtons(hasOutput);
            _start.interactable = machine != null && recipe != null
                                  && string.IsNullOrEmpty(machine.activeRecipeId) && !hasOutput;
            ShipyardCustomerOrderState order = machine == null ? null : production.OrderFor(_selectedMachineId);
            _fulfill.interactable = hasOutput && order != null
                                    && order.status == ShipyardCustomerOrderState.Active;
        }

        private void RebuildMachineTabs(CannonProductionService production)
        {
            int builtCount = 0;
            for (int i = 0; i < ShipyardProgression.MachineIds.Length; i++)
            {
                ShipyardMachineState machine = production.MachineFor(ShipyardProgression.MachineIds[i]);
                if (machine != null && machine.constructionState == ShipyardMachineState.Built) builtCount++;
            }
            if (_tabsBuilt && builtCount == _tabMachineCount) return;

            for (int i = 0; i < _machineButtons.Count; i++)
                if (_machineButtons[i] != null) Destroy(_machineButtons[i].gameObject);
            _machineButtons.Clear();
            _tabsBuilt = true;
            _tabMachineCount = builtCount;
            for (int i = 0; i < ShipyardProgression.MachineIds.Length; i++)
            {
                string id = ShipyardProgression.MachineIds[i];
                ShipyardMachineState machine = production.MachineFor(id);
                if (machine == null || machine.constructionState != ShipyardMachineState.Built) continue;
                string captured = id;
                Button button = MakeButton(_machineTabs, MachineName(id), 0f, 44f, Vector2.zero,
                                           () => SelectMachine(captured));
                _machineButtons.Add(button);
            }
            if (_machineButtons.Count == 0) _selectedMachineId = null;
        }

        private void SelectMachine(string machineId)
        {
            _selectedMachineId = machineId;
            Refresh();
        }

        private void StartSelectedMachine()
        {
            if (bridge != null && !string.IsNullOrEmpty(_selectedMachineId))
                bridge.TryStart(_selectedMachineId, _selectedRecipeId);
            Refresh();
        }

        private static string MachineName(string machineId)
        {
            string suffix = machineId == null ? "" : machineId.Replace("Station_", "").ToLowerInvariant();
            return Loc.T("shipyard.machine." + suffix);
        }

        private static string IngredientSummary(CannonProductionService production, ShipyardRecipeDefinition recipe)
        {
            StringBuilder line = new StringBuilder(Loc.T("shipyard.input"));
            ShipyardRecipeDefinition.Ingredient[] ingredients = recipe.Ingredients;
            for (int i = 0; ingredients != null && i < ingredients.Length; i++)
            {
                ShipyardRecipeDefinition.Ingredient ingredient = ingredients[i];
                if (i > 0) line.Append("    ");
                line.Append(Loc.T("shipyard.material." + ingredient.ResourceId));
                line.Append(" ").Append(production.MaterialQuantity(ingredient.ResourceId).ToString("0.##"));
                line.Append(" / ").Append(ingredient.Quantity.ToString("0.##"));
            }
            return line.ToString();
        }

        private void SetOutputButtons(bool enabled)
        {
            _sell.interactable = enabled;
            _equip.interactable = enabled;
            _store.interactable = enabled;
            _salvage.interactable = enabled;
        }

        private void TogglePanel()
        {
            _panel.SetActive(!_panel.activeSelf);
            if (_panel.activeSelf) Refresh();
        }

        private void Sell() { Act((p, machine, id) => p.SellOutput(machine, id)); }
        private void Equip() { Act((p, machine, id) => p.EquipOutput(machine, id)); }
        private void Store() { Act((p, machine, id) => p.StoreOutput(machine, id)); }
        private void Salvage() { Act((p, machine, id) => p.SalvageOutput(machine, id) > 0L); }
        private void Fulfill() { Act((p, machine, id) => p.FulfillOrder(machine, id)); }

        private void Act(Func<CannonProductionWorldBridge, string, string, bool> action)
        {
            ShipyardFinishedItemState output = bridge != null
                ? bridge.FinishedOutputAt(_selectedMachineId, 0) : null;
            if (output != null) action(bridge, _selectedMachineId, output.itemId);
            Refresh();
        }

        private Text MakeLabel(Transform parent, string value, int size, Color color, float height)
        {
            GameObject go = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            Text label = go.GetComponent<Text>();
            label.font = _font;
            label.text = value;
            label.fontSize = size;
            label.color = color;
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            go.GetComponent<LayoutElement>().preferredHeight = height;
            return label;
        }

        private Button MakeButton(Transform parent, string label, float width, float height,
                                  Vector2 position, UnityEngine.Events.UnityAction action)
        {
            GameObject go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.color = buttonColor;
            Button button = go.GetComponent<Button>();
            button.onClick.AddListener(action);
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(buttonColor.r + 0.1f, buttonColor.g + 0.1f, buttonColor.b + 0.1f, 1f);
            colors.pressedColor = accentColor;
            button.colors = colors;
            LayoutElement layout = go.GetComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.preferredHeight = height;

            Text text = MakeLabel(go.transform, label, 18, Color.white, height);
            text.alignment = TextAnchor.MiddleCenter;
            return button;
        }

        private static void SetBottomRight(RectTransform rect, float width, float height, float right, float bottom)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(right, bottom);
        }

        /// <summary>
        /// The card is runtime-created on an overlay canvas, so it cannot inherit the authored HUD's
        /// safe-area root. Keep both the opener and the card inside the current notch/gesture-bar
        /// bounds, including when the device rotates or the editor changes its Game view aspect.
        /// </summary>
        private void ApplySafeArea()
        {
            if (_open == null || _panel == null) return;
            Rect safe = Screen.safeArea;
            Vector2Int screen = new Vector2Int(Screen.width, Screen.height);
            if (safe == _lastSafeArea && screen == _lastScreenSize) return;
            _lastSafeArea = safe;
            _lastScreenSize = screen;
            float right = -(Screen.width - safe.xMax + 24f);
            float bottom = safe.yMin + 24f;
            SetBottomRight(_open.transform as RectTransform, 170f, 64f, right, bottom);
            SetBottomRight(_panel.transform as RectTransform, 440f, 720f, right, safe.yMin + 96f);
        }
    }
}
