using System;
using UnityEngine;
using UnityEngine.UI;
using BioFarme.Audio;
using BioFarme.Core;
using BioFarme.Data;

namespace BioFarme.UI
{
    /// <summary>
    /// Contest Screen #1 — Главное меню ("BIOFARME: СЕРДЦЕ КОЛОНИИ").
    /// Self-constructing 1920x1080 uGUI screen featuring left-aligned biopunk menu over Screen_MainMenuBg,
    /// multi-tab Help & Lore codex modal, Audio/Fullscreen settings modal, and WebGL-safe quit toast.
    /// </summary>
    public sealed class MainMenuUI : MonoBehaviour
    {
        private Canvas _canvas;
        private GameObject _rootPanel;
        private GameObject _helpModal;
        private GameObject _settingsModal;

        // Help Modal Tabs
        private readonly Button[] _helpTabButtons = new Button[5];
        private Image _helpTabIconLeft;
        private Image _helpTabIconRight;
        private Text _helpTabHeader;
        private Text _helpTabBody;
        private int _currentHelpTab;

        // Settings Controls
        private Slider _masterSlider;
        private Slider _musicSlider;
        private Slider _sfxSlider;
        private Text _fullscreenLabel;

        // WebGL Quit / Menu Toast
        private CanvasGroup _toastGroup;
        private Text _toastText;
        private float _toastTimer;

        private bool _built;

        private void Awake()
        {
            BuildMenuIfNeeded();
        }

        private void OnEnable()
        {
            GameEvents.OnGameStateChanged += HandleGameStateChanged;
            RefreshVisibilityForState(GameManager.InstanceIfExists != null ? GameManager.InstanceIfExists.State : GameState.MainMenu);
        }

        private void OnDisable()
        {
            GameEvents.OnGameStateChanged -= HandleGameStateChanged;
        }

        private void Start()
        {
            _ = AudioManager.Instance;
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayMusic(AudioKeys.BgmAmbientLoop, restartIfSame: false);
            }

            GameState currentState = GameManager.Instance != null ? GameManager.Instance.State : GameState.MainMenu;
            RefreshVisibilityForState(currentState);
        }

        private void Update()
        {
            if (_rootPanel == null || !_rootPanel.activeSelf) return;

            if (_toastTimer > 0f)
            {
                _toastTimer -= Time.unscaledDeltaTime;
                if (_toastGroup != null)
                {
                    _toastGroup.alpha = _toastTimer > 0.5f ? 1f : Mathf.Clamp01(_toastTimer / 0.5f);
                    if (_toastTimer <= 0f)
                    {
                        _toastGroup.gameObject.SetActive(false);
                    }
                }
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_helpModal != null && _helpModal.activeSelf)
                {
                    BiopunkUiBuilder.PlayClickSfx();
                    _helpModal.SetActive(false);
                }
                else if (_settingsModal != null && _settingsModal.activeSelf)
                {
                    BiopunkUiBuilder.PlayClickSfx();
                    _settingsModal.SetActive(false);
                }
            }
        }

        private void HandleGameStateChanged(GameState prev, GameState next)
        {
            RefreshVisibilityForState(next);
        }

        private void RefreshVisibilityForState(GameState state)
        {
            BuildMenuIfNeeded();
            bool showMenu = state == GameState.MainMenu;
            if (_rootPanel != null && _rootPanel.activeSelf != showMenu)
            {
                _rootPanel.SetActive(showMenu);
            }

            if (!showMenu)
            {
                if (_helpModal != null) _helpModal.SetActive(false);
                if (_settingsModal != null) _settingsModal.SetActive(false);

                // Ensure GameplayUiRoot exists if running inside a unified single scene
                if (FindFirstObjectByType<GameplayUiRoot>() == null)
                {
                    GameObject hudHost = new GameObject("[GameplayUiRoot]");
                    hudHost.AddComponent<GameplayUiRoot>();
                }
            }
        }

        private void BuildMenuIfNeeded()
        {
            if (_built) return;
            _built = true;

            _canvas = GetComponentInChildren<Canvas>(true);
            if (_canvas == null)
            {
                _canvas = BiopunkUiBuilder.CreateScreenCanvas("MainMenuCanvas", transform, sortingOrder: 220);
            }
            else
            {
                BiopunkUiBuilder.EnsureEventSystem();
            }

            RectTransform rootRt = BiopunkUiBuilder.CreateStretchRect("MainMenuRoot", _canvas.transform);
            _rootPanel = rootRt.gameObject;

            // 1. Full-screen 1920x1080 pixel-art Main Menu background
            Image bgImg = BiopunkUiBuilder.CreateSpriteImage(
                rootRt, "BackgroundArt", "UI/Screen_MainMenuBg",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, preserveAspect: false, raycastTarget: true);
            BiopunkUiBuilder.Stretch(bgImg.rectTransform);

            // 2. Left-aligned Biopunk Menu Column (positioned over dark cave archways x=58..738 so Heart Core & Hazmat Colonist shine on right!)
            Image menuColumn = BiopunkUiBuilder.CreatePanel(
                rootRt, "LeftMenuColumn",
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(58f, 10f), new Vector2(680f, 890f),
                new Color(1f, 1f, 1f, 0.95f), raycastTarget: true);

            // Decorative top header crest inside column
            RectTransform headerBox = BiopunkUiBuilder.CreateRect(
                "HeaderBox", menuColumn.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -26f), new Vector2(624f, 236f));

            Image headerCardBg = headerBox.gameObject.AddComponent<Image>();
            headerCardBg.sprite = SpriteRegistry.Get("UI/UI_Card_Frame");
            headerCardBg.color = Color.white;
            headerCardBg.raycastTarget = false;

            Text protocolTag = BiopunkUiBuilder.CreateText(
                headerBox, "ProtocolTag", "ПРОТОКОЛ КОЛОНИЗАЦИИ ГЛУБИН • V1.0",
                18, FontStyle.Bold, BiopunkUiBuilder.AccentCyan, TextAnchor.MiddleCenter);
            protocolTag.rectTransform.anchorMin = new Vector2(0.06f, 0.76f);
            protocolTag.rectTransform.anchorMax = new Vector2(0.94f, 0.93f);
            protocolTag.rectTransform.offsetMin = Vector2.zero;
            protocolTag.rectTransform.offsetMax = Vector2.zero;

            Text mainTitle = BiopunkUiBuilder.CreateText(
                headerBox, "GameTitle", "BIOFARME:\nСЕРДЦЕ КОЛОНИИ",
                42, FontStyle.Bold, BiopunkUiBuilder.AccentLime, TextAnchor.MiddleCenter);
            mainTitle.lineSpacing = 0.96f;
            mainTitle.rectTransform.anchorMin = new Vector2(0.05f, 0.28f);
            mainTitle.rectTransform.anchorMax = new Vector2(0.95f, 0.77f);
            mainTitle.rectTransform.offsetMin = Vector2.zero;
            mainTitle.rectTransform.offsetMax = Vector2.zero;

            Text subtitle = BiopunkUiBuilder.CreateText(
                headerBox, "Subtitle",
                "Тактическая биопанк-стратегия: Ферма × Завод × Оборона",
                19, FontStyle.Italic, BiopunkUiBuilder.TextPrimary, TextAnchor.MiddleCenter);
            subtitle.rectTransform.anchorMin = new Vector2(0.05f, 0.08f);
            subtitle.rectTransform.anchorMax = new Vector2(0.95f, 0.28f);
            subtitle.rectTransform.offsetMin = Vector2.zero;
            subtitle.rectTransform.offsetMax = Vector2.zero;

            // Button Stack
            RectTransform btnStack = BiopunkUiBuilder.CreateRect(
                "MenuButtons", menuColumn.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -284f), new Vector2(588f, 380f));

            VerticalLayoutGroup vlg = btnStack.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 18f;
            vlg.padding = new RectOffset(8, 8, 6, 6);
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            BiopunkUiBuilder.CreateButton(
                btnStack, "BtnStartSymbiosis", "НАЧАТЬ СИМБИОЗ",
                new Vector2(570f, 74f),
                OnStartSymbiosisClicked,
                fontSize: 26,
                baseTint: new Color(0.70f, 1.00f, 0.56f, 1f),
                textColor: new Color(0.96f, 1.00f, 0.90f, 1f),
                iconSprite: SpriteRegistry.Get("Core_LivingHeart"));

            BiopunkUiBuilder.CreateButton(
                btnStack, "BtnHelpTutorial", "УПРАВЛЕНИЕ И ОБУЧЕНИЕ",
                new Vector2(570f, 68f),
                () => OpenHelpModal(0),
                fontSize: 23,
                baseTint: new Color(0.86f, 0.96f, 0.84f, 1f),
                textColor: BiopunkUiBuilder.TextPrimary,
                iconSprite: SpriteRegistry.Get("UI/Icon_SeedPouch"));

            BiopunkUiBuilder.CreateButton(
                btnStack, "BtnAudioSettings", "НАСТРОЙКИ АУДИО",
                new Vector2(570f, 68f),
                OpenSettingsModal,
                fontSize: 23,
                baseTint: new Color(0.84f, 0.94f, 0.98f, 1f),
                textColor: BiopunkUiBuilder.TextPrimary,
                iconSprite: SpriteRegistry.Get("UI/Icon_Building_Incubator"));

            BiopunkUiBuilder.CreateButton(
                btnStack, "BtnExitGame", "ВЫХОД",
                new Vector2(570f, 66f),
                OnQuitClicked,
                fontSize: 23,
                baseTint: new Color(0.98f, 0.74f, 0.72f, 1f),
                textColor: new Color(1.0f, 0.90f, 0.88f, 1f),
                iconSprite: SpriteRegistry.Get("UI/Icon_MutagenRed"));

            // Mission Briefing Box at bottom of left column
            Image briefPanel = BiopunkUiBuilder.CreatePanel(
                menuColumn.transform, "MissionBriefBox",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 24f), new Vector2(616f, 178f),
                new Color(0.82f, 0.90f, 0.80f, 0.95f), raycastTarget: false);

            Text briefText = BiopunkUiBuilder.CreateText(
                briefPanel.transform, "BriefText",
                "<color=#8CE844><b>ДИРЕКТИВА СЕРДЦА КОЛОНИИ:</b></color>\n" +
                "• Возделывайте живую почву и собирайте урожай (<b>Биомасса, Споры, Плазма, Хитин</b>).\n" +
                "• Подключайте турели-растения и био-реакторы сосудистыми венами к Ядру.\n" +
                "• Отразите <b>5 волн Улья</b> (включая Альфа-Матку) и синтезируйте <b>10 ед. Мутагена</b>!",
                18, FontStyle.Normal, BiopunkUiBuilder.TextPrimary, TextAnchor.MiddleLeft);
            BiopunkUiBuilder.Stretch(briefText.rectTransform, 18f, 12f, 18f, 12f);

            // Right-bottom atmospheric caption badge (does not cover farmer or heart)
            Image cornerTag = BiopunkUiBuilder.CreatePanel(
                rootRt, "RightAtmosphereBadge",
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-32f, 24f), new Vector2(460f, 52f),
                new Color(0.85f, 0.92f, 0.85f, 0.88f), raycastTarget: false);
            Text cornerTxt = BiopunkUiBuilder.CreateText(
                cornerTag.transform, "BadgeTxt",
                "Живое Сердце ждёт пробуждения • Нажмите «НАЧАТЬ СИМБИОЗ»",
                18, FontStyle.Normal, BiopunkUiBuilder.TextSecondary, TextAnchor.MiddleCenter);
            BiopunkUiBuilder.Stretch(cornerTxt.rectTransform, 12f, 4f, 12f, 4f);

            // 3. Multi-Tab Help & Lore Modal Window
            BuildHelpModal(rootRt);

            // 4. Audio & Fullscreen Settings Modal Window
            BuildSettingsModal(rootRt);

            // 5. WebGL / Menu Toast Notification
            BuildToastPopup(rootRt);

            BiopunkTooltipPopup.AttachToCanvas(_canvas);
        }

        private void BuildHelpModal(RectTransform rootRt)
        {
            RectTransform modalOverlay = BiopunkUiBuilder.CreateStretchRect("HelpModalOverlay", rootRt);
            Image dim = modalOverlay.gameObject.AddComponent<Image>();
            dim.sprite = SpriteRegistry.GetWhiteSquareSprite();
            dim.color = BiopunkUiBuilder.PanelBackdropDim;
            dim.raycastTarget = true;
            _helpModal = modalOverlay.gameObject;

            Image win = BiopunkUiBuilder.CreatePanel(
                modalOverlay, "HelpWindow",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1340f, 820f));

            Text title = BiopunkUiBuilder.CreateText(
                win.transform, "ModalTitle", "СПРАВОЧНИК КОЛОНИСТА: МЕХАНИКИ И УПРАВЛЕНИЕ",
                30, FontStyle.Bold, BiopunkUiBuilder.AccentLime, TextAnchor.MiddleLeft);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -20f);
            title.rectTransform.sizeDelta = new Vector2(-180f, 48f);
            title.rectTransform.offsetMin = new Vector2(32f, -68f);
            title.rectTransform.offsetMax = new Vector2(-210f, -20f);

            Button closeBtn = BiopunkUiBuilder.CreateButton(
                win.transform, "BtnCloseHelp", "ЗАКРЫТЬ [Esc]",
                new Vector2(180f, 48f),
                () => _helpModal.SetActive(false),
                fontSize: 19,
                baseTint: new Color(0.95f, 0.72f, 0.70f, 1f));
            closeBtn.image.rectTransform.anchorMin = new Vector2(1f, 1f);
            closeBtn.image.rectTransform.anchorMax = new Vector2(1f, 1f);
            closeBtn.image.rectTransform.pivot = new Vector2(1f, 1f);
            closeBtn.image.rectTransform.anchoredPosition = new Vector2(-26f, -20f);

            // 5 Category Tabs
            RectTransform tabRow = BiopunkUiBuilder.CreateRect(
                "TabRow", win.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -84f), new Vector2(1284f, 56f));

            HorizontalLayoutGroup hlg = tabRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10f;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            string[] tabNames =
            {
                "1. ФЕРМА И ПОЧВА",
                "2. БИО-СЕТЬ И ЗАВОДЫ",
                "3. ТУРЕЛИ И ОБОРОНА",
                "4. МИНИ-ИГРЫ И МУТАГЕН",
                "5. УПРАВЛЕНИЕ (ПК)"
            };

            for (int i = 0; i < tabNames.Length; i++)
            {
                int captured = i;
                _helpTabButtons[i] = BiopunkUiBuilder.CreateButton(
                    tabRow, $"HelpTab_{i}", tabNames[i],
                    new Vector2(246f, 54f),
                    () => SelectHelpTab(captured),
                    fontSize: 18);
            }

            // Content Frame
            Image bodyFrame = BiopunkUiBuilder.CreatePanel(
                win.transform, "HelpContentFrame",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 26f), new Vector2(1284f, 642f),
                new Color(0.82f, 0.88f, 0.82f, 0.98f));

            // Top Banner inside content with 2 showcase icons + section header
            RectTransform sectionHeaderRt = BiopunkUiBuilder.CreateRect(
                "SectionHeader", bodyFrame.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -20f), new Vector2(1228f, 92f));

            _helpTabIconLeft = BiopunkUiBuilder.CreateSpriteImage(
                sectionHeaderRt, "IconLeft", "Icon_Crop_Sporecap",
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(12f, 0f), new Vector2(80f, 80f), preserveAspect: true);

            _helpTabIconRight = BiopunkUiBuilder.CreateSpriteImage(
                sectionHeaderRt, "IconRight", "Icon_Tool_BioSprayer",
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-12f, 0f), new Vector2(80f, 80f), preserveAspect: true);

            _helpTabHeader = BiopunkUiBuilder.CreateText(
                sectionHeaderRt, "TabHeader", string.Empty,
                26, FontStyle.Bold, BiopunkUiBuilder.AccentCyan, TextAnchor.MiddleCenter);
            BiopunkUiBuilder.Stretch(_helpTabHeader.rectTransform, 110f, 4f, 110f, 4f);

            _helpTabBody = BiopunkUiBuilder.CreateText(
                bodyFrame.transform, "TabBody", string.Empty,
                21, FontStyle.Normal, BiopunkUiBuilder.TextPrimary, TextAnchor.UpperLeft);
            _helpTabBody.lineSpacing = 1.18f;
            _helpTabBody.rectTransform.anchorMin = new Vector2(0f, 0f);
            _helpTabBody.rectTransform.anchorMax = new Vector2(1f, 1f);
            _helpTabBody.rectTransform.offsetMin = new Vector2(36f, 28f);
            _helpTabBody.rectTransform.offsetMax = new Vector2(-36f, -124f);

            SelectHelpTab(0);
            _helpModal.SetActive(false);
        }

        private void OpenHelpModal(int tabIndex)
        {
            if (_settingsModal != null) _settingsModal.SetActive(false);
            if (_helpModal != null)
            {
                _helpModal.SetActive(true);
                SelectHelpTab(tabIndex);
            }
        }

        private void SelectHelpTab(int index)
        {
            _currentHelpTab = Mathf.Clamp(index, 0, 4);
            for (int i = 0; i < _helpTabButtons.Length; i++)
            {
                if (_helpTabButtons[i] != null && _helpTabButtons[i].image != null)
                {
                    _helpTabButtons[i].image.color = (i == _currentHelpTab)
                        ? new Color(0.68f, 1.00f, 0.52f, 1f)
                        : new Color(0.82f, 0.86f, 0.80f, 1f);
                }
            }

            switch (_currentHelpTab)
            {
                case 0:
                    _helpTabIconLeft.sprite = SpriteRegistry.Get("UI/Icon_Crop_Sporecap");
                    _helpTabIconRight.sprite = SpriteRegistry.Get("UI/Icon_Crop_PlasmaBulb");
                    _helpTabHeader.text = "СИМБИОТИЧЕСКАЯ АГРОНОМИЯ (В ДУХЕ STARDEW VALLEY)";
                    _helpTabBody.text =
                        "• <b>1. Подготовка грядок:</b> Возьмите <b>Культиватор-мотыгу [1]</b> и вспашите плоть-почву или плодородный зелёный мох (мох даёт <b>+25..30%</b> к скорости роста!).\n" +
                        "• <b>2. Орошение:</b> Поливайте грядки <b>Био-распылителем [2]</b> (зажать ЛКМ) или проложите рядом <b>Плазмо-вену</b>, которая автоматически увлажняет соседние клетки 3×3!\n" +
                        "• <b>3. Четыре био-культуры:</b>\n" +
                        "   — <color=#FAD040><b>Спорошляпка</b></color> (6 Биомассы -> 12–18 Спор за 13с)\n" +
                        "   — <color=#59EB6B><b>Багровый стручок</b></color> (6 Спор -> 30–44 Биомассы за 15с)\n" +
                        "   — <color=#38E5FF><b>Плазмолуковица</b></color> (10 Спор + 10 Био -> 11–17 Биоплазмы за 21с)\n" +
                        "   — <color=#EDE3C7><b>Костяной корень</b></color> (8 Спор + 12 Био -> 9–15 Костяного хитина за 23с)\n" +
                        "• <b>4. Сбор урожая:</b> Ударьте созревший плод мотыгой [ЛКМ], нажмите <b>[E]</b> вблизи или постройте <b>Симбиота-сборщика</b> для полной автоматизации!";
                    break;

                case 1:
                    _helpTabIconLeft.sprite = SpriteRegistry.Get("UI/Icon_Vein_Plasma");
                    _helpTabIconRight.sprite = SpriteRegistry.Get("UI/Icon_Building_BioReactor");
                    _helpTabHeader.text = "СОСУДИСТАЯ ЛОГИСТИКА И ЗАВОДЫ (В ДУХЕ MINDUSTRY)";
                    _helpTabBody.text =
                        "• <b>Сосудистые каналы (Био-вена, Плазмо-вена, Магистральная артерия):</b> Соединяют <b>Живое Сердце Колонии</b>, генераторы и сборщики с турелями, реакторами и инкубаторами.\n" +
                        "• <b>Живые пакеты ресурсов:</b> По венам пульсируют капли Биомассы, Спор и Биоплазмы — подключённые турели стреляют быстрее, а реакторы автоматически получают сырьё!\n" +
                        "• <b>Ключевые узлы синтеза:</b>\n" +
                        "   — <b>Пульс-луковица</b> и <b>Био-реактор</b> вырабатывают дефицитную Биоплазму.\n" +
                        "   — <b>Инкубатор мутагена</b> перерабатывает Биоплазму в победный <color=#F547E0><b>Мутаген</b></color>!\n" +
                        "• <b>Приоритеты сети [B -> Логистика] или [Space]:</b> Переключайте распределение потока между <i>Сбалансированным</i>, <i>Обороной (Турели)</i>, <i>Синтезом (Реакторы)</i> и <i>Накоплением в Ядре</i>!";
                    break;

                case 2:
                    _helpTabIconLeft.sprite = SpriteRegistry.Get("UI/Icon_Turret_Flytrap");
                    _helpTabIconRight.sprite = SpriteRegistry.Get("UI/Icon_Turret_BileCannon");
                    _helpTabHeader.text = "РАСТЕНИЯ-ТУРЕЛИ И ОБОРОНА ОТ РОЯ (В ДУХЕ PVZ)";
                    _helpTabBody.text =
                        "• Четыре подземных логова по краям пещеры извергают <b>5 нарастающих волн Улья</b>: от быстрых Клещей роя и Богомолов-жнецов до Панцирных крабов и колоссальной <b>Альфа-Матки</b>!\n" +
                        "• <b>Формирование лабиринтов:</b> Перекрывайте проходы <b>Костяными стенами</b> и <b>Хитиновыми изгородями</b>, направляя жуков в узкие огневые коридоры.\n" +
                        "• <b>Арсенал живой обороны:</b>\n" +
                        "   — <color=#8CE844><b>Мухоловка-страж:</b></color> скорострельная кислотная турель против стай клещей.\n" +
                        "   — <color=#FAD040><b>Спороцвет-миномёт:</b></color> бьёт по площади и замедляет толпы врагов на 40%.\n" +
                        "   — <color=#38E5FF><b>Желчная пушка:</b></color> сокрушительный урон против крабов и Альфа-Матки.\n" +
                        "   — <color=#59EB6B><b>Целительное щупальце:</b></color> регенерирует стены и само Живое Сердце!";
                    break;

                case 3:
                    _helpTabIconLeft.sprite = SpriteRegistry.Get("UI/Icon_Building_Incubator");
                    _helpTabIconRight.sprite = SpriteRegistry.Get("Core_LivingHeart");
                    _helpTabHeader.text = "ИНТЕРАКТИВНЫЕ МИНИ-ИГРЫ И ПРОБУЖДЕНИЕ СЕРДЦА";
                    _helpTabBody.text =
                        "• <b>Мини-игра 1 — «Био-Синтез и Мутационное Орошение» ([M] или [E] у Инкубатора):</b>\n" +
                        "   Стабилизируйте клапаны давления и поймайте 3 генетических импульса в зелёной/золотой зоне резонанса. Награда: <b>+1.5..2.5 Мутагена</b>, <b>рост культур x2</b> и <b>перегрузка турелей x1.5</b> на 45 секунд!\n\n" +
                        "• <b>Мини-игра 2 — «Дефибрилляция и Подкормка Сердца» ([H] или [E] у Живого Сердца):</b>\n" +
                        "   Быстро очистите артерии Сердца от пульсирующих паразитических тромбов за 10 секунд! Награда: <b>мгновенное лечение Ядра</b>, мощная <b>круговая ударная волна</b> по врагам, <b>+Биоплазма</b> и <b>+Мутаген</b>!\n\n" +
                        "• <b>Условие Победы:</b> Накопите <b>10.0 ед. Мутагена</b> (100% пробуждения) и выстойте во всех <b>5 волнах Улья</b>!";
                    break;

                default:
                    _helpTabIconLeft.sprite = SpriteRegistry.Get("UI/Icon_Tool_Cultivator");
                    _helpTabIconRight.sprite = SpriteRegistry.Get("UI/Icon_Tool_BioAxe");
                    _helpTabHeader.text = "ПОЛНАЯ ТАБЛИЦА УПРАВЛЕНИЯ (КЛАВИАТУРА И МЫШЬ)";
                    _helpTabBody.text =
                        "• <b>[W / A / S / D] или [Стрелки]</b>  —  Перемещение колониста (или камеры в Такт. паузе)\n" +
                        "• <b>[ЛКМ] (Левая кнопка мыши)</b>  —  Использовать инструмент / Посадить семя / Построить объект\n" +
                        "• <b>[ПКМ] (Правая кнопка мыши)</b>  —  Разобрать постройку с возвратом <b>70%</b> ресурсов\n" +
                        "• <b>[1 .. 8] или [Колесо мыши] / Клик по слоту</b>  —  Выбор активного слота панели быстрого доступа\n" +
                        "• <b>[B] или [Tab]</b>  —  Открыть <b>Био-Кодекс</b> (Семена, Постройки, Приоритеты вен, Био-обмен)\n" +
                        "• <b>[E]</b>  —  Взаимодействие с объектом рядом (Живое Сердце, Инкубатор, сбор зрелого куста)\n" +
                        "• <b>[M] / [H]</b>  —  Быстрый запуск мини-игры <b>Био-Синтеза [M]</b> или <b>Дефибрилляции Ядра [H]</b>\n" +
                        "• <b>[Пробел (Space)]</b>  —  <b>Тактическая пауза</b> (остановка боя для планирования и кликов по мини-карте)\n" +
                        "• <b>[F1] или [Esc]</b>  —  Окно паузы, настроек громкости и памятки управления";
                    break;
            }
        }

        private void BuildSettingsModal(RectTransform rootRt)
        {
            RectTransform modalOverlay = BiopunkUiBuilder.CreateStretchRect("SettingsModalOverlay", rootRt);
            Image dim = modalOverlay.gameObject.AddComponent<Image>();
            dim.sprite = SpriteRegistry.GetWhiteSquareSprite();
            dim.color = BiopunkUiBuilder.PanelBackdropDim;
            dim.raycastTarget = true;
            _settingsModal = modalOverlay.gameObject;

            Image win = BiopunkUiBuilder.CreatePanel(
                modalOverlay, "SettingsWindow",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(740f, 560f));

            Text title = BiopunkUiBuilder.CreateText(
                win.transform, "SettingsTitle", "НАСТРОЙКИ АУДИО И ЭКРАНА",
                30, FontStyle.Bold, BiopunkUiBuilder.AccentLime, TextAnchor.MiddleCenter);
            title.rectTransform.anchorMin = new Vector2(0.05f, 1f);
            title.rectTransform.anchorMax = new Vector2(0.95f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -24f);
            title.rectTransform.sizeDelta = new Vector2(0f, 48f);

            RectTransform content = BiopunkUiBuilder.CreateRect(
                "SlidersContainer", win.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 18f), new Vector2(630f, 320f));

            VerticalLayoutGroup vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 22f;
            vlg.padding = new RectOffset(14, 14, 10, 10);
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            float master = AudioManager.Instance != null ? AudioManager.Instance.MasterVolume : 0.85f;
            float music = AudioManager.Instance != null ? AudioManager.Instance.MusicVolume : 0.55f;
            float sfx = AudioManager.Instance != null ? AudioManager.Instance.SfxVolume : 0.80f;

            _masterSlider = BiopunkUiBuilder.CreateLabeledSlider(
                content, "MasterSlider", "Общая громкость (Master)", master,
                BiopunkUiBuilder.AccentLime,
                val => AudioManager.Instance?.SetMasterVolume(val));

            _musicSlider = BiopunkUiBuilder.CreateLabeledSlider(
                content, "MusicSlider", "Атмосфера и музыка пещер", music,
                BiopunkUiBuilder.AccentCyan,
                val => AudioManager.Instance?.SetMusicVolume(val));

            _sfxSlider = BiopunkUiBuilder.CreateLabeledSlider(
                content, "SfxSlider", "Звуковые эффекты (Бой и Ферма)", sfx,
                BiopunkUiBuilder.AccentMutagen,
                val => AudioManager.Instance?.SetSfxVolume(val));

            // Fullscreen Toggle Button
            Button fsBtn = BiopunkUiBuilder.CreateButton(
                content, "BtnFullscreenToggle", GetFullscreenButtonLabel(),
                new Vector2(600f, 54f),
                ToggleFullscreen,
                fontSize: 20,
                baseTint: new Color(0.84f, 0.92f, 0.96f, 1f));
            _fullscreenLabel = fsBtn.GetComponentInChildren<Text>();

            // Bottom Row: Test Sound + Close
            RectTransform bottomRow = BiopunkUiBuilder.CreateRect(
                "BottomRow", win.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 26f), new Vector2(630f, 58f));

            HorizontalLayoutGroup botHlg = bottomRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            botHlg.spacing = 20f;
            botHlg.childAlignment = TextAnchor.MiddleCenter;
            botHlg.childControlWidth = true;
            botHlg.childControlHeight = true;
            botHlg.childForceExpandWidth = true;
            botHlg.childForceExpandHeight = true;

            BiopunkUiBuilder.CreateButton(
                bottomRow, "BtnTestSfx", "ПРОВЕРИТЬ ЗВУК",
                new Vector2(290f, 56f),
                () => AudioManager.Instance?.PlaySfx(AudioKeys.SfxHeartPulse, 1.0f, 0.02f),
                fontSize: 20,
                baseTint: new Color(0.76f, 0.96f, 0.72f, 1f));

            BiopunkUiBuilder.CreateButton(
                bottomRow, "BtnCloseSettings", "ГОТОВО [Esc]",
                new Vector2(290f, 56f),
                () => _settingsModal.SetActive(false),
                fontSize: 20,
                baseTint: new Color(0.92f, 0.88f, 0.76f, 1f));

            _settingsModal.SetActive(false);
        }

        private void OpenSettingsModal()
        {
            if (_helpModal != null) _helpModal.SetActive(false);
            if (_settingsModal != null)
            {
                if (AudioManager.Instance != null)
                {
                    if (_masterSlider != null) _masterSlider.value = AudioManager.Instance.MasterVolume;
                    if (_musicSlider != null) _musicSlider.value = AudioManager.Instance.MusicVolume;
                    if (_sfxSlider != null) _sfxSlider.value = AudioManager.Instance.SfxVolume;
                }
                if (_fullscreenLabel != null)
                {
                    _fullscreenLabel.text = GetFullscreenButtonLabel();
                }
                _settingsModal.SetActive(true);
            }
        }

        private void ToggleFullscreen()
        {
            Screen.fullScreen = !Screen.fullScreen;
            if (_fullscreenLabel != null)
            {
                _fullscreenLabel.text = GetFullscreenButtonLabel();
            }
        }

        private static string GetFullscreenButtonLabel()
        {
            return Screen.fullScreen
                ? "РЕЖИМ ЭКРАНА: ПОЛНОЭКРАННЫЙ (Нажмите для оконного)"
                : "РЕЖИМ ЭКРАНА: ОКОННЫЙ (Нажмите для полного экрана)";
        }

        private void BuildToastPopup(RectTransform rootRt)
        {
            Image toastPanel = BiopunkUiBuilder.CreatePanel(
                rootRt, "MenuToast",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 36f), new Vector2(680f, 64f),
                new Color(0.95f, 0.86f, 0.72f, 0.98f), raycastTarget: false);

            _toastGroup = toastPanel.gameObject.AddComponent<CanvasGroup>();
            _toastGroup.blocksRaycasts = false;
            _toastGroup.alpha = 0f;

            _toastText = BiopunkUiBuilder.CreateText(
                toastPanel.transform, "ToastText", string.Empty,
                21, FontStyle.Bold, BiopunkUiBuilder.AccentAmber, TextAnchor.MiddleCenter);
            BiopunkUiBuilder.Stretch(_toastText.rectTransform, 16f, 6f, 16f, 6f);

            toastPanel.gameObject.SetActive(false);
        }

        private void ShowMenuToast(string message)
        {
            if (_toastGroup == null || _toastText == null) return;
            _toastText.text = message;
            _toastGroup.alpha = 1f;
            _toastGroup.gameObject.SetActive(true);
            _toastTimer = 3.5f;
        }

        private void OnStartSymbiosisClicked()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.StartNewGame();
            }
        }

        private void OnQuitClicked()
        {
            ShowMenuToast("В веб-версии закройте вкладку браузера");
            Application.Quit();
        }
    }
}
