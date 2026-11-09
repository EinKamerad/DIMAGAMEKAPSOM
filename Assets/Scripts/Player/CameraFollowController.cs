using UnityEngine;
using UnityEngine.EventSystems;
using BioFarme.Core;
using BioFarme.Data;
using BioFarme.Grid;

namespace BioFarme.Player
{
    /// <summary>
    /// Smooth 2D orthographic camera controller for the 36x24 subterranean cavern.
    /// Follows PlayerController during GameState.Playing, supports Z/X, +/-, and Ctrl+Scroll zoom (4.2..11.0),
    /// and allows free WASD/Arrow tactical inspection panning during GameState.TacticalPause.
    /// </summary>
    [DefaultExecutionOrder(40)]
    public sealed class CameraFollowController : MonoBehaviour
    {
        public const float DefaultOrthoSize = 6.2f;
        public const float MinOrthoSize = 4.2f;
        public const float MaxOrthoSize = 11.0f;

        private static CameraFollowController _instance;
        private static bool _applicationQuitting;

        public static CameraFollowController InstanceIfExists => _instance;

        public static CameraFollowController Instance
        {
            get
            {
                if (_applicationQuitting) return _instance;
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<CameraFollowController>();
                    if (_instance == null)
                    {
                        Camera mainCam = Camera.main;
                        if (mainCam != null)
                        {
                            _instance = mainCam.gameObject.AddComponent<CameraFollowController>();
                        }
                        else
                        {
                            GameObject camGo = new GameObject("Main Camera");
                            camGo.tag = "MainCamera";
                            Camera createdCam = camGo.AddComponent<Camera>();
                            camGo.AddComponent<AudioListener>();
                            ConfigureCameraDefaults(createdCam);
                            _instance = camGo.AddComponent<CameraFollowController>();
                        }
                    }
                }
                return _instance;
            }
        }

        [Header("Follow & Pan Settings")]
        [SerializeField] private float _followSharpness = 8.5f;
        [SerializeField] private float _aimLookAheadDistance = 0.42f;
        [SerializeField] private float _tacticalPanSpeed = 14.5f;
        [SerializeField] private float _zoomSharpness = 12.0f;
        [SerializeField] private float _keyZoomSpeed = 5.5f;
        [SerializeField] private float _scrollZoomStep = 1.6f;

        private Camera _camera;
        private float _targetOrthoSize = DefaultOrthoSize;
        private Vector2 _currentLogicalPos = new Vector2(18.5f, 8.5f);
        private Vector2 _tacticalPanPos = new Vector2(18.5f, 8.5f);
        private bool _wasTacticalPaused;

        // Subtle screen trauma / shake for shockwaves & heavy impacts
        private float _shakeTrauma;
        private float _shakeSeed;

        private Color _baseBackgroundColor = new Color(0.06f, 0.04f, 0.08f, 1f);

        public Camera TargetCamera => _camera != null ? _camera : (_camera = ResolveCamera());
        public float CurrentOrthoSize => TargetCamera != null ? TargetCamera.orthographicSize : _targetOrthoSize;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }

            _instance = this;
            _shakeSeed = Random.Range(0f, 100f);

            _camera = ResolveCamera();
            if (_camera != null)
            {
                ConfigureCameraDefaults(_camera);
            }

            _targetOrthoSize = DefaultOrthoSize;
            _currentLogicalPos = new Vector2(18.5f, 8.5f);
            _tacticalPanPos = _currentLogicalPos;
            ApplyCameraTransform(_currentLogicalPos, Vector2.zero);
        }

        private void OnEnable()
        {
            GameEvents.OnNewGameSessionInitialized += HandleNewSessionInitialized;
            GameEvents.OnGameStateChanged += HandleGameStateChanged;
            GameEvents.OnCoreDefibShockwave += HandleCoreDefibShockwave;
            GameEvents.OnCoreHealthChanged += HandleCoreHealthChanged;
        }

        private void OnDisable()
        {
            GameEvents.OnNewGameSessionInitialized -= HandleNewSessionInitialized;
            GameEvents.OnGameStateChanged -= HandleGameStateChanged;
            GameEvents.OnCoreDefibShockwave -= HandleCoreDefibShockwave;
            GameEvents.OnCoreHealthChanged -= HandleCoreHealthChanged;
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

        private Camera ResolveCamera()
        {
            Camera own = GetComponent<Camera>();
            if (own != null) return own;

            Camera main = Camera.main;
            if (main != null) return main;

            Camera any = FindFirstObjectByType<Camera>();
            return any;
        }

        private static void ConfigureCameraDefaults(Camera cam)
        {
            if (cam == null) return;
            cam.orthographic = true;
            cam.orthographicSize = DefaultOrthoSize;
            cam.nearClipPlane = -20f;
            cam.farClipPlane = 50f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.04f, 0.08f, 1f);
        }

        private float _lastKnownCoreHp = -1f;

        private void HandleCoreHealthChanged(float currentHp, float maxHp)
        {
            if (_lastKnownCoreHp > 0f && currentHp < _lastKnownCoreHp - 0.5f)
            {
                AddTrauma(0.28f);
            }
            _lastKnownCoreHp = currentHp;
        }

        private void HandleCoreDefibShockwave(Vector2 centerWorld, float radius, float damage)
        {
            AddTrauma(0.55f);
        }

        private void HandleNewSessionInitialized()
        {
            _targetOrthoSize = DefaultOrthoSize;
            if (_camera != null)
            {
                _camera.orthographicSize = DefaultOrthoSize;
            }

            Vector2 spawnPos = WorldEntityRegistry.PlayerTransform != null
                ? (Vector2)WorldEntityRegistry.PlayerTransform.position
                : new Vector2(18.5f, 8.5f);

            _currentLogicalPos = ClampToWorldBounds(spawnPos);
            _tacticalPanPos = _currentLogicalPos;
            _wasTacticalPaused = false;
            _shakeTrauma = 0f;
            _lastKnownCoreHp = -1f;

            ApplyCameraTransform(_currentLogicalPos, Vector2.zero);
        }

        private void HandleGameStateChanged(GameState oldState, GameState newState)
        {
            if (newState == GameState.TacticalPause && oldState != GameState.TacticalPause)
            {
                _tacticalPanPos = _currentLogicalPos;
                _wasTacticalPaused = true;
            }
            else if (newState != GameState.TacticalPause)
            {
                _wasTacticalPaused = false;
            }
        }

        public void AddTrauma(float amount)
        {
            _shakeTrauma = Mathf.Clamp01(_shakeTrauma + Mathf.Max(0f, amount));
        }

        public void SnapToPosition(Vector2 worldPos)
        {
            _currentLogicalPos = ClampToWorldBounds(worldPos);
            _tacticalPanPos = _currentLogicalPos;
            ApplyCameraTransform(_currentLogicalPos, Vector2.zero);
        }

        private void LateUpdate()
        {
            Camera cam = TargetCamera;
            if (cam == null) return;

            if (!cam.orthographic)
            {
                cam.orthographic = true;
            }

            float dt = Mathf.Min(0.1f, Time.unscaledDeltaTime);
            if (dt <= 0f) return;

            GameState state = GameManager.InstanceIfExists != null
                ? GameManager.InstanceIfExists.State
                : GameState.Playing;

            // 1. Handle Zoom in Playing & TacticalPause
            if (state == GameState.Playing || state == GameState.TacticalPause)
            {
                HandleZoomInput(dt);
            }

            float zoomFactor = 1f - Mathf.Exp(-_zoomSharpness * dt);
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, _targetOrthoSize, zoomFactor);

            // Subtle ambient cavern tint from active weather
            UpdateAmbientBackgroundTint(cam, dt);

            // 2. Determine Desired Camera Position by GameState
            if (state == GameState.TacticalPause)
            {
                if (!_wasTacticalPaused)
                {
                    _tacticalPanPos = _currentLogicalPos;
                    _wasTacticalPaused = true;
                }

                Vector2 panInput = ReadPanInput();
                if (panInput.sqrMagnitude > 0.001f)
                {
                    float zoomScale = Mathf.Clamp(cam.orthographicSize / DefaultOrthoSize, 0.75f, 1.85f);
                    _tacticalPanPos += panInput * (_tacticalPanSpeed * zoomScale * dt);
                }

                _tacticalPanPos = ClampToWorldBounds(_tacticalPanPos);
                float panLerp = 1f - Mathf.Exp(-14f * dt);
                _currentLogicalPos = Vector2.Lerp(_currentLogicalPos, _tacticalPanPos, panLerp);
            }
            else if (state == GameState.MainMenu)
            {
                // Gentle living-colony showcase drift around Heart Core while in Main Menu
                Vector2 coreCenter = BioGridManager.InstanceIfExists != null
                    ? (Vector2)BioGridManager.InstanceIfExists.HeartCoreCenterWorld
                    : new Vector2(18.5f, 12.5f);
                float t = Time.unscaledTime * 0.25f;
                Vector2 orbit = coreCenter + new Vector2(Mathf.Cos(t) * 1.6f, Mathf.Sin(t * 0.8f) * 1.0f - 1.2f);
                _currentLogicalPos = ClampToWorldBounds(Vector2.Lerp(_currentLogicalPos, orbit, 1f - Mathf.Exp(-3f * dt)));
            }
            else
            {
                // Follow PlayerController smoothly during Playing (and keep steady during overlays)
                Vector2 targetPos = GetPlayerFollowTarget(cam, state == GameState.Playing);
                targetPos = ClampToWorldBounds(targetPos);

                float followLerp = 1f - Mathf.Exp(-_followSharpness * dt);
                _currentLogicalPos = Vector2.Lerp(_currentLogicalPos, targetPos, followLerp);
            }

            _currentLogicalPos = ClampToWorldBounds(_currentLogicalPos);

            // 3. Apply Screen Shake Offset (decays smoothly)
            Vector2 shakeOffset = Vector2.zero;
            if (_shakeTrauma > 0.001f && state == GameState.Playing)
            {
                float shakeMag = _shakeTrauma * _shakeTrauma * 0.28f;
                float timeVal = Time.unscaledTime * 28f;
                shakeOffset = new Vector2(
                    (Mathf.PerlinNoise(_shakeSeed, timeVal) - 0.5f) * 2f * shakeMag,
                    (Mathf.PerlinNoise(_shakeSeed + 31.7f, timeVal) - 0.5f) * 2f * shakeMag);
                _shakeTrauma = Mathf.Max(0f, _shakeTrauma - dt * 1.75f);
            }
            else
            {
                _shakeTrauma = 0f;
            }

            Vector2 finalClamped = ClampToWorldBounds(_currentLogicalPos + shakeOffset);
            ApplyCameraTransform(finalClamped, Vector2.zero);
        }

        private void HandleZoomInput(float dt)
        {
            float deltaZoom = 0f;

            // Z / + zooms in (smaller orthographicSize), X / - zooms out (larger orthographicSize)
            if (Input.GetKey(KeyCode.Z) || Input.GetKey(KeyCode.Equals) || Input.GetKey(KeyCode.Plus) || Input.GetKey(KeyCode.KeypadPlus))
            {
                deltaZoom -= _keyZoomSpeed * dt;
            }
            if (Input.GetKey(KeyCode.X) || Input.GetKey(KeyCode.Minus) || Input.GetKey(KeyCode.KeypadMinus))
            {
                deltaZoom += _keyZoomSpeed * dt;
            }

            // Ctrl + MouseScrollWheel zoom
            bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrlHeld)
            {
                bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
                if (!overUi)
                {
                    float scroll = Input.mouseScrollDelta.y;
                    if (Mathf.Abs(scroll) < 0.001f)
                    {
                        scroll = Input.GetAxisRaw("Mouse ScrollWheel") * 10f;
                    }
                    if (Mathf.Abs(scroll) > 0.01f)
                    {
                        deltaZoom -= Mathf.Sign(scroll) * _scrollZoomStep * 0.5f;
                    }
                }
            }

            if (Mathf.Abs(deltaZoom) > 0.0001f)
            {
                _targetOrthoSize = Mathf.Clamp(_targetOrthoSize + deltaZoom, MinOrthoSize, MaxOrthoSize);
            }
        }

        private static Vector2 ReadPanInput()
        {
            float hx = 0f;
            float vy = 0f;

            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) hx -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) hx += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) vy -= 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) vy += 1f;

            Vector2 v = new Vector2(hx, vy);
            if (v.sqrMagnitude > 1f)
            {
                v.Normalize();
            }
            return v;
        }

        private Vector2 GetPlayerFollowTarget(Camera cam, bool includeMouseLookAhead)
        {
            Vector2 basePos = WorldEntityRegistry.PlayerTransform != null
                ? (Vector2)WorldEntityRegistry.PlayerTransform.position
                : (PlayerController.InstanceIfExists != null
                    ? (Vector2)PlayerController.InstanceIfExists.transform.position
                    : _currentLogicalPos);

            if (!includeMouseLookAhead || cam == null || _aimLookAheadDistance <= 0.001f)
            {
                return basePos;
            }

            Vector3 mouseScreen = Input.mousePosition;
            if (mouseScreen.x < 0f || mouseScreen.y < 0f || mouseScreen.x > Screen.width || mouseScreen.y > Screen.height)
            {
                return basePos;
            }

            Vector3 mouseWorld = cam.ScreenToWorldPoint(new Vector3(mouseScreen.x, mouseScreen.y, -cam.transform.position.z));
            Vector2 offset = (Vector2)mouseWorld - basePos;
            float clampedMag = Mathf.Min(offset.magnitude * 0.08f, _aimLookAheadDistance);
            if (offset.sqrMagnitude > 0.01f)
            {
                basePos += offset.normalized * clampedMag;
            }
            return basePos;
        }

        private Vector2 ClampToWorldBounds(Vector2 desiredPos)
        {
            Camera cam = TargetCamera;
            Rect mapBounds = BioGridManager.InstanceIfExists != null
                ? BioGridManager.InstanceIfExists.WorldMapBounds
                : new Rect(0f, 0f, BioGridManager.Width * BioGridManager.CellSize, BioGridManager.Height * BioGridManager.CellSize);

            if (cam == null)
            {
                return new Vector2(
                    Mathf.Clamp(desiredPos.x, mapBounds.xMin, mapBounds.xMax),
                    Mathf.Clamp(desiredPos.y, mapBounds.yMin, mapBounds.yMax));
            }

            float halfHeight = cam.orthographicSize;
            float aspect = cam.aspect > 0.05f ? cam.aspect : (16f / 9f);
            float halfWidth = halfHeight * aspect;

            float clampedX = (halfWidth * 2f >= mapBounds.width)
                ? mapBounds.center.x
                : Mathf.Clamp(desiredPos.x, mapBounds.xMin + halfWidth, mapBounds.xMax - halfWidth);

            float clampedY = (halfHeight * 2f >= mapBounds.height)
                ? mapBounds.center.y
                : Mathf.Clamp(desiredPos.y, mapBounds.yMin + halfHeight, mapBounds.yMax - halfHeight);

            return new Vector2(clampedX, clampedY);
        }

        private void UpdateAmbientBackgroundTint(Camera cam, float dt)
        {
            if (GameManager.InstanceIfExists == null) return;
            WeatherDefinition wDef = GameManager.InstanceIfExists.CurrentWeatherDefinition;
            if (wDef == null) return;

            Color desired = new Color(
                _baseBackgroundColor.r * wDef.ambientTint.r,
                _baseBackgroundColor.g * wDef.ambientTint.g,
                _baseBackgroundColor.b * wDef.ambientTint.b,
                1f);
            cam.backgroundColor = Color.Lerp(cam.backgroundColor, desired, 1f - Mathf.Exp(-3f * dt));
        }

        private void ApplyCameraTransform(Vector2 xy, Vector2 extraOffset)
        {
            Camera cam = TargetCamera;
            Transform targetTransform = cam != null ? cam.transform : transform;
            targetTransform.position = new Vector3(xy.x + extraOffset.x, xy.y + extraOffset.y, -10f);
        }
    }
}
