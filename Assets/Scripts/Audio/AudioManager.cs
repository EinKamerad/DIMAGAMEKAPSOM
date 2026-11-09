using System;
using System.Collections.Generic;
using UnityEngine;

namespace BioFarme.Audio
{
    /// <summary>
    /// Well-known audio clip keys matching Assets/Resources/Audio/*.wav.
    /// </summary>
    public static class AudioKeys
    {
        public const string BgmAmbientLoop = "Bgm_BiopunkAmbientLoop";
        public const string SfxSprayLoop = "Sfx_FlamethrowerBurst";
        public const string SfxTill = "Sfx_TillSoil";
        public const string SfxWater = "Sfx_WaterSpray";
        public const string SfxPlant = "Sfx_PlantSeed";
        public const string SfxHarvest = "Sfx_HarvestCrop";
        public const string SfxBuild = "Sfx_BuildStructure";
        public const string SfxDemolish = "Sfx_RepairWall";
        public const string SfxRepair = "Sfx_RepairWall";
        public const string SfxTurretShoot = "Sfx_AcidSpit";
        public const string SfxSporeMortar = "Sfx_AcidSpit";
        public const string SfxBileCannon = "Sfx_BileCannon";
        public const string SfxEnemyHit = "Sfx_EnemyHit";
        public const string SfxEnemyDie = "Sfx_EnemyDeath";
        public const string SfxCoreHit = "Sfx_HeartDamage";
        public const string SfxHeartPulse = "Sfx_HeartPulse";
        public const string SfxWaveAlert = "Sfx_WaveSiren";
        public const string SfxMiniGameHit = "Sfx_MiniGameHit";
        public const string SfxMiniGameSuccess = "Sfx_MiniGameHit";
        public const string SfxMiniGameFail = "Sfx_MiniGameMiss";
        public const string SfxUiClick = "Sfx_UiClick";
        public const string SfxVictory = "Sfx_VictoryFanfare";
        public const string SfxDefeat = "Sfx_DefeatFlatline";
    }

    /// <summary>
    /// Self-instantiating persistent audio controller for music, multi-voice pitched SFX, and bio-sprayer loops.
    /// Includes procedural WebGL-compatible synth fallbacks if any clip is absent from Resources/Audio.
    /// </summary>
    public sealed class AudioManager : MonoBehaviour
    {
        private const int SfxPoolSize = 8;
        private const string PrefMasterVol = "BioFarme_MasterVol";
        private const string PrefMusicVol = "BioFarme_MusicVol";
        private const string PrefSfxVol = "BioFarme_SfxVol";

        private static AudioManager _instance;
        private static bool _applicationQuitting;

        public static AudioManager Instance
        {
            get
            {
                if (_applicationQuitting) return _instance;
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<AudioManager>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("[AudioManager]");
                        _instance = go.AddComponent<AudioManager>();
                    }
                }
                return _instance;
            }
        }

        private AudioSource _musicSource;
        private AudioSource _sprayLoopSource;
        private readonly List<AudioSource> _sfxPool = new List<AudioSource>(SfxPoolSize);
        private int _nextSfxVoiceIndex;

        private readonly Dictionary<string, AudioClip> _clipCache = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AudioClip> _normalizedClips = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> _lastPlayTimestamp = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private bool _indexedResources;

        public float MasterVolume { get; private set; } = 0.85f;
        public float MusicVolume { get; private set; } = 0.55f;
        public float SfxVolume { get; private set; } = 0.80f;
        public bool IsSprayLoopPlaying => _sprayLoopSource != null && _sprayLoopSource.isPlaying;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            MasterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(PrefMasterVol, 0.85f));
            MusicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(PrefMusicVol, 0.55f));
            SfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(PrefSfxVol, 0.80f));

            SetupAudioSources();
            EnsureIndexed();
            ApplyVolumes();
        }

        private void Start()
        {
            if (_musicSource != null && !_musicSource.isPlaying)
            {
                PlayMusic(AudioKeys.BgmAmbientLoop);
            }
        }

        private void OnApplicationQuit()
        {
            _applicationQuitting = true;
        }

        private void SetupAudioSources()
        {
            _musicSource = gameObject.AddComponent<AudioSource>();
            _musicSource.loop = true;
            _musicSource.playOnAwake = false;
            _musicSource.spatialBlend = 0f;
            _musicSource.priority = 32;

            _sprayLoopSource = gameObject.AddComponent<AudioSource>();
            _sprayLoopSource.loop = true;
            _sprayLoopSource.playOnAwake = false;
            _sprayLoopSource.spatialBlend = 0f;
            _sprayLoopSource.priority = 96;

            _sfxPool.Clear();
            for (int i = 0; i < SfxPoolSize; i++)
            {
                AudioSource voice = gameObject.AddComponent<AudioSource>();
                voice.loop = false;
                voice.playOnAwake = false;
                voice.spatialBlend = 0f;
                voice.priority = 128;
                _sfxPool.Add(voice);
            }
        }

        private void EnsureIndexed()
        {
            if (_indexedResources) return;
            _indexedResources = true;

            try
            {
                AudioClip[] clips = Resources.LoadAll<AudioClip>("Audio");
                if (clips != null)
                {
                    for (int i = 0; i < clips.Length; i++)
                    {
                        AudioClip clip = clips[i];
                        if (clip == null) continue;
                        _clipCache[clip.name] = clip;
                        string norm = Normalize(clip.name);
                        if (!string.IsNullOrEmpty(norm) && !_normalizedClips.ContainsKey(norm))
                        {
                            _normalizedClips[norm] = clip;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AudioManager] Audio index warning: {ex.Message}");
            }
        }

        public AudioClip GetClip(string clipName)
        {
            EnsureIndexed();
            if (string.IsNullOrWhiteSpace(clipName))
            {
                clipName = AudioKeys.SfxUiClick;
            }

            if (_clipCache.TryGetValue(clipName, out AudioClip cached) && cached != null)
            {
                return cached;
            }

            AudioClip direct = Resources.Load<AudioClip>($"Audio/{clipName}")
                            ?? Resources.Load<AudioClip>(clipName);
            if (direct != null)
            {
                _clipCache[clipName] = direct;
                return direct;
            }

            string norm = Normalize(clipName);
            if (_normalizedClips.TryGetValue(norm, out AudioClip normHit) && normHit != null)
            {
                _clipCache[clipName] = normHit;
                return normHit;
            }

            // Map common shorthand SFX names to exact curated WAVs in Resources/Audio
            string mappedAlias = ResolveKnownAudioAlias(norm);
            if (!string.IsNullOrEmpty(mappedAlias) &&
                _normalizedClips.TryGetValue(Normalize(mappedAlias), out AudioClip aliasHit) &&
                aliasHit != null)
            {
                _clipCache[clipName] = aliasHit;
                return aliasHit;
            }

            // Try stripped Sfx_ / Bgm_ prefix
            if (clipName.StartsWith("Sfx_", StringComparison.OrdinalIgnoreCase) ||
                clipName.StartsWith("Bgm_", StringComparison.OrdinalIgnoreCase))
            {
                string shortKey = clipName.Substring(4);
                string shortNorm = Normalize(shortKey);
                if (_normalizedClips.TryGetValue(shortNorm, out AudioClip shortHit) && shortHit != null)
                {
                    _clipCache[clipName] = shortHit;
                    return shortHit;
                }
            }

            // Procedural fallback clip so no SFX or ambient loop is ever silent or null
            AudioClip generated = GenerateProceduralClip(clipName);
            _clipCache[clipName] = generated;
            return generated;
        }

        public void PlayMusic(string clipName = AudioKeys.BgmAmbientLoop, bool restartIfSame = false)
        {
            if (_musicSource == null) return;
            AudioClip clip = GetClip(clipName);
            if (clip == null) return;

            if (_musicSource.clip == clip && _musicSource.isPlaying && !restartIfSame)
            {
                return;
            }

            _musicSource.clip = clip;
            _musicSource.volume = MasterVolume * MusicVolume;
            _musicSource.loop = true;
            _musicSource.Play();
        }

        public void StopMusic()
        {
            if (_musicSource != null)
            {
                _musicSource.Stop();
            }
        }

        public void PlaySfx(string clipName, float volume = 1f, float pitchVariance = 0.06f)
        {
            if (_sfxPool.Count == 0 || MasterVolume <= 0.001f || SfxVolume <= 0.001f) return;
            if (string.IsNullOrWhiteSpace(clipName)) return;

            // Prevent ear-splitting stacking if 10 turrets/mites fire on the exact same frame
            float now = Time.unscaledTime;
            if (_lastPlayTimestamp.TryGetValue(clipName, out float prevTime) && (now - prevTime) < 0.03f)
            {
                return;
            }
            _lastPlayTimestamp[clipName] = now;

            AudioClip clip = GetClip(clipName);
            if (clip == null) return;

            AudioSource voice = _sfxPool[_nextSfxVoiceIndex];
            _nextSfxVoiceIndex = (_nextSfxVoiceIndex + 1) % _sfxPool.Count;

            float pitchOffset = pitchVariance > 0f ? UnityEngine.Random.Range(-pitchVariance, pitchVariance) : 0f;
            voice.pitch = Mathf.Clamp(1f + pitchOffset, 0.5f, 2.0f);
            voice.volume = Mathf.Clamp01(volume) * MasterVolume * SfxVolume;
            voice.PlayOneShot(clip);
        }

        public void PlaySfxAtPosition(string clipName, Vector2 worldPos, float volume = 1f, float pitchVariance = 0.06f)
        {
            Camera cam = Camera.main;
            float distanceAttenuation = 1f;
            if (cam != null)
            {
                float dist = Vector2.Distance(cam.transform.position, worldPos);
                distanceAttenuation = Mathf.Clamp01(1.15f - dist / 28f);
            }
            if (distanceAttenuation > 0.05f)
            {
                PlaySfx(clipName, volume * distanceAttenuation, pitchVariance);
            }
        }

        // --- Dedicated Bio-Sprayer / Flamethrower Loop Helpers ---

        public void StartSprayLoop(string clipName = AudioKeys.SfxSprayLoop, float volumeMultiplier = 0.65f)
        {
            if (_sprayLoopSource == null) return;
            AudioClip clip = GetClip(clipName);
            if (clip == null) return;

            _sprayLoopSource.volume = Mathf.Clamp01(volumeMultiplier) * MasterVolume * SfxVolume;
            if (_sprayLoopSource.clip != clip)
            {
                _sprayLoopSource.clip = clip;
            }
            if (!_sprayLoopSource.isPlaying)
            {
                _sprayLoopSource.Play();
            }
        }

        public void StopSprayLoop()
        {
            if (_sprayLoopSource != null && _sprayLoopSource.isPlaying)
            {
                _sprayLoopSource.Stop();
            }
        }

        public void SetSprayLoopActive(bool active, string clipName = AudioKeys.SfxSprayLoop, float volumeMultiplier = 0.65f)
        {
            if (active)
            {
                StartSprayLoop(clipName, volumeMultiplier);
            }
            else
            {
                StopSprayLoop();
            }
        }

        // --- Volume Controls ---

        public void SetMasterVolume(float volume)
        {
            MasterVolume = Mathf.Clamp01(volume);
            PlayerPrefs.SetFloat(PrefMasterVol, MasterVolume);
            ApplyVolumes();
        }

        public void SetMusicVolume(float volume)
        {
            MusicVolume = Mathf.Clamp01(volume);
            PlayerPrefs.SetFloat(PrefMusicVol, MusicVolume);
            ApplyVolumes();
        }

        public void SetSfxVolume(float volume)
        {
            SfxVolume = Mathf.Clamp01(volume);
            PlayerPrefs.SetFloat(PrefSfxVol, SfxVolume);
            ApplyVolumes();
        }

        private void ApplyVolumes()
        {
            if (_musicSource != null)
            {
                _musicSource.volume = MasterVolume * MusicVolume;
            }
            if (_sprayLoopSource != null && _sprayLoopSource.isPlaying)
            {
                _sprayLoopSource.volume = 0.65f * MasterVolume * SfxVolume;
            }
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            char[] chars = new char[s.Length];
            int count = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsLetterOrDigit(s[i]))
                {
                    chars[count++] = char.ToLowerInvariant(s[i]);
                }
            }
            return new string(chars, 0, count);
        }

        private static string ResolveKnownAudioAlias(string norm)
        {
            if (norm.Contains("till")) return "Sfx_TillSoil";
            if (norm.Contains("water")) return "Sfx_WaterSpray";
            if (norm.Contains("spray") || norm.Contains("flame")) return "Sfx_FlamethrowerBurst";
            if (norm.Contains("plant") || norm.Contains("seed")) return "Sfx_PlantSeed";
            if (norm.Contains("harvest")) return "Sfx_HarvestCrop";
            if (norm.Contains("build") || norm.Contains("place")) return "Sfx_BuildStructure";
            if (norm.Contains("repair") || norm.Contains("demolish")) return "Sfx_RepairWall";
            if (norm.Contains("bile") || norm.Contains("cannon")) return "Sfx_BileCannon";
            if (norm.Contains("shoot") || norm.Contains("spit") || norm.Contains("mortar") || norm.Contains("turret")) return "Sfx_AcidSpit";
            if (norm.Contains("enemydie") || norm.Contains("enemydeath") || norm.Contains("death")) return "Sfx_EnemyDeath";
            if (norm.Contains("enemyhit") || norm.Contains("hit")) return "Sfx_EnemyHit";
            if (norm.Contains("corehit") || norm.Contains("heartdamage")) return "Sfx_HeartDamage";
            if (norm.Contains("pulse") || norm.Contains("defib")) return "Sfx_HeartPulse";
            if (norm.Contains("wave") || norm.Contains("siren") || norm.Contains("alert")) return "Sfx_WaveSiren";
            if (norm.Contains("victory") || norm.Contains("win")) return "Sfx_VictoryFanfare";
            if (norm.Contains("defeat") || norm.Contains("lose") || norm.Contains("flatline")) return "Sfx_DefeatFlatline";
            if (norm.Contains("miss") || norm.Contains("fail")) return "Sfx_MiniGameMiss";
            if (norm.Contains("success")) return "Sfx_MiniGameHit";
            if (norm.Contains("click") || norm.Contains("button")) return "Sfx_UiClick";
            return null;
        }

        // --- Lightweight WebGL-Safe Procedural Synth Fallbacks ---

        private static AudioClip GenerateProceduralClip(string clipName)
        {
            string lower = clipName.ToLowerInvariant();
            const int sampleRate = 22050;

            if (lower.Contains("bgm") || lower.Contains("ambient"))
            {
                // Seamless 8-second dark biopunk cavernous pad + heartbeat pulse
                int lengthSamples = sampleRate * 8;
                float[] data = new float[lengthSamples];
                for (int i = 0; i < lengthSamples; i++)
                {
                    float t = i / (float)sampleRate;
                    float drone = Mathf.Sin(t * Mathf.PI * 2f * 55f) * 0.12f
                                + Mathf.Sin(t * Mathf.PI * 2f * 82.5f) * 0.08f
                                + Mathf.Sin(t * Mathf.PI * 2f * 110f + Mathf.Sin(t * Mathf.PI * 0.5f)) * 0.05f;
                    float beatPhase = (t % 2.0f);
                    float heartbeat = beatPhase < 0.18f ? Mathf.Sin(beatPhase * Mathf.PI * 2f * 38f) * (1f - beatPhase / 0.18f) * 0.16f : 0f;
                    data[i] = Mathf.Clamp(drone + heartbeat, -0.5f, 0.5f);
                }
                AudioClip bgm = AudioClip.Create($"Synth_{clipName}", lengthSamples, 1, sampleRate, false);
                bgm.SetData(data, 0);
                return bgm;
            }

            if (lower.Contains("sprayloop") || lower.Contains("flamethrower"))
            {
                // Seamless 1-second pressurized bio-mist hiss
                int lengthSamples = sampleRate;
                float[] data = new float[lengthSamples];
                uint rng = 1337u;
                float prev = 0f;
                for (int i = 0; i < lengthSamples; i++)
                {
                    rng = rng * 1664525u + 1013904223u;
                    float white = ((rng & 0xFFFF) / 32768f) - 1f;
                    prev = prev * 0.72f + white * 0.28f;
                    float env = 0.85f + 0.15f * Mathf.Sin((i / (float)lengthSamples) * Mathf.PI * 2f * 6f);
                    data[i] = prev * env * 0.22f;
                }
                AudioClip hiss = AudioClip.Create($"Synth_{clipName}", lengthSamples, 1, sampleRate, false);
                hiss.SetData(data, 0);
                return hiss;
            }

            // Short procedural SFX
            float duration = 0.16f;
            float startFreq = 320f;
            float endFreq = 160f;
            float noiseMix = 0.12f;

            if (lower.Contains("harvest") || lower.Contains("success") || lower.Contains("victory"))
            {
                duration = lower.Contains("victory") ? 0.65f : 0.24f;
                startFreq = 380f;
                endFreq = 760f;
                noiseMix = 0.02f;
            }
            else if (lower.Contains("build") || lower.Contains("plant") || lower.Contains("till"))
            {
                duration = 0.14f;
                startFreq = 190f;
                endFreq = 110f;
                noiseMix = 0.25f;
            }
            else if (lower.Contains("shoot") || lower.Contains("mortar") || lower.Contains("cannon"))
            {
                duration = 0.15f;
                startFreq = lower.Contains("cannon") ? 240f : 520f;
                endFreq = 95f;
                noiseMix = 0.18f;
            }
            else if (lower.Contains("click") || lower.Contains("ui"))
            {
                duration = 0.06f;
                startFreq = 640f;
                endFreq = 480f;
                noiseMix = 0.0f;
            }
            else if (lower.Contains("wave") || lower.Contains("defeat") || lower.Contains("corehit"))
            {
                duration = 0.45f;
                startFreq = 165f;
                endFreq = 72f;
                noiseMix = 0.22f;
            }

            int samples = Mathf.Max(256, Mathf.RoundToInt(sampleRate * duration));
            float[] sfxData = new float[samples];
            float phase = 0f;
            uint nSeed = (uint)clipName.GetHashCode();

            for (int i = 0; i < samples; i++)
            {
                float progress = i / (float)samples;
                float freq = Mathf.Lerp(startFreq, endFreq, progress);
                phase += (freq * Mathf.PI * 2f) / sampleRate;
                nSeed = nSeed * 1664525u + 1013904223u;
                float noise = (((nSeed & 0xFFFF) / 32768f) - 1f) * noiseMix;
                float envelope = (1f - progress) * (1f - progress);
                sfxData[i] = (Mathf.Sin(phase) * (1f - noiseMix) + noise) * envelope * 0.32f;
            }

            AudioClip sfx = AudioClip.Create($"Synth_{clipName}", samples, 1, sampleRate, false);
            sfx.SetData(sfxData, 0);
            return sfx;
        }
    }
}
