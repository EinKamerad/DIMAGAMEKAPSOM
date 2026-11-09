using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BioFarme.Audio;
using BioFarme.Core;
using BioFarme.Data;
using BioFarme.Grid;

namespace BioFarme.UI
{
    /// <summary>
    /// Additional Functional Window — Окно Био-Инженерии, Семян и Приоритетов Сети ([B] / [Tab] / HUD button).
    /// Features 4 tabs:
    /// 1) Посевы (Ферма)
    /// 2) Турели (PvZ)
    /// 3) Заводы и Стены (Mindustry)
    /// 4) Приоритеты Логистики + Экстренный Био-Обмен
    /// </summary>
    public sealed class BioCodexWindowUI : MonoBehaviour
    {
        private sealed class CodexCardBinding
        {
            public ResourceCost[] Costs;
            public Text CostLabel;
            public Button EquipButton;
        }

        private sealed class ExchangeRecipeBinding
        {
            public ResourceCost[] Inputs;
            public ResourceType OutputType;
            public float OutputAmount;
            public Button ActionButton;
            public Text CostStatusText;
        }

        private RectTransform _modalRoot;
        private readonly Button[] _tabButtons = new Button[4];
        private readonly GameObject[] _tabPages = new GameObject[4];
        private readonly List<CodexCardBinding> _cardBindings = new List<CodexCardBinding>(20);
        private readonly List<ExchangeRecipeBinding> _exchangeBindings = new List<ExchangeRecipeBinding>(4);
        private readonly Button[] _priorityModeButtons = new Button[4];

        private Text _liveResourceSummaryText;
        private Text _veinTelemetryText;
        private int _activeTabIndex;

        public bool IsOpen => _modalRoot != null && _modalRoot.gameObject.activeSelf;

        public void Build(Transform parent)
        {
            _modalRoot = BiopunkUiBuilder.CreateStretchRect("BioCodexModalOverlay", parent);
            Image dim = _modalRoot.gameObject.AddComponent<Image>();
            dim.sprite = SpriteRegistry.GetWhiteSquareSprite();
            dim.color = new Color(0.04f, 0.07f, 0.05f, 0.82f);
            dim.raycastTarget = true;

            // Main Window Frame (1480 x 880)
            Image win = BiopunkUiBuilder.CreatePanel(
                _modalRoot, "BioCodexWindow",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1480f, 880f),
                new Color(0.95f, 1.00f, 0.95f, 0.99f), raycastTarget: true);

            // Header Bar
            Image headerBar = BiopunkUiBuilder.CreatePanel(
                win.transform, "CodexHeader",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -14f), new Vector2(1440f, 68f),
                new Color(0.78f, 0.90f, 0.78f, 0.98f), raycastTarget: false);

            BiopunkUiBuilder.CreateSpriteImage(
                headerBar.transform, "HeaderIcon", "UI/Icon_SeedPouch",
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(18f, 0f), new Vector2(48f, 48f), preserveAspect: true);

            Text title = BiopunkUiBuilder.CreateText(
                headerBar.transform, "CodexTitle",
                "БИО-КОДЕКС: ИНЖЕНЕРИЯ, СЕМЕНА И ЛОГИСТИКА СЕТИ",
                22, FontStyle.Bold, BiopunkUiBuilder.AccentLime, TextAnchor.MiddleLeft);
            title.rectTransform.anchorMin = new Vector2(0f, 0f);
            title.rectTransform.anchorMax = new Vector2(0.57f, 1f);
            title.rectTransform.offsetMin = new Vector2(76f, 4f);
            title.rectTransform.offsetMax = Vector2.zero;
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = 16;
            title.resizeTextMaxSize = 22;

            _liveResourceSummaryText = BiopunkUiBuilder.CreateText(
                headerBar.transform, "LiveResourcesText", string.Empty,
                17, FontStyle.Bold, BiopunkUiBuilder.TextPrimary, TextAnchor.MiddleRight);
            _liveResourceSummaryText.rectTransform.anchorMin = new Vector2(0.57f, 0f);
            _liveResourceSummaryText.rectTransform.anchorMax = new Vector2(1f, 1f);
            _liveResourceSummaryText.rectTransform.offsetMin = Vector2.zero;
            _liveResourceSummaryText.rectTransform.offsetMax = new Vector2(-206f, 0f);

            Button closeBtn = BiopunkUiBuilder.CreateButton(
                headerBar.transform, "BtnCloseCodex", "Закрыть [B / Esc]",
                new Vector2(186f, 46f),
                Close,
                fontSize: 18,
                baseTint: new Color(0.96f, 0.72f, 0.70f, 1f));
            closeBtn.image.rectTransform.anchorMin = new Vector2(1f, 0.5f);
            closeBtn.image.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            closeBtn.image.rectTransform.pivot = new Vector2(1f, 0.5f);
            closeBtn.image.rectTransform.anchoredPosition = new Vector2(-12f, 0f);

            // 4 Category Tabs Row
            RectTransform tabsRow = BiopunkUiBuilder.CreateRect(
                "CodexTabsRow", win.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -92f), new Vector2(1440f, 54f));

            HorizontalLayoutGroup tHlg = tabsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            tHlg.spacing = 12f;
            tHlg.childAlignment = TextAnchor.MiddleCenter;
            tHlg.childControlWidth = true;
            tHlg.childControlHeight = true;
            tHlg.childForceExpandWidth = true;
            tHlg.childForceExpandHeight = true;

            string[] tabTitles =
            {
                "Посевы (Ферма)",
                "Турели (PvZ)",
                "Заводы и Стены (Mindustry)",
                "Приоритеты Логистики"
            };

            for (int i = 0; i < tabTitles.Length; i++)
            {
                int captured = i;
                _tabButtons[i] = BiopunkUiBuilder.CreateButton(
                    tabsRow, $"CodexTab_{i}", tabTitles[i],
                    new Vector2(340f, 52f),
                    () => SelectTab(captured),
                    fontSize: 20);
            }

            // Tab Pages Area (1440 x 654)
            RectTransform pagesArea = BiopunkUiBuilder.CreateRect(
                "CodexPagesArea", win.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -156f), new Vector2(1440f, 646f));

            _tabPages[0] = BuildCropsTabPage(pagesArea);
            _tabPages[1] = BuildTurretsTabPage(pagesArea);
            _tabPages[2] = BuildFactoriesAndWallsTabPage(pagesArea);
            _tabPages[3] = BuildLogisticsAndExchangeTabPage(pagesArea);

            // Footer Bar: Quick Colonist Tools Restore Strip
            BuildToolsFooterStrip(win.transform);

            SelectTab(0);
            _modalRoot.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            GameEvents.OnResourceChanged += HandleResourceChanged;
            GameEvents.OnNetworkPriorityChanged += HandlePriorityChanged;
        }

        private void OnDisable()
        {
            GameEvents.OnResourceChanged -= HandleResourceChanged;
            GameEvents.OnNetworkPriorityChanged -= HandlePriorityChanged;
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public void Open(int? tabOverride = null)
        {
            if (_modalRoot == null) return;
            _modalRoot.gameObject.SetActive(true);
            _modalRoot.SetAsLastSibling();
            if (tabOverride.HasValue)
            {
                SelectTab(tabOverride.Value);
            }
            RefreshAffordabilityAndTelemetry();
        }

        public void Close()
        {
            if (_modalRoot != null && _modalRoot.gameObject.activeSelf)
            {
                _modalRoot.gameObject.SetActive(false);
                BiopunkTooltipPopup.Hide();
            }
        }

        private void SelectTab(int index)
        {
            _activeTabIndex = Mathf.Clamp(index, 0, _tabPages.Length - 1);
            for (int i = 0; i < _tabPages.Length; i++)
            {
                if (_tabPages[i] != null) _tabPages[i].SetActive(i == _activeTabIndex);
                if (_tabButtons[i] != null && _tabButtons[i].image != null)
                {
                    _tabButtons[i].image.color = (i == _activeTabIndex)
                        ? new Color(0.68f, 1.00f, 0.54f, 1f)
                        : new Color(0.82f, 0.86f, 0.82f, 1f);
                }
            }
            RefreshAffordabilityAndTelemetry();
        }

        // =====================================================================
        // TAB 1: ПОСЕВЫ (ФЕРМА) — All 4 CropDefinitions (2x2 Grid)
        // =====================================================================

        private GameObject BuildCropsTabPage(Transform parent)
        {
            RectTransform page = BiopunkUiBuilder.CreateStretchRect("Page_Crops", parent);
            GridLayoutGroup glg = page.gameObject.AddComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(708f, 312f);
            glg.spacing = new Vector2(20f, 18f);
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 2;
            glg.childAlignment = TextAnchor.UpperCenter;

            IReadOnlyList<CropDefinition> crops = BalanceDatabase.AllCrops;
            for (int i = 0; i < crops.Count; i++)
            {
                CropDefinition def = crops[i];
                string stats =
                    $"Рост: <b>{def.growthDurationSec:0}с</b>  |  Урожай: <b>{def.harvestMin}–{def.harvestMax} {BalanceDatabase.GetResourceNameRu(def.harvestResource)}</b>  |  Мох: <b>x{def.fertileBonusMultiplier:0.##}</b>";
                HotbarSlotData slotPayload = BalanceDatabase.CreateSeedSlot(-1, string.Empty, def.cropType);

                CreateLargeCodexCard(
                    page,
                    $"Семена: {def.displayName}",
                    def.description,
                    stats,
                    SpriteRegistry.GetCropIcon(def.cropType),
                    def.GetSeedCosts(),
                    slotPayload);
            }

            return page.gameObject;
        }

        // =====================================================================
        // TAB 2: ТУРЕЛИ (PvZ) — Turrets & Combat Tendril (2x2 Grid)
        // =====================================================================

        private GameObject BuildTurretsTabPage(Transform parent)
        {
            RectTransform page = BiopunkUiBuilder.CreateStretchRect("Page_Turrets", parent);
            GridLayoutGroup glg = page.gameObject.AddComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(708f, 312f);
            glg.spacing = new Vector2(20f, 18f);
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 2;
            glg.childAlignment = TextAnchor.UpperCenter;

            IReadOnlyList<StructureDefinition> structs = BalanceDatabase.BuildableStructures;
            for (int i = 0; i < structs.Count; i++)
            {
                StructureDefinition def = structs[i];
                if (def.category != StructureCategory.Turret && def.structureType != StructureType.MendTendril)
                {
                    continue;
                }

                string stats = def.damage < 0f
                    ? $"Прочность: <b>{def.maxHp:0} HP</b>  |  Лечение: <b>+{Mathf.Abs(def.damage):0} HP</b>  |  Радиус: <b>{def.range:0.#} кл.</b>"
                    : $"Прочность: <b>{def.maxHp:0} HP</b>  |  Урон: <b>{def.damage:0}</b> ({def.fireRate:0.##}/с)  |  Дальность: <b>{def.range:0.#}</b>";

                HotbarSlotData slotPayload = BalanceDatabase.CreateStructureSlot(-1, string.Empty, def.structureType);
                CreateLargeCodexCard(
                    page,
                    def.displayName,
                    def.description,
                    stats,
                    SpriteRegistry.GetStructureIcon(def.structureType),
                    def.costs,
                    slotPayload);
            }

            return page.gameObject;
        }

        // =====================================================================
        // TAB 3: ЗАВОДЫ И СТЕНЫ (Mindustry) — All Remaining BuildableStructures (3x3 Grid)
        // =====================================================================

        private GameObject BuildFactoriesAndWallsTabPage(Transform parent)
        {
            RectTransform page = BiopunkUiBuilder.CreateStretchRect("Page_FactoriesWalls", parent);
            GridLayoutGroup glg = page.gameObject.AddComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(470f, 206f);
            glg.spacing = new Vector2(14f, 12f);
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 3;
            glg.childAlignment = TextAnchor.UpperCenter;

            IReadOnlyList<StructureDefinition> structs = BalanceDatabase.BuildableStructures;
            for (int i = 0; i < structs.Count; i++)
            {
                StructureDefinition def = structs[i];
                if (def.category == StructureCategory.Turret || def.structureType == StructureType.MendTendril)
                {
                    continue;
                }

                string stats;
                if (def.outputPerCycle > 0f)
                {
                    stats = $"HP: <b>{def.maxHp:0}</b> | Выход: <b>+{def.outputPerCycle:0.#} {BalanceDatabase.GetResourceNameRu(def.producedResource)}</b> / {def.cycleIntervalSec:0.#}с";
                }
                else if (def.isConduit)
                {
                    stats = $"HP: <b>{def.maxHp:0}</b> | Канал Ур.{def.conduitTier}" + (def.conduitTier >= 2 ? " (Авто-полив 3×3!)" : " (Транспорт ресурсов)");
                }
                else
                {
                    stats = $"Прочность: <b>{def.maxHp:0} HP</b> | Категория: <b>{def.categoryLabel}</b>";
                }

                HotbarSlotData slotPayload = BalanceDatabase.CreateStructureSlot(-1, string.Empty, def.structureType);
                CreateCompactCodexCard(
                    page,
                    def.displayName,
                    def.description,
                    stats,
                    SpriteRegistry.GetStructureIcon(def.structureType),
                    def.costs,
                    slotPayload);
            }

            return page.gameObject;
        }

        // =====================================================================
        // TAB 4: ПРИОРИТЕТЫ ЛОГИСТИКИ + ЭКСТРЕННЫЙ БИО-ОБМЕН
        // =====================================================================

        private GameObject BuildLogisticsAndExchangeTabPage(Transform parent)
        {
            RectTransform page = BiopunkUiBuilder.CreateStretchRect("Page_Logistics", parent);

            // Top Network Telemetry Banner
            Image telemetryBox = BiopunkUiBuilder.CreatePanel(
                page, "VeinTelemetryBanner",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(1440f, 58f),
                new Color(0.80f, 0.92f, 0.94f, 0.96f), raycastTarget: false);

            _veinTelemetryText = BiopunkUiBuilder.CreateText(
                telemetryBox.transform, "TelemetryText",
                "СОСУДИСТАЯ СЕТЬ КОЛОНИИ: Активных кластеров: 1 | Ресурсных пакетов в потоке: 0",
                20, FontStyle.Bold, BiopunkUiBuilder.AccentCyan, TextAnchor.MiddleCenter);
            BiopunkUiBuilder.Stretch(_veinTelemetryText.rectTransform, 18f, 4f, 18f, 4f);

            // Section 1: 4-Way Network Priority Selector
            Image priorityPanel = BiopunkUiBuilder.CreatePanel(
                page, "PrioritySelectorSection",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -68f), new Vector2(1440f, 268f),
                new Color(0.86f, 0.92f, 0.86f, 0.96f), raycastTarget: true);

            Text prioHeader = BiopunkUiBuilder.CreateText(
                priorityPanel.transform, "PrioHeader",
                "МАРШРУТИЗАЦИЯ БИО-ВЕН (РАСПРЕДЕЛЕНИЕ ПАКЕТОВ РЕСУРСОВ В ДУХЕ MINDUSTRY)",
                21, FontStyle.Bold, BiopunkUiBuilder.AccentLime, TextAnchor.MiddleCenter);
            prioHeader.rectTransform.anchorMin = new Vector2(0.03f, 0.78f);
            prioHeader.rectTransform.anchorMax = new Vector2(0.97f, 0.96f);
            prioHeader.rectTransform.offsetMin = Vector2.zero;
            prioHeader.rectTransform.offsetMax = Vector2.zero;

            RectTransform prioGrid = BiopunkUiBuilder.CreateRect(
                "PriorityCardsRow", priorityPanel.transform,
                new Vector2(0.5f, 0.38f), new Vector2(0.5f, 0.38f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1400f, 186f));

            HorizontalLayoutGroup pHlg = prioGrid.gameObject.AddComponent<HorizontalLayoutGroup>();
            pHlg.spacing = 14f;
            pHlg.childControlWidth = true;
            pHlg.childControlHeight = true;
            pHlg.childForceExpandWidth = true;
            pHlg.childForceExpandHeight = true;

            (NetworkPriorityMode mode, string title, string desc, string iconKey)[] modes =
            {
                (NetworkPriorityMode.Balanced, "Сбалансированный", "Равномерное снабжение турелей, реакторов и Живого Сердца.", "UI/Icon_Vein_Biomass"),
                (NetworkPriorityMode.PrioritizeTurrets, "Приоритет: Оборона (Турели)", "Направляет максимум Биомассы, Спор и Плазмы в боевые турели!", "UI/Icon_Turret_Flytrap"),
                (NetworkPriorityMode.PrioritizeReactors, "Приоритет: Синтез (Реакторы)", "Ускоряет подачу сырья в Био-реакторы и Инкубатор мутагена!", "UI/Icon_Building_BioReactor"),
                (NetworkPriorityMode.PrioritizeCore, "Приоритет: Накопление в Ядре", "Концентрирует поток ресурсов в Живом Сердце и щупальцах ремонта.", "Core_LivingHeart")
            };

            for (int i = 0; i < modes.Length; i++)
            {
                var entry = modes[i];
                GameObject cardGo = new GameObject($"PrioMode_{entry.mode}");
                cardGo.transform.SetParent(prioGrid, false);
                RectTransform cardRt = cardGo.AddComponent<RectTransform>();

                Image bg = cardGo.AddComponent<Image>();
                bg.sprite = SpriteRegistry.Get("UI/UI_Card_Frame");
                bg.color = Color.white;

                Button btn = cardGo.AddComponent<Button>();
                btn.targetGraphic = bg;
                NetworkPriorityMode capturedMode = entry.mode;
                btn.onClick.AddListener(() =>
                {
                    BiopunkUiBuilder.PlayClickSfx();
                    ResourceManager.Instance?.SetNetworkPriority(capturedMode);
                    GameEvents.RaiseBannerToast(
                        $"Режим био-сети: {entry.title}",
                        BiopunkUiBuilder.AccentCyan);
                    RefreshPriorityButtonsVisual();
                });

                BiopunkUiBuilder.CreateSpriteImage(
                    cardRt, "Icon", entry.iconKey,
                    new Vector2(0.5f, 0.74f), new Vector2(0.5f, 0.74f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(54f, 54f), preserveAspect: true);

                Text mTitle = BiopunkUiBuilder.CreateText(
                    cardRt, "ModeTitle", entry.title,
                    18, FontStyle.Bold, BiopunkUiBuilder.AccentLime, TextAnchor.MiddleCenter);
                mTitle.rectTransform.anchorMin = new Vector2(0.06f, 0.42f);
                mTitle.rectTransform.anchorMax = new Vector2(0.94f, 0.58f);
                mTitle.rectTransform.offsetMin = Vector2.zero;
                mTitle.rectTransform.offsetMax = Vector2.zero;

                Text mDesc = BiopunkUiBuilder.CreateText(
                    cardRt, "ModeDesc", entry.desc,
                    16, FontStyle.Normal, BiopunkUiBuilder.TextPrimary, TextAnchor.MiddleCenter);
                mDesc.rectTransform.anchorMin = new Vector2(0.07f, 0.08f);
                mDesc.rectTransform.anchorMax = new Vector2(0.93f, 0.42f);
                mDesc.rectTransform.offsetMin = Vector2.zero;
                mDesc.rectTransform.offsetMax = Vector2.zero;

                _priorityModeButtons[i] = btn;
            }

            // Section 2: Emergency Bio-Exchange (Экстренный Био-Обмен)
            Image exchangePanel = BiopunkUiBuilder.CreatePanel(
                page, "BioExchangeSection",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                Vector2.zero, new Vector2(1440f, 298f),
                new Color(0.88f, 0.86f, 0.92f, 0.96f), raycastTarget: true);

            Text exHeader = BiopunkUiBuilder.CreateText(
                exchangePanel.transform, "ExchangeHeader",
                "ЭКСТРЕННЫЙ БИО-ОБМЕН ЯДРА (МГНОВЕННАЯ ТРАНСМУТАЦИЯ РЕСУРСОВ)",
                21, FontStyle.Bold, BiopunkUiBuilder.AccentMutagen, TextAnchor.MiddleCenter);
            exHeader.rectTransform.anchorMin = new Vector2(0.03f, 0.80f);
            exHeader.rectTransform.anchorMax = new Vector2(0.97f, 0.96f);
            exHeader.rectTransform.offsetMin = Vector2.zero;
            exHeader.rectTransform.offsetMax = Vector2.zero;

            RectTransform exGrid = BiopunkUiBuilder.CreateRect(
                "ExchangeGrid", exchangePanel.transform,
                new Vector2(0.5f, 0.39f), new Vector2(0.5f, 0.39f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1400f, 214f));

            GridLayoutGroup eGlg = exGrid.gameObject.AddComponent<GridLayoutGroup>();
            eGlg.cellSize = new Vector2(690f, 98f);
            eGlg.spacing = new Vector2(18f, 14f);
            eGlg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            eGlg.constraintCount = 2;

            CreateExchangeRecipeCard(
                exGrid,
                "Синтез Спор из Биомассы",
                new[] { new ResourceCost(ResourceType.Biomass, 50f) },
                ResourceType.Spores, 20f);

            CreateExchangeRecipeCard(
                exGrid,
                "Перегонка Биомассы в Биоплазму",
                new[] { new ResourceCost(ResourceType.Biomass, 60f) },
                ResourceType.BioPlasma, 18f);

            CreateExchangeRecipeCard(
                exGrid,
                "Кальцификация Костяного хитина",
                new[]
                {
                    new ResourceCost(ResourceType.Biomass, 45f),
                    new ResourceCost(ResourceType.Spores, 15f)
                },
                ResourceType.BoneChitin, 12f);

            CreateExchangeRecipeCard(
                exGrid,
                "Концентрация чистого Мутагена Сердца",
                new[]
                {
                    new ResourceCost(ResourceType.Biomass, 60f),
                    new ResourceCost(ResourceType.BioPlasma, 10f)
                },
                ResourceType.Mutagen, 1.0f);

            return page.gameObject;
        }

        // =====================================================================
        // CARD & FOOTER BUILDERS
        // =====================================================================

        private void CreateLargeCodexCard(
            Transform parent,
            string titleRu,
            string descRu,
            string statsRu,
            Sprite iconSprite,
            ResourceCost[] costs,
            HotbarSlotData slotPayload)
        {
            GameObject cardGo = new GameObject($"Card_{titleRu}");
            cardGo.transform.SetParent(parent, false);
            RectTransform rt = cardGo.AddComponent<RectTransform>();

            Image cardBg = cardGo.AddComponent<Image>();
            cardBg.sprite = SpriteRegistry.Get("UI/UI_Card_Frame");
            cardBg.color = Color.white;

            // 96x96 crisp icon on left
            Image iconSlotBg = BiopunkUiBuilder.CreateSpriteImage(
                rt, "IconFrame", "UI/UI_Frame_SlotNormal",
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(28f, 16f), new Vector2(118f, 118f), preserveAspect: true);

            Image iconImg = BiopunkUiBuilder.CreateRect(
                "Icon", iconSlotBg.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(92f, 92f)).gameObject.AddComponent<Image>();
            iconImg.sprite = iconSprite;
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;

            Text nameTxt = BiopunkUiBuilder.CreateText(
                rt, "Name", titleRu, 23, FontStyle.Bold, BiopunkUiBuilder.AccentLime, TextAnchor.MiddleLeft);
            nameTxt.rectTransform.anchorMin = new Vector2(0f, 0.75f);
            nameTxt.rectTransform.anchorMax = new Vector2(1f, 0.92f);
            nameTxt.rectTransform.offsetMin = new Vector2(162f, 0f);
            nameTxt.rectTransform.offsetMax = new Vector2(-28f, 0f);

            Text descTxt = BiopunkUiBuilder.CreateText(
                rt, "Description", descRu, 18, FontStyle.Normal, BiopunkUiBuilder.TextPrimary, TextAnchor.UpperLeft);
            descTxt.rectTransform.anchorMin = new Vector2(0f, 0.44f);
            descTxt.rectTransform.anchorMax = new Vector2(1f, 0.74f);
            descTxt.rectTransform.offsetMin = new Vector2(162f, 0f);
            descTxt.rectTransform.offsetMax = new Vector2(-28f, 0f);

            Text statsTxt = BiopunkUiBuilder.CreateText(
                rt, "Stats", statsRu, 17, FontStyle.Normal, BiopunkUiBuilder.AccentCyan, TextAnchor.MiddleLeft);
            statsTxt.rectTransform.anchorMin = new Vector2(0f, 0.28f);
            statsTxt.rectTransform.anchorMax = new Vector2(1f, 0.44f);
            statsTxt.rectTransform.offsetMin = new Vector2(162f, 0f);
            statsTxt.rectTransform.offsetMax = new Vector2(-28f, 0f);

            Text costTxt = BiopunkUiBuilder.CreateText(
                rt, "CostText", $"Стоимость: {ResourceManager.FormatCostsRu(costs)}",
                19, FontStyle.Bold, BiopunkUiBuilder.AccentLime, TextAnchor.MiddleLeft);
            costTxt.rectTransform.anchorMin = new Vector2(0f, 0.08f);
            costTxt.rectTransform.anchorMax = new Vector2(0.64f, 0.26f);
            costTxt.rectTransform.offsetMin = new Vector2(32f, 0f);
            costTxt.rectTransform.offsetMax = Vector2.zero;

            Button equipBtn = BiopunkUiBuilder.CreateButton(
                rt, "BtnEquipSlot", "Взять в слот",
                new Vector2(210f, 46f),
                () => AssignItemToHotbar(slotPayload),
                fontSize: 19,
                baseTint: new Color(0.70f, 1.00f, 0.56f, 1f));
            equipBtn.image.rectTransform.anchorMin = new Vector2(1f, 0.09f);
            equipBtn.image.rectTransform.anchorMax = new Vector2(1f, 0.09f);
            equipBtn.image.rectTransform.pivot = new Vector2(1f, 0f);
            equipBtn.image.rectTransform.anchoredPosition = new Vector2(-28f, 0f);

            _cardBindings.Add(new CodexCardBinding
            {
                Costs = costs,
                CostLabel = costTxt,
                EquipButton = equipBtn
            });
        }

        private void CreateCompactCodexCard(
            Transform parent,
            string titleRu,
            string descRu,
            string statsRu,
            Sprite iconSprite,
            ResourceCost[] costs,
            HotbarSlotData slotPayload)
        {
            GameObject cardGo = new GameObject($"CompactCard_{titleRu}");
            cardGo.transform.SetParent(parent, false);
            RectTransform rt = cardGo.AddComponent<RectTransform>();

            Image cardBg = cardGo.AddComponent<Image>();
            cardBg.sprite = SpriteRegistry.Get("UI/UI_Card_Frame");
            cardBg.color = Color.white;

            Image iconImg = BiopunkUiBuilder.CreateRect(
                "Icon", rt,
                new Vector2(0f, 0.62f), new Vector2(0f, 0.62f), new Vector2(0f, 0.5f),
                new Vector2(22f, 0f), new Vector2(82f, 82f)).gameObject.AddComponent<Image>();
            iconImg.sprite = iconSprite;
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;

            Text nameTxt = BiopunkUiBuilder.CreateText(
                rt, "Name", titleRu, 20, FontStyle.Bold, BiopunkUiBuilder.AccentLime, TextAnchor.MiddleLeft);
            nameTxt.rectTransform.anchorMin = new Vector2(0f, 0.73f);
            nameTxt.rectTransform.anchorMax = new Vector2(1f, 0.92f);
            nameTxt.rectTransform.offsetMin = new Vector2(114f, 0f);
            nameTxt.rectTransform.offsetMax = new Vector2(-20f, 0f);

            Text descTxt = BiopunkUiBuilder.CreateText(
                rt, "Desc", descRu, 16, FontStyle.Normal, BiopunkUiBuilder.TextPrimary, TextAnchor.UpperLeft);
            descTxt.rectTransform.anchorMin = new Vector2(0f, 0.42f);
            descTxt.rectTransform.anchorMax = new Vector2(1f, 0.73f);
            descTxt.rectTransform.offsetMin = new Vector2(114f, 0f);
            descTxt.rectTransform.offsetMax = new Vector2(-20f, 0f);

            Text statsTxt = BiopunkUiBuilder.CreateText(
                rt, "Stats", statsRu, 15, FontStyle.Normal, BiopunkUiBuilder.AccentCyan, TextAnchor.MiddleLeft);
            statsTxt.rectTransform.anchorMin = new Vector2(0f, 0.27f);
            statsTxt.rectTransform.anchorMax = new Vector2(1f, 0.42f);
            statsTxt.rectTransform.offsetMin = new Vector2(24f, 0f);
            statsTxt.rectTransform.offsetMax = new Vector2(-20f, 0f);

            Text costTxt = BiopunkUiBuilder.CreateText(
                rt, "CostText", ResourceManager.FormatCostsRu(costs),
                16, FontStyle.Bold, BiopunkUiBuilder.AccentLime, TextAnchor.MiddleLeft);
            costTxt.rectTransform.anchorMin = new Vector2(0f, 0.08f);
            costTxt.rectTransform.anchorMax = new Vector2(0.62f, 0.27f);
            costTxt.rectTransform.offsetMin = new Vector2(24f, 0f);
            costTxt.rectTransform.offsetMax = Vector2.zero;

            Button equipBtn = BiopunkUiBuilder.CreateButton(
                rt, "BtnEquipSlot", "Взять в слот",
                new Vector2(154f, 38f),
                () => AssignItemToHotbar(slotPayload),
                fontSize: 17,
                baseTint: new Color(0.70f, 1.00f, 0.56f, 1f));
            equipBtn.image.rectTransform.anchorMin = new Vector2(1f, 0.09f);
            equipBtn.image.rectTransform.anchorMax = new Vector2(1f, 0.09f);
            equipBtn.image.rectTransform.pivot = new Vector2(1f, 0f);
            equipBtn.image.rectTransform.anchoredPosition = new Vector2(-20f, 0f);

            _cardBindings.Add(new CodexCardBinding
            {
                Costs = costs,
                CostLabel = costTxt,
                EquipButton = equipBtn
            });
        }

        private void CreateExchangeRecipeCard(
            Transform parent,
            string recipeTitle,
            ResourceCost[] inputs,
            ResourceType outputType,
            float outputAmount)
        {
            Image card = BiopunkUiBuilder.CreatePanel(
                parent, $"Exchange_{outputType}",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(690f, 98f),
                new Color(0.82f, 0.88f, 0.86f, 0.98f), raycastTarget: true);

            Image outIcon = BiopunkUiBuilder.CreateRect(
                "OutIcon", card.transform,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(16f, 0f), new Vector2(62f, 62f)).gameObject.AddComponent<Image>();
            outIcon.sprite = SpriteRegistry.GetResourceIcon(outputType);
            outIcon.preserveAspect = true;
            outIcon.raycastTarget = false;

            Text titleTxt = BiopunkUiBuilder.CreateText(
                card.transform, "Title", recipeTitle,
                19, FontStyle.Bold, BiopunkUiBuilder.TextPrimary, TextAnchor.MiddleLeft);
            titleTxt.rectTransform.anchorMin = new Vector2(0f, 0.52f);
            titleTxt.rectTransform.anchorMax = new Vector2(0.68f, 0.92f);
            titleTxt.rectTransform.offsetMin = new Vector2(90f, 0f);
            titleTxt.rectTransform.offsetMax = Vector2.zero;

            string outAmtStr = outputType == ResourceType.Mutagen ? $"{outputAmount:0.0}" : $"{outputAmount:0}";
            string formula = $"{ResourceManager.FormatCostsRu(inputs)}  →  +{outAmtStr} {BalanceDatabase.GetResourceNameRu(outputType)}";
            Text costTxt = BiopunkUiBuilder.CreateText(
                card.transform, "Formula", formula,
                17, FontStyle.Bold, BiopunkUiBuilder.AccentLime, TextAnchor.MiddleLeft);
            costTxt.rectTransform.anchorMin = new Vector2(0f, 0.10f);
            costTxt.rectTransform.anchorMax = new Vector2(0.68f, 0.52f);
            costTxt.rectTransform.offsetMin = new Vector2(90f, 0f);
            costTxt.rectTransform.offsetMax = Vector2.zero;

            ExchangeRecipeBinding binding = new ExchangeRecipeBinding
            {
                Inputs = inputs,
                OutputType = outputType,
                OutputAmount = outputAmount,
                CostStatusText = costTxt
            };

            Button btn = BiopunkUiBuilder.CreateButton(
                card.transform, "BtnExecuteExchange", "ОБМЕНЯТЬ",
                new Vector2(186f, 52f),
                () => ExecuteBioExchange(binding),
                fontSize: 19,
                baseTint: new Color(0.76f, 0.98f, 0.68f, 1f));
            btn.image.rectTransform.anchorMin = new Vector2(1f, 0.5f);
            btn.image.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            btn.image.rectTransform.pivot = new Vector2(1f, 0.5f);
            btn.image.rectTransform.anchoredPosition = new Vector2(-16f, 0f);

            binding.ActionButton = btn;
            _exchangeBindings.Add(binding);
        }

        private void BuildToolsFooterStrip(Transform winTransform)
        {
            Image footer = BiopunkUiBuilder.CreatePanel(
                winTransform, "ToolsQuickFooter",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 14f), new Vector2(1440f, 54f),
                new Color(0.80f, 0.86f, 0.80f, 0.96f), raycastTarget: true);

            Text label = BiopunkUiBuilder.CreateText(
                footer.transform, "FooterHint",
                "БАЗОВЫЕ ИНСТРУМЕНТЫ КОЛОНИСТА (Вернуть в активный слот):",
                18, FontStyle.Bold, BiopunkUiBuilder.TextSecondary, TextAnchor.MiddleLeft);
            label.rectTransform.anchorMin = new Vector2(0f, 0f);
            label.rectTransform.anchorMax = new Vector2(0.46f, 1f);
            label.rectTransform.offsetMin = new Vector2(18f, 0f);
            label.rectTransform.offsetMax = Vector2.zero;

            RectTransform toolsRow = BiopunkUiBuilder.CreateRect(
                "ToolsButtonsRow", footer.transform,
                new Vector2(0.47f, 0.5f), new Vector2(0.99f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(0f, 40f));

            HorizontalLayoutGroup hlg = toolsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            IReadOnlyList<ToolDefinition> tools = BalanceDatabase.AllTools;
            for (int i = 0; i < tools.Count; i++)
            {
                ToolDefinition tDef = tools[i];
                HotbarSlotData toolSlot = BalanceDatabase.CreateToolSlot(-1, string.Empty, tDef.toolType);
                BiopunkUiBuilder.CreateButton(
                    toolsRow, $"BtnTool_{tDef.toolType}", tDef.displayName,
                    new Vector2(230f, 40f),
                    () => AssignItemToHotbar(toolSlot),
                    fontSize: 17,
                    baseTint: new Color(0.84f, 0.94f, 0.84f, 1f),
                    iconSprite: SpriteRegistry.GetToolIcon(tDef.toolType));
            }
        }

        // =====================================================================
        // ACTIONS & REFRESH
        // =====================================================================

        private void AssignItemToHotbar(HotbarSlotData template)
        {
            if (template == null) return;

            HotbarSlotData copy = new HotbarSlotData
            {
                slotIndex = template.slotIndex,
                keyLabel = template.keyLabel,
                category = template.category,
                toolType = template.toolType,
                cropType = template.cropType,
                structureType = template.structureType,
                displayName = template.displayName,
                description = template.description,
                iconKey = template.iconKey,
                costs = template.costs
            };

            GameEvents.RaiseAssignHotbarSelection(copy);
            GameEvents.RaiseBannerToast(
                $"Взято в активный слот: {copy.displayName}",
                BiopunkUiBuilder.AccentLime);
            Close();
        }

        private void ExecuteBioExchange(ExchangeRecipeBinding recipe)
        {
            ResourceManager rm = ResourceManager.Instance;
            if (rm == null) return;

            if (!rm.TrySpend(recipe.Inputs))
            {
                RefreshAffordabilityAndTelemetry();
                return;
            }

            rm.Add(recipe.OutputType, recipe.OutputAmount);
            AudioManager.Instance?.PlaySfx(AudioKeys.SfxHarvest, 0.85f, 0.04f);

            string outFmt = recipe.OutputType == ResourceType.Mutagen
                ? $"{recipe.OutputAmount:0.0}"
                : $"{recipe.OutputAmount:0}";
            GameEvents.RaiseBannerToast(
                $"Био-обмен: +{outFmt} {BalanceDatabase.GetResourceNameRu(recipe.OutputType)}!",
                BalanceDatabase.GetResourceColor(recipe.OutputType));

            RefreshAffordabilityAndTelemetry();
        }

        private void HandleResourceChanged(ResourceType type, float newAmount, float delta)
        {
            if (IsOpen) RefreshAffordabilityAndTelemetry();
        }

        private void HandlePriorityChanged(NetworkPriorityMode mode)
        {
            if (IsOpen) RefreshPriorityButtonsVisual();
        }

        private void RefreshAffordabilityAndTelemetry()
        {
            ResourceManager rm = ResourceManager.InstanceIfExists;

            if (_liveResourceSummaryText != null)
            {
                float bio = rm != null ? rm.Get(ResourceType.Biomass) : 0f;
                float sp = rm != null ? rm.Get(ResourceType.Spores) : 0f;
                float pl = rm != null ? rm.Get(ResourceType.BioPlasma) : 0f;
                float ch = rm != null ? rm.Get(ResourceType.BoneChitin) : 0f;
                float mut = rm != null ? rm.Get(ResourceType.Mutagen) : 0f;
                _liveResourceSummaryText.text =
                    $"<color=#59EB6B>Био: {bio:0}</color>  |  <color=#FAD040>Споры: {sp:0}</color>  |  <color=#38E5FF>Плазма: {pl:0}</color>  |  <color=#EDE3C7>Хитин: {ch:0}</color>  |  <color=#F547E0>Мутаген: {mut:0.0}/10</color>";
            }

            for (int i = 0; i < _cardBindings.Count; i++)
            {
                CodexCardBinding b = _cardBindings[i];
                bool canAfford = rm == null || rm.CanAfford(b.Costs);
                if (b.CostLabel != null)
                {
                    b.CostLabel.color = canAfford ? BiopunkUiBuilder.AccentLime : BiopunkUiBuilder.AccentDanger;
                }
            }

            for (int i = 0; i < _exchangeBindings.Count; i++)
            {
                ExchangeRecipeBinding ex = _exchangeBindings[i];
                bool canAfford = rm != null && rm.CanAfford(ex.Inputs);
                if (ex.ActionButton != null) ex.ActionButton.interactable = canAfford;
                if (ex.CostStatusText != null)
                {
                    ex.CostStatusText.color = canAfford ? BiopunkUiBuilder.AccentLime : BiopunkUiBuilder.AccentDanger;
                }
            }

            if (_veinTelemetryText != null)
            {
                VeinNetworkSystem vn = BioGridManager.InstanceIfExists != null ? BioGridManager.InstanceIfExists.VeinNetwork : null;
                int clusters = vn != null ? vn.ClusterCount : 1;
                int packets = vn != null ? vn.ActivePacketCount : 0;
                NetworkPriorityMode prio = rm != null ? rm.CurrentPriority : NetworkPriorityMode.Balanced;
                _veinTelemetryText.text =
                    $"СОСУДИСТАЯ СЕТЬ: Кластеров: {clusters}  |  Пакетов в потоке: {packets}  |  Активный режим: {ResourceManager.GetPriorityDisplayNameRu(prio)}";
            }

            RefreshPriorityButtonsVisual();
        }

        private void RefreshPriorityButtonsVisual()
        {
            NetworkPriorityMode current = ResourceManager.InstanceIfExists != null
                ? ResourceManager.InstanceIfExists.CurrentPriority
                : NetworkPriorityMode.Balanced;

            for (int i = 0; i < _priorityModeButtons.Length; i++)
            {
                if (_priorityModeButtons[i] != null && _priorityModeButtons[i].image != null)
                {
                    bool selected = ((int)current == i);
                    _priorityModeButtons[i].image.color = selected
                        ? new Color(0.65f, 1.00f, 0.72f, 1f)
                        : new Color(0.84f, 0.86f, 0.84f, 0.92f);
                }
            }
        }
    }
}
