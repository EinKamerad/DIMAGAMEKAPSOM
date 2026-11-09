using UnityEngine;
using UnityEngine.UI;
using BioFarme.Core;
using BioFarme.Data;

namespace BioFarme.UI
{
    /// <summary>
    /// Tactical Pause Overlay shown when GameManager.Instance.IsTacticalPaused ([Space]).
    /// Renders non-blocking glowing cyan-lime tactical perimeter lines (keeping world grid clickable!)
    /// plus a compact command bar with planning hints and 1-click Vein Network Priority buttons.
    /// </summary>
    public sealed class TacticalOverlayUI : MonoBehaviour
    {
        private RectTransform _overlayRoot;
        private readonly Image[] _borderEdges = new Image[4];
        private readonly Button[] _priorityButtons = new Button[4];

        public void Build(Transform parent)
        {
            _overlayRoot = BiopunkUiBuilder.CreateStretchRect("TacticalPauseOverlay", parent);

            // 4 Non-blocking glowing perimeter borders (raycastTarget = false so building & minimap clicks pass right through!)
            _borderEdges[0] = CreatePerimeterBar(_overlayRoot, "TopEdge", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 5f));
            _borderEdges[1] = CreatePerimeterBar(_overlayRoot, "BottomEdge", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 5f));
            _borderEdges[2] = CreatePerimeterBar(_overlayRoot, "LeftEdge", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(5f, 0f));
            _borderEdges[3] = CreatePerimeterBar(_overlayRoot, "RightEdge", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(5f, 0f));

            // Compact Tactical Command Bar positioned cleanly above the bottom hotbar (y=206)
            Image cmdPanel = BiopunkUiBuilder.CreatePanel(
                _overlayRoot, "TacticalCommandPanel",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 206f), new Vector2(936f, 108f),
                new Color(0.76f, 0.96f, 1.00f, 0.96f), raycastTarget: true);

            Text header = BiopunkUiBuilder.CreateText(
                cmdPanel.transform, "TacticalHeader",
                "[||] ТАКТИЧЕСКАЯ ПАУЗА — ПЛАНИРУЙТЕ ОБОРОНУ, КЛИКАЙТЕ ПО МИНИ-КАРТЕ И НАСТРАИВАЙТЕ ПОТОК ВЕН",
                18, FontStyle.Bold, BiopunkUiBuilder.AccentCyan, TextAnchor.MiddleCenter);
            header.rectTransform.anchorMin = new Vector2(0.02f, 0.56f);
            header.rectTransform.anchorMax = new Vector2(0.98f, 0.94f);
            header.rectTransform.offsetMin = Vector2.zero;
            header.rectTransform.offsetMax = Vector2.zero;

            RectTransform buttonsRow = BiopunkUiBuilder.CreateRect(
                "TacticalPriorityRow", cmdPanel.transform,
                new Vector2(0.5f, 0.28f), new Vector2(0.5f, 0.28f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(904f, 42f));

            HorizontalLayoutGroup hlg = buttonsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            (NetworkPriorityMode mode, string label)[] items =
            {
                (NetworkPriorityMode.Balanced, "Сеть: Баланс"),
                (NetworkPriorityMode.PrioritizeTurrets, "Приоритет: Турели"),
                (NetworkPriorityMode.PrioritizeReactors, "Приоритет: Синтез"),
                (NetworkPriorityMode.PrioritizeCore, "Приоритет: Ядро")
            };

            for (int i = 0; i < items.Length; i++)
            {
                NetworkPriorityMode captured = items[i].mode;
                _priorityButtons[i] = BiopunkUiBuilder.CreateButton(
                    buttonsRow, $"BtnTacPrio_{captured}", items[i].label,
                    new Vector2(170f, 40f),
                    () =>
                    {
                        ResourceManager.Instance?.SetNetworkPriority(captured);
                        RefreshPriorityHighlights();
                    },
                    fontSize: 16);
            }

            BiopunkUiBuilder.CreateButton(
                buttonsRow, "BtnResumeTactical", "► В БОЙ [Space]",
                new Vector2(176f, 40f),
                () => GameManager.Instance?.ToggleTacticalPause(),
                fontSize: 17,
                baseTint: new Color(0.68f, 1.00f, 0.56f, 1f));

            _overlayRoot.gameObject.SetActive(false);
        }

        private static Image CreatePerimeterBar(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 sizeDelta)
        {
            RectTransform rt = BiopunkUiBuilder.CreateRect(name, parent, anchorMin, anchorMax, pivot, Vector2.zero, sizeDelta);
            Image img = rt.gameObject.AddComponent<Image>();
            img.sprite = SpriteRegistry.GetWhiteSquareSprite();
            img.color = new Color(0.28f, 0.94f, 1.00f, 0.72f);
            img.raycastTarget = false;
            return img;
        }

        private void OnEnable()
        {
            GameEvents.OnTacticalPauseChanged += HandleTacticalPauseChanged;
            GameEvents.OnGameStateChanged += HandleGameStateChanged;
            GameEvents.OnNetworkPriorityChanged += HandleNetworkPriorityChanged;
        }

        private void OnDisable()
        {
            GameEvents.OnTacticalPauseChanged -= HandleTacticalPauseChanged;
            GameEvents.OnGameStateChanged -= HandleGameStateChanged;
            GameEvents.OnNetworkPriorityChanged -= HandleNetworkPriorityChanged;
        }

        private void Update()
        {
            if (_overlayRoot == null || !_overlayRoot.gameObject.activeSelf) return;

            float alpha = 0.45f + 0.35f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3.2f));
            Color pulseCol = new Color(0.28f, 0.94f, 1.00f, alpha);
            for (int i = 0; i < _borderEdges.Length; i++)
            {
                if (_borderEdges[i] != null) _borderEdges[i].color = pulseCol;
            }
        }

        private void HandleTacticalPauseChanged(bool isTacticalPaused)
        {
            SyncVisibility();
        }

        private void HandleGameStateChanged(GameState prev, GameState next)
        {
            SyncVisibility();
        }

        private void HandleNetworkPriorityChanged(NetworkPriorityMode mode)
        {
            if (_overlayRoot != null && _overlayRoot.gameObject.activeSelf)
            {
                RefreshPriorityHighlights();
            }
        }

        public void SyncVisibility()
        {
            if (_overlayRoot == null) return;
            bool show = GameManager.InstanceIfExists != null && GameManager.InstanceIfExists.IsTacticalPaused;
            if (_overlayRoot.gameObject.activeSelf != show)
            {
                _overlayRoot.gameObject.SetActive(show);
            }
            if (show)
            {
                RefreshPriorityHighlights();
            }
        }

        private void RefreshPriorityHighlights()
        {
            NetworkPriorityMode active = ResourceManager.InstanceIfExists != null
                ? ResourceManager.InstanceIfExists.CurrentPriority
                : NetworkPriorityMode.Balanced;

            for (int i = 0; i < _priorityButtons.Length; i++)
            {
                if (_priorityButtons[i] != null && _priorityButtons[i].image != null)
                {
                    _priorityButtons[i].image.color = ((int)active == i)
                        ? new Color(0.58f, 1.00f, 0.88f, 1f)
                        : new Color(0.82f, 0.88f, 0.90f, 0.92f);
                }
            }
        }
    }
}
