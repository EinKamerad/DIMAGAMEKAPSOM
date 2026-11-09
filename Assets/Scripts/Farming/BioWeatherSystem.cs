using System.Collections.Generic;
using UnityEngine;
using BioFarme.Core;
using BioFarme.Data;
using BioFarme.Grid;

namespace BioFarme.Farming
{
    /// <summary>
    /// Dynamic subterranean biosphere weather controller & pooled WebGL-safe SpriteRenderer VFX manager.
    /// Drives CalmSymbiosis, AcidSporeRain, BioDrought, and ParasiteSwarm, ticks soil hydration
    /// (1.8x faster evaporation during BioDrought, automatic random bed watering during AcidSporeRain),
    /// and renders atmospheric streaks/spores around the active camera.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public sealed class BioWeatherSystem : MonoBehaviour
    {
        private const int InitialVfxPoolCount = 96;
        private const int MaxActiveVfxCount = 180;

        private sealed class PooledSpriteParticle
        {
            public GameObject GameObject;
            public Transform Transform;
            public SpriteRenderer Renderer;
            public Vector3 Velocity;
            public Color StartColor;
            public Vector3 StartScale;
            public Vector3 EndScale;
            public float Elapsed;
            public float Duration;
            public float GravityY;
            public bool Active;
        }

        private static BioWeatherSystem _instance;
        private static bool _applicationQuitting;

        public static BioWeatherSystem InstanceIfExists => _instance;

        public static BioWeatherSystem Instance
        {
            get
            {
                if (_applicationQuitting) return _instance;
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<BioWeatherSystem>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("[BioWeatherSystem]");
                        _instance = go.AddComponent<BioWeatherSystem>();
                    }
                }
                return _instance;
            }
        }

        private WeatherType _currentWeather = WeatherType.CalmSymbiosis;
        private float _weatherTimer = 48f;
        private float _rainWaterBedTimer;
        private float _ambientSpawnAccumulator;

        private Transform _vfxRoot;
        private readonly Queue<PooledSpriteParticle> _inactivePool = new Queue<PooledSpriteParticle>(InitialVfxPoolCount);
        private readonly List<PooledSpriteParticle> _activeParticles = new List<PooledSpriteParticle>(InitialVfxPoolCount);
        private readonly List<Vector2Int> _dryBedScratch = new List<Vector2Int>(64);

        public WeatherType CurrentWeather => _currentWeather;
        public float WeatherRemainingSeconds => _weatherTimer;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            SetupVfxPool();
        }

        private void OnEnable()
        {
            GameEvents.OnWeatherChanged += HandleExternalWeatherChanged;
            GameEvents.OnWaveStarted += HandleWaveStarted;
            GameEvents.OnNewGameSessionInitialized += HandleNewSessionInitialized;
        }

        private void Start()
        {
            if (GameManager.InstanceIfExists != null)
            {
                _currentWeather = GameManager.InstanceIfExists.CurrentWeather;
                _weatherTimer = Mathf.Max(10f, GameManager.InstanceIfExists.WeatherRemainingSeconds);
            }
        }

        private void OnDisable()
        {
            GameEvents.OnWeatherChanged -= HandleExternalWeatherChanged;
            GameEvents.OnWaveStarted -= HandleWaveStarted;
            GameEvents.OnNewGameSessionInitialized -= HandleNewSessionInitialized;
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

        private void SetupVfxPool()
        {
            GameObject rootGo = new GameObject("CavernWeatherAndFarmVFX");
            rootGo.transform.SetParent(transform, false);
            _vfxRoot = rootGo.transform;

            for (int i = 0; i < InitialVfxPoolCount; i++)
            {
                PooledSpriteParticle p = CreateParticleObject();
                p.Active = false;
                p.GameObject.SetActive(false);
                _inactivePool.Enqueue(p);
            }
        }

        private PooledSpriteParticle CreateParticleObject()
        {
            GameObject go = new GameObject("BioVfxMote");
            go.transform.SetParent(_vfxRoot, false);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 65;

            return new PooledSpriteParticle
            {
                GameObject = go,
                Transform = go.transform,
                Renderer = sr
            };
        }

        private bool _suppressWeatherCallback;

        private void HandleExternalWeatherChanged(WeatherType weather, float remainingSec)
        {
            if (_suppressWeatherCallback) return;
            _currentWeather = weather;
            _weatherTimer = Mathf.Max(5f, remainingSec);
        }

        private void HandleWaveStarted(int waveNumber, WaveDefinition waveDef)
        {
            if (waveDef == null) return;
            ApplyWeatherState(waveDef.waveWeather, Random.Range(40f, 54f));
        }

        private void HandleNewSessionInitialized()
        {
            _currentWeather = WeatherType.CalmSymbiosis;
            _weatherTimer = 50f;
            _rainWaterBedTimer = 0f;
            _ambientSpawnAccumulator = 0f;
        }

        public void ApplyWeatherState(WeatherType nextWeather, float durationSec)
        {
            _currentWeather = nextWeather;
            _weatherTimer = Mathf.Max(8f, durationSec);

            if (GameManager.InstanceIfExists != null)
            {
                _suppressWeatherCallback = true;
                GameManager.InstanceIfExists.SetWeather(_currentWeather, _weatherTimer);
                _suppressWeatherCallback = false;
            }
        }

        private void Update()
        {
            float simDt = GameManager.InstanceIfExists != null
                ? GameManager.InstanceIfExists.SimulationDeltaTime
                : Time.deltaTime;

            if (simDt > 0f)
            {
                // Sync from GameManager if it drifted
                if (GameManager.InstanceIfExists != null)
                {
                    _currentWeather = GameManager.InstanceIfExists.CurrentWeather;
                }

                // 1. Advance weather cycle slightly ahead of GameManager fallback so BioWeatherSystem drives transitions cleanly
                _weatherTimer -= simDt;
                if (_weatherTimer <= 0.25f)
                {
                    WeatherType next = PickNextWeather(_currentWeather);
                    ApplyWeatherState(next, Random.Range(38f, 54f));
                }

                // 2. Tick soil hydration with 1.8x evaporation during BioDrought
                if (BioGridManager.InstanceIfExists != null)
                {
                    float evaporationMult = (_currentWeather == WeatherType.BioDrought) ? 1.8f : 1.0f;
                    BioGridManager.InstanceIfExists.TickSoilHydration(simDt * evaporationMult);
                }

                // 3. During AcidSporeRain, periodically auto-water TilledBed tiles across the cavern
                if (_currentWeather == WeatherType.AcidSporeRain)
                {
                    _rainWaterBedTimer += simDt;
                    if (_rainWaterBedTimer >= 0.40f)
                    {
                        _rainWaterBedTimer = 0f;
                        AutoWaterRandomTilledBeds(2);
                    }
                }

                // 4. Spawn atmospheric weather particles around Camera.main
                EmitAmbientWeatherParticles(simDt);
            }

            // Tick active VFX particles (freeze motion cleanly during TacticalPause / Pause, or tick with simDt/Time.deltaTime)
            float vfxDt = (GameManager.InstanceIfExists != null && GameManager.InstanceIfExists.IsSimulationPaused)
                ? (GameManager.InstanceIfExists.State == GameState.TacticalPause ? Time.unscaledDeltaTime * 0.65f : 0f)
                : Time.deltaTime;

            if (vfxDt > 0f)
            {
                TickParticles(vfxDt);
            }
        }

        private static WeatherType PickNextWeather(WeatherType current)
        {
            float roll = Random.value;
            WeatherType candidate;
            if (roll < 0.34f) candidate = WeatherType.CalmSymbiosis;
            else if (roll < 0.62f) candidate = WeatherType.AcidSporeRain;
            else if (roll < 0.84f) candidate = WeatherType.BioDrought;
            else candidate = WeatherType.ParasiteSwarm;

            if (candidate == current)
            {
                candidate = (current == WeatherType.CalmSymbiosis)
                    ? WeatherType.AcidSporeRain
                    : WeatherType.CalmSymbiosis;
            }
            return candidate;
        }

        private void AutoWaterRandomTilledBeds(int count)
        {
            BioGridManager grid = BioGridManager.InstanceIfExists;
            if (grid == null) return;

            _dryBedScratch.Clear();
            for (int x = 1; x < BioGridManager.Width - 1; x++)
            {
                for (int y = 1; y < BioGridManager.Height - 1; y++)
                {
                    Vector2Int cell = new Vector2Int(x, y);
                    if (grid.GetGroundType(cell) == TileGroundType.TilledBed)
                    {
                        _dryBedScratch.Add(cell);
                    }
                }
            }

            for (int i = 0; i < count; i++)
            {
                Vector2Int target;
                if (_dryBedScratch.Count > 0)
                {
                    int pick = Random.Range(0, _dryBedScratch.Count);
                    target = _dryBedScratch[pick];
                    _dryBedScratch.RemoveAt(pick);
                }
                else
                {
                    target = new Vector2Int(
                        Random.Range(2, BioGridManager.Width - 2),
                        Random.Range(2, BioGridManager.Height - 2));
                }

                if (grid.CanWater(target))
                {
                    grid.WaterTile(target, 1.0f);
                    Vector3 worldCenter = grid.GridToWorldCenter(target);
                    SpawnPuffBurst(worldCenter, new Color(0.35f, 1.0f, 0.72f, 0.75f), 3, 0.55f);
                }
            }
        }

        private void EmitAmbientWeatherParticles(float simDt)
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            float spawnRatePerSec;
            switch (_currentWeather)
            {
                case WeatherType.AcidSporeRain:
                    spawnRatePerSec = 36f;
                    break;
                case WeatherType.BioDrought:
                    spawnRatePerSec = 14f;
                    break;
                case WeatherType.ParasiteSwarm:
                    spawnRatePerSec = 18f;
                    break;
                default:
                    spawnRatePerSec = 6f;
                    break;
            }

            _ambientSpawnAccumulator += spawnRatePerSec * simDt;
            int toSpawn = Mathf.Min(6, Mathf.FloorToInt(_ambientSpawnAccumulator));
            _ambientSpawnAccumulator -= toSpawn;

            if (toSpawn <= 0) return;

            float halfH = cam.orthographicSize + 1.5f;
            float halfW = halfH * (cam.aspect > 0.1f ? cam.aspect : 1.77f) + 1.5f;
            Vector2 camCenter = cam.transform.position;

            for (int i = 0; i < toSpawn; i++)
            {
                Vector2 spawnPos = camCenter + new Vector2(
                    Random.Range(-halfW, halfW),
                    Random.Range(-halfH, halfH));

                if (_currentWeather == WeatherType.AcidSporeRain)
                {
                    // Crisp diagonal green-cyan bioluminescent rain streak
                    Vector2 rainStart = camCenter + new Vector2(
                        Random.Range(-halfW, halfW + 2.5f),
                        Random.Range(-halfH * 0.2f, halfH + 2.5f));
                    Vector2 rainVel = new Vector2(-3.2f, -10.5f) * Random.Range(0.85f, 1.2f);
                    float angle = Mathf.Atan2(rainVel.y, rainVel.x) * Mathf.Rad2Deg;

                    SpawnCustomParticle(
                        sprite: SpriteRegistry.GetWhiteSquareSprite(),
                        pos: rainStart,
                        vel: rainVel,
                        rotationDeg: angle,
                        startScale: new Vector3(0.42f, 0.032f, 1f),
                        endScale: new Vector3(0.28f, 0.020f, 1f),
                        color: new Color(0.42f, 1.0f, 0.72f, 0.42f),
                        duration: Random.Range(0.45f, 0.75f),
                        gravityY: 0f,
                        sortingOrder: 75);
                }
                else if (_currentWeather == WeatherType.BioDrought)
                {
                    // Rising thermal ember motes
                    SpawnCustomParticle(
                        sprite: SpriteRegistry.GetSoftCircleSprite(),
                        pos: spawnPos,
                        vel: new Vector2(Random.Range(-0.4f, 0.4f), Random.Range(0.8f, 1.7f)),
                        rotationDeg: 0f,
                        startScale: Vector3.one * 0.16f,
                        endScale: Vector3.one * 0.04f,
                        color: new Color(1.0f, 0.62f, 0.28f, 0.36f),
                        duration: Random.Range(1.1f, 1.8f),
                        gravityY: 0.15f,
                        sortingOrder: 72);
                }
                else if (_currentWeather == WeatherType.ParasiteSwarm)
                {
                    // Swirling violet-magenta pheromone spores
                    SpawnCustomParticle(
                        sprite: SpriteRegistry.GetSoftCircleSprite(),
                        pos: spawnPos,
                        vel: new Vector2(Random.Range(-1.8f, 1.8f), Random.Range(-0.9f, 1.1f)),
                        rotationDeg: 0f,
                        startScale: Vector3.one * 0.18f,
                        endScale: Vector3.one * 0.06f,
                        color: new Color(0.96f, 0.34f, 0.90f, 0.40f),
                        duration: Random.Range(0.9f, 1.5f),
                        gravityY: 0f,
                        sortingOrder: 72);
                }
                else
                {
                    // Calm symbiosis bioluminescent cavern fireflies
                    SpawnCustomParticle(
                        sprite: SpriteRegistry.GetSoftCircleSprite(),
                        pos: spawnPos,
                        vel: new Vector2(Random.Range(-0.35f, 0.35f), Random.Range(0.15f, 0.55f)),
                        rotationDeg: 0f,
                        startScale: Vector3.one * 0.14f,
                        endScale: Vector3.one * 0.05f,
                        color: new Color(0.45f, 0.98f, 0.78f, 0.30f),
                        duration: Random.Range(1.4f, 2.2f),
                        gravityY: 0f,
                        sortingOrder: 70);
                }
            }
        }

        // --- Public VFX Burst Helpers for Farming & Player Tools ---

        public void SpawnPuffBurst(Vector2 center, Color color, int count = 6, float speedScale = 1.0f)
        {
            Sprite softCircle = SpriteRegistry.GetSoftCircleSprite();
            for (int i = 0; i < count; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float spd = Random.Range(0.6f, 2.1f) * speedScale;
                Vector2 vel = new Vector2(Mathf.Cos(angle) * spd, Mathf.Sin(angle) * spd + 0.35f);
                float size = Random.Range(0.16f, 0.30f);

                SpawnCustomParticle(
                    sprite: softCircle,
                    pos: center + Random.insideUnitCircle * 0.18f,
                    vel: vel,
                    rotationDeg: 0f,
                    startScale: Vector3.one * size,
                    endScale: Vector3.one * (size * 0.25f),
                    color: new Color(color.r, color.g, color.b, Mathf.Clamp01(color.a > 0.05f ? color.a : 0.85f)),
                    duration: Random.Range(0.28f, 0.52f),
                    gravityY: -1.6f,
                    sortingOrder: 62);
            }
        }

        public void SpawnDirectionalSprayDroplets(Vector2 muzzleWorld, Vector2 aimDir, Color color, int count = 2, float range = 4.0f)
        {
            Sprite softCircle = SpriteRegistry.GetSoftCircleSprite();
            float baseAngle = Mathf.Atan2(aimDir.y, aimDir.x);

            for (int i = 0; i < count; i++)
            {
                float spread = Random.Range(-0.42f, 0.42f); // ~24 deg half-angle
                float a = baseAngle + spread;
                float spd = Random.Range(6.5f, 10.5f);
                Vector2 vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * spd;
                float dur = Mathf.Clamp(range / spd, 0.18f, 0.45f);
                float s = Random.Range(0.16f, 0.32f);

                SpawnCustomParticle(
                    sprite: softCircle,
                    pos: muzzleWorld + Random.insideUnitCircle * 0.08f,
                    vel: vel,
                    rotationDeg: 0f,
                    startScale: Vector3.one * s,
                    endScale: Vector3.one * (s * 1.65f),
                    color: color,
                    duration: dur,
                    gravityY: 0f,
                    sortingOrder: 58);
            }
        }

        private void SpawnCustomParticle(
            Sprite sprite,
            Vector2 pos,
            Vector2 vel,
            float rotationDeg,
            Vector3 startScale,
            Vector3 endScale,
            Color color,
            float duration,
            float gravityY,
            int sortingOrder)
        {
            if (_vfxRoot == null) return;

            PooledSpriteParticle p;
            if (_inactivePool.Count > 0)
            {
                p = _inactivePool.Dequeue();
            }
            else if (_activeParticles.Count >= MaxActiveVfxCount)
            {
                p = _activeParticles[0];
                _activeParticles.RemoveAt(0);
            }
            else
            {
                p = CreateParticleObject();
            }

            p.Renderer.sprite = sprite;
            p.Renderer.color = color;
            p.Renderer.sortingOrder = sortingOrder;

            p.Transform.position = new Vector3(pos.x, pos.y, 0f);
            p.Transform.localRotation = Quaternion.Euler(0f, 0f, rotationDeg);
            p.Transform.localScale = startScale;

            p.Velocity = new Vector3(vel.x, vel.y, 0f);
            p.StartColor = color;
            p.StartScale = startScale;
            p.EndScale = endScale;
            p.Elapsed = 0f;
            p.Duration = Mathf.Max(0.08f, duration);
            p.GravityY = gravityY;
            p.Active = true;

            p.GameObject.SetActive(true);
            _activeParticles.Add(p);
        }

        private void TickParticles(float dt)
        {
            for (int i = _activeParticles.Count - 1; i >= 0; i--)
            {
                PooledSpriteParticle p = _activeParticles[i];
                if (p == null || p.GameObject == null || !p.Active)
                {
                    _activeParticles.RemoveAt(i);
                    continue;
                }

                p.Elapsed += dt;
                if (p.Elapsed >= p.Duration)
                {
                    p.Active = false;
                    p.GameObject.SetActive(false);
                    _activeParticles.RemoveAt(i);
                    _inactivePool.Enqueue(p);
                    continue;
                }

                float t = p.Elapsed / p.Duration;
                p.Velocity.y += p.GravityY * dt;
                p.Transform.position += p.Velocity * dt;
                p.Transform.localScale = Vector3.Lerp(p.StartScale, p.EndScale, t);

                Color c = p.StartColor;
                c.a = p.StartColor.a * (1f - t * t);
                p.Renderer.color = c;
            }
        }
    }
}
