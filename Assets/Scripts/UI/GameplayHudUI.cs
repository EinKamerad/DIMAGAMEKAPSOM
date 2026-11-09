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
    /// Contest Screen #2 — Игровой экран (Gameplay HUD).
    /// Faithfully implements the biopunk layout from ref_gameplay.jpg:
    /// Top-Left 5-slot resource bar + Stamina + Buff badge, Left collapsible Mission Objectives,
    /// Top-Center Alert/Weather banner + Living Heart Core HP & Awakening bars,
    /// Top-Right Wave banner + Early Wave button + Functional Window buttons,
    /// Bottom-Center 8-slot Hotbar (UI_Panel_Hotbar8) + usage strip, Bottom-Right MinimapUI, and animated Toasts.
    /// </summary>
    public sealed class GameplayHudUI : MonoBehaviour
    {
        private sealed class ResourceSlotRefs
        {
            public ResourceType Type;
            public RectTransform Root;
            public Image NormalFrame;
            public Image HighlightFrame;
            public Image Icon;
            public Text ValueText;
            public float FlashTimer;
            public Color FlashColor;
        }

        private sealed class HotbarSlotRefs
        {
            public int Index;
            public Button Button;
            public Image SelectedFrame;
            public Image Icon;
            public Text KeyText;
            public Text CostText;
        }

        private sealed class ToastEntry
        {
            public GameObject Root;
            public CanvasGroup Group;
            public Image BorderAccent;
            public Text Label;
            public float RemainingSec;
            public float TotalSec;
        }

        private GameplayUiRoot _uiRoot;
        private RectTransform _hudRoot;
        private MinimapUI _minimap;

        // Top-Left Resources, Stamina & Buffs
        private readonly ResourceSlotRefs[] _resourceSlots = new ResourceSlotRefs[5];
        private BiopunkProgressBar _staminaBar;
        private GameObject _buffBadgeObj;
        private Text _buffBadgeText;

        // Left Mission Objectives
        private GameObject _objectivesBodyObj;
        private Text _objectivesToggleLabel;
        private readonly Text[] _objectiveTexts = new Text[5];
        private bool _objectivesExpanded = true;

        // Top-Center Alert & Heart Core Bars
        private Image _alertBannerImg;
        private Image _weatherIconImg;
        private Text _alertBannerText;
        private BiopunkProgressBar _coreHpBar;
        private BiopunkProgressBar _coreAwakeningBar;
        private float _lastKnownCoreHp = 1500f;
        private float _lastKnownCoreMaxHp = 1500f;
        private float _coreUnderAttackTimer;

        // Top-Right Wave & Action Buttons
        private Text _waveTitleText;
        private Text _waveSubText;
        private Button _earlyWaveButton;
        private Text _earlyWaveLabel;
        private Button _geneMiniGameBtn;
        private Text _geneMiniGameBtnLabel;
        private Button _defibMiniGameBtn;
        private Text _defibMiniGameBtnLabel;
        private Button _tacticalPauseBtn;
        private Text _tacticalPauseBtnLabel;

        // Bottom-Center 8-Slot Hotbar
        private readonly HotbarSlotRefs[] _hotbarSlots = new HotbarSlotRefs[8];
        private HotbarSlotData[] _currentLoadout = Array.Empty<HotbarSlotData>();
        private int _selectedHotbarIndex;
        private Text _selectedItemTitleText;
        private Text _selectedItemHintText;

        // Toast Notification Stack
        private RectTransform _toastStackRoot;
        private readonly List<ToastEntry> _toasts = new List<ToastEntry>(4);

        // State tracking for Wave HUD
        private bool _isWaveInProgress;
        private int _displayWaveNumber = 1;
        private float _waveCountdownSec = 45f;
        private float _periodicRefreshTimer;

        public MinimapUI Minimap => _minimap;

        public void Initialize(GameplayUiRoot uiRoot, Transform parent)
        {
            _uiRoot = uiRoot;
            _hudRoot = BiopunkUiBuilder.CreateStretchRect("GameplayHudRoot", parent);

            BuildTopLeftResourceCluster();
            BuildLeftObjectivesTracker();
            BuildTopCenterHeartCluster();
            BuildTopRightWaveAndActionsCluster();
            BuildBottomHotbarCluster();
            BuildBottomRightMinimap();
            BuildToastStack();

            // Populate initial loadout & resource numbers immediately
            _currentLoadout = BalanceDatabase.GetDefaultHotbarCopy();
            RefreshAllResources();
            RefreshHotbarVisuals();
            RefreshCoreBarsFromWorld();
            RefreshObjectivesTracker();
            RefreshAlertAndWeatherBanner();
            RefreshWaveBanner();
        }

        private void OnEnable()
        {
            GameEvents.OnNewGameSessionInitialized += HandleSessionReset;
            GameEvents.OnResourceChanged += HandleResourceChanged;
            GameEvents.OnInsufficientResource += HandleInsufficientResource;
            GameEvents.OnGlobalBuffUpdated += HandleBuffUpdated;
            GameEvents.OnCoreHealthChanged += HandleCoreHealthChanged;
            GameEvents.OnCoreAwakeningChanged += HandleCoreAwakeningChanged;
            GameEvents.OnWeatherChanged += HandleWeatherChanged;
            GameEvents.OnWaveCountdownTick += HandleWaveCountdownTick;
            GameEvents.OnWaveStarted += HandleWaveStarted;
            GameEvents.OnWaveCleared += HandleWaveCleared;
            GameEvents.OnHotbarLoadoutChanged += HandleHotbarLoadoutChanged;
            GameEvents.OnHotbarSlotSelected += HandleHotbarSlotSelected;
            GameEvents.OnTacticalPauseChanged += HandleTacticalPauseChanged;
            GameEvents.OnBannerToast += HandleBannerToast;
        }

        private void OnDisable()
        {
            GameEvents.OnNewGameSessionInitialized -= HandleSessionReset;
            GameEvents.OnResourceChanged -= HandleResourceChanged;
            GameEvents.OnInsufficientResource -= HandleInsufficientResource;
            GameEvents.OnGlobalBuffUpdated -= HandleBuffUpdated;
            GameEvents.OnCoreHealthChanged -= HandleCoreHealthChanged;
            GameEvents.OnCoreAwakeningChanged -= HandleCoreAwakeningChanged;
            GameEvents.OnWeatherChanged -= HandleWeatherChanged;
            GameEvents.OnWaveCountdownTick -= HandleWaveCountdownTick;
            GameEvents.OnWaveStarted -= HandleWaveStarted;
            GameEvents.OnWaveCleared -= HandleWaveCleared;
            GameEvents.OnHotbarLoadoutChanged -= HandleHotbarLoadoutChanged;
            GameEvents.OnHotbarSlotSelected -= HandleHotbarSlotSelected;
            GameEvents.OnTacticalPauseChanged -= HandleTacticalPauseChanged;
            GameEvents.OnBannerToast -= HandleBannerToast;
        }

        public void SetVisible(bool visible)
        {
            if (_hudRoot != null && _hudRoot.gameObject.activeSelf != visible)
            {
                _hudRoot.gameObject.SetActive(visible);
            }
        }

        private void Update()
        {
            if (_hudRoot == null || !_hudRoot.gameObject.activeSelf) return;

            float dt = Time.unscaledDeltaTime;

            TickResourceSlotFlashes(dt);
            TickStaminaAndBuffStatus();
            TickCoreAlertPulse(dt);
            TickMiniGameButtonLabels();
            TickToastAnimations(dt);

            _periodicRefreshTimer -= dt;
            if (_periodicRefreshTimer <= 0f)
            {
                _periodicRefreshTimer = 0.25f;
                RefreshObjectivesTracker();
                RefreshHotbarAffordability();
                RefreshCoreBarsFromWorld();
                if (_isWaveInProgress)
                {
                    RefreshWaveBanner();
                }
            }
        }

        // =====================================================================
        // 1. TOP-LEFT RESOURCE & STATUS CLUSTER (matching ref_gameplay.jpg)
        // =====================================================================

        private void BuildTopLeftResourceCluster()
        {
            Image barPanel = BiopunkUiBuilder.CreatePanel(
                _hudRoot, "TopLeftResourceBar",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -12f), new Vector2(486f, 96f),
                Color.white, raycastTarget: true);

            RectTransform slotsRow = BiopunkUiBuilder.CreateStretchRect("ResourceSlotsRow", barPanel.transform, 10f, 8f, 10f, 8f);
            HorizontalLayoutGroup hlg = slotsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            ResourceType[] types =
            {
                ResourceType.Biomass,
                ResourceType.Spores,
                ResourceType.BioPlasma,
                ResourceType.BoneChitin,
                ResourceType.Mutagen
            };

            for (int i = 0; i < types.Length; i++)
            {
                ResourceType rType = types[i];
                GameObject slotGo = new GameObject($"ResSlot_{rType}");
                slotGo.transform.SetParent(slotsRow, false);
                RectTransform slotRt = slotGo.AddComponent<RectTransform>();

                Image normalImg = slotGo.AddComponent<Image>();
                normalImg.sprite = SpriteRegistry.Get("UI/UI_Frame_SlotNormal");
                normalImg.color = Color.white;
                normalImg.raycastTarget = true;

                Image highlightImg = BiopunkUiBuilder.CreateSpriteImage(
                    slotRt, "FlashFrame", "UI/UI_Frame_SlotSelected",
                    Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                    Vector2.zero, Vector2.zero, preserveAspect: false, raycastTarget: false);
                BiopunkUiBuilder.Stretch(highlightImg.rectTransform, -2f, -2f, -2f, -2f);
                highlightImg.gameObject.SetActive(false);

                Image iconImg = BiopunkUiBuilder.CreateRect(
                    "Icon", slotRt,
                    new Vector2(0.5f, 0.62f), new Vector2(0.5f, 0.62f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(44f, 44f)).gameObject.AddComponent<Image>();
                iconImg.sprite = SpriteRegistry.GetResourceIcon(rType);
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;

                Text valTxt = BiopunkUiBuilder.CreateText(
                    slotRt, "Amount", "0", 18, FontStyle.Bold,
                    BalanceDatabase.GetResourceColor(rType), TextAnchor.LowerCenter);
                valTxt.rectTransform.anchorMin = new Vector2(0.04f, 0.06f);
                valTxt.rectTransform.anchorMax = new Vector2(0.96f, 0.36f);
                valTxt.rectTransform.offsetMin = Vector2.zero;
                valTxt.rectTransform.offsetMax = Vector2.zero;
                valTxt.horizontalOverflow = HorizontalWrapMode.Overflow;

                ResourceType capturedType = rType;
                BiopunkUiBuilder.AttachDynamicTooltip(
                    slotGo,
                    BalanceDatabase.GetResourceNameRu(capturedType),
                    () => BuildResourceTooltipText(capturedType));

                _resourceSlots[i] = new ResourceSlotRefs
                {
                    Type = rType,
                    Root = slotRt,
                    NormalFrame = normalImg,
                    HighlightFrame = highlightImg,
                    Icon = iconImg,
                    ValueText = valTxt
                };
            }

            // Compact Stamina Bar directly beneath Resource Bar
            Image staminaPanel = BiopunkUiBuilder.CreatePanel(
                _hudRoot, "StaminaRow",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -114f), new Vector2(486f, 32f),
                new Color(0.90f, 0.94f, 0.90f, 0.94f), raycastTarget: false);

            Text stamTitle = BiopunkUiBuilder.CreateText(
                staminaPanel.transform, "StamLabel", "ВЫНОСЛИВОСТЬ:",
                16, FontStyle.Bold, BiopunkUiBuilder.TextSecondary, TextAnchor.MiddleLeft);
            stamTitle.rectTransform.anchorMin = new Vector2(0f, 0f);
            stamTitle.rectTransform.anchorMax = new Vector2(0.36f, 1f);
            stamTitle.rectTransform.offsetMin = new Vector2(12f, 0f);
            stamTitle.rectTransform.offsetMax = Vector2.zero;

            _staminaBar = BiopunkUiBuilder.CreateProgressBar(
                staminaPanel.transform, "StaminaBar",
                new Vector2(296f, 22f),
                new Color(0.46f, 0.92f, 0.42f, 1f),
                "100%", 16);
            _staminaBar.Root.anchorMin = new Vector2(0.37f, 0.5f);
            _staminaBar.Root.anchorMax = new Vector2(0.98f, 0.5f);
            _staminaBar.Root.pivot = new Vector2(0.5f, 0.5f);
            _staminaBar.Root.anchoredPosition = Vector2.zero;
            _staminaBar.Root.sizeDelta = new Vector2(0f, 22f);

            // Active Bio-Synthesis Buff Badge (shown when ResourceManager.Instance.HasActiveBuff)
            Image buffPanel = BiopunkUiBuilder.CreatePanel(
                _hudRoot, "ActiveBuffBadge",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -150f), new Vector2(486f, 34f),
                new Color(0.92f, 0.72f, 1.00f, 0.96f), raycastTarget: false);
            _buffBadgeObj = buffPanel.gameObject;

            _buffBadgeText = BiopunkUiBuilder.CreateText(
                buffPanel.transform, "BuffText", "Ускорение x2: 45с",
                18, FontStyle.Bold, BiopunkUiBuilder.AccentLime, TextAnchor.MiddleCenter);
            BiopunkUiBuilder.Stretch(_buffBadgeText.rectTransform, 10f, 2f, 10f, 2f);
            _buffBadgeObj.SetActive(false);
        }

        private static string BuildResourceTooltipText(ResourceType type)
        {
            float amt = ResourceManager.InstanceIfExists != null ? ResourceManager.InstanceIfExists.Get(type) : 0f;
            switch (type)
            {
                case ResourceType.Biomass:
                    return $"Запас: <b>{amt:0}</b>\nБазовая живая материя для построек, био-вен и семян Спорошляпки.\nИсточник: Багровый стручок, Симбиот-сборщик, поверженные жуки.";
                case ResourceType.Spores:
                    return $"Запас: <b>{amt:0}</b>\nРепродуктивные споры для посева культур и питания Спороцветов.\nИсточник: урожай Спорошляпки и обмен в Био-Кодексе [B].";
                case ResourceType.BioPlasma:
                    return $"Запас: <b>{amt:0}</b>\nВысокоэнергетическая плазма для Желчных пушек, Плазмо-вен и Инкубатора.\nИсточник: Плазмолуковица, Пульс-луковица, Био-реактор.";
                case ResourceType.BoneChitin:
                    return $"Запас: <b>{amt:0}</b>\nПрочный минеральный хитин для Костяных стен и тяжёлых орудий.\nИсточник: Костяной корень и панцири Крабов Улья.";
                case ResourceType.Mutagen:
                    return $"Прогресс: <b>{amt:0.0} / {BalanceDatabase.TargetMutagenForVictory:0}</b>\nКлючевой катализатор пробуждения Живого Сердца Колонии!\nИсточник: Мини-игра Био-Синтеза [M], Инкубатор мутагена, Альфа-Матка.";
                default:
                    return string.Empty;
            }
        }

        // =====================================================================
        // 2. LEFT MISSION OBJECTIVES TRACKER (Collapsible)
        // =====================================================================

        private void BuildLeftObjectivesTracker()
        {
            RectTransform trackerRoot = BiopunkUiBuilder.CreateRect(
                "ObjectivesTracker", _hudRoot,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -190f), new Vector2(404f, 240f));

            // Header Bar with collapse button
            Image headerBg = BiopunkUiBuilder.CreatePanel(
                trackerRoot, "TrackerHeader",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0f, 38f),
                new Color(0.86f, 0.96f, 0.84f, 0.95f), raycastTarget: true);

            Text headerTxt = BiopunkUiBuilder.CreateText(
                headerBg.transform, "HeaderLabel", "ЗАДАЧИ СИМБИОЗА",
                18, FontStyle.Bold, BiopunkUiBuilder.AccentLime, TextAnchor.MiddleLeft);
            headerTxt.rectTransform.anchorMin = new Vector2(0f, 0f);
            headerTxt.rectTransform.anchorMax = new Vector2(0.82f, 1f);
            headerTxt.rectTransform.offsetMin = new Vector2(14f, 0f);
            headerTxt.rectTransform.offsetMax = Vector2.zero;

            Button collapseBtn = BiopunkUiBuilder.CreateButton(
                headerBg.transform, "BtnToggleObjectives", "[–]",
                new Vector2(54f, 28f),
                ToggleObjectivesExpanded,
                fontSize: 18,
                baseTint: new Color(0.80f, 0.90f, 0.80f, 1f));
            collapseBtn.image.rectTransform.anchorMin = new Vector2(1f, 0.5f);
            collapseBtn.image.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            collapseBtn.image.rectTransform.pivot = new Vector2(1f, 0.5f);
            collapseBtn.image.rectTransform.anchoredPosition = new Vector2(-6f, 0f);
            _objectivesToggleLabel = collapseBtn.GetComponentInChildren<Text>();

            // Checklist Body
            Image bodyBg = BiopunkUiBuilder.CreatePanel(
                trackerRoot, "TrackerBody",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -40f), new Vector2(0f, 186f),
                new Color(0.84f, 0.88f, 0.84f, 0.90f), raycastTarget: false);
            _objectivesBodyObj = bodyBg.gameObject;

            VerticalLayoutGroup vlg = bodyBg.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(14, 12, 10, 10);
            vlg.spacing = 6f;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = true;

            for (int i = 0; i < _objectiveTexts.Length; i++)
            {
                Text item = BiopunkUiBuilder.CreateText(
                    bodyBg.transform, $"Objective_{i}", string.Empty,
                    17, FontStyle.Normal, BiopunkUiBuilder.TextPrimary, TextAnchor.MiddleLeft);
                item.horizontalOverflow = HorizontalWrapMode.Wrap;
                _objectiveTexts[i] = item;
            }
        }

        private void ToggleObjectivesExpanded()
        {
            _objectivesExpanded = !_objectivesExpanded;
            if (_objectivesBodyObj != null) _objectivesBodyObj.SetActive(_objectivesExpanded);
            if (_objectivesToggleLabel != null) _objectivesToggleLabel.text = _objectivesExpanded ? "[–]" : "[+]";
        }

        // =====================================================================
        // 3. TOP-CENTER HEART CORE & ALERT CLUSTER
        // =====================================================================

        private void BuildTopCenterHeartCluster()
        {
            RectTransform centerRoot = BiopunkUiBuilder.CreateRect(
                "TopCenterHeartCluster", _hudRoot,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -8f), new Vector2(560f, 148f));

            // UI_Banner_Alert (931x187 native ~ 5:1 ratio -> 550x82)
            _alertBannerImg = BiopunkUiBuilder.CreateSpriteImage(
                centerRoot, "AlertBanner", "UI/UI_Banner_Alert",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(552f, 78f), preserveAspect: false, raycastTarget: false);

            _weatherIconImg = BiopunkUiBuilder.CreateSpriteImage(
                _alertBannerImg.transform, "WeatherIcon", "Icon_Weather_CalmSymbiosis",
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(44f, 0f), new Vector2(36f, 36f), preserveAspect: true, raycastTarget: false);

            _alertBannerText = BiopunkUiBuilder.CreateText(
                _alertBannerImg.transform, "AlertText", "СИМБИОЗ СТАБИЛЕН",
                20, FontStyle.Bold, BiopunkUiBuilder.TextPrimary, TextAnchor.MiddleCenter);
            BiopunkUiBuilder.Stretch(_alertBannerText.rectTransform, 84f, 10f, 52f, 10f);

            // Living Heart Core HP Bar
            _coreHpBar = BiopunkUiBuilder.CreateProgressBar(
                centerRoot, "HeartHpBar",
                new Vector2(500f, 28f),
                new Color(0.92f, 0.24f, 0.34f, 1f),
                "ЖИВОЕ СЕРДЦЕ: 1500 / 1500 HP", 18);
            _coreHpBar.Root.anchorMin = new Vector2(0.5f, 1f);
            _coreHpBar.Root.anchorMax = new Vector2(0.5f, 1f);
            _coreHpBar.Root.pivot = new Vector2(0.5f, 1f);
            _coreHpBar.Root.anchoredPosition = new Vector2(0f, -82f);

            // Heart Awakening (Mutagen X / 10) Progress Bar
            _coreAwakeningBar = BiopunkUiBuilder.CreateProgressBar(
                centerRoot, "HeartAwakeningBar",
                new Vector2(500f, 26f),
                new Color(0.28f, 0.86f, 0.98f, 1f),
                "ПРОБУЖДЕНИЕ СЕРДЦА: Мутаген 0.0 / 10", 17);
            _coreAwakeningBar.Root.anchorMin = new Vector2(0.5f, 1f);
            _coreAwakeningBar.Root.anchorMax = new Vector2(0.5f, 1f);
            _coreAwakeningBar.Root.pivot = new Vector2(0.5f, 1f);
            _coreAwakeningBar.Root.anchoredPosition = new Vector2(0f, -114f);
        }

        // =====================================================================
        // 4. TOP-RIGHT WAVE & ACTION CLUSTER
        // =====================================================================

        private void BuildTopRightWaveAndActionsCluster()
        {
            RectTransform rightRoot = BiopunkUiBuilder.CreateRect(
                "TopRightWaveCluster", _hudRoot,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-16f, -12f), new Vector2(392f, 304f));

            // UI_Banner_Wave (378x103 native -> 392x86)
            Image waveBanner = BiopunkUiBuilder.CreateSpriteImage(
                rightRoot, "WaveBanner", "UI/UI_Banner_Wave",
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                Vector2.zero, new Vector2(392f, 86f), preserveAspect: false, raycastTarget: false);

            _waveTitleText = BiopunkUiBuilder.CreateText(
                waveBanner.transform, "WaveTitle", "ВОЛНА 1 / 5",
                24, FontStyle.Bold, BiopunkUiBuilder.AccentLime, TextAnchor.MiddleCenter);
            _waveTitleText.rectTransform.anchorMin = new Vector2(0.06f, 0.48f);
            _waveTitleText.rectTransform.anchorMax = new Vector2(0.94f, 0.90f);
            _waveTitleText.rectTransform.offsetMin = Vector2.zero;
            _waveTitleText.rectTransform.offsetMax = Vector2.zero;

            _waveSubText = BiopunkUiBuilder.CreateText(
                waveBanner.transform, "WaveCountdown", "До волны: 45с",
                18, FontStyle.Bold, BiopunkUiBuilder.TextPrimary, TextAnchor.MiddleCenter);
            _waveSubText.rectTransform.anchorMin = new Vector2(0.06f, 0.12f);
            _waveSubText.rectTransform.anchorMax = new Vector2(0.94f, 0.50f);
            _waveSubText.rectTransform.offsetMin = Vector2.zero;
            _waveSubText.rectTransform.offsetMax = Vector2.zero;

            // Early Wave Call Button during prep countdown
            _earlyWaveButton = BiopunkUiBuilder.CreateButton(
                rightRoot, "BtnSendWaveEarly", "ВЫЗВАТЬ ВОЛНУ СЕЙЧАС (+35 БИО)",
                new Vector2(392f, 40f),
                OnSendNextWaveEarlyClicked,
                fontSize: 18,
                baseTint: new Color(0.98f, 0.80f, 0.48f, 1f),
                textColor: new Color(1.0f, 0.96f, 0.84f, 1f));
            _earlyWaveButton.image.rectTransform.anchorMin = new Vector2(1f, 1f);
            _earlyWaveButton.image.rectTransform.anchorMax = new Vector2(1f, 1f);
            _earlyWaveButton.image.rectTransform.pivot = new Vector2(1f, 1f);
            _earlyWaveButton.image.rectTransform.anchoredPosition = new Vector2(0f, -92f);
            _earlyWaveLabel = _earlyWaveButton.GetComponentInChildren<Text>();

            // Compact 2x2 Functional Window Action Buttons + Heart Defib quick button
            RectTransform actionGrid = BiopunkUiBuilder.CreateRect(
                "WindowActionGrid", rightRoot,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -140f), new Vector2(392f, 92f));

            GridLayoutGroup glg = actionGrid.gameObject.AddComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(192f, 42f);
            glg.spacing = new Vector2(8f, 8f);
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 2;

            Button codexBtn = BiopunkUiBuilder.CreateButton(
                actionGrid, "BtnOpenBioCodex", "[B] Био-Кодекс",
                new Vector2(192f, 42f),
                () => _uiRoot?.ToggleBioCodex(),
                fontSize: 18,
                baseTint: new Color(0.76f, 0.96f, 0.68f, 1f),
                iconSprite: SpriteRegistry.Get("UI/Icon_SeedPouch"));
            BiopunkUiBuilder.AttachTooltip(
                codexBtn.gameObject,
                "Био-Кодекс [B / Tab]",
                "Открыть каталог семян, турелей, заводов, приоритетов сети и экстренного био-обмена.");

            _geneMiniGameBtn = BiopunkUiBuilder.CreateButton(
                actionGrid, "BtnGeneMiniGame", "[M] Био-Синтез",
                new Vector2(192f, 42f),
                () => _uiRoot?.RequestGeneSplicingMiniGame(),
                fontSize: 18,
                baseTint: new Color(0.92f, 0.72f, 1.00f, 1f),
                iconSprite: SpriteRegistry.Get("UI/Icon_Building_Incubator"));
            _geneMiniGameBtnLabel = _geneMiniGameBtn.GetComponentInChildren<Text>();
            BiopunkUiBuilder.AttachTooltip(
                _geneMiniGameBtn.gameObject,
                "Мини-игра: Био-Синтез [M]",
                "Калибровка генетического импульса: даёт +1.5..2.5 Мутагена и ускоряет рост культур x2 на 45с!");

            _tacticalPauseBtn = BiopunkUiBuilder.CreateButton(
                actionGrid, "BtnTacticalPause", "[Space] Такт. пауза",
                new Vector2(192f, 42f),
                () => GameManager.Instance?.ToggleTacticalPause(),
                fontSize: 17,
                baseTint: new Color(0.76f, 0.92f, 1.00f, 1f));
            _tacticalPauseBtnLabel = _tacticalPauseBtn.GetComponentInChildren<Text>();
            BiopunkUiBuilder.AttachTooltip(
                _tacticalPauseBtn.gameObject,
                "Тактическая пауза [Пробел]",
                "Останавливает время боя, позволяя строить защиту, менять приоритеты вен и осматривать карту.");

            Button pauseHelpBtn = BiopunkUiBuilder.CreateButton(
                actionGrid, "BtnPauseHelp", "[F1/Esc] Справка",
                new Vector2(192f, 42f),
                () => _uiRoot?.TogglePauseMenu(),
                fontSize: 18,
                baseTint: new Color(0.88f, 0.88f, 0.82f, 1f));
            BiopunkUiBuilder.AttachTooltip(
                pauseHelpBtn.gameObject,
                "Пауза и Управление [F1 / Esc]",
                "Настройки звука, памятка клавиш, перезапуск миссии или выход в меню.");

            // Quick Core Defibrillation Mini-Game Button right below the 2x2 grid
            _defibMiniGameBtn = BiopunkUiBuilder.CreateButton(
                rightRoot, "BtnCoreDefibMiniGame", "[H] Дефибрилляция Сердца (Мини-игра)",
                new Vector2(392f, 38f),
                () => _uiRoot?.RequestCoreDefibMiniGame(),
                fontSize: 17,
                baseTint: new Color(0.76f, 0.96f, 0.98f, 1f),
                iconSprite: SpriteRegistry.Get("Core_LivingHeart"));
            _defibMiniGameBtn.image.rectTransform.anchorMin = new Vector2(1f, 1f);
            _defibMiniGameBtn.image.rectTransform.anchorMax = new Vector2(1f, 1f);
            _defibMiniGameBtn.image.rectTransform.pivot = new Vector2(1f, 1f);
            _defibMiniGameBtn.image.rectTransform.anchoredPosition = new Vector2(0f, -240f);
            _defibMiniGameBtnLabel = _defibMiniGameBtn.GetComponentInChildren<Text>();
            BiopunkUiBuilder.AttachTooltip(
                _defibMiniGameBtn.gameObject,
                "Реанимация Живого Сердца [H / E у Ядра]",
                "Очистите сосуды Сердца от тромбов, чтобы исцелить Ядро, вызвать ударную волну и получить Биоплазму + Мутаген!");
        }

        private void OnSendNextWaveEarlyClicked()
        {
            if (_isWaveInProgress) return;
            GameManager gm = GameManager.InstanceIfExists;
            if (gm != null && gm.WavesSurvived >= BalanceDatabase.TotalWaves) return;

            GameEvents.RaiseSendNextWaveEarlyRequested();
        }

        // =====================================================================
        // 5. BOTTOM-CENTER 8-SLOT HOTBAR (UI/UI_Panel_Hotbar8)
        // =====================================================================

        private void BuildBottomHotbarCluster()
        {
            RectTransform hotbarCluster = BiopunkUiBuilder.CreateRect(
                "BottomHotbarCluster", _hudRoot,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 10f), new Vector2(936f, 188f));

            // Selected Item Name & LMB/RMB Usage Hint Bar above hotbar
            Image infoStrip = BiopunkUiBuilder.CreatePanel(
                hotbarCluster, "SelectedSlotHintBar",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(936f, 40f),
                new Color(0.84f, 0.92f, 0.84f, 0.93f), raycastTarget: false);

            _selectedItemTitleText = BiopunkUiBuilder.CreateText(
                infoStrip.transform, "SelectedTitle", "Культиватор-мотыга",
                17, FontStyle.Bold, BiopunkUiBuilder.AccentLime, TextAnchor.MiddleLeft);
            _selectedItemTitleText.rectTransform.anchorMin = new Vector2(0f, 0f);
            _selectedItemTitleText.rectTransform.anchorMax = new Vector2(0.49f, 1f);
            _selectedItemTitleText.rectTransform.offsetMin = new Vector2(14f, 2f);
            _selectedItemTitleText.rectTransform.offsetMax = new Vector2(-6f, -2f);
            _selectedItemTitleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _selectedItemTitleText.verticalOverflow = VerticalWrapMode.Truncate;
            _selectedItemTitleText.resizeTextForBestFit = true;
            _selectedItemTitleText.resizeTextMinSize = 13;
            _selectedItemTitleText.resizeTextMaxSize = 17;

            _selectedItemHintText = BiopunkUiBuilder.CreateText(
                infoStrip.transform, "SelectedUsageHint", "[ЛКМ] Рыхлить почву / Собрать урожай",
                16, FontStyle.Normal, BiopunkUiBuilder.TextPrimary, TextAnchor.MiddleRight);
            _selectedItemHintText.rectTransform.anchorMin = new Vector2(0.50f, 0f);
            _selectedItemHintText.rectTransform.anchorMax = new Vector2(1f, 1f);
            _selectedItemHintText.rectTransform.offsetMin = new Vector2(6f, 2f);
            _selectedItemHintText.rectTransform.offsetMax = new Vector2(-14f, -2f);
            _selectedItemHintText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _selectedItemHintText.verticalOverflow = VerticalWrapMode.Truncate;
            _selectedItemHintText.resizeTextForBestFit = true;
            _selectedItemHintText.resizeTextMinSize = 12;
            _selectedItemHintText.resizeTextMaxSize = 16;

            // Main 8-Slot Pixel-Art Hotbar Panel (UI/UI_Panel_Hotbar8 = 1081x172)
            Image hotbarBg = BiopunkUiBuilder.CreateSpriteImage(
                hotbarCluster, "Hotbar8Panel", "UI/UI_Panel_Hotbar8",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                Vector2.zero, new Vector2(936f, 146f), preserveAspect: false, raycastTarget: true);

            // Inner 8-slot row aligned with the 8 dark cells inside UI_Panel_Hotbar8
            RectTransform slotsContainer = BiopunkUiBuilder.CreateRect(
                "HotbarSlotsRow", hotbarBg.transform,
                new Vector2(0.081f, 0.09f), new Vector2(0.921f, 0.80f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            slotsContainer.offsetMin = Vector2.zero;
            slotsContainer.offsetMax = Vector2.zero;

            HorizontalLayoutGroup hlg = slotsContainer.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            for (int i = 0; i < 8; i++)
            {
                int slotIdx = i;
                GameObject slotGo = new GameObject($"HotbarSlot_{i + 1}");
                slotGo.transform.SetParent(slotsContainer, false);
                RectTransform slotRt = slotGo.AddComponent<RectTransform>();

                Image hitImg = slotGo.AddComponent<Image>();
                hitImg.sprite = SpriteRegistry.GetWhiteSquareSprite();
                hitImg.color = new Color(1f, 1f, 1f, 0.01f);
                hitImg.raycastTarget = true;

                Button slotBtn = slotGo.AddComponent<Button>();
                slotBtn.targetGraphic = hitImg;
                Navigation nav = slotBtn.navigation;
                nav.mode = Navigation.Mode.None;
                slotBtn.navigation = nav;
                slotBtn.onClick.AddListener(() => OnHotbarSlotClicked(slotIdx));

                Image selFrame = BiopunkUiBuilder.CreateSpriteImage(
                    slotRt, "SelectedFrame", "UI/UI_Frame_SlotSelected",
                    Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                    Vector2.zero, Vector2.zero, preserveAspect: false, raycastTarget: false);
                BiopunkUiBuilder.Stretch(selFrame.rectTransform, -4f, -4f, -4f, -4f);
                selFrame.gameObject.SetActive(i == 0);

                Image iconImg = BiopunkUiBuilder.CreateRect(
                    "ItemIcon", slotRt,
                    new Vector2(0.5f, 0.54f), new Vector2(0.5f, 0.54f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(58f, 58f)).gameObject.AddComponent<Image>();
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;

                Text keyTxt = BiopunkUiBuilder.CreateText(
                    slotRt, "KeyBadge", (i + 1).ToString(),
                    16, FontStyle.Bold, BiopunkUiBuilder.TextSecondary, TextAnchor.UpperLeft);
                keyTxt.rectTransform.anchorMin = new Vector2(0.08f, 0.64f);
                keyTxt.rectTransform.anchorMax = new Vector2(0.45f, 0.95f);
                keyTxt.rectTransform.offsetMin = Vector2.zero;
                keyTxt.rectTransform.offsetMax = Vector2.zero;

                Text costTxt = BiopunkUiBuilder.CreateText(
                    slotRt, "CostBadge", string.Empty,
                    16, FontStyle.Bold, BiopunkUiBuilder.AccentLime, TextAnchor.LowerRight);
                costTxt.rectTransform.anchorMin = new Vector2(0.08f, 0.05f);
                costTxt.rectTransform.anchorMax = new Vector2(0.93f, 0.34f);
                costTxt.rectTransform.offsetMin = Vector2.zero;
                costTxt.rectTransform.offsetMax = Vector2.zero;
                costTxt.horizontalOverflow = HorizontalWrapMode.Overflow;

                BiopunkUiBuilder.AttachDynamicTooltip(
                    slotGo,
                    $"Слот [{i + 1}]",
                    () => BuildHotbarSlotTooltip(slotIdx));

                _hotbarSlots[i] = new HotbarSlotRefs
                {
                    Index = i,
                    Button = slotBtn,
                    SelectedFrame = selFrame,
                    Icon = iconImg,
                    KeyText = keyTxt,
                    CostText = costTxt
                };
            }
        }

        private void OnHotbarSlotClicked(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _hotbarSlots.Length) return;
            BiopunkUiBuilder.PlayClickSfx();

            // Notify player hotbar controller if present on Player GameObject or in scene
            if (WorldEntityRegistry.PlayerTransform != null)
            {
                WorldEntityRegistry.PlayerTransform.BroadcastMessage("SelectSlot", slotIndex, SendMessageOptions.DontRequireReceiver);
            }

            HotbarSlotData data = (slotIndex < _currentLoadout.Length) ? _currentLoadout[slotIndex] : null;
            _selectedHotbarIndex = slotIndex;
            GameEvents.RaiseHotbarSlotSelected(slotIndex, data);
        }

        private string BuildHotbarSlotTooltip(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _currentLoadout.Length || _currentLoadout[slotIndex] == null || _currentLoadout[slotIndex].IsEmpty)
            {
                return "Пустой слот.\nОткройте Био-Кодекс [B], чтобы назначить сюда семена или постройку.";
            }

            HotbarSlotData s = _currentLoadout[slotIndex];
            string costLine = ResourceManager.FormatCostsRu(s.costs);
            return $"<b>{s.displayName}</b>\nСтоимость: {costLine}\n{s.description}";
        }

        // =====================================================================
        // 6. BOTTOM-RIGHT REAL-TIME MINIMAP & TOAST NOTIFICATIONS
        // =====================================================================

        private void BuildBottomRightMinimap()
        {
            GameObject mapHost = new GameObject("MinimapController");
            mapHost.transform.SetParent(_hudRoot, false);
            _minimap = mapHost.AddComponent<MinimapUI>();
            _minimap.Build(_hudRoot);
        }

        private void BuildToastStack()
        {
            _toastStackRoot = BiopunkUiBuilder.CreateRect(
                "ToastBannerStack", _hudRoot,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -148f), new Vector2(620f, 92f));

            VerticalLayoutGroup vlg = _toastStackRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4f;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
        }

        // =====================================================================
        // EVENT HANDLERS & LIVE REFRESHES
        // =====================================================================

        private void HandleSessionReset()
        {
            _isWaveInProgress = false;
            _displayWaveNumber = 1;
            _waveCountdownSec = 45f;
            _coreUnderAttackTimer = 0f;
            _lastKnownCoreHp = 1500f;
            _lastKnownCoreMaxHp = 1500f;

            RefreshAllResources();
            RefreshCoreBarsFromWorld();
            RefreshObjectivesTracker();
            RefreshAlertAndWeatherBanner();
            RefreshWaveBanner();
            RefreshHotbarVisuals();
        }

        private void HandleResourceChanged(ResourceType type, float newAmount, float delta)
        {
            UpdateSingleResourceSlot(type, newAmount);
            if (delta > 0.01f)
            {
                TriggerSlotFlash(type, new Color(0.55f, 1.0f, 0.45f, 1f));
            }
            RefreshHotbarAffordability();
        }

        private void HandleInsufficientResource(ResourceType missingType)
        {
            TriggerSlotFlash(missingType, BiopunkUiBuilder.AccentDanger);
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySfx(AudioKeys.SfxMiniGameFail, 0.55f, 0.04f);
            }
        }

        private void HandleBuffUpdated(float cropMult, float turretMult, float secondsRemaining)
        {
            TickStaminaAndBuffStatus();
        }

        private void HandleCoreHealthChanged(float currentHp, float maxHp)
        {
            if (currentHp < _lastKnownCoreHp - 0.5f)
            {
                _coreUnderAttackTimer = 4.5f;
            }
            _lastKnownCoreHp = currentHp;
            _lastKnownCoreMaxHp = Mathf.Max(1f, maxHp);
            UpdateCoreHpVisual(_lastKnownCoreHp, _lastKnownCoreMaxHp);
            RefreshAlertAndWeatherBanner();
        }

        private void HandleCoreAwakeningChanged(float progress01, float currentMutagen, float targetMutagen)
        {
            if (_coreAwakeningBar != null)
            {
                int pct = Mathf.RoundToInt(progress01 * 100f);
                Color barCol = progress01 >= 0.999f
                    ? BiopunkUiBuilder.AccentLime
                    : Color.Lerp(BiopunkUiBuilder.AccentCyan, BiopunkUiBuilder.AccentMutagen, progress01);
                _coreAwakeningBar.SetProgress(
                    progress01,
                    $"ПРОБУЖДЕНИЕ СЕРДЦА: Мутаген {currentMutagen:0.0} / {targetMutagen:0} ({pct}%)",
                    barCol);
            }
        }

        private void HandleWeatherChanged(WeatherType weather, float remainingSec)
        {
            RefreshAlertAndWeatherBanner();
        }

        private void HandleWaveCountdownTick(int nextWaveNumber, float secondsLeft)
        {
            _isWaveInProgress = false;
            _displayWaveNumber = Mathf.Clamp(nextWaveNumber, 1, BalanceDatabase.TotalWaves);
            _waveCountdownSec = Mathf.Max(0f, secondsLeft);
            RefreshWaveBanner();
        }

        private void HandleWaveStarted(int waveNumber, WaveDefinition waveDef)
        {
            _isWaveInProgress = true;
            _displayWaveNumber = Mathf.Clamp(waveNumber, 1, BalanceDatabase.TotalWaves);
            RefreshWaveBanner();
        }

        private void HandleWaveCleared(int waveNumber)
        {
            _isWaveInProgress = false;
            _displayWaveNumber = Mathf.Clamp(waveNumber + 1, 1, BalanceDatabase.TotalWaves);
            RefreshWaveBanner();
            RefreshObjectivesTracker();
        }

        private void HandleHotbarLoadoutChanged(HotbarSlotData[] slots)
        {
            if (slots != null && slots.Length > 0)
            {
                _currentLoadout = slots;
                RefreshHotbarVisuals();
            }
        }

        private void HandleHotbarSlotSelected(int slotIndex, HotbarSlotData slotData)
        {
            _selectedHotbarIndex = slotIndex;
            if (slotData != null && slotIndex >= 0 && slotIndex < _currentLoadout.Length)
            {
                _currentLoadout[slotIndex] = slotData;
            }
            RefreshHotbarVisuals();
            UpdateSelectedHotbarHint(slotData);
        }

        private void HandleTacticalPauseChanged(bool isPaused)
        {
            if (_tacticalPauseBtn != null && _tacticalPauseBtn.image != null)
            {
                _tacticalPauseBtn.image.color = isPaused
                    ? new Color(1.00f, 0.86f, 0.42f, 1f)
                    : new Color(0.76f, 0.92f, 1.00f, 1f);
            }
            if (_tacticalPauseBtnLabel != null)
            {
                _tacticalPauseBtnLabel.text = isPaused ? "[Space] В БОЙ ►" : "[Space] Такт. пауза";
            }
        }

        private void HandleBannerToast(string message, Color accentColor)
        {
            if (string.IsNullOrWhiteSpace(message) || _toastStackRoot == null) return;

            while (_toasts.Count >= 2)
            {
                ToastEntry oldest = _toasts[0];
                _toasts.RemoveAt(0);
                if (oldest.Root != null) Destroy(oldest.Root);
            }

            Image card = BiopunkUiBuilder.CreatePanel(
                _toastStackRoot, "ToastItem",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(600f, 36f),
                new Color(0.90f, 0.94f, 0.90f, 0.96f), raycastTarget: false);

            LayoutElement le = card.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 36f;

            CanvasGroup cg = card.gameObject.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.alpha = 1f;

            RectTransform stripRt = BiopunkUiBuilder.CreateRect(
                "AccentBar", card.transform,
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(5f, 0f), new Vector2(5f, -8f));
            Image stripImg = stripRt.gameObject.AddComponent<Image>();
            stripImg.sprite = SpriteRegistry.GetWhiteSquareSprite();
            stripImg.color = accentColor;
            stripImg.raycastTarget = false;

            Text txt = BiopunkUiBuilder.CreateText(
                card.transform, "Msg", message, 16, FontStyle.Bold, accentColor, TextAnchor.MiddleCenter);
            txt.resizeTextForBestFit = true;
            txt.resizeTextMinSize = 13;
            txt.resizeTextMaxSize = 16;
            BiopunkUiBuilder.Stretch(txt.rectTransform, 16f, 3f, 12f, 3f);

            _toasts.Add(new ToastEntry
            {
                Root = card.gameObject,
                Group = cg,
                BorderAccent = stripImg,
                Label = txt,
                RemainingSec = 3.2f,
                TotalSec = 3.2f
            });
        }

        private void RefreshAllResources()
        {
            ResourceManager rm = ResourceManager.InstanceIfExists;
            for (int i = 0; i < _resourceSlots.Length; i++)
            {
                if (_resourceSlots[i] == null) continue;
                ResourceType t = _resourceSlots[i].Type;
                float val = rm != null ? rm.Get(t) : BalanceDatabase.GetStartingResource(t);
                UpdateSingleResourceSlot(t, val);
            }
        }

        private void UpdateSingleResourceSlot(ResourceType type, float displayVal)
        {
            for (int i = 0; i < _resourceSlots.Length; i++)
            {
                ResourceSlotRefs s = _resourceSlots[i];
                if (s == null || s.Type != type) continue;

                if (type == ResourceType.Mutagen)
                {
                    s.ValueText.text = $"{displayVal:0.0}/10";
                }
                else
                {
                    s.ValueText.text = Mathf.FloorToInt(displayVal + 0.001f).ToString();
                }
                break;
            }
        }

        private void TriggerSlotFlash(ResourceType type, Color flashColor)
        {
            for (int i = 0; i < _resourceSlots.Length; i++)
            {
                ResourceSlotRefs s = _resourceSlots[i];
                if (s == null || s.Type != type) continue;

                s.FlashTimer = 0.55f;
                s.FlashColor = flashColor;
                if (s.HighlightFrame != null)
                {
                    s.HighlightFrame.color = flashColor;
                    s.HighlightFrame.gameObject.SetActive(true);
                }
                break;
            }
        }

        private void TickResourceSlotFlashes(float dt)
        {
            for (int i = 0; i < _resourceSlots.Length; i++)
            {
                ResourceSlotRefs s = _resourceSlots[i];
                if (s == null || s.FlashTimer <= 0f) continue;

                s.FlashTimer -= dt;
                float t = Mathf.Clamp01(s.FlashTimer / 0.55f);
                float scale = 1f + Mathf.Sin(t * Mathf.PI) * 0.10f;
                s.Root.localScale = new Vector3(scale, scale, 1f);

                if (s.HighlightFrame != null)
                {
                    Color c = s.FlashColor;
                    c.a = t;
                    s.HighlightFrame.color = c;
                }

                if (s.FlashTimer <= 0f)
                {
                    s.Root.localScale = Vector3.one;
                    if (s.HighlightFrame != null) s.HighlightFrame.gameObject.SetActive(false);
                }
            }
        }

        private void TickStaminaAndBuffStatus()
        {
            if (_staminaBar != null)
            {
                float stam = Mathf.Clamp01(WorldEntityRegistry.PlayerStaminaNormalized);
                Color col = stam < 0.25f
                    ? BiopunkUiBuilder.AccentDanger
                    : Color.Lerp(BiopunkUiBuilder.AccentAmber, BiopunkUiBuilder.AccentLime, stam);
                _staminaBar.SetProgress(stam, $"{Mathf.RoundToInt(stam * 100f)}%", col);
            }

            ResourceManager rm = ResourceManager.InstanceIfExists;
            bool hasBuff = rm != null && rm.HasActiveBuff;
            if (_buffBadgeObj != null)
            {
                if (_buffBadgeObj.activeSelf != hasBuff)
                {
                    _buffBadgeObj.SetActive(hasBuff);
                }
                if (hasBuff && _buffBadgeText != null)
                {
                    int sec = Mathf.CeilToInt(rm.BuffRemainingSeconds);
                    _buffBadgeText.text =
                        $"Ускорение x{rm.CropGrowthMultiplier:0.#} | Турели x{rm.TurretFireRateMultiplier:0.##}: {sec}с";
                }
            }
        }

        private void TickCoreAlertPulse(float dt)
        {
            if (_coreUnderAttackTimer > 0f)
            {
                _coreUnderAttackTimer -= dt;
                float pulse = 0.65f + 0.35f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 8.5f));
                if (_alertBannerImg != null)
                {
                    _alertBannerImg.color = Color.Lerp(Color.white, new Color(1.0f, 0.45f, 0.35f, 1f), pulse);
                }
                if (_alertBannerText != null)
                {
                    _alertBannerText.color = Color.Lerp(BiopunkUiBuilder.AccentAmber, BiopunkUiBuilder.AccentDanger, pulse);
                }

                if (_coreUnderAttackTimer <= 0f)
                {
                    if (_alertBannerImg != null) _alertBannerImg.color = Color.white;
                    RefreshAlertAndWeatherBanner();
                }
            }
            else if (GameManager.InstanceIfExists != null && GameManager.InstanceIfExists.State == GameState.Playing)
            {
                // Update weather remaining timer cleanly
                RefreshAlertAndWeatherBanner();
            }
        }

        private void RefreshAlertAndWeatherBanner()
        {
            if (_alertBannerText == null) return;

            if (_coreUnderAttackTimer > 0f)
            {
                _alertBannerText.text = "ЯДРО ПОД АТАКОЙ!";
                if (_weatherIconImg != null)
                {
                    _weatherIconImg.sprite = SpriteRegistry.Get("Core_LivingHeart");
                }
                return;
            }

            GameManager gm = GameManager.InstanceIfExists;
            WeatherType wType = gm != null ? gm.CurrentWeather : WeatherType.CalmSymbiosis;
            WeatherDefinition wDef = BalanceDatabase.GetWeather(wType);

            if (_weatherIconImg != null)
            {
                _weatherIconImg.sprite = SpriteRegistry.GetWeatherIcon(wType);
            }

            if (wType == WeatherType.CalmSymbiosis)
            {
                _alertBannerText.text = "СИМБИОЗ СТАБИЛЕН • СПОКОЙНЫЙ КЛИМАТ";
                _alertBannerText.color = BiopunkUiBuilder.AccentLime;
            }
            else
            {
                int rem = gm != null ? Mathf.CeilToInt(gm.WeatherRemainingSeconds) : 30;
                string nameUpper = wDef != null ? wDef.displayName.ToUpperInvariant() : wType.ToString();
                _alertBannerText.text = $"ПОГОДА: {nameUpper} ({rem}с)";
                _alertBannerText.color = wType == WeatherType.AcidSporeRain
                    ? BiopunkUiBuilder.AccentCyan
                    : BiopunkUiBuilder.AccentAmber;
            }
        }

        private void RefreshCoreBarsFromWorld()
        {
            BioGridManager grid = BioGridManager.InstanceIfExists;
            if (grid != null)
            {
                IDamageable coreDmg = grid.GetDamageableAt(grid.HeartCoreCenterGrid);
                if (coreDmg != null)
                {
                    _lastKnownCoreHp = coreDmg.CurrentHp;
                    _lastKnownCoreMaxHp = Mathf.Max(1f, coreDmg.MaxHp);
                }
            }
            UpdateCoreHpVisual(_lastKnownCoreHp, _lastKnownCoreMaxHp);

            float mut = ResourceManager.InstanceIfExists != null ? ResourceManager.InstanceIfExists.Get(ResourceType.Mutagen) : 0f;
            float prog = GameManager.InstanceIfExists != null
                ? GameManager.InstanceIfExists.HeartAwakeningPercent / 100f
                : Mathf.Clamp01(mut / BalanceDatabase.TargetMutagenForVictory);
            HandleCoreAwakeningChanged(prog, mut, BalanceDatabase.TargetMutagenForVictory);
        }

        private void UpdateCoreHpVisual(float hp, float maxHp)
        {
            if (_coreHpBar == null) return;
            float ratio = Mathf.Clamp01(hp / Mathf.Max(1f, maxHp));
            Color col = ratio > 0.5f
                ? Color.Lerp(BiopunkUiBuilder.AccentAmber, new Color(0.38f, 0.92f, 0.48f, 1f), (ratio - 0.5f) * 2f)
                : Color.Lerp(BiopunkUiBuilder.AccentDanger, BiopunkUiBuilder.AccentAmber, ratio * 2f);

            _coreHpBar.SetProgress(
                ratio,
                $"ЖИВОЕ СЕРДЦЕ: {Mathf.CeilToInt(hp)} / {Mathf.RoundToInt(maxHp)} HP",
                col);
        }

        private void RefreshWaveBanner()
        {
            if (_waveTitleText == null || _waveSubText == null) return;

            GameManager gm = GameManager.InstanceIfExists;
            int survived = gm != null ? gm.WavesSurvived : 0;

            if (survived >= BalanceDatabase.TotalWaves)
            {
                _waveTitleText.text = $"ВОЛНА {BalanceDatabase.TotalWaves} / {BalanceDatabase.TotalWaves}";
                _waveTitleText.color = BiopunkUiBuilder.AccentLime;
                _waveSubText.text = "ВСЕ ВОЛНЫ РОЯ ОТБИТЫ!";
                _waveSubText.color = BiopunkUiBuilder.AccentCyan;
                if (_earlyWaveButton != null) _earlyWaveButton.gameObject.SetActive(false);
                return;
            }

            int waveNum = Mathf.Clamp(_displayWaveNumber, 1, BalanceDatabase.TotalWaves);
            _waveTitleText.text = $"ВОЛНА {waveNum} / {BalanceDatabase.TotalWaves}";

            if (_isWaveInProgress)
            {
                int activeCount = WorldEntityRegistry.ActiveEnemies != null ? WorldEntityRegistry.ActiveEnemies.Count : 0;
                _waveTitleText.color = BiopunkUiBuilder.AccentDanger;
                _waveSubText.text = activeCount > 0
                    ? $"ИДЁТ АТАКА РОЯ! (Врагов: {activeCount})"
                    : "ИДЁТ АТАКА РОЯ!";
                _waveSubText.color = BiopunkUiBuilder.AccentAmber;
                if (_earlyWaveButton != null) _earlyWaveButton.gameObject.SetActive(false);
            }
            else
            {
                _waveTitleText.color = BiopunkUiBuilder.AccentLime;
                _waveSubText.text = $"До волны: {Mathf.CeilToInt(_waveCountdownSec)}с";
                _waveSubText.color = BiopunkUiBuilder.TextPrimary;
                if (_earlyWaveButton != null)
                {
                    _earlyWaveButton.gameObject.SetActive(true);
                    if (_earlyWaveLabel != null)
                    {
                        _earlyWaveLabel.text = "ВЫЗВАТЬ ВОЛНУ +35 БИО";
                    }
                }
            }
        }

        private void TickMiniGameButtonLabels()
        {
            if (_uiRoot == null) return;

            if (_geneMiniGameBtnLabel != null)
            {
                float cd = _uiRoot.GeneSplicingCooldown;
                if (cd > 0.05f)
                {
                    _geneMiniGameBtnLabel.text = $"[M] Синтез ({Mathf.CeilToInt(cd)}с)";
                    if (_geneMiniGameBtn != null) _geneMiniGameBtn.interactable = false;
                }
                else
                {
                    _geneMiniGameBtnLabel.text = "[M] Био-Синтез";
                    if (_geneMiniGameBtn != null) _geneMiniGameBtn.interactable = true;
                }
            }

            if (_defibMiniGameBtnLabel != null)
            {
                float cd = _uiRoot.CoreDefibCooldown;
                if (cd > 0.05f)
                {
                    _defibMiniGameBtnLabel.text = $"[H] Дефибрилляция ({Mathf.CeilToInt(cd)}с)";
                    if (_defibMiniGameBtn != null) _defibMiniGameBtn.interactable = false;
                }
                else
                {
                    _defibMiniGameBtnLabel.text = "[H] Дефибрилляция Сердца";
                    if (_defibMiniGameBtn != null) _defibMiniGameBtn.interactable = true;
                }
            }
        }

        private void RefreshObjectivesTracker()
        {
            GameManager gm = GameManager.InstanceIfExists;
            ResourceManager rm = ResourceManager.InstanceIfExists;

            int crops = gm != null ? gm.CropsHarvested : 0;
            int structs = gm != null ? gm.StructuresBuilt : 0;
            int miniGames = gm != null ? gm.MiniGamesCompleted : 0;
            float mutagen = rm != null ? rm.Get(ResourceType.Mutagen) : 0f;
            int waves = gm != null ? gm.WavesSurvived : 0;

            FormatObjectiveRow(0, crops >= 1, $"Вырастить и собрать урожай ({crops})");
            FormatObjectiveRow(1, structs >= 1, $"Построить защиту или био-вену ({structs})");
            FormatObjectiveRow(2, miniGames >= 1, $"Калибровка в Мини-игре ({miniGames})");
            FormatObjectiveRow(3, mutagen >= BalanceDatabase.TargetMutagenForVictory - 0.01f,
                $"Синтез Мутагена для Сердца ({mutagen:0.#} / 10)");
            FormatObjectiveRow(4, waves >= BalanceDatabase.TotalWaves,
                $"Выжить во всех 5 волнах Улья ({waves} / 5)");
        }

        private void FormatObjectiveRow(int idx, bool completed, string description)
        {
            if (idx < 0 || idx >= _objectiveTexts.Length || _objectiveTexts[idx] == null) return;
            if (completed)
            {
                _objectiveTexts[idx].text = $"<color=#8CE844><b>[√]</b> {description}</color>";
            }
            else
            {
                _objectiveTexts[idx].text = $"<color=#F8D258>[ ]</color> {description}";
            }
        }

        private void RefreshHotbarVisuals()
        {
            for (int i = 0; i < _hotbarSlots.Length; i++)
            {
                HotbarSlotRefs ui = _hotbarSlots[i];
                if (ui == null) continue;

                ui.SelectedFrame.gameObject.SetActive(i == _selectedHotbarIndex);

                HotbarSlotData data = (i < _currentLoadout.Length) ? _currentLoadout[i] : null;
                if (data == null || data.IsEmpty)
                {
                    ui.Icon.enabled = false;
                    ui.CostText.text = string.Empty;
                }
                else
                {
                    ui.Icon.enabled = true;
                    ui.Icon.sprite = SpriteRegistry.Get(data.iconKey, 64f);
                    UpdateSlotCostBadge(ui, data);
                }
            }

            HotbarSlotData activeData = (_selectedHotbarIndex >= 0 && _selectedHotbarIndex < _currentLoadout.Length)
                ? _currentLoadout[_selectedHotbarIndex]
                : null;
            UpdateSelectedHotbarHint(activeData);
        }

        private void RefreshHotbarAffordability()
        {
            for (int i = 0; i < _hotbarSlots.Length; i++)
            {
                HotbarSlotRefs ui = _hotbarSlots[i];
                if (ui == null) continue;
                HotbarSlotData data = (i < _currentLoadout.Length) ? _currentLoadout[i] : null;
                if (data != null && !data.IsEmpty)
                {
                    UpdateSlotCostBadge(ui, data);
                }
            }
        }

        private static void UpdateSlotCostBadge(HotbarSlotRefs ui, HotbarSlotData data)
        {
            if (data.costs == null || data.costs.Length == 0 || data.costs[0].amount <= 0f)
            {
                ui.CostText.text = string.Empty;
                return;
            }

            float primaryAmt = data.costs[0].amount;
            ui.CostText.text = data.costs.Length > 1 ? $"{primaryAmt:0}+" : $"{primaryAmt:0}";

            bool affordable = ResourceManager.InstanceIfExists == null || ResourceManager.InstanceIfExists.CanAfford(data.costs);
            ui.CostText.color = affordable ? BiopunkUiBuilder.AccentLime : BiopunkUiBuilder.AccentDanger;
        }

        private void UpdateSelectedHotbarHint(HotbarSlotData slot)
        {
            if (_selectedItemTitleText == null || _selectedItemHintText == null) return;

            if (slot == null || slot.IsEmpty)
            {
                _selectedItemTitleText.text = "Пустой слот";
                _selectedItemHintText.text = "Откройте [B] Био-Кодекс для выбора семян или построек";
                return;
            }

            string costSuffix = (slot.costs != null && slot.costs.Length > 0)
                ? $"  <color=#B8C8AC>({ResourceManager.FormatCostsRu(slot.costs)})</color>"
                : string.Empty;
            _selectedItemTitleText.text = $"{slot.displayName}{costSuffix}";

            switch (slot.category)
            {
                case HotbarItemCategory.Tool:
                    if (slot.toolType == ToolType.CultivatorHoe)
                        _selectedItemHintText.text = "[ЛКМ] Вспахать почву / Собрать урожай";
                    else if (slot.toolType == ToolType.BioSprayer)
                        _selectedItemHintText.text = "[ЛКМ зажать] Орошать грядки и жечь рой кислотой";
                    else
                        _selectedItemHintText.text = "[ЛКМ] Чинить постройку | [ПКМ] Снос (70% возврат)";
                    break;

                case HotbarItemCategory.Seed:
                    _selectedItemHintText.text = "[ЛКМ] Посадить на вспаханную грядку | [B] Другие семена";
                    break;

                case HotbarItemCategory.Structure:
                    _selectedItemHintText.text = "[ЛКМ] Разместить постройку | [ПКМ] Снос (70% возврат)";
                    break;
            }
        }

        private void TickToastAnimations(float dt)
        {
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                ToastEntry t = _toasts[i];
                if (t.Root == null)
                {
                    _toasts.RemoveAt(i);
                    continue;
                }

                t.RemainingSec -= dt;
                if (t.Group != null)
                {
                    t.Group.alpha = t.RemainingSec < 0.55f ? Mathf.Clamp01(t.RemainingSec / 0.55f) : 1f;
                }

                if (t.RemainingSec <= 0f)
                {
                    Destroy(t.Root);
                    _toasts.RemoveAt(i);
                }
            }
        }
    }
}
