using System;
using UnityEngine;
using UnityEngine.UI;
using BioFarme.Audio;
using BioFarme.Core;
using BioFarme.Data;
using BioFarme.Grid;
using BioFarme.UI;

namespace BioFarme.MiniGames
{
    /// <summary>
    /// Contest Screen #3 — Экран мини-игры (Visually & functionally distinct modal laboratory layer).
    /// Implements both interactive biopunk mini-games:
    /// A) MiniGameKind.GeneSplicing — «БИО-СИНТЕЗ И МУТАЦИОННОЕ ОРОШЕНИЕ» (Oscilloscope + Pressure Valves)
    /// B) MiniGameKind.CoreDefibrillation — «ДЕФИБРИЛЛЯЦИЯ И ПОДКОРМКА СЕРДЦА» (Anatomical Arterial Clot Clearance)
    /// </summary>
    public sealed class BioMiniGameController : MonoBehaviour
    {
        private sealed class ClotNodeUI
        {
            public GameObject Root;
            public RectTransform Rect;
            public Button Button;
            public Image OuterGlow;
            public Image CoreIcon;
            public Vector2 BasePos;
            public float SpawnAtElapsed;
            public bool Spawned;
            public bool Cleared;
            public float PhaseOffset;
        }

        private const int GeneRoundsTotal = 3;
        private const int DefibClotsTotal = 6;
        private const float DefibDurationSec = 10.5f;

        private RectTransform _modalRoot;
        private Text _windowHeaderText;
        private Image _headerLeftIcon;

        // Mode Containers
        private GameObject _geneModeRoot;
        private GameObject _defibModeRoot;
        private GameObject _summaryOverlay;
        private Text _summaryTitleText;
        private Text _summaryBodyText;

        // --- Gene Splicing UI Refs ---
        private readonly Image[] _roundPipBgs = new Image[GeneRoundsTotal];
        private readonly Text[] _roundPipLabels = new Text[GeneRoundsTotal];
        private RectTransform _spectrumTrackRect;
        private RectTransform _greenZoneRect;
        private RectTransform _goldCritZoneRect;
        private RectTransform _cursorBeamRect;
        private BiopunkProgressBar _pressureBar;
        private Text _pressureStatusText;
        private Button _lockPulseButton;

        // --- Gene Splicing Runtime State ---
        private int _currentRoundIndex;
        private int _successfulRounds;
        private int _totalSplicePoints; // +1 green, +2 gold crit
        private float _cursorPos01;
        private float _cursorDir = 1f;
        private float _targetCenter01 = 0.52f;
        private float _baseGreenHalfWidth = 0.125f;
        private float _effectiveGreenHalfWidth = 0.125f;
        private float _goldHalfWidth = 0.036f;
        private float _pressure01 = 0.50f;
        private float _pressureDriftSpeed = 0.14f;
        private float _roundFreezeTimer;

        // --- Core Defibrillation UI Refs ---
        private BiopunkProgressBar _stabilityBar;
        private Text _clotsCounterText;
        private RectTransform _heartSurgeryArena;
        private RectTransform _centralHeartRect;
        private readonly ClotNodeUI[] _clots = new ClotNodeUI[DefibClotsTotal];

        // --- Core Defibrillation Runtime State ---
        private float _defibElapsed;
        private float _stability01 = 1f;
        private int _clotsCleared;
        private float _shockAssistCooldown;
        private Text _shockAssistLabel;
        private Button _shockAssistBtn;

        // --- Shared State ---
        private MiniGameKind _activeKind = MiniGameKind.GeneSplicing;
        private bool _inResultSummary;
        private float _summaryAutoCloseTimer;
        private bool _pendingSuccess;
        private int _pendingScoreTier;
        private bool _suppressSpaceThisFrame;

        public bool IsOpen => _modalRoot != null && _modalRoot.gameObject.activeSelf;

        public void Build(Transform parent)
        {
            _modalRoot = BiopunkUiBuilder.CreateStretchRect("MiniGameModalOverlay", parent);
            Image backdrop = _modalRoot.gameObject.AddComponent<Image>();
            backdrop.sprite = SpriteRegistry.GetWhiteSquareSprite();
            backdrop.color = new Color(0.03f, 0.06f, 0.05f, 0.88f);
            backdrop.raycastTarget = true;

            // Main Laboratory Frame (1220 x 760)
            Image labWindow = BiopunkUiBuilder.CreatePanel(
                _modalRoot, "BioLabWindow",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1220f, 760f),
                new Color(0.95f, 1.00f, 0.96f, 0.99f), raycastTarget: true);

            // Top Header Strip
            Image headerBar = BiopunkUiBuilder.CreatePanel(
                labWindow.transform, "LabHeaderBar",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -16f), new Vector2(1176f, 74f),
                new Color(0.78f, 0.92f, 0.82f, 0.98f), raycastTarget: false);

            _headerLeftIcon = BiopunkUiBuilder.CreateSpriteImage(
                headerBar.transform, "LabIcon", "UI/Icon_Building_Incubator",
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(18f, 0f), new Vector2(54f, 54f), preserveAspect: true);

            _windowHeaderText = BiopunkUiBuilder.CreateText(
                headerBar.transform, "HeaderTitle",
                "БИО-СИНТЕЗ И МУТАЦИОННОЕ ОРОШЕНИЕ",
                27, FontStyle.Bold, BiopunkUiBuilder.AccentLime, TextAnchor.MiddleLeft);
            _windowHeaderText.rectTransform.anchorMin = new Vector2(0f, 0f);
            _windowHeaderText.rectTransform.anchorMax = new Vector2(1f, 1f);
            _windowHeaderText.rectTransform.offsetMin = new Vector2(86f, 6f);
            _windowHeaderText.rectTransform.offsetMax = new Vector2(-210f, -6f);

            Button cancelBtn = BiopunkUiBuilder.CreateButton(
                headerBar.transform, "BtnCloseMiniGame", "Закрыть (Esc)",
                new Vector2(184f, 48f),
                CancelMiniGameClicked,
                fontSize: 19,
                baseTint: new Color(0.96f, 0.72f, 0.70f, 1f));
            cancelBtn.image.rectTransform.anchorMin = new Vector2(1f, 0.5f);
            cancelBtn.image.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            cancelBtn.image.rectTransform.pivot = new Vector2(1f, 0.5f);
            cancelBtn.image.rectTransform.anchoredPosition = new Vector2(-14f, 0f);

            BuildGeneSplicingLayout(labWindow.transform);
            BuildCoreDefibrillationLayout(labWindow.transform);
            BuildResultSummaryOverlay(labWindow.transform);

            _modalRoot.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            GameEvents.OnMiniGameRequested += HandleMiniGameRequested;
            GameEvents.OnGameStateChanged += HandleGameStateChanged;
        }

        private void OnDisable()
        {
            GameEvents.OnMiniGameRequested -= HandleMiniGameRequested;
            GameEvents.OnGameStateChanged -= HandleGameStateChanged;
        }

        private void HandleMiniGameRequested(MiniGameKind kind)
        {
            StartMiniGameSession(kind);
        }

        private void HandleGameStateChanged(GameState prev, GameState next)
        {
            if (next == GameState.MiniGame && !IsOpen)
            {
                MiniGameKind kind = GameManager.InstanceIfExists != null
                    ? GameManager.InstanceIfExists.ActiveMiniGame
                    : MiniGameKind.GeneSplicing;
                StartMiniGameSession(kind);
            }
            else if (next != GameState.MiniGame && IsOpen)
            {
                _modalRoot.gameObject.SetActive(false);
            }
        }

        // =====================================================================
        // MINI-GAME A LAYOUT: «БИО-СИНТЕЗ И МУТАЦИОННОЕ ОРОШЕНИЕ»
        // =====================================================================

        private void BuildGeneSplicingLayout(Transform winTransform)
        {
            RectTransform root = BiopunkUiBuilder.CreateRect(
                "GeneSplicingMode", winTransform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 22f), new Vector2(1176f, 640f));
            _geneModeRoot = root.gameObject;

            // Instructions Banner
            Image instrBox = BiopunkUiBuilder.CreatePanel(
                root, "GeneInstructions",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(1152f, 84f),
                new Color(0.84f, 0.90f, 0.86f, 0.95f), raycastTarget: false);

            Text instrTxt = BiopunkUiBuilder.CreateText(
                instrBox.transform, "InstrText",
                "<b>ИНСТРУКЦИЯ ЛАБОРАТОРИИ:</b> Поймайте бегущий импульс в <color=#8CE844><b>ЗЕЛЁНОЙ</b></color> (или <color=#FFD940><b>ЗОЛОТОЙ крит-зоне</b></color>) 3 раза клавишей <b>[ПРОБЕЛ]</b> или кнопкой внизу. Регулируйте давление клапанами <b>[A / D]</b>, чтобы зона резонанса не сжималась!",
                19, FontStyle.Normal, BiopunkUiBuilder.TextPrimary, TextAnchor.MiddleCenter);
            BiopunkUiBuilder.Stretch(instrTxt.rectTransform, 22f, 8f, 22f, 8f);

            // 3 Round Indicators Row
            RectTransform roundsRow = BiopunkUiBuilder.CreateRect(
                "RoundIndicators", root,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -98f), new Vector2(900f, 58f));

            HorizontalLayoutGroup rHlg = roundsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            rHlg.spacing = 24f;
            rHlg.childAlignment = TextAnchor.MiddleCenter;
            rHlg.childControlWidth = true;
            rHlg.childControlHeight = true;
            rHlg.childForceExpandWidth = true;
            rHlg.childForceExpandHeight = true;

            for (int i = 0; i < GeneRoundsTotal; i++)
            {
                Image pipBg = BiopunkUiBuilder.CreatePanel(
                    roundsRow, $"RoundPip_{i + 1}",
                    Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(270f, 54f),
                    Color.white, raycastTarget: false);
                Text pipTxt = BiopunkUiBuilder.CreateText(
                    pipBg.transform, "Label", $"ЦИКЛ {i + 1}: ОЖИДАНИЕ",
                    19, FontStyle.Bold, BiopunkUiBuilder.TextSecondary, TextAnchor.MiddleCenter);
                BiopunkUiBuilder.Stretch(pipTxt.rectTransform, 8f, 4f, 8f, 4f);

                _roundPipBgs[i] = pipBg;
                _roundPipLabels[i] = pipTxt;
            }

            // Mechanic 1: Oscilloscope Genetic Pulse Track
            Image oscFrame = BiopunkUiBuilder.CreatePanel(
                root, "OscilloscopePanel",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -172f), new Vector2(1152f, 178f),
                new Color(0.80f, 0.90f, 0.92f, 0.98f), raycastTarget: false);

            Text oscLabel = BiopunkUiBuilder.CreateText(
                oscFrame.transform, "OscTitle",
                "ОСЦИЛЛОГРАФ ГЕНЕТИЧЕСКОГО ИМПУЛЬСА",
                20, FontStyle.Bold, BiopunkUiBuilder.AccentCyan, TextAnchor.MiddleCenter);
            oscLabel.rectTransform.anchorMin = new Vector2(0.05f, 0.72f);
            oscLabel.rectTransform.anchorMax = new Vector2(0.95f, 0.95f);
            oscLabel.rectTransform.offsetMin = Vector2.zero;
            oscLabel.rectTransform.offsetMax = Vector2.zero;

            _spectrumTrackRect = BiopunkUiBuilder.CreateRect(
                "SpectrumTrack", oscFrame.transform,
                new Vector2(0.5f, 0.36f), new Vector2(0.5f, 0.36f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1040f, 82f));

            Image trackBg = _spectrumTrackRect.gameObject.AddComponent<Image>();
            trackBg.sprite = BiopunkUiBuilder.GetBarTroughSprite();
            trackBg.type = Image.Type.Sliced;
            trackBg.color = new Color(0.92f, 0.95f, 0.98f, 1f);

            // Subtle organic grid ticks along oscilloscope track
            for (int t = 1; t < 10; t++)
            {
                float frac = t / 10f;
                RectTransform tick = BiopunkUiBuilder.CreateRect(
                    $"Tick_{t}", _spectrumTrackRect,
                    new Vector2(frac, 0.12f), new Vector2(frac, 0.88f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(2f, 0f));
                Image tickImg = tick.gameObject.AddComponent<Image>();
                tickImg.sprite = SpriteRegistry.GetWhiteSquareSprite();
                tickImg.color = new Color(0.25f, 0.42f, 0.34f, 0.45f);
            }

            // Green Resonance Zone
            _greenZoneRect = BiopunkUiBuilder.CreateRect(
                "GreenResonanceZone", _spectrumTrackRect,
                new Vector2(0.4f, 0.08f), new Vector2(0.65f, 0.92f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            Image greenImg = _greenZoneRect.gameObject.AddComponent<Image>();
            greenImg.sprite = SpriteRegistry.GetWhiteSquareSprite();
            greenImg.color = new Color(0.32f, 0.88f, 0.36f, 0.78f);

            // Gold Critical Mutation Core Zone
            _goldCritZoneRect = BiopunkUiBuilder.CreateRect(
                "GoldCritZone", _spectrumTrackRect,
                new Vector2(0.48f, 0.06f), new Vector2(0.56f, 0.94f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            Image goldImg = _goldCritZoneRect.gameObject.AddComponent<Image>();
            goldImg.sprite = SpriteRegistry.GetWhiteSquareSprite();
            goldImg.color = new Color(1.00f, 0.86f, 0.22f, 0.95f);

            // Sweeping Cyan Genetic Cursor Beam
            _cursorBeamRect = BiopunkUiBuilder.CreateRect(
                "PulseCursor", _spectrumTrackRect,
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(14f, 14f));
            Image cursorImg = _cursorBeamRect.gameObject.AddComponent<Image>();
            cursorImg.sprite = SpriteRegistry.GetWhiteSquareSprite();
            cursorImg.color = new Color(0.35f, 0.98f, 1.00f, 1f);

            // Mechanic 2: Nutrient Pressure Valves
            Image valvePanel = BiopunkUiBuilder.CreatePanel(
                root, "PressureValvesPanel",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -366f), new Vector2(1152f, 144f),
                new Color(0.86f, 0.88f, 0.82f, 0.96f), raycastTarget: true);

            _pressureStatusText = BiopunkUiBuilder.CreateText(
                valvePanel.transform, "PressureTitle",
                "КЛАПАНЫ ПИТАТЕЛЬНОГО ДАВЛЕНИЯ: НОРМА (Широкая зона резонанса)",
                19, FontStyle.Bold, BiopunkUiBuilder.AccentLime, TextAnchor.MiddleCenter);
            _pressureStatusText.rectTransform.anchorMin = new Vector2(0.05f, 0.64f);
            _pressureStatusText.rectTransform.anchorMax = new Vector2(0.95f, 0.94f);
            _pressureStatusText.rectTransform.offsetMin = Vector2.zero;
            _pressureStatusText.rectTransform.offsetMax = Vector2.zero;

            RectTransform valveRow = BiopunkUiBuilder.CreateRect(
                "ValveControlsRow", valvePanel.transform,
                new Vector2(0.5f, 0.30f), new Vector2(0.5f, 0.30f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1100f, 58f));

            HorizontalLayoutGroup vHlg = valveRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            vHlg.spacing = 18f;
            vHlg.childAlignment = TextAnchor.MiddleCenter;
            vHlg.childControlWidth = false;
            vHlg.childControlHeight = true;
            vHlg.childForceExpandWidth = false;

            BiopunkUiBuilder.CreateButton(
                valveRow, "BtnReleasePressure", "◄ СБРОСИТЬ КЛАПАН [A / ←]",
                new Vector2(300f, 54f),
                () => AdjustPressure(-0.18f),
                fontSize: 18,
                baseTint: new Color(0.76f, 0.92f, 1.00f, 1f));

            _pressureBar = BiopunkUiBuilder.CreateProgressBar(
                valveRow, "PressureGauge",
                new Vector2(456f, 44f),
                BiopunkUiBuilder.AccentLime,
                "ДАВЛЕНИЕ: 50% (ОПТИМУМ 35–65%)", 18);

            BiopunkUiBuilder.CreateButton(
                valveRow, "BtnPumpPressure", "ПОДКАЧАТЬ ПЛАЗМУ [D / →] ►",
                new Vector2(300f, 54f),
                () => AdjustPressure(+0.18f),
                fontSize: 18,
                baseTint: new Color(0.96f, 0.80f, 0.96f, 1f));

            // Primary Action Button: LOCK PULSE
            _lockPulseButton = BiopunkUiBuilder.CreateButton(
                root, "BtnLockPulse", "ФИКСИРОВАТЬ ИМПУЛЬС [ПРОБЕЛ]",
                new Vector2(620f, 76f),
                LockCurrentGenePulse,
                fontSize: 26,
                baseTint: new Color(0.68f, 1.00f, 0.54f, 1f),
                textColor: new Color(0.96f, 1.00f, 0.90f, 1f),
                iconSprite: SpriteRegistry.Get("UI/Icon_SerumGreen"));
            _lockPulseButton.image.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            _lockPulseButton.image.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            _lockPulseButton.image.rectTransform.pivot = new Vector2(0.5f, 0f);
            _lockPulseButton.image.rectTransform.anchoredPosition = new Vector2(0f, 18f);
        }

        // =====================================================================
        // MINI-GAME B LAYOUT: «ДЕФИБРИЛЛЯЦИЯ И ПОДКОРМКА СЕРДЦА»
        // =====================================================================

        private void BuildCoreDefibrillationLayout(Transform winTransform)
        {
            RectTransform root = BiopunkUiBuilder.CreateRect(
                "CoreDefibMode", winTransform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 22f), new Vector2(1176f, 640f));
            _defibModeRoot = root.gameObject;

            Image instrBox = BiopunkUiBuilder.CreatePanel(
                root, "DefibInstructions",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(1152f, 72f),
                new Color(0.84f, 0.92f, 0.94f, 0.95f), raycastTarget: false);

            Text instrTxt = BiopunkUiBuilder.CreateText(
                instrBox.transform, "DefibInstrText",
                "<b>ЭКСТРЕННАЯ РЕАНИМАЦИЯ ЯДРА:</b> Нажимайте <b>[ЛКМ]</b> по пульсирующим <color=#FF5850><b>паразитическим тромбам</b></color> на артериях Живого Сердца, пока шкала стабильности не иссякла! Клавиша <b>[ПРОБЕЛ]</b> даёт электро-разряд!",
                19, FontStyle.Normal, BiopunkUiBuilder.TextPrimary, TextAnchor.MiddleCenter);
            BiopunkUiBuilder.Stretch(instrTxt.rectTransform, 20f, 6f, 20f, 6f);

            // Top Telemetry Row: Stability Bar + Clot Counter
            RectTransform statusRow = BiopunkUiBuilder.CreateRect(
                "DefibStatusRow", root,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -82f), new Vector2(1152f, 44f));

            _stabilityBar = BiopunkUiBuilder.CreateProgressBar(
                statusRow, "StabilityBar",
                new Vector2(730f, 38f),
                BiopunkUiBuilder.AccentCyan,
                "СТАБИЛЬНОСТЬ СЕРДЦА: 10.0с", 19);
            _stabilityBar.Root.anchorMin = new Vector2(0f, 0.5f);
            _stabilityBar.Root.anchorMax = new Vector2(0f, 0.5f);
            _stabilityBar.Root.pivot = new Vector2(0f, 0.5f);
            _stabilityBar.Root.anchoredPosition = Vector2.zero;

            Image counterBox = BiopunkUiBuilder.CreatePanel(
                statusRow, "ClotCounterBox",
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                Vector2.zero, new Vector2(398f, 40f),
                new Color(0.92f, 0.84f, 0.86f, 0.96f), raycastTarget: false);
            _clotsCounterText = BiopunkUiBuilder.CreateText(
                counterBox.transform, "CounterLabel", "ОЧИЩЕНО ТРОМБОВ: 0 / 6",
                20, FontStyle.Bold, BiopunkUiBuilder.AccentLime, TextAnchor.MiddleCenter);
            BiopunkUiBuilder.Stretch(_clotsCounterText.rectTransform, 10f, 2f, 10f, 2f);

            // Anatomical Heart Surgical Arena
            Image arenaPanel = BiopunkUiBuilder.CreatePanel(
                root, "HeartSurgicalArena",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -134f), new Vector2(1152f, 420f),
                new Color(0.76f, 0.84f, 0.86f, 0.98f), raycastTarget: true);
            _heartSurgeryArena = arenaPanel.rectTransform;

            // 6 Radiating Arterial Conduits drawn behind the Living Heart Core
            float[] conduitAngles = { -152f, -28f, 24f, 156f, 92f, -92f };
            for (int i = 0; i < conduitAngles.Length; i++)
            {
                RectTransform artery = BiopunkUiBuilder.CreateRect(
                    $"ArterialConduit_{i}", _heartSurgeryArena,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f),
                    Vector2.zero, new Vector2(460f, 24f));
                artery.localRotation = Quaternion.Euler(0f, 0f, conduitAngles[i]);
                Image artImg = artery.gameObject.AddComponent<Image>();
                artImg.sprite = SpriteRegistry.Get("Vein_Artery_H");
                artImg.color = new Color(0.95f, 0.55f, 0.72f, 0.88f);
                artImg.raycastTarget = false;
            }

            // Central Beating Living Heart Core Preview
            Image heartPreview = BiopunkUiBuilder.CreateSpriteImage(
                _heartSurgeryArena, "LivingHeartCenter", "Core_LivingHeart",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(228f, 228f), preserveAspect: true, raycastTarget: false);
            _centralHeartRect = heartPreview.rectTransform;

            // Pre-create 6 Clickable Parasite Clot Nodes along the arteries
            Vector2[] clotAnchors =
            {
                new Vector2(-340f,  112f),
                new Vector2( 335f,  118f),
                new Vector2(-365f, -108f),
                new Vector2( 355f, -104f),
                new Vector2(-195f,  150f),
                new Vector2( 205f, -148f)
            };

            for (int i = 0; i < DefibClotsTotal; i++)
            {
                int idx = i;
                RectTransform clotRt = BiopunkUiBuilder.CreateRect(
                    $"ParasiteClot_{i}", _heartSurgeryArena,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    clotAnchors[i], new Vector2(96f, 96f));

                Image glow = clotRt.gameObject.AddComponent<Image>();
                glow.sprite = SpriteRegistry.GetSoftCircleSprite();
                glow.color = new Color(1.0f, 0.22f, 0.28f, 0.88f);
                glow.raycastTarget = true;

                Button btn = clotRt.gameObject.AddComponent<Button>();
                btn.targetGraphic = glow;
                btn.onClick.AddListener(() => OnParasiteClotClicked(idx));

                Image parasiteSprite = BiopunkUiBuilder.CreateSpriteImage(
                    clotRt, "ClotSprite", "Enemy_SwarmMite_01",
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(68f, 68f), preserveAspect: true, raycastTarget: false);
                parasiteSprite.color = new Color(1.0f, 0.55f, 0.62f, 1f);

                Text tag = BiopunkUiBuilder.CreateText(
                    clotRt, "ClickTag", "ТРОМБ!",
                    15, FontStyle.Bold, BiopunkUiBuilder.AccentAmber, TextAnchor.LowerCenter);
                tag.rectTransform.anchorMin = new Vector2(0f, -0.18f);
                tag.rectTransform.anchorMax = new Vector2(1f, 0.16f);
                tag.rectTransform.offsetMin = Vector2.zero;
                tag.rectTransform.offsetMax = Vector2.zero;

                _clots[i] = new ClotNodeUI
                {
                    Root = clotRt.gameObject,
                    Rect = clotRt,
                    Button = btn,
                    OuterGlow = glow,
                    CoreIcon = parasiteSprite,
                    BasePos = clotAnchors[i],
                    PhaseOffset = i * 1.15f
                };
            }

            // Bottom Defib Assist Button
            _shockAssistBtn = BiopunkUiBuilder.CreateButton(
                root, "BtnShockPulseAssist", "ЭЛЕКТРО-РАЗРЯД ПО ТРОМБУ [ПРОБЕЛ]",
                new Vector2(560f, 62f),
                TriggerDefibAssistZap,
                fontSize: 22,
                baseTint: new Color(0.68f, 0.96f, 1.00f, 1f),
                iconSprite: SpriteRegistry.Get("UI/Icon_BioPlasma"));
            _shockAssistBtn.image.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            _shockAssistBtn.image.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            _shockAssistBtn.image.rectTransform.pivot = new Vector2(0.5f, 0f);
            _shockAssistBtn.image.rectTransform.anchoredPosition = new Vector2(0f, 12f);
            _shockAssistLabel = _shockAssistBtn.GetComponentInChildren<Text>();
        }

        // =====================================================================
        // RESULT SUMMARY OVERLAY
        // =====================================================================

        private void BuildResultSummaryOverlay(Transform winTransform)
        {
            RectTransform overlay = BiopunkUiBuilder.CreateStretchRect("ResultSummaryOverlay", winTransform, 80f, 120f, 80f, 140f);
            Image card = overlay.gameObject.AddComponent<Image>();
            card.sprite = BiopunkUiBuilder.GetBeveledPanelSprite();
            card.type = Image.Type.Sliced;
            card.color = new Color(0.95f, 1.00f, 0.95f, 0.99f);
            card.raycastTarget = true;
            _summaryOverlay = overlay.gameObject;

            _summaryTitleText = BiopunkUiBuilder.CreateText(
                overlay, "SummaryTitle", "КАЛИБРОВКА ЗАВЕРШЕНА!",
                34, FontStyle.Bold, BiopunkUiBuilder.AccentLime, TextAnchor.MiddleCenter);
            _summaryTitleText.rectTransform.anchorMin = new Vector2(0.06f, 0.66f);
            _summaryTitleText.rectTransform.anchorMax = new Vector2(0.94f, 0.90f);
            _summaryTitleText.rectTransform.offsetMin = Vector2.zero;
            _summaryTitleText.rectTransform.offsetMax = Vector2.zero;

            _summaryBodyText = BiopunkUiBuilder.CreateText(
                overlay, "SummaryBody", string.Empty,
                22, FontStyle.Normal, BiopunkUiBuilder.TextPrimary, TextAnchor.MiddleCenter);
            _summaryBodyText.lineSpacing = 1.22f;
            _summaryBodyText.rectTransform.anchorMin = new Vector2(0.08f, 0.28f);
            _summaryBodyText.rectTransform.anchorMax = new Vector2(0.92f, 0.66f);
            _summaryBodyText.rectTransform.offsetMin = Vector2.zero;
            _summaryBodyText.rectTransform.offsetMax = Vector2.zero;

            Button finishBtn = BiopunkUiBuilder.CreateButton(
                overlay, "BtnConfirmMiniGameResult", "ПРИМЕНИТЬ К КОЛОНИИ [ПРОБЕЛ / КЛИК]",
                new Vector2(520f, 66f),
                FinalizeAndCloseMiniGame,
                fontSize: 22,
                baseTint: new Color(0.68f, 1.00f, 0.56f, 1f));
            finishBtn.image.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            finishBtn.image.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            finishBtn.image.rectTransform.pivot = new Vector2(0.5f, 0f);
            finishBtn.image.rectTransform.anchoredPosition = new Vector2(0f, 28f);

            _summaryOverlay.SetActive(false);
        }

        // =====================================================================
        // SESSION START & UPDATE LOOP
        // =====================================================================

        public void StartMiniGameSession(MiniGameKind kind)
        {
            if (_modalRoot == null) return;

            _activeKind = kind;
            _inResultSummary = false;
            _suppressSpaceThisFrame = true;
            _summaryOverlay.SetActive(false);
            _modalRoot.gameObject.SetActive(true);
            _modalRoot.SetAsLastSibling();

            if (kind == MiniGameKind.GeneSplicing)
            {
                _windowHeaderText.text = "БИО-СИНТЕЗ И МУТАЦИОННОЕ ОРОШЕНИЕ";
                _windowHeaderText.color = BiopunkUiBuilder.AccentLime;
                _headerLeftIcon.sprite = SpriteRegistry.Get("UI/Icon_Building_Incubator");
                _geneModeRoot.SetActive(true);
                _defibModeRoot.SetActive(false);
                ResetGeneSplicingState();
            }
            else
            {
                _windowHeaderText.text = "ДЕФИБРИЛЛЯЦИЯ И ПОДКОРМКА СЕРДЦА";
                _windowHeaderText.color = BiopunkUiBuilder.AccentCyan;
                _headerLeftIcon.sprite = SpriteRegistry.Get("Core_LivingHeart");
                _geneModeRoot.SetActive(false);
                _defibModeRoot.SetActive(true);
                ResetCoreDefibrillationState();
            }
        }

        private void Update()
        {
            if (!IsOpen) return;

            float dt = Mathf.Min(0.05f, Time.unscaledDeltaTime);

            if (_suppressSpaceThisFrame)
            {
                _suppressSpaceThisFrame = false;
                return;
            }

            if (_inResultSummary)
            {
                _summaryAutoCloseTimer -= dt;
                if (_summaryAutoCloseTimer <= 0f || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
                {
                    FinalizeAndCloseMiniGame();
                }
                return;
            }

            if (_activeKind == MiniGameKind.GeneSplicing)
            {
                TickGeneSplicing(dt);
            }
            else
            {
                TickCoreDefibrillation(dt);
            }
        }

        // =====================================================================
        // GENE SPLICING GAMEPLAY LOGIC
        // =====================================================================

        private void ResetGeneSplicingState()
        {
            _currentRoundIndex = 0;
            _successfulRounds = 0;
            _totalSplicePoints = 0;
            _pressure01 = 0.50f;
            _pressureDriftSpeed = UnityEngine.Random.value > 0.5f ? 0.14f : -0.14f;
            _roundFreezeTimer = 0f;

            for (int i = 0; i < GeneRoundsTotal; i++)
            {
                _roundPipBgs[i].color = new Color(0.82f, 0.86f, 0.82f, 1f);
                _roundPipLabels[i].text = i == 0 ? "ЦИКЛ 1: АКТИВЕН" : $"ЦИКЛ {i + 1}: ОЖИДАНИЕ";
                _roundPipLabels[i].color = i == 0 ? BiopunkUiBuilder.AccentCyan : BiopunkUiBuilder.TextSecondary;
            }

            SetupNextGeneRound();
        }

        private void SetupNextGeneRound()
        {
            _cursorPos01 = (_currentRoundIndex % 2 == 0) ? 0.06f : 0.94f;
            _cursorDir = (_currentRoundIndex % 2 == 0) ? 1f : -1f;
            _targetCenter01 = UnityEngine.Random.Range(0.26f, 0.74f);
            _baseGreenHalfWidth = Mathf.Lerp(0.135f, 0.105f, _currentRoundIndex / 2f);
            _goldHalfWidth = 0.038f;
            UpdateOscilloscopeZonesVisual();
        }

        private void AdjustPressure(float delta)
        {
            if (_inResultSummary) return;
            _pressure01 = Mathf.Clamp01(_pressure01 + delta);
            // Reverse/dampen drift slightly when manually stabilized
            _pressureDriftSpeed = (_pressure01 > 0.5f ? 1f : -1f) * UnityEngine.Random.Range(0.11f, 0.17f);
            UpdateOscilloscopeZonesVisual();
        }

        private void TickGeneSplicing(float dt)
        {
            // Keyboard Valve shortcuts
            if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow))
            {
                BiopunkUiBuilder.PlayClickSfx();
                AdjustPressure(-0.18f);
            }
            else if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow))
            {
                BiopunkUiBuilder.PlayClickSfx();
                AdjustPressure(+0.18f);
            }

            if (_roundFreezeTimer > 0f)
            {
                _roundFreezeTimer -= dt;
                if (_roundFreezeTimer <= 0f)
                {
                    AdvanceGeneRoundOrFinish();
                }
                return;
            }

            // Natural organic pressure drift
            _pressure01 = Mathf.Clamp01(_pressure01 + _pressureDriftSpeed * dt);
            if (_pressure01 <= 0.03f) _pressureDriftSpeed = Mathf.Abs(_pressureDriftSpeed);
            else if (_pressure01 >= 0.97f) _pressureDriftSpeed = -Mathf.Abs(_pressureDriftSpeed);

            // Sweep cursor across horizontal spectrum
            float sweepSpeed = 0.92f + _currentRoundIndex * 0.24f;
            _cursorPos01 += _cursorDir * sweepSpeed * dt;
            if (_cursorPos01 >= 0.98f)
            {
                _cursorPos01 = 0.98f;
                _cursorDir = -1f;
            }
            else if (_cursorPos01 <= 0.02f)
            {
                _cursorPos01 = 0.02f;
                _cursorDir = 1f;
            }

            UpdateOscilloscopeZonesVisual();

            if (Input.GetKeyDown(KeyCode.Space))
            {
                LockCurrentGenePulse();
            }
        }

        private void UpdateOscilloscopeZonesVisual()
        {
            float distFromOptimal = Mathf.Abs(_pressure01 - 0.50f);
            bool optimal = distFromOptimal <= 0.18f;
            bool danger = distFromOptimal >= 0.32f;

            float widthScale = danger ? 0.52f : (optimal ? 1.05f : 0.80f);
            _effectiveGreenHalfWidth = _baseGreenHalfWidth * widthScale;

            if (_greenZoneRect != null)
            {
                _greenZoneRect.anchorMin = new Vector2(Mathf.Clamp01(_targetCenter01 - _effectiveGreenHalfWidth), 0.08f);
                _greenZoneRect.anchorMax = new Vector2(Mathf.Clamp01(_targetCenter01 + _effectiveGreenHalfWidth), 0.92f);
                _greenZoneRect.offsetMin = Vector2.zero;
                _greenZoneRect.offsetMax = Vector2.zero;
            }

            if (_goldCritZoneRect != null)
            {
                _goldCritZoneRect.anchorMin = new Vector2(Mathf.Clamp01(_targetCenter01 - _goldHalfWidth), 0.06f);
                _goldCritZoneRect.anchorMax = new Vector2(Mathf.Clamp01(_targetCenter01 + _goldHalfWidth), 0.94f);
                _goldCritZoneRect.offsetMin = Vector2.zero;
                _goldCritZoneRect.offsetMax = Vector2.zero;
            }

            if (_cursorBeamRect != null)
            {
                _cursorBeamRect.anchorMin = new Vector2(_cursorPos01, -0.06f);
                _cursorBeamRect.anchorMax = new Vector2(_cursorPos01, 1.06f);
                _cursorBeamRect.anchoredPosition = Vector2.zero;
            }

            if (_pressureBar != null)
            {
                int pct = Mathf.RoundToInt(_pressure01 * 100f);
                Color pCol = danger
                    ? BiopunkUiBuilder.AccentDanger
                    : (optimal ? BiopunkUiBuilder.AccentLime : BiopunkUiBuilder.AccentAmber);
                _pressureBar.SetProgress(_pressure01, $"ДАВЛЕНИЕ: {pct}% (ОПТИМУМ 32–68%)", pCol);
            }

            if (_pressureStatusText != null)
            {
                if (danger)
                {
                    _pressureStatusText.text = "КРИТИЧЕСКОЕ ДАВЛЕНИЕ! Зона резонанса сжата — выровняйте клапанами [A / D]!";
                    _pressureStatusText.color = BiopunkUiBuilder.AccentDanger;
                }
                else if (optimal)
                {
                    _pressureStatusText.text = "ДАВЛЕНИЕ СТАБИЛЬНО: Максимальная ширина зелёной зоны резонанса!";
                    _pressureStatusText.color = BiopunkUiBuilder.AccentLime;
                }
                else
                {
                    _pressureStatusText.text = "ДАВЛЕНИЕ КОЛЕБЛЕТСЯ: Подкорректируйте клапаны [◄ Сброс / Подкачка ►]";
                    _pressureStatusText.color = BiopunkUiBuilder.AccentAmber;
                }
            }
        }

        private void LockCurrentGenePulse()
        {
            if (_inResultSummary || _roundFreezeTimer > 0f || _currentRoundIndex >= GeneRoundsTotal)
            {
                return;
            }

            float diff = Mathf.Abs(_cursorPos01 - _targetCenter01);
            bool isCrit = diff <= _goldHalfWidth;
            bool isHit = diff <= _effectiveGreenHalfWidth;

            if (isCrit)
            {
                _successfulRounds++;
                _totalSplicePoints += 2;
                _roundPipBgs[_currentRoundIndex].color = new Color(1.00f, 0.90f, 0.45f, 1f);
                _roundPipLabels[_currentRoundIndex].text = $"ЦИКЛ {_currentRoundIndex + 1}: КРИТ-МУТАЦИЯ!";
                _roundPipLabels[_currentRoundIndex].color = BiopunkUiBuilder.AccentAmber;
                AudioManager.Instance?.PlaySfx(AudioKeys.SfxMiniGameHit, 1.0f, 0.02f);
            }
            else if (isHit)
            {
                _successfulRounds++;
                _totalSplicePoints += 1;
                _roundPipBgs[_currentRoundIndex].color = new Color(0.66f, 1.00f, 0.56f, 1f);
                _roundPipLabels[_currentRoundIndex].text = $"ЦИКЛ {_currentRoundIndex + 1}: РЕЗОНАНС √";
                _roundPipLabels[_currentRoundIndex].color = BiopunkUiBuilder.AccentLime;
                AudioManager.Instance?.PlaySfx(AudioKeys.SfxMiniGameHit, 0.85f, 0.03f);
            }
            else
            {
                _roundPipBgs[_currentRoundIndex].color = new Color(1.00f, 0.62f, 0.60f, 1f);
                _roundPipLabels[_currentRoundIndex].text = $"ЦИКЛ {_currentRoundIndex + 1}: МИМО ×";
                _roundPipLabels[_currentRoundIndex].color = BiopunkUiBuilder.AccentDanger;
                AudioManager.Instance?.PlaySfx(AudioKeys.SfxMiniGameFail, 0.85f, 0.03f);
            }

            _roundFreezeTimer = 0.34f;
        }

        private void AdvanceGeneRoundOrFinish()
        {
            _currentRoundIndex++;
            if (_currentRoundIndex < GeneRoundsTotal)
            {
                _roundPipLabels[_currentRoundIndex].text = $"ЦИКЛ {_currentRoundIndex + 1}: АКТИВЕН";
                _roundPipLabels[_currentRoundIndex].color = BiopunkUiBuilder.AccentCyan;
                SetupNextGeneRound();
                return;
            }

            // Finish Gene Splicing!
            bool success = _successfulRounds >= 1;
            int tier = (_successfulRounds >= 3 || _totalSplicePoints >= 4) ? 2 : (_successfulRounds >= 1 ? 1 : 0);

            _pendingSuccess = success;
            _pendingScoreTier = tier;
            _inResultSummary = true;
            _summaryAutoCloseTimer = 2.4f;
            _summaryOverlay.SetActive(true);

            if (success)
            {
                float mutBonus = tier >= 2 ? 2.5f : 1.5f;
                float bioBonus = tier >= 2 ? 35f : 20f;
                float cropMult = tier >= 2 ? 2.25f : 2.0f;
                float turMult = tier >= 2 ? 1.55f : 1.40f;
                int dur = tier >= 2 ? 55 : 45;

                // Grant extra Biomass harvest boost directly alongside TriggerGeneSpliceReward
                ResourceManager.InstanceIfExists?.Add(ResourceType.Biomass, bioBonus);

                _summaryTitleText.text = tier >= 2
                    ? "ИДЕАЛЬНЫЙ ГЕНО-СПЛАЙСИНГ!"
                    : "БИО-СИНТЕЗ УСПЕШЕН!";
                _summaryTitleText.color = BiopunkUiBuilder.AccentLime;
                _summaryBodyText.text =
                    $"Успешных циклов резонанса: <b>{_successfulRounds} / {GeneRoundsTotal}</b>\n" +
                    $"Получено: <color=#F547E0><b>+{mutBonus:0.0} Мутагена</b></color> и <color=#59EB6B><b>+{bioBonus:0} Биомассы</b></color>!\n" +
                    $"Глобальный бафф колонии: <b>Рост культур x{cropMult:0.##}</b> и <b>Перегрузка турелей x{turMult:0.##}</b> на <b>{dur} сек</b>!";
            }
            else
            {
                _summaryTitleText.text = "СБОЙ РЕЗОНАНСА!";
                _summaryTitleText.color = BiopunkUiBuilder.AccentDanger;
                _summaryBodyText.text =
                    "Ни один из 3 импульсов не попал в зелёную зону спектра.\n" +
                    "Следите за клапанами давления и попробуйте снова!";
            }
        }

        // =====================================================================
        // CORE DEFIBRILLATION GAMEPLAY LOGIC
        // =====================================================================

        private void ResetCoreDefibrillationState()
        {
            _defibElapsed = 0f;
            _stability01 = 1f;
            _clotsCleared = 0;
            _shockAssistCooldown = 0f;

            for (int i = 0; i < DefibClotsTotal; i++)
            {
                ClotNodeUI c = _clots[i];
                c.Cleared = false;
                // First 3 clots appear immediately; remaining 3 appear rapidly over 0.8s..2.6s
                c.SpawnAtElapsed = i < 3 ? 0f : (i - 2) * 0.85f;
                c.Spawned = c.SpawnAtElapsed <= 0.01f;
                c.Root.SetActive(c.Spawned);
                c.Rect.anchoredPosition = c.BasePos;
            }

            UpdateDefibTelemetryUI();
        }

        private void TickCoreDefibrillation(float dt)
        {
            _defibElapsed += dt;

            // Spawn staggered clots along arteries
            int activeClotsOnHeart = 0;
            for (int i = 0; i < DefibClotsTotal; i++)
            {
                ClotNodeUI c = _clots[i];
                if (c.Cleared) continue;

                if (!c.Spawned && _defibElapsed >= c.SpawnAtElapsed)
                {
                    c.Spawned = true;
                    c.Root.SetActive(true);
                }

                if (c.Spawned)
                {
                    activeClotsOnHeart++;
                    float wobbleX = Mathf.Sin(Time.unscaledTime * 4.5f + c.PhaseOffset) * 10f;
                    float wobbleY = Mathf.Cos(Time.unscaledTime * 3.8f + c.PhaseOffset) * 8f;
                    c.Rect.anchoredPosition = c.BasePos + new Vector2(wobbleX, wobbleY);

                    float pulseScale = 1.0f + 0.12f * Mathf.Sin(Time.unscaledTime * 8f + c.PhaseOffset);
                    c.Rect.localScale = new Vector3(pulseScale, pulseScale, 1f);
                }
            }

            // Animate central Living Heart Core beat
            if (_centralHeartRect != null)
            {
                float beat = 1f + 0.06f * Mathf.Sin(Time.unscaledTime * 6.5f);
                _centralHeartRect.localScale = new Vector3(beat, beat, 1f);
            }

            // Stability drains over ~10.5 seconds (slightly faster if many clots left untouched)
            float drainRate = (1f / DefibDurationSec) * (0.85f + activeClotsOnHeart * 0.05f);
            _stability01 = Mathf.Max(0f, _stability01 - drainRate * dt);

            if (_shockAssistCooldown > 0f)
            {
                _shockAssistCooldown = Mathf.Max(0f, _shockAssistCooldown - dt);
            }

            if (Input.GetKeyDown(KeyCode.Space) && _shockAssistCooldown <= 0.01f)
            {
                TriggerDefibAssistZap();
            }

            UpdateDefibTelemetryUI();

            if (_clotsCleared >= DefibClotsTotal)
            {
                FinishCoreDefibrillation(allCleared: true);
            }
            else if (_stability01 <= 0.001f)
            {
                FinishCoreDefibrillation(allCleared: false);
            }
        }

        private void OnParasiteClotClicked(int index)
        {
            if (_inResultSummary || index < 0 || index >= _clots.Length) return;
            ClotNodeUI c = _clots[index];
            if (!c.Spawned || c.Cleared) return;

            c.Cleared = true;
            c.Root.SetActive(false);
            _clotsCleared++;
            _stability01 = Mathf.Clamp01(_stability01 + 0.07f);

            AudioManager.Instance?.PlaySfx(AudioKeys.SfxMiniGameHit, 0.95f, 0.04f);
            UpdateDefibTelemetryUI();

            if (_clotsCleared >= DefibClotsTotal)
            {
                FinishCoreDefibrillation(allCleared: true);
            }
        }

        private void TriggerDefibAssistZap()
        {
            if (_inResultSummary || _shockAssistCooldown > 0.01f) return;

            for (int i = 0; i < DefibClotsTotal; i++)
            {
                if (_clots[i].Spawned && !_clots[i].Cleared)
                {
                    _shockAssistCooldown = 2.4f;
                    AudioManager.Instance?.PlaySfx(AudioKeys.SfxHeartPulse, 0.9f, 0.02f);
                    OnParasiteClotClicked(i);
                    return;
                }
            }
        }

        private void UpdateDefibTelemetryUI()
        {
            if (_stabilityBar != null)
            {
                float secEst = _stability01 * DefibDurationSec;
                Color col = _stability01 > 0.35f ? BiopunkUiBuilder.AccentCyan : BiopunkUiBuilder.AccentDanger;
                _stabilityBar.SetProgress(_stability01, $"СТАБИЛЬНОСТЬ АРТЕРИЙ: {secEst:0.0}с ({Mathf.RoundToInt(_stability01 * 100f)}%)", col);
            }

            if (_clotsCounterText != null)
            {
                _clotsCounterText.text = $"ОЧИЩЕНО ТРОМБОВ: {_clotsCleared} / {DefibClotsTotal}";
            }

            if (_shockAssistBtn != null && _shockAssistLabel != null)
            {
                if (_shockAssistCooldown > 0.05f)
                {
                    _shockAssistBtn.interactable = false;
                    _shockAssistLabel.text = $"ПЕРЕЗАРЯДКА РАЗРЯДА: {_shockAssistCooldown:0.0}с";
                }
                else
                {
                    _shockAssistBtn.interactable = true;
                    _shockAssistLabel.text = "ЭЛЕКТРО-РАЗРЯД ПО ТРОМБУ [ПРОБЕЛ]";
                }
            }
        }

        private void FinishCoreDefibrillation(bool allCleared)
        {
            bool success = allCleared || _clotsCleared >= 4;
            int tier = (allCleared && _stability01 >= 0.40f) ? 2 : (success ? 1 : 0);

            _pendingSuccess = success;
            _pendingScoreTier = tier;
            _inResultSummary = true;
            _summaryAutoCloseTimer = 2.2f;
            _summaryOverlay.SetActive(true);

            if (success)
            {
                int healAmt = tier >= 2 ? 450 : 280;
                int plasmaBonus = tier >= 2 ? 60 : 35;
                float mutBonus = tier >= 2 ? 2.0f : 1.0f;

                _summaryTitleText.text = "СЕРДЦЕ КОЛОНИИ РЕАНИМИРОВАНО!";
                _summaryTitleText.color = BiopunkUiBuilder.AccentCyan;
                _summaryBodyText.text =
                    $"Удалено паразитических тромбов: <b>{_clotsCleared} / {DefibClotsTotal}</b>\n" +
                    $"Живое Сердце исцелено на <color=#59EB6B><b>+{healAmt} HP</b></color> + выпущена <b>ударная волна (120–180 урона)</b>!\n" +
                    $"Награда: <color=#38E5FF><b>+{plasmaBonus} Биоплазмы</b></color>, <color=#F547E0><b>+{mutBonus:0.0} Мутагена</b></color> и ускорение турелей!";
            }
            else
            {
                _summaryTitleText.text = "ДЕФИБРИЛЛЯЦИЯ СОРВАНА!";
                _summaryTitleText.color = BiopunkUiBuilder.AccentDanger;
                _summaryBodyText.text =
                    $"Очищено лишь {_clotsCleared} из {DefibClotsTotal} тромбов до падения стабильности.\n" +
                    "Быстрее кликайте по багровым узлам на артериях!";
            }
        }

        // =====================================================================
        // COMPLETION & CANCEL
        // =====================================================================

        private void FinalizeAndCloseMiniGame()
        {
            if (!IsOpen) return;
            _inResultSummary = false;
            _summaryOverlay.SetActive(false);
            _modalRoot.gameObject.SetActive(false);

            if (_activeKind == MiniGameKind.CoreDefibrillation && _pendingSuccess && ResourceManager.InstanceIfExists == null)
            {
                Vector2 center = BioGridManager.InstanceIfExists != null
                    ? (Vector2)BioGridManager.InstanceIfExists.HeartCoreCenterWorld
                    : new Vector2(18.5f, 12.5f);
                GameEvents.RaiseCoreDefibShockwave(center, 7.5f, 120f);
            }

            if (GameManager.Instance != null)
            {
                GameManager.Instance.CompleteMiniGame(_activeKind, _pendingSuccess, _pendingScoreTier);
            }
        }

        private void CancelMiniGameClicked()
        {
            _inResultSummary = false;
            if (_summaryOverlay != null) _summaryOverlay.SetActive(false);
            if (_modalRoot != null) _modalRoot.gameObject.SetActive(false);

            if (GameManager.Instance != null)
            {
                GameManager.Instance.CancelMiniGame();
            }
        }
    }
}
