using UnityEngine;
using UnityEngine.UI;
using BioFarme.Core;
using BioFarme.Data;
using BioFarme.Grid;

namespace BioFarme.UI
{
    /// <summary>
    /// Contest Screens #4 (Экран победы — GameState.Victory) & #5 (Экран поражения — GameState.Defeat).
    /// Renders full-screen 1920x1080 artwork (Screen_VictoryBg / Screen_DefeatBg), run rank,
    /// defeat diagnosis, complete statistics table, and Replay / Main Menu buttons.
    /// </summary>
    public sealed class EndScreensUI : MonoBehaviour
    {
        // Victory Screen Refs
        private GameObject _victoryRoot;
        private Text _victoryRankText;
        private Text _victoryStatsLeftText;
        private Text _victoryStatsRightText;

        // Defeat Screen Refs
        private GameObject _defeatRoot;
        private Text _defeatReasonText;
        private Text _defeatStatsLeftText;
        private Text _defeatStatsRightText;
        private Text _defeatAdviceText;

        public void Build(Transform parent)
        {
            BuildVictoryScreen(parent);
            BuildDefeatScreen(parent);
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
            bool isVictory = state == GameState.Victory;
            bool isDefeat = state == GameState.Defeat;

            if (_victoryRoot != null)
            {
                _victoryRoot.SetActive(isVictory);
                if (isVictory)
                {
                    _victoryRoot.transform.SetAsLastSibling();
                    PopulateVictoryStatistics();
                }
            }

            if (_defeatRoot != null)
            {
                _defeatRoot.SetActive(isDefeat);
                if (isDefeat)
                {
                    _defeatRoot.transform.SetAsLastSibling();
                    PopulateDefeatStatistics();
                }
            }
        }

        // =====================================================================
        // CONTEST SCREEN #4: ЭКРАН ПОБЕДЫ (GameState.Victory)
        // =====================================================================

        private void BuildVictoryScreen(Transform parent)
        {
            RectTransform root = BiopunkUiBuilder.CreateStretchRect("VictoryScreenRoot", parent);
            _victoryRoot = root.gameObject;

            Image bg = BiopunkUiBuilder.CreateSpriteImage(
                root, "VictoryBgArt", "UI/Screen_VictoryBg",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, preserveAspect: false, raycastTarget: true);
            BiopunkUiBuilder.Stretch(bg.rectTransform);

            // Top Triumphant Header Card (leaves the glowing Golden-Cyan Awakened Heart visible in center!)
            Image topCard = BiopunkUiBuilder.CreatePanel(
                root, "VictoryHeaderCard",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -26f), new Vector2(1260f, 168f),
                new Color(0.84f, 1.00f, 0.88f, 0.92f), raycastTarget: false);

            Text header = BiopunkUiBuilder.CreateText(
                topCard.transform, "VictoryHeader",
                "СЕРДЦЕ КОЛОНИИ ПРОБУДИЛОСЬ! ПОБЕДА!",
                40, FontStyle.Bold, BiopunkUiBuilder.AccentLime, TextAnchor.MiddleCenter);
            header.rectTransform.anchorMin = new Vector2(0.04f, 0.50f);
            header.rectTransform.anchorMax = new Vector2(0.96f, 0.92f);
            header.rectTransform.offsetMin = Vector2.zero;
            header.rectTransform.offsetMax = Vector2.zero;

            _victoryRankText = BiopunkUiBuilder.CreateText(
                topCard.transform, "VictoryRankBadge",
                "РАНГ S — ВЕРХОВНЫЙ БИО-АРХИТЕКТОР СИМБИОЗА",
                24, FontStyle.Bold, BiopunkUiBuilder.AccentCyan, TextAnchor.MiddleCenter);
            _victoryRankText.rectTransform.anchorMin = new Vector2(0.05f, 0.12f);
            _victoryRankText.rectTransform.anchorMax = new Vector2(0.95f, 0.48f);
            _victoryRankText.rectTransform.offsetMin = Vector2.zero;
            _victoryRankText.rectTransform.offsetMax = Vector2.zero;

            // Bottom Translucent Summary & Action Panel (keeps center Heart Core visible between y=460..870!)
            Image summaryCard = BiopunkUiBuilder.CreatePanel(
                root, "VictorySummaryPanel",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 26f), new Vector2(1240f, 396f),
                new Color(0.88f, 0.98f, 0.90f, 0.93f), raycastTarget: true);

            Text statsTitle = BiopunkUiBuilder.CreateText(
                summaryCard.transform, "StatsTitle", "ИТОГОВАЯ СВОДКА СИМБИОТИЧЕСКОЙ ЭКСПЕДИЦИИ",
                22, FontStyle.Bold, BiopunkUiBuilder.AccentLime, TextAnchor.MiddleCenter);
            statsTitle.rectTransform.anchorMin = new Vector2(0.05f, 0.84f);
            statsTitle.rectTransform.anchorMax = new Vector2(0.95f, 0.96f);
            statsTitle.rectTransform.offsetMin = Vector2.zero;
            statsTitle.rectTransform.offsetMax = Vector2.zero;

            _victoryStatsLeftText = BiopunkUiBuilder.CreateText(
                summaryCard.transform, "StatsLeft", string.Empty,
                21, FontStyle.Normal, BiopunkUiBuilder.TextPrimary, TextAnchor.MiddleLeft);
            _victoryStatsLeftText.lineSpacing = 1.28f;
            _victoryStatsLeftText.rectTransform.anchorMin = new Vector2(0.06f, 0.28f);
            _victoryStatsLeftText.rectTransform.anchorMax = new Vector2(0.50f, 0.82f);
            _victoryStatsLeftText.rectTransform.offsetMin = Vector2.zero;
            _victoryStatsLeftText.rectTransform.offsetMax = Vector2.zero;

            _victoryStatsRightText = BiopunkUiBuilder.CreateText(
                summaryCard.transform, "StatsRight", string.Empty,
                21, FontStyle.Normal, BiopunkUiBuilder.TextPrimary, TextAnchor.MiddleLeft);
            _victoryStatsRightText.lineSpacing = 1.28f;
            _victoryStatsRightText.rectTransform.anchorMin = new Vector2(0.53f, 0.28f);
            _victoryStatsRightText.rectTransform.anchorMax = new Vector2(0.96f, 0.82f);
            _victoryStatsRightText.rectTransform.offsetMin = Vector2.zero;
            _victoryStatsRightText.rectTransform.offsetMax = Vector2.zero;

            RectTransform btnRow = BiopunkUiBuilder.CreateRect(
                "VictoryButtonsRow", summaryCard.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 22f), new Vector2(880f, 68f));

            HorizontalLayoutGroup hlg = btnRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 32f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            BiopunkUiBuilder.CreateButton(
                btnRow, "BtnVictoryPlayAgain", "ИГРАТЬ СНОВА",
                new Vector2(410f, 66f),
                () => GameManager.Instance?.StartNewGame(),
                fontSize: 24,
                baseTint: new Color(0.68f, 1.00f, 0.56f, 1f));

            BiopunkUiBuilder.CreateButton(
                btnRow, "BtnVictoryMainMenu", "В ГЛАВНОЕ МЕНЮ",
                new Vector2(410f, 66f),
                () => GameManager.Instance?.LoadMainMenu(),
                fontSize: 24,
                baseTint: new Color(0.80f, 0.92f, 0.98f, 1f));

            _victoryRoot.SetActive(false);
        }

        // =====================================================================
        // CONTEST SCREEN #5: ЭКРАН ПОРАЖЕНИЯ (GameState.Defeat)
        // =====================================================================

        private void BuildDefeatScreen(Transform parent)
        {
            RectTransform root = BiopunkUiBuilder.CreateStretchRect("DefeatScreenRoot", parent);
            _defeatRoot = root.gameObject;

            Image bg = BiopunkUiBuilder.CreateSpriteImage(
                root, "DefeatBgArt", "UI/Screen_DefeatBg",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, preserveAspect: false, raycastTarget: true);
            BiopunkUiBuilder.Stretch(bg.rectTransform);

            // Top Dramatic Defeat Banner
            Image topCard = BiopunkUiBuilder.CreatePanel(
                root, "DefeatHeaderCard",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -26f), new Vector2(1260f, 174f),
                new Color(1.00f, 0.78f, 0.78f, 0.94f), raycastTarget: false);

            Text header = BiopunkUiBuilder.CreateText(
                topCard.transform, "DefeatHeader",
                "КОЛОНИЯ ПАЛА — СЕРДЦЕ УГАСЛО",
                42, FontStyle.Bold, BiopunkUiBuilder.AccentDanger, TextAnchor.MiddleCenter);
            header.rectTransform.anchorMin = new Vector2(0.04f, 0.50f);
            header.rectTransform.anchorMax = new Vector2(0.96f, 0.92f);
            header.rectTransform.offsetMin = Vector2.zero;
            header.rectTransform.offsetMax = Vector2.zero;

            _defeatReasonText = BiopunkUiBuilder.CreateText(
                topCard.transform, "DefeatReason",
                "Живое Сердце Колонии уничтожено роем!",
                23, FontStyle.Bold, BiopunkUiBuilder.AccentAmber, TextAnchor.MiddleCenter);
            _defeatReasonText.rectTransform.anchorMin = new Vector2(0.05f, 0.10f);
            _defeatReasonText.rectTransform.anchorMax = new Vector2(0.95f, 0.48f);
            _defeatReasonText.rectTransform.offsetMin = Vector2.zero;
            _defeatReasonText.rectTransform.offsetMax = Vector2.zero;

            // Bottom Summary & Retry Panel
            Image summaryCard = BiopunkUiBuilder.CreatePanel(
                root, "DefeatSummaryPanel",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 24f), new Vector2(1240f, 418f),
                new Color(0.96f, 0.84f, 0.84f, 0.94f), raycastTarget: true);

            _defeatAdviceText = BiopunkUiBuilder.CreateText(
                summaryCard.transform, "TacticalAdvice", string.Empty,
                19, FontStyle.Italic, BiopunkUiBuilder.AccentCyan, TextAnchor.MiddleCenter);
            _defeatAdviceText.rectTransform.anchorMin = new Vector2(0.04f, 0.83f);
            _defeatAdviceText.rectTransform.anchorMax = new Vector2(0.96f, 0.96f);
            _defeatAdviceText.rectTransform.offsetMin = Vector2.zero;
            _defeatAdviceText.rectTransform.offsetMax = Vector2.zero;

            _defeatStatsLeftText = BiopunkUiBuilder.CreateText(
                summaryCard.transform, "DefeatStatsLeft", string.Empty,
                21, FontStyle.Normal, BiopunkUiBuilder.TextPrimary, TextAnchor.MiddleLeft);
            _defeatStatsLeftText.lineSpacing = 1.28f;
            _defeatStatsLeftText.rectTransform.anchorMin = new Vector2(0.06f, 0.27f);
            _defeatStatsLeftText.rectTransform.anchorMax = new Vector2(0.50f, 0.81f);
            _defeatStatsLeftText.rectTransform.offsetMin = Vector2.zero;
            _defeatStatsLeftText.rectTransform.offsetMax = Vector2.zero;

            _defeatStatsRightText = BiopunkUiBuilder.CreateText(
                summaryCard.transform, "DefeatStatsRight", string.Empty,
                21, FontStyle.Normal, BiopunkUiBuilder.TextPrimary, TextAnchor.MiddleLeft);
            _defeatStatsRightText.lineSpacing = 1.28f;
            _defeatStatsRightText.rectTransform.anchorMin = new Vector2(0.53f, 0.27f);
            _defeatStatsRightText.rectTransform.anchorMax = new Vector2(0.96f, 0.81f);
            _defeatStatsRightText.rectTransform.offsetMin = Vector2.zero;
            _defeatStatsRightText.rectTransform.offsetMax = Vector2.zero;

            RectTransform btnRow = BiopunkUiBuilder.CreateRect(
                "DefeatButtonsRow", summaryCard.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 20f), new Vector2(880f, 68f));

            HorizontalLayoutGroup hlg = btnRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 32f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            BiopunkUiBuilder.CreateButton(
                btnRow, "BtnDefeatRetry", "ПОПРОБОВАТЬ СНОВА",
                new Vector2(410f, 66f),
                () => GameManager.Instance?.StartNewGame(),
                fontSize: 24,
                baseTint: new Color(0.72f, 0.98f, 0.62f, 1f));

            BiopunkUiBuilder.CreateButton(
                btnRow, "BtnDefeatMainMenu", "В ГЛАВНОЕ МЕНЮ",
                new Vector2(410f, 66f),
                () => GameManager.Instance?.LoadMainMenu(),
                fontSize: 24,
                baseTint: new Color(0.92f, 0.84f, 0.82f, 1f));

            _defeatRoot.SetActive(false);
        }

        // =====================================================================
        // STATISTICS FORMATTING
        // =====================================================================

        private void PopulateVictoryStatistics()
        {
            GameManager gm = GameManager.InstanceIfExists;
            BuildColumnsText(gm, out string leftCol, out string rightCol);

            if (_victoryStatsLeftText != null) _victoryStatsLeftText.text = leftCol;
            if (_victoryStatsRightText != null) _victoryStatsRightText.text = rightCol;

            float coreRatio = 1f;
            if (BioGridManager.InstanceIfExists != null)
            {
                IDamageable coreDmg = BioGridManager.InstanceIfExists.GetDamageableAt(BioGridManager.InstanceIfExists.HeartCoreCenterGrid);
                if (coreDmg != null && coreDmg.MaxHp > 0f)
                {
                    coreRatio = coreDmg.CurrentHp / coreDmg.MaxHp;
                }
            }

            int miniGames = gm != null ? gm.MiniGamesCompleted : 0;
            float elapsed = gm != null ? gm.ElapsedPlayTime : 0f;

            string rankStr;
            if (coreRatio >= 0.72f && (miniGames >= 1 || elapsed <= 420f))
            {
                rankStr = "РАНГ [S] — ВЕРХОВНЫЙ БИО-АРХИТЕКТОР СИМБИОЗА";
                _victoryRankText.color = BiopunkUiBuilder.AccentLime;
            }
            else if (coreRatio >= 0.40f)
            {
                rankStr = "РАНГ [A] — СТАРШИЙ ХРАНИТЕЛЬ ЖИВОГО СЕРДЦА";
                _victoryRankText.color = BiopunkUiBuilder.AccentCyan;
            }
            else
            {
                rankStr = "РАНГ [B] — ВЫЖИВШИЙ ЗАЩИТНИК ПЕЩЕР";
                _victoryRankText.color = BiopunkUiBuilder.AccentAmber;
            }

            if (_victoryRankText != null) _victoryRankText.text = rankStr;
        }

        private void PopulateDefeatStatistics()
        {
            GameManager gm = GameManager.InstanceIfExists;
            BuildColumnsText(gm, out string leftCol, out string rightCol);

            if (_defeatStatsLeftText != null) _defeatStatsLeftText.text = leftCol;
            if (_defeatStatsRightText != null) _defeatStatsRightText.text = rightCol;

            string reason = (gm != null && !string.IsNullOrWhiteSpace(gm.DefeatReason))
                ? gm.DefeatReason
                : "Живое Сердце Колонии уничтожено роем!";
            if (_defeatReasonText != null)
            {
                _defeatReasonText.text = $"Причина гибели: {reason}";
            }

            if (_defeatAdviceText != null)
            {
                if (gm != null && gm.MiniGamesCompleted == 0)
                {
                    _defeatAdviceText.text = "Совет: Используйте [H] Дефибрилляцию Сердца для лечения Ядра и [M] Био-Синтез для ускорения роста x2!";
                }
                else if (gm != null && gm.StructuresBuilt < 4)
                {
                    _defeatAdviceText.text = "Совет: Перекрывайте туннели Костяными стенами и соединяйте турели Био-венами от Сердца!";
                }
                else
                {
                    _defeatAdviceText.text = "Совет: Против Панцирных крабов и Альфа-Матки стройте Желчные пушки и включайте Приоритет Турелей в [Space]!";
                }
            }
        }

        private static void BuildColumnsText(GameManager gm, out string left, out string right)
        {
            float sec = gm != null ? gm.ElapsedPlayTime : 0f;
            int mins = Mathf.FloorToInt(sec / 60f);
            int remSec = Mathf.FloorToInt(sec % 60f);

            int waves = gm != null ? gm.WavesSurvived : 0;
            float mut = gm != null ? gm.MutagenSynthesized : 0f;
            float bio = gm != null ? gm.TotalBiomassHarvested : 0f;
            int crops = gm != null ? gm.CropsHarvested : 0;
            int structs = gm != null ? gm.StructuresBuilt : 0;
            int kills = gm != null ? gm.EnemiesKilled : 0;
            int miniGames = gm != null ? gm.MiniGamesCompleted : 0;

            left =
                $"• Время миссии: <b>{mins:00}:{remSec:00}</b>\n" +
                $"• Отражено волн Улья: <color=#8CE844><b>{waves} / {BalanceDatabase.TotalWaves}</b></color>\n" +
                $"• Синтезировано Мутагена: <color=#F547E0><b>{mut:0.0} ед.</b></color>\n" +
                $"• Добыто Биомассы: <color=#59EB6B><b>{Mathf.RoundToInt(bio)}</b></color>";

            right =
                $"• Собрано био-культур: <b>{crops}</b>\n" +
                $"• Возведено построек и вен: <b>{structs}</b>\n" +
                $"• Уничтожено тварей роя: <color=#FAD040><b>{kills}</b></color>\n" +
                $"• Калибровок в мини-играх: <color=#38E5FF><b>{miniGames}</b></color>";
        }
    }
}
