using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using BioFarme.Audio;
using BioFarme.Core;

namespace BioFarme.UI
{
    /// <summary>
    /// Reusable handle to a programmatic biopunk progress bar.
    /// </summary>
    public sealed class BiopunkProgressBar
    {
        public RectTransform Root;
        public RectTransform FillRect;
        public Image BackgroundImage;
        public Image FillImage;
        public Text LabelText;
        public Text ValueText;

        public void SetProgress(float normalized01, string valueLabel = null, Color? fillColor = null)
        {
            float clamped = Mathf.Clamp01(normalized01);
            if (FillRect != null)
            {
                FillRect.anchorMin = Vector2.zero;
                FillRect.anchorMax = new Vector2(clamped, 1f);
                FillRect.offsetMin = Vector2.zero;
                FillRect.offsetMax = Vector2.zero;
            }

            if (fillColor.HasValue && FillImage != null)
            {
                FillImage.color = fillColor.Value;
            }

            if (valueLabel != null && ValueText != null)
            {
                ValueText.text = valueLabel;
            }
        }
    }

    /// <summary>
    /// Hover tooltip trigger attached to HUD slots, codex cards, and tactical buttons.
    /// </summary>
    public sealed class BiopunkTooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
    {
        public string Title;
        public Func<string> BodyProvider;
        public string StaticBody;

        public void OnPointerEnter(PointerEventData eventData)
        {
            string body = BodyProvider != null ? BodyProvider.Invoke() : StaticBody;
            BiopunkTooltipPopup.Show(Title, body, eventData.position);
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            BiopunkTooltipPopup.UpdatePosition(eventData.position);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            BiopunkTooltipPopup.Hide(this);
        }

        private void OnDisable()
        {
            BiopunkTooltipPopup.Hide(this);
        }
    }

    /// <summary>
    /// Screen-clamped floating biopunk tooltip box shared across UI canvases.
    /// </summary>
    public sealed class BiopunkTooltipPopup : MonoBehaviour
    {
        private static BiopunkTooltipPopup _activeInstance;

        private RectTransform _canvasRect;
        private RectTransform _boxRect;
        private CanvasGroup _group;
        private Text _titleText;
        private Text _bodyText;
        private bool _visible;

        public static BiopunkTooltipPopup AttachToCanvas(Canvas canvas)
        {
            if (canvas == null) return null;

            GameObject go = new GameObject("BiopunkTooltipPopup");
            go.transform.SetParent(canvas.transform, false);

            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(390f, 130f);

            BiopunkTooltipPopup popup = go.AddComponent<BiopunkTooltipPopup>();
            popup.Build(canvas.GetComponent<RectTransform>(), rt);
            _activeInstance = popup;
            return popup;
        }

        private void Build(RectTransform canvasRect, RectTransform rootRect)
        {
            _canvasRect = canvasRect;
            _boxRect = rootRect;

            _group = gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _group.alpha = 0f;

            Image bg = gameObject.AddComponent<Image>();
            bg.sprite = BiopunkUiBuilder.GetBeveledPanelSprite();
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.09f, 0.11f, 0.09f, 0.97f);
            bg.raycastTarget = false;

            VerticalLayoutGroup vlg = gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(16, 16, 12, 12);
            vlg.spacing = 6f;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            ContentSizeFitter csf = gameObject.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _titleText = BiopunkUiBuilder.CreateText(
                transform, "Title", string.Empty, 20, FontStyle.Bold,
                BiopunkUiBuilder.AccentLime, TextAnchor.MiddleLeft);
            _titleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _titleText.verticalOverflow = VerticalWrapMode.Overflow;

            _bodyText = BiopunkUiBuilder.CreateText(
                transform, "Body", string.Empty, 18, FontStyle.Normal,
                BiopunkUiBuilder.TextPrimary, TextAnchor.UpperLeft);
            _bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _bodyText.verticalOverflow = VerticalWrapMode.Overflow;

            gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            _activeInstance = this;
        }

        public static void Show(string title, string body, Vector2 screenPos)
        {
            if (_activeInstance == null || (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body)))
            {
                return;
            }

            _activeInstance.gameObject.SetActive(true);
            _activeInstance.transform.SetAsLastSibling();
            _activeInstance._visible = true;
            _activeInstance._group.alpha = 1f;

            _activeInstance._titleText.gameObject.SetActive(!string.IsNullOrWhiteSpace(title));
            _activeInstance._titleText.text = title ?? string.Empty;

            _activeInstance._bodyText.gameObject.SetActive(!string.IsNullOrWhiteSpace(body));
            _activeInstance._bodyText.text = body ?? string.Empty;

            LayoutRebuilder.ForceRebuildLayoutImmediate(_activeInstance._boxRect);
            UpdatePosition(screenPos);
        }

        public static void UpdatePosition(Vector2 screenPos)
        {
            if (_activeInstance == null || !_activeInstance._visible || _activeInstance._canvasRect == null)
            {
                return;
            }

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _activeInstance._canvasRect, screenPos, null, out Vector2 localPt))
            {
                Rect canvasBounds = _activeInstance._canvasRect.rect;
                Vector2 size = _activeInstance._boxRect.sizeDelta;
                if (size.y < 40f) size.y = _activeInstance._boxRect.rect.height;

                float x = localPt.x - canvasBounds.xMin + 20f;
                float y = localPt.y - canvasBounds.yMin - 18f;

                if (x + size.x > canvasBounds.width - 16f)
                {
                    x = Mathf.Max(16f, (localPt.x - canvasBounds.xMin) - size.x - 16f);
                }
                if (y - size.y < 16f)
                {
                    y = Mathf.Min(canvasBounds.height - 16f, size.y + 18f);
                }

                _activeInstance._boxRect.anchoredPosition = new Vector2(x, y);
            }
        }

        public static void Hide(BiopunkTooltipTrigger caller = null)
        {
            if (_activeInstance == null) return;
            _activeInstance._visible = false;
            _activeInstance._group.alpha = 0f;
            if (_activeInstance.gameObject.activeSelf)
            {
                _activeInstance.gameObject.SetActive(false);
            }
        }
    }

    /// <summary>
    /// Shared programmatic UI builder for "BioFarme: Сердце Колонии".
    /// Enforces 1920x1080 CanvasScaler, Cyrillic-safe builtin fonts, crisp pixel-art borders, and tactile audio feedback.
    /// </summary>
    public static class BiopunkUiBuilder
    {
        public static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

        // Earthy dark-biopunk palette (#1E221D charcoal-olive base + #5A6B47 chitin frame + #8CE844 bioluminescent accents)
        public static readonly Color PanelBackdropDim = new Color(0.04f, 0.06f, 0.05f, 0.80f);
        public static readonly Color PanelDarkBg = new Color(0.118f, 0.133f, 0.114f, 0.97f);      // #1E221D
        public static readonly Color PanelSectionBg = new Color(0.086f, 0.102f, 0.082f, 0.96f);   // #161A15
        public static readonly Color PanelRecessBg = new Color(0.059f, 0.071f, 0.059f, 0.96f);    // #0F120F
        public static readonly Color BorderOlive = new Color(0.353f, 0.420f, 0.278f, 1f);         // #5A6B47
        public static readonly Color BorderOliveBright = new Color(0.490f, 0.600f, 0.365f, 1f);
        public static readonly Color AccentLime = new Color(0.549f, 0.910f, 0.267f, 1f);          // #8CE844
        public static readonly Color AccentCyan = new Color(0.220f, 0.898f, 1.000f, 1f);          // #38E5FF
        public static readonly Color AccentMutagen = new Color(0.961f, 0.278f, 0.878f, 1f);       // #F547E0
        public static readonly Color AccentAmber = new Color(0.980f, 0.780f, 0.250f, 1f);
        public static readonly Color AccentDanger = new Color(1.000f, 0.310f, 0.270f, 1f);
        public static readonly Color TextPrimary = new Color(0.945f, 0.961f, 0.918f, 1f);
        public static readonly Color TextSecondary = new Color(0.745f, 0.796f, 0.698f, 1f);
        public static readonly Color TextOutlineDark = new Color(0.03f, 0.04f, 0.03f, 0.94f);

        private static Sprite _beveledPanelSprite;
        private static Sprite _buttonFrameSprite;
        private static Sprite _barTroughSprite;

        public static Font GetFont()
        {
            return WorldPopupManager.GetBuiltinFont();
        }

        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;

            EventSystem existing = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
            if (existing != null) return;

            GameObject esGo = new GameObject("[EventSystem]");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<StandaloneInputModule>();
        }

        public static Canvas CreateScreenCanvas(string name, Transform parent, int sortingOrder = 100)
        {
            EnsureEventSystem();

            GameObject canvasGo = new GameObject(name);
            if (parent != null)
            {
                canvasGo.transform.SetParent(parent, false);
            }

            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            canvas.pixelPerfect = false;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static RectTransform CreateRect(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 sizeDelta)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = sizeDelta;
            rt.localScale = Vector3.one;
            return rt;
        }

        public static RectTransform CreateStretchRect(
            string name,
            Transform parent,
            float padLeft = 0f,
            float padBottom = 0f,
            float padRight = 0f,
            float padTop = 0f)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            Stretch(rt, padLeft, padBottom, padRight, padTop);
            rt.localScale = Vector3.one;
            return rt;
        }

        public static void Stretch(
            RectTransform rt,
            float padLeft = 0f,
            float padBottom = 0f,
            float padRight = 0f,
            float padTop = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(padLeft, padBottom);
            rt.offsetMax = new Vector2(-padRight, -padTop);
        }

        public static Image CreatePanel(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPos,
            Vector2 sizeDelta,
            Color? tint = null,
            bool raycastTarget = true)
        {
            RectTransform rt = CreateRect(name, parent, anchorMin, anchorMax, pivot, anchoredPos, sizeDelta);
            Image img = rt.gameObject.AddComponent<Image>();
            img.sprite = GetBeveledPanelSprite();
            img.type = Image.Type.Sliced;
            img.color = tint ?? Color.white;
            img.raycastTarget = raycastTarget;
            return img;
        }

        public static Image CreateStretchPanel(
            Transform parent,
            string name,
            Color tint,
            float padLeft = 0f,
            float padBottom = 0f,
            float padRight = 0f,
            float padTop = 0f,
            bool slicedBevel = false,
            bool raycastTarget = true)
        {
            RectTransform rt = CreateStretchRect(name, parent, padLeft, padBottom, padRight, padTop);
            Image img = rt.gameObject.AddComponent<Image>();
            if (slicedBevel)
            {
                img.sprite = GetBeveledPanelSprite();
                img.type = Image.Type.Sliced;
            }
            else
            {
                img.sprite = SpriteRegistry.GetWhiteSquareSprite();
                img.type = Image.Type.Simple;
            }
            img.color = tint;
            img.raycastTarget = raycastTarget;
            return img;
        }

        public static Image CreateSpriteImage(
            Transform parent,
            string name,
            string spriteKey,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPos,
            Vector2 sizeDelta,
            bool preserveAspect = false,
            bool raycastTarget = false)
        {
            RectTransform rt = CreateRect(name, parent, anchorMin, anchorMax, pivot, anchoredPos, sizeDelta);
            Image img = rt.gameObject.AddComponent<Image>();
            Sprite sp = SpriteRegistry.Get(spriteKey);
            if (sp != null && sp.texture != null)
            {
                sp.texture.filterMode = FilterMode.Point;
            }
            img.sprite = sp;
            img.preserveAspect = preserveAspect;
            img.color = Color.white;
            img.raycastTarget = raycastTarget;
            return img;
        }

        public static Text CreateText(
            Transform parent,
            string name,
            string content,
            int fontSize = 20,
            FontStyle style = FontStyle.Normal,
            Color? color = null,
            TextAnchor alignment = TextAnchor.MiddleLeft,
            bool addOutline = true)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            RectTransform rt = go.AddComponent<RectTransform>();
            rt.localScale = Vector3.one;

            Text txt = go.AddComponent<Text>();
            txt.font = GetFont();
            txt.text = content ?? string.Empty;
            txt.fontSize = Mathf.Max(15, fontSize);
            txt.fontStyle = style;
            txt.color = color ?? TextPrimary;
            txt.alignment = alignment;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.supportRichText = true;
            txt.raycastTarget = false;

            if (addOutline)
            {
                Outline outline = go.AddComponent<Outline>();
                outline.effectColor = TextOutlineDark;
                outline.effectDistance = new Vector2(1.5f, -1.5f);
            }
            else
            {
                Shadow shadow = go.AddComponent<Shadow>();
                shadow.effectColor = TextOutlineDark;
                shadow.effectDistance = new Vector2(1.5f, -1.5f);
            }

            return txt;
        }

        public static Button CreateButton(
            Transform parent,
            string name,
            string label,
            Vector2 sizeDelta,
            Action onClick,
            int fontSize = 20,
            Color? baseTint = null,
            Color? textColor = null,
            Sprite iconSprite = null)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            RectTransform rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = sizeDelta;
            rt.localScale = Vector3.one;

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = sizeDelta.x;
            le.preferredHeight = sizeDelta.y;
            le.minHeight = Mathf.Min(34f, sizeDelta.y);

            Image bg = go.AddComponent<Image>();
            bg.sprite = GetButtonSprite();
            bg.type = Image.Type.Sliced;
            Color normalCol = baseTint ?? Color.white;
            bg.color = normalCol;
            bg.raycastTarget = true;

            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.18f, 1.24f, 1.12f, 1f);
            cb.pressedColor = new Color(0.76f, 0.84f, 0.72f, 1f);
            cb.selectedColor = new Color(1.08f, 1.16f, 1.05f, 1f);
            cb.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.72f);
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.07f;
            btn.colors = cb;

            Navigation nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;

            if (iconSprite != null)
            {
                float iconPad = Mathf.Clamp(sizeDelta.y - 16f, 22f, 44f);
                RectTransform iconRt = CreateRect(
                    "Icon", rt,
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(12f, 0f), new Vector2(iconPad, iconPad));
                Image iconImg = iconRt.gameObject.AddComponent<Image>();
                iconImg.sprite = iconSprite;
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;

                Text txt = CreateText(
                    rt, "Label", label, fontSize, FontStyle.Bold,
                    textColor ?? TextPrimary, TextAnchor.MiddleLeft);
                Stretch(txt.rectTransform, 18f + iconPad, 4f, 12f, 4f);
                txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            }
            else
            {
                Text txt = CreateText(
                    rt, "Label", label, fontSize, FontStyle.Bold,
                    textColor ?? TextPrimary, TextAnchor.MiddleCenter);
                Stretch(txt.rectTransform, 10f, 4f, 10f, 4f);
            }

            btn.onClick.AddListener(() =>
            {
                PlayClickSfx();
                onClick?.Invoke();
            });

            return btn;
        }

        public static BiopunkProgressBar CreateProgressBar(
            Transform parent,
            string name,
            Vector2 sizeDelta,
            Color fillColor,
            string initialValueText = "",
            int fontSize = 18)
        {
            GameObject rootGo = new GameObject(name);
            rootGo.transform.SetParent(parent, false);

            RectTransform rootRt = rootGo.AddComponent<RectTransform>();
            rootRt.sizeDelta = sizeDelta;
            rootRt.localScale = Vector3.one;

            LayoutElement le = rootGo.AddComponent<LayoutElement>();
            le.preferredWidth = sizeDelta.x;
            le.preferredHeight = sizeDelta.y;

            Image frameImg = rootGo.AddComponent<Image>();
            frameImg.sprite = GetBarTroughSprite();
            frameImg.type = Image.Type.Sliced;
            frameImg.color = Color.white;
            frameImg.raycastTarget = false;

            RectTransform innerContainer = CreateStretchRect("Inner", rootRt, 4f, 4f, 4f, 4f);

            RectTransform fillRt = CreateRect(
                "Fill", innerContainer,
                Vector2.zero, Vector2.one, new Vector2(0f, 0.5f),
                Vector2.zero, Vector2.zero);

            Image fillImg = fillRt.gameObject.AddComponent<Image>();
            fillImg.sprite = SpriteRegistry.GetWhiteSquareSprite();
            fillImg.type = Image.Type.Simple;
            fillImg.color = fillColor;
            fillImg.raycastTarget = false;

            // Subtle top specular strip on fill bar for pixel-art depth
            RectTransform glossRt = CreateRect(
                "Gloss", fillRt,
                new Vector2(0f, 0.62f), Vector2.one, new Vector2(0.5f, 1f),
                Vector2.zero, Vector2.zero);
            Image glossImg = glossRt.gameObject.AddComponent<Image>();
            glossImg.sprite = SpriteRegistry.GetWhiteSquareSprite();
            glossImg.color = new Color(1f, 1f, 1f, 0.20f);
            glossImg.raycastTarget = false;

            Text valTxt = CreateText(
                rootRt, "ValueText", initialValueText, fontSize, FontStyle.Bold,
                TextPrimary, TextAnchor.MiddleCenter);
            Stretch(valTxt.rectTransform, 8f, 0f, 8f, 0f);
            valTxt.horizontalOverflow = HorizontalWrapMode.Overflow;

            return new BiopunkProgressBar
            {
                Root = rootRt,
                FillRect = fillRt,
                BackgroundImage = frameImg,
                FillImage = fillImg,
                ValueText = valTxt
            };
        }

        public static Slider CreateLabeledSlider(
            Transform parent,
            string name,
            string labelRu,
            float initialValue01,
            Color fillColor,
            Action<float> onValueChanged)
        {
            GameObject rowGo = new GameObject(name);
            rowGo.transform.SetParent(parent, false);
            RectTransform rowRt = rowGo.AddComponent<RectTransform>();
            rowRt.sizeDelta = new Vector2(460f, 64f);

            LayoutElement rowLe = rowGo.AddComponent<LayoutElement>();
            rowLe.preferredHeight = 64f;
            rowLe.flexibleWidth = 1f;

            Text titleTxt = CreateText(
                rowRt, "Label", labelRu, 20, FontStyle.Bold, TextPrimary, TextAnchor.MiddleLeft);
            titleTxt.rectTransform.anchorMin = new Vector2(0f, 0.52f);
            titleTxt.rectTransform.anchorMax = new Vector2(0.75f, 1f);
            titleTxt.rectTransform.offsetMin = Vector2.zero;
            titleTxt.rectTransform.offsetMax = Vector2.zero;

            Text pctTxt = CreateText(
                rowRt, "Percent", $"{Mathf.RoundToInt(initialValue01 * 100f)}%", 20, FontStyle.Bold,
                AccentLime, TextAnchor.MiddleRight);
            pctTxt.rectTransform.anchorMin = new Vector2(0.75f, 0.52f);
            pctTxt.rectTransform.anchorMax = new Vector2(1f, 1f);
            pctTxt.rectTransform.offsetMin = Vector2.zero;
            pctTxt.rectTransform.offsetMax = Vector2.zero;

            RectTransform sliderRt = CreateRect(
                "Slider", rowRt,
                new Vector2(0f, 0f), new Vector2(1f, 0.46f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            sliderRt.offsetMin = new Vector2(0f, 2f);
            sliderRt.offsetMax = new Vector2(0f, -2f);

            Image bgImg = sliderRt.gameObject.AddComponent<Image>();
            bgImg.sprite = GetBarTroughSprite();
            bgImg.type = Image.Type.Sliced;
            bgImg.color = Color.white;

            RectTransform fillArea = CreateStretchRect("FillArea", sliderRt, 6f, 5f, 6f, 5f);
            RectTransform fillRt = CreateRect("Fill", fillArea, Vector2.zero, Vector2.one, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            Image fillImg = fillRt.gameObject.AddComponent<Image>();
            fillImg.sprite = SpriteRegistry.GetWhiteSquareSprite();
            fillImg.color = fillColor;

            RectTransform handleArea = CreateStretchRect("HandleSlideArea", sliderRt, 10f, 0f, 10f, 0f);
            RectTransform handleRt = CreateRect(
                "Handle", handleArea,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(20f, 30f));
            Image handleImg = handleRt.gameObject.AddComponent<Image>();
            handleImg.sprite = GetButtonSprite();
            handleImg.type = Image.Type.Sliced;
            handleImg.color = AccentLime;

            Slider slider = sliderRt.gameObject.AddComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.SetValueWithoutNotify(Mathf.Clamp01(initialValue01));

            slider.onValueChanged.AddListener(val =>
            {
                pctTxt.text = $"{Mathf.RoundToInt(val * 100f)}%";
                onValueChanged?.Invoke(val);
            });

            return slider;
        }

        public static BiopunkTooltipTrigger AttachTooltip(GameObject target, string title, string staticBody)
        {
            if (target == null) return null;
            BiopunkTooltipTrigger trigger = target.GetComponent<BiopunkTooltipTrigger>();
            if (trigger == null) trigger = target.AddComponent<BiopunkTooltipTrigger>();
            trigger.Title = title;
            trigger.StaticBody = staticBody;
            trigger.BodyProvider = null;
            return trigger;
        }

        public static BiopunkTooltipTrigger AttachDynamicTooltip(GameObject target, string title, Func<string> bodyProvider)
        {
            if (target == null) return null;
            BiopunkTooltipTrigger trigger = target.GetComponent<BiopunkTooltipTrigger>();
            if (trigger == null) trigger = target.AddComponent<BiopunkTooltipTrigger>();
            trigger.Title = title;
            trigger.BodyProvider = bodyProvider;
            return trigger;
        }

        public static void PlayClickSfx()
        {
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySfx(AudioKeys.SfxUiClick, 0.80f, 0.03f);
            }
        }

        // --- Crisp 9-Sliced Pixel-Art Procedural UI Sprites (FilterMode.Point) ---

        public static Sprite GetBeveledPanelSprite()
        {
            if (_beveledPanelSprite != null) return _beveledPanelSprite;

            const int size = 32;
            Color[] px = new Color[size * size];

            Color outerBlack = new Color(0.05f, 0.06f, 0.05f, 0.99f);
            Color chitinRim = new Color(0.353f, 0.420f, 0.278f, 0.99f);      // #5A6B47
            Color chitinHighlight = new Color(0.510f, 0.620f, 0.380f, 0.99f);
            Color chitinShadow = new Color(0.200f, 0.240f, 0.165f, 0.99f);
            Color innerGroove = new Color(0.08f, 0.09f, 0.08f, 0.98f);
            Color centerBody = new Color(0.118f, 0.133f, 0.114f, 0.97f);     // #1E221D
            Color cornerMoss = new Color(0.45f, 0.72f, 0.26f, 1.00f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int distEdge = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y));
                    bool isCornerCut = (x == 0 || x == size - 1) && (y == 0 || y == size - 1);
                    if (isCornerCut)
                    {
                        px[y * size + x] = Color.clear;
                        continue;
                    }

                    if (distEdge == 0 || distEdge == 1)
                    {
                        px[y * size + x] = outerBlack;
                    }
                    else if (distEdge >= 2 && distEdge <= 4)
                    {
                        bool cornerBolt = (x <= 5 || x >= size - 6) && (y <= 5 || y >= size - 6);
                        if (cornerBolt && distEdge == 3)
                        {
                            px[y * size + x] = cornerMoss;
                        }
                        else if (y >= size - 4 || x <= 3)
                        {
                            px[y * size + x] = chitinHighlight;
                        }
                        else if (y <= 3 || x >= size - 4)
                        {
                            px[y * size + x] = chitinShadow;
                        }
                        else
                        {
                            px[y * size + x] = chitinRim;
                        }
                    }
                    else if (distEdge == 5)
                    {
                        px[y * size + x] = innerGroove;
                    }
                    else
                    {
                        px[y * size + x] = centerBody;
                    }
                }
            }

            _beveledPanelSprite = BakeSlicedSprite(px, size, size, new Vector4(8, 8, 8, 8), "UI_Generated_BeveledPanel");
            return _beveledPanelSprite;
        }

        public static Sprite GetButtonSprite()
        {
            if (_buttonFrameSprite != null) return _buttonFrameSprite;

            const int size = 24;
            Color[] px = new Color[size * size];

            Color rimDark = new Color(0.06f, 0.08f, 0.06f, 1f);
            Color topEdge = new Color(0.46f, 0.56f, 0.36f, 1f);
            Color botEdge = new Color(0.18f, 0.22f, 0.15f, 1f);
            Color sideEdge = new Color(0.34f, 0.42f, 0.27f, 1f);
            Color bodyTop = new Color(0.19f, 0.23f, 0.18f, 0.98f);
            Color bodyBot = new Color(0.14f, 0.17f, 0.14f, 0.98f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if ((x == 0 || x == size - 1) && (y == 0 || y == size - 1))
                    {
                        px[y * size + x] = Color.clear;
                        continue;
                    }

                    int dist = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y));
                    if (dist == 0)
                    {
                        px[y * size + x] = rimDark;
                    }
                    else if (dist <= 2)
                    {
                        if (y >= size - 3) px[y * size + x] = topEdge;
                        else if (y <= 2) px[y * size + x] = botEdge;
                        else px[y * size + x] = sideEdge;
                    }
                    else
                    {
                        px[y * size + x] = y >= size / 2 ? bodyTop : bodyBot;
                    }
                }
            }

            _buttonFrameSprite = BakeSlicedSprite(px, size, size, new Vector4(6, 6, 6, 6), "UI_Generated_Button");
            return _buttonFrameSprite;
        }

        public static Sprite GetBarTroughSprite()
        {
            if (_barTroughSprite != null) return _barTroughSprite;

            const int size = 20;
            Color[] px = new Color[size * size];

            Color outer = new Color(0.05f, 0.06f, 0.05f, 1f);
            Color bevel = new Color(0.33f, 0.39f, 0.26f, 1f);
            Color trough = new Color(0.07f, 0.08f, 0.07f, 0.97f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int dist = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y));
                    if (dist == 0) px[y * size + x] = outer;
                    else if (dist <= 2) px[y * size + x] = bevel;
                    else px[y * size + x] = trough;
                }
            }

            _barTroughSprite = BakeSlicedSprite(px, size, size, new Vector4(5, 5, 5, 5), "UI_Generated_BarTrough");
            return _barTroughSprite;
        }

        private static Sprite BakeSlicedSprite(Color[] pixels, int w, int h, Vector4 border, string name)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = name
            };
            tex.SetPixels(pixels);
            tex.Apply(false, false);

            Sprite sp = Sprite.Create(
                tex,
                new Rect(0f, 0f, w, h),
                new Vector2(0.5f, 0.5f),
                100f,
                0u,
                SpriteMeshType.FullRect,
                border);
            sp.name = name;
            return sp;
        }
    }
}
