using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using BioFarme.Data;

namespace BioFarme.Core
{
    /// <summary>
    /// Individual pooled floating world-space text item animated by WorldPopupManager.
    /// </summary>
    public sealed class FloatingTextPopup : MonoBehaviour
    {
        private RectTransform _rectTransform;
        private Text _uiText;
        private CanvasGroup _canvasGroup;

        private Vector3 _startPos;
        private Vector3 _velocity;
        private float _elapsed;
        private float _duration;
        private float _baseScale;
        private bool _active;

        public bool IsActive => _active;

        public void InitializeComponents(RectTransform rect, Text text, CanvasGroup group)
        {
            _rectTransform = rect;
            _uiText = text;
            _canvasGroup = group;
        }

        public void Activate(Vector2 worldPos, string content, Color color, float scale, bool isCrit)
        {
            _active = true;
            _elapsed = 0f;
            _duration = isCrit ? 1.15f : 0.90f;
            _baseScale = 0.014f * Mathf.Clamp(scale, 0.5f, 2.5f) * (isCrit ? 1.28f : 1f);

            float jitterX = UnityEngine.Random.Range(-0.22f, 0.22f);
            float jitterY = UnityEngine.Random.Range(0.10f, 0.28f);
            _startPos = new Vector3(worldPos.x + jitterX, worldPos.y + jitterY, -0.5f);
            _velocity = new Vector3(UnityEngine.Random.Range(-0.25f, 0.25f), isCrit ? 1.55f : 1.15f, 0f);

            if (_uiText != null)
            {
                _uiText.text = content;
                _uiText.color = color;
                _uiText.fontStyle = isCrit ? FontStyle.BoldAndItalic : FontStyle.Bold;
            }

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
            }

            transform.position = _startPos;
            transform.localScale = Vector3.one * (_baseScale * 0.6f);
            gameObject.SetActive(true);
        }

        public bool TickPopup(float dt)
        {
            if (!_active) return false;

            _elapsed += dt;
            float t = Mathf.Clamp01(_elapsed / _duration);

            // Ease-out upward float
            _velocity.y = Mathf.Max(0.25f, _velocity.y - dt * 1.35f);
            transform.position += _velocity * dt;

            // Punch scale at start, settle to 1.0
            float punch = t < 0.18f
                ? Mathf.Lerp(0.65f, 1.22f, t / 0.18f)
                : Mathf.Lerp(1.22f, 0.95f, (t - 0.18f) / 0.82f);
            transform.localScale = Vector3.one * (_baseScale * punch);

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = t < 0.60f ? 1f : Mathf.Clamp01(1f - (t - 0.60f) / 0.40f);
            }

            if (_elapsed >= _duration)
            {
                Deactivate();
                return false;
            }

            return true;
        }

        public void Deactivate()
        {
            _active = false;
            gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Pooled world-space popup spawner for damage numbers ("21", "545", "255") and resource pickups ("+12 Биомасса").
    /// Also exposes Cyrillic-ready runtime Font and TMP_FontAsset helpers for WebGL & Linux compatibility.
    /// </summary>
    public sealed class WorldPopupManager : MonoBehaviour
    {
        private const int InitialPoolCapacity = 32;
        private const int MaxActivePopups = 96;

        private static WorldPopupManager _instance;
        private static bool _applicationQuitting;
        private static Font _cachedBuiltinFont;
        private static TMP_FontAsset _cachedTmpFont;

        public static WorldPopupManager Instance
        {
            get
            {
                if (_applicationQuitting) return _instance;
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<WorldPopupManager>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("[WorldPopupManager]");
                        _instance = go.AddComponent<WorldPopupManager>();
                    }
                }
                return _instance;
            }
        }

        private Canvas _worldCanvas;
        private readonly Queue<FloatingTextPopup> _inactivePool = new Queue<FloatingTextPopup>(InitialPoolCapacity);
        private readonly List<FloatingTextPopup> _activePopups = new List<FloatingTextPopup>(InitialPoolCapacity);

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            SetupWorldCanvas();
            PrewarmPool(InitialPoolCapacity);
        }

        private void OnEnable()
        {
            GameEvents.OnWorldPopupRequested += HandlePopupRequested;
        }

        private void OnDisable()
        {
            GameEvents.OnWorldPopupRequested -= HandlePopupRequested;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void OnApplicationQuit()
        {
            _applicationQuitting = true;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;

            for (int i = _activePopups.Count - 1; i >= 0; i--)
            {
                FloatingTextPopup popup = _activePopups[i];
                if (popup == null)
                {
                    _activePopups.RemoveAt(i);
                    continue;
                }

                if (!popup.TickPopup(dt))
                {
                    _activePopups.RemoveAt(i);
                    _inactivePool.Enqueue(popup);
                }
            }
        }

        private void SetupWorldCanvas()
        {
            GameObject canvasGo = new GameObject("WorldPopupCanvas");
            canvasGo.transform.SetParent(transform, false);

            _worldCanvas = canvasGo.AddComponent<Canvas>();
            _worldCanvas.renderMode = RenderMode.WorldSpace;
            _worldCanvas.sortingOrder = 500;

            RectTransform rt = canvasGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(100f, 100f);
            rt.position = Vector3.zero;
            rt.localScale = Vector3.one;
        }

        private void PrewarmPool(int count)
        {
            for (int i = 0; i < count; i++)
            {
                FloatingTextPopup item = CreatePopupInstance();
                item.Deactivate();
                _inactivePool.Enqueue(item);
            }
        }

        private FloatingTextPopup CreatePopupInstance()
        {
            GameObject go = new GameObject("FloatingPopup");
            go.transform.SetParent(_worldCanvas.transform, false);

            RectTransform rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(360f, 80f);

            CanvasGroup group = go.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            Text uiText = go.AddComponent<Text>();
            uiText.font = GetBuiltinFont();
            uiText.fontSize = 36;
            uiText.alignment = TextAnchor.MiddleCenter;
            uiText.horizontalOverflow = HorizontalWrapMode.Overflow;
            uiText.verticalOverflow = VerticalWrapMode.Overflow;
            uiText.raycastTarget = false;

            Outline outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0.05f, 0.02f, 0.08f, 0.95f);
            outline.effectDistance = new Vector2(2f, -2f);

            FloatingTextPopup popup = go.AddComponent<FloatingTextPopup>();
            popup.InitializeComponents(rect, uiText, group);
            return popup;
        }

        private void HandlePopupRequested(Vector2 worldPos, string text, Color color, float scale, bool isCrit)
        {
            SpawnInternal(worldPos, text, color, scale, isCrit);
        }

        private void SpawnInternal(Vector2 worldPos, string text, Color color, float scale, bool isCrit)
        {
            if (string.IsNullOrEmpty(text) || _worldCanvas == null) return;

            FloatingTextPopup popup;
            if (_inactivePool.Count > 0)
            {
                popup = _inactivePool.Dequeue();
            }
            else if (_activePopups.Count >= MaxActivePopups)
            {
                // Recycle oldest active popup during intense swarms
                popup = _activePopups[0];
                _activePopups.RemoveAt(0);
            }
            else
            {
                popup = CreatePopupInstance();
            }

            popup.Activate(worldPos, text, color, scale, isCrit);
            _activePopups.Add(popup);
        }

        // --- Static Convenience API for All Gameplay Systems ---

        public static void SpawnDamage(Vector2 worldPos, float damage, bool isCrit = false)
        {
            if (Instance == null) return;
            int dmgInt = Mathf.Max(1, Mathf.RoundToInt(damage));
            Color col = isCrit
                ? new Color(1.0f, 0.85f, 0.20f, 1f)
                : new Color(1.0f, 0.42f, 0.36f, 1f);
            string label = isCrit ? $"{dmgInt}!" : dmgInt.ToString();
            Instance.SpawnInternal(worldPos, label, col, isCrit ? 1.25f : 1.0f, isCrit);
        }

        public static void SpawnHeal(Vector2 worldPos, float healAmount)
        {
            if (Instance == null) return;
            int healInt = Mathf.Max(1, Mathf.RoundToInt(healAmount));
            Instance.SpawnInternal(worldPos, $"+{healInt}", new Color(0.38f, 1.0f, 0.58f, 1f), 0.95f, false);
        }

        public static void SpawnResource(Vector2 worldPos, string label, Color color)
        {
            if (Instance == null) return;
            Instance.SpawnInternal(worldPos, label, color, 1.0f, false);
        }

        public static void SpawnResourceGain(Vector2 worldPos, ResourceType resourceType, float amount)
        {
            if (Instance == null) return;
            string sign = amount >= 0f ? "+" : string.Empty;
            string formatted = resourceType == ResourceType.Mutagen ? $"{amount:0.#}" : $"{Mathf.RoundToInt(amount)}";
            string text = $"{sign}{formatted} {BalanceDatabase.GetResourceNameRu(resourceType)}";
            Instance.SpawnInternal(worldPos, text, BalanceDatabase.GetResourceColor(resourceType), 1.05f, false);
        }

        public static void SpawnText(Vector2 worldPos, string text, Color color, float scale = 1f, bool isCrit = false)
        {
            if (Instance == null) return;
            Instance.SpawnInternal(worldPos, text, color, scale, isCrit);
        }

        // --- Shared Cyrillic-Safe Font Providers ---

        public static Font GetBuiltinFont()
        {
            if (_cachedBuiltinFont != null) return _cachedBuiltinFont;

            // 1. Embedded Cyrillic-complete TrueType font (Assets/Resources/Fonts/LiberationSans.ttf)
            //    Mandatory for WebGL & Linux builds where OS system fonts are not accessible.
            _cachedBuiltinFont = Resources.Load<Font>("Fonts/LiberationSans");
            if (_cachedBuiltinFont == null)
            {
                _cachedBuiltinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            if (_cachedBuiltinFont == null)
            {
                _cachedBuiltinFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            if (_cachedBuiltinFont == null)
            {
                _cachedBuiltinFont = Font.CreateDynamicFontFromOSFont("Arial", 24);
            }
            return _cachedBuiltinFont;
        }

        public static TMP_FontAsset GetSharedTmpFont()
        {
            if (_cachedTmpFont != null) return _cachedTmpFont;

            Font baseFont = GetBuiltinFont();
            if (baseFont != null)
            {
                _cachedTmpFont = TMP_FontAsset.CreateFontAsset(baseFont);
                if (_cachedTmpFont != null)
                {
                    _cachedTmpFont.name = "BioFarme_DynamicCyrillicTMP";
                }
            }
            return _cachedTmpFont;
        }
    }
}
