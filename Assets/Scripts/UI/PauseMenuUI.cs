using UnityEngine;
using UnityEngine.UI;
using BioFarme.Audio;
using BioFarme.Core;
using BioFarme.Data;

namespace BioFarme.UI
{
    /// <summary>
    /// Pause & Controls Reference Window ([Esc] / [F1] / HUD button).
    /// Displays Master, Music, and SFX sliders, Fullscreen toggle, complete Keyboard/Mouse controls table,
    /// and Resume / Restart / Main Menu actions.
    /// </summary>
    public sealed class PauseMenuUI : MonoBehaviour
    {
        private RectTransform _modalRoot;
        private Slider _masterSlider;
        private Slider _musicSlider;
        private Slider _sfxSlider;

        public bool IsOpen => _modalRoot != null && _modalRoot.gameObject.activeSelf;

        public void Build(Transform parent)
        {
            _modalRoot = BiopunkUiBuilder.CreateStretchRect("PauseMenuModalOverlay", parent);
            Image dim = _modalRoot.gameObject.AddComponent<Image>();
            dim.sprite = SpriteRegistry.GetWhiteSquareSprite();
            dim.color = new Color(0.03f, 0.05f, 0.04f, 0.84f);
            dim.raycastTarget = true;

            Image win = BiopunkUiBuilder.CreatePanel(
                _modalRoot, "PauseWindow",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1260f, 760f),
                new Color(0.95f, 1.00f, 0.95f, 0.99f), raycastTarget: true);

            // Header
            Image header = BiopunkUiBuilder.CreatePanel(
                win.transform, "PauseHeader",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -16f), new Vector2(1216f, 68f),
                new Color(0.80f, 0.92f, 0.80f, 0.98f), raycastTarget: false);

            Text title = BiopunkUiBuilder.CreateText(
                header.transform, "PauseTitle", "ПАУЗА МИССИИ • НАСТРОЙКИ И УПРАВЛЕНИЕ",
                27, FontStyle.Bold, BiopunkUiBuilder.AccentLime, TextAnchor.MiddleCenter);
            BiopunkUiBuilder.Stretch(title.rectTransform, 24f, 6f, 24f, 6f);

            // Left Column: Audio Sliders + Session Actions
            Image leftCol = BiopunkUiBuilder.CreatePanel(
                win.transform, "LeftSettingsColumn",
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(22f, 22f), new Vector2(520f, 636f),
                new Color(0.84f, 0.90f, 0.84f, 0.96f), raycastTarget: true);

            Text audioHeader = BiopunkUiBuilder.CreateText(
                leftCol.transform, "AudioTitle", "АУДИОСИСТЕМА ПЕЩЕР",
                22, FontStyle.Bold, BiopunkUiBuilder.AccentCyan, TextAnchor.MiddleCenter);
            audioHeader.rectTransform.anchorMin = new Vector2(0.05f, 0.90f);
            audioHeader.rectTransform.anchorMax = new Vector2(0.95f, 0.98f);
            audioHeader.rectTransform.offsetMin = Vector2.zero;
            audioHeader.rectTransform.offsetMax = Vector2.zero;

            RectTransform slidersStack = BiopunkUiBuilder.CreateRect(
                "SlidersStack", leftCol.transform,
                new Vector2(0.5f, 0.88f), new Vector2(0.5f, 0.88f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(468f, 236f));

            VerticalLayoutGroup sVlg = slidersStack.gameObject.AddComponent<VerticalLayoutGroup>();
            sVlg.spacing = 14f;
            sVlg.childControlWidth = true;
            sVlg.childControlHeight = false;
            sVlg.childForceExpandWidth = true;

            float mVol = AudioManager.Instance != null ? AudioManager.Instance.MasterVolume : 0.85f;
            float bgmVol = AudioManager.Instance != null ? AudioManager.Instance.MusicVolume : 0.55f;
            float sfxVol = AudioManager.Instance != null ? AudioManager.Instance.SfxVolume : 0.80f;

            _masterSlider = BiopunkUiBuilder.CreateLabeledSlider(
                slidersStack, "PauseMasterSlider", "Общая громкость", mVol,
                BiopunkUiBuilder.AccentLime, val => AudioManager.Instance?.SetMasterVolume(val));

            _musicSlider = BiopunkUiBuilder.CreateLabeledSlider(
                slidersStack, "PauseMusicSlider", "Громкость музыки", bgmVol,
                BiopunkUiBuilder.AccentCyan, val => AudioManager.Instance?.SetMusicVolume(val));

            _sfxSlider = BiopunkUiBuilder.CreateLabeledSlider(
                slidersStack, "PauseSfxSlider", "Звуковые эффекты", sfxVol,
                BiopunkUiBuilder.AccentMutagen, val => AudioManager.Instance?.SetSfxVolume(val));

            // Action Buttons Stack
            RectTransform actionsStack = BiopunkUiBuilder.CreateRect(
                "ActionsStack", leftCol.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 24f), new Vector2(468f, 236f));

            VerticalLayoutGroup aVlg = actionsStack.gameObject.AddComponent<VerticalLayoutGroup>();
            aVlg.spacing = 14f;
            aVlg.childAlignment = TextAnchor.LowerCenter;
            aVlg.childControlWidth = true;
            aVlg.childControlHeight = false;
            aVlg.childForceExpandWidth = true;

            BiopunkUiBuilder.CreateButton(
                actionsStack, "BtnResume", "Продолжить [Esc]",
                new Vector2(468f, 62f),
                () => GameManager.Instance?.ResumeFromPause(),
                fontSize: 22,
                baseTint: new Color(0.68f, 1.00f, 0.56f, 1f));

            BiopunkUiBuilder.CreateButton(
                actionsStack, "BtnRestartMission", "Перезапустить миссию",
                new Vector2(468f, 58f),
                () => GameManager.Instance?.StartNewGame(),
                fontSize: 21,
                baseTint: new Color(0.98f, 0.86f, 0.62f, 1f));

            BiopunkUiBuilder.CreateButton(
                actionsStack, "BtnToMainMenu", "В главное меню",
                new Vector2(468f, 58f),
                () => GameManager.Instance?.LoadMainMenu(),
                fontSize: 21,
                baseTint: new Color(0.96f, 0.72f, 0.70f, 1f));

            // Right Column: Full Keyboard & Mouse Controls Table
            Image rightCol = BiopunkUiBuilder.CreatePanel(
                win.transform, "RightControlsColumn",
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-22f, 22f), new Vector2(680f, 636f),
                new Color(0.82f, 0.88f, 0.86f, 0.96f), raycastTarget: false);

            Text ctrlTitle = BiopunkUiBuilder.CreateText(
                rightCol.transform, "CtrlTitle", "ПАМЯТКА УПРАВЛЕНИЯ И СИМБИОЗА",
                22, FontStyle.Bold, BiopunkUiBuilder.AccentLime, TextAnchor.MiddleCenter);
            ctrlTitle.rectTransform.anchorMin = new Vector2(0.05f, 0.90f);
            ctrlTitle.rectTransform.anchorMax = new Vector2(0.95f, 0.98f);
            ctrlTitle.rectTransform.offsetMin = Vector2.zero;
            ctrlTitle.rectTransform.offsetMax = Vector2.zero;

            Text ctrlTable = BiopunkUiBuilder.CreateText(
                rightCol.transform, "ControlsTable",
                "<color=#8CE844><b>ПЕРЕМЕЩЕНИЕ И ДЕЙСТВИЯ:</b></color>\n" +
                "• <b>[W / A / S / D]</b> или <b>[Стрелки]</b> — Движение колониста по пещере\n" +
                "• <b>[ЛКМ]</b> — Вспахать / Посадить семя / Построить / Стрелять распылителем\n" +
                "• <b>[ПКМ]</b> — Разобрать постройку (возврат <b>70%</b> стоимости)\n" +
                "• <b>[1 .. 8]</b> или <b>[Колесо мыши]</b> — Выбор слота панели быстрого доступа\n" +
                "• <b>[E]</b> — Взаимодействие с Живым Сердцем, Инкубатором или урожаем\n\n" +
                "<color=#38E5FF><b>ОКНА, ТАКТИКА И МИНИ-ИГРЫ:</b></color>\n" +
                "• <b>[B]</b> или <b>[Tab]</b> — <b>Био-Кодекс</b> (Семена, Турели, Заводы, Обмен ресурсов)\n" +
                "• <b>[M]</b> — Мини-игра <b>«Био-Синтез»</b> (+Мутаген, рост культур x2, турели x1.5)\n" +
                "• <b>[H]</b> — Мини-игра <b>«Дефибрилляция Сердца»</b> (лечение Ядра + ударная волна)\n" +
                "• <b>[Space]</b> — <b>Тактическая пауза</b> (планирование и клик по мини-карте)\n" +
                "• <b>[N]</b> — Свернуть / развернуть радар мини-карты\n" +
                "• <b>[F1] / [Esc]</b> — Открыть / закрыть это меню\n\n" +
                "<color=#F547E0><b>ЦЕЛЬ МИССИИ:</b></color> Выжить в <b>5 волнах Улья</b> и накопить <b>10.0 Мутагена</b>!",
                18, FontStyle.Normal, BiopunkUiBuilder.TextPrimary, TextAnchor.UpperLeft);
            ctrlTable.lineSpacing = 1.16f;
            ctrlTable.rectTransform.anchorMin = new Vector2(0f, 0f);
            ctrlTable.rectTransform.anchorMax = new Vector2(1f, 0.89f);
            ctrlTable.rectTransform.offsetMin = new Vector2(26f, 20f);
            ctrlTable.rectTransform.offsetMax = new Vector2(-24f, -6f);

            _modalRoot.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            GameEvents.OnGameStateChanged += HandleGameStateChanged;
        }

        private void OnDisable()
        {
            GameEvents.OnGameStateChanged -= HandleGameStateChanged;
        }

        private void HandleGameStateChanged(GameState prev, GameState next)
        {
            SyncWithState(next);
        }

        public void SyncWithState(GameState state)
        {
            if (_modalRoot == null) return;
            bool shouldShow = state == GameState.Paused;
            if (_modalRoot.gameObject.activeSelf != shouldShow)
            {
                _modalRoot.gameObject.SetActive(shouldShow);
            }

            if (shouldShow)
            {
                _modalRoot.SetAsLastSibling();
                if (AudioManager.Instance != null)
                {
                    if (_masterSlider != null) _masterSlider.value = AudioManager.Instance.MasterVolume;
                    if (_musicSlider != null) _musicSlider.value = AudioManager.Instance.MusicVolume;
                    if (_sfxSlider != null) _sfxSlider.value = AudioManager.Instance.SfxVolume;
                }
            }
        }
    }
}
