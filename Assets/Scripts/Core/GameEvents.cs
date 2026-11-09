using System;
using UnityEngine;
using BioFarme.Data;

namespace BioFarme.Core
{
    /// <summary>
    /// Decoupled, null-safe static event bus connecting Core, Grid, Farming, Defense, MiniGames, and UI.
    /// </summary>
    public static class GameEvents
    {
        // --- Game Loop & State ---
        public static event Action OnNewGameSessionInitialized;
        public static event Action<GameState, GameState> OnGameStateChanged;
        public static event Action<bool> OnTacticalPauseChanged;
        public static event Action<WeatherType, float> OnWeatherChanged;

        // --- Resources, Buffs & Network Priority ---
        public static event Action<ResourceType, float, float> OnResourceChanged; // (type, newAmount, delta)
        public static event Action<ResourceType> OnInsufficientResource;
        public static event Action<float, float, float> OnGlobalBuffUpdated; // (cropMult, turretMult, secondsRemaining)
        public static event Action<NetworkPriorityMode> OnNetworkPriorityChanged;

        // --- Grid, Farming & Structures ---
        public static event Action<Vector2Int, TileGroundType> OnTileChanged;
        public static event Action<Vector2Int, StructureType, IGridEntity> OnStructurePlaced;
        public static event Action<Vector2Int, StructureType> OnStructureRemoved;
        public static event Action<Vector2Int, CropType> OnCropPlanted;
        public static event Action<Vector2Int, CropType, ResourceType, int> OnCropHarvested;

        // --- Combat, Waves & Living Heart Core ---
        public static event Action<int, float> OnWaveCountdownTick; // (nextWaveNumber, secondsLeft)
        public static event Action<int, WaveDefinition> OnWaveStarted;
        public static event Action<int> OnWaveCleared;
        public static event Action<EnemyType, Vector2> OnEnemySpawned;
        public static event Action<EnemyType, Vector2> OnEnemyKilled;
        public static event Action<float, float> OnCoreHealthChanged; // (currentHp, maxHp)
        public static event Action<float, float, float> OnCoreAwakeningChanged; // (progress01, currentMutagen, targetMutagen)
        public static event Action<Vector2, float, float> OnCoreDefibShockwave; // (centerWorld, radius, damageToEnemies)

        // --- Mini-Games, Hotbar & UI Notifications ---
        public static event Action<MiniGameKind> OnMiniGameRequested;
        public static event Action<MiniGameKind, bool, int> OnMiniGameCompleted; // (kind, success, scoreTier)
        public static event Action<int, HotbarSlotData> OnHotbarSlotSelected;
        public static event Action<HotbarSlotData[]> OnHotbarLoadoutChanged;
        public static event Action<HotbarSlotData> OnAssignHotbarSelection; // UI -> Player Hotbar
        public static event Action OnSendNextWaveEarlyRequested; // UI -> WaveDirector
        public static event Action<string, Color> OnBannerToast;
        public static event Action<Vector2, string, Color, float, bool> OnWorldPopupRequested;

        // --- Cross-Module Command Delegates (Decouples Player <-> Farming <-> Factory) ---
        public static Func<Vector2Int, StructureType, bool> BuildStructureHandler;
        public static Func<Vector2Int, bool> DemolishStructureHandler;
        public static Func<Vector2Int, CropType, bool> PlantCropHandler;

        public static bool TryRequestBuildStructure(Vector2Int gridPos, StructureType type)
            => BuildStructureHandler != null && BuildStructureHandler.Invoke(gridPos, type);

        public static bool TryRequestDemolishStructure(Vector2Int gridPos)
            => DemolishStructureHandler != null && DemolishStructureHandler.Invoke(gridPos);

        public static bool TryRequestPlantCrop(Vector2Int gridPos, CropType cropType)
            => PlantCropHandler != null && PlantCropHandler.Invoke(gridPos, cropType);

        public static void RaiseHotbarLoadoutChanged(HotbarSlotData[] slots)
            => SafeInvoke(() => OnHotbarLoadoutChanged?.Invoke(slots));

        public static void RaiseAssignHotbarSelection(HotbarSlotData slotData)
            => SafeInvoke(() => OnAssignHotbarSelection?.Invoke(slotData));

        public static void RaiseSendNextWaveEarlyRequested()
            => SafeInvoke(OnSendNextWaveEarlyRequested);

        // --- Safe Invokers ---

        public static void RaiseNewGameSessionInitialized()
            => SafeInvoke(OnNewGameSessionInitialized);

        public static void RaiseGameStateChanged(GameState oldState, GameState newState)
            => SafeInvoke(() => OnGameStateChanged?.Invoke(oldState, newState));

        public static void RaiseTacticalPauseChanged(bool isPaused)
            => SafeInvoke(() => OnTacticalPauseChanged?.Invoke(isPaused));

        public static void RaiseWeatherChanged(WeatherType weather, float remainingSec)
            => SafeInvoke(() => OnWeatherChanged?.Invoke(weather, remainingSec));

        public static void RaiseResourceChanged(ResourceType type, float newAmount, float delta)
            => SafeInvoke(() => OnResourceChanged?.Invoke(type, newAmount, delta));

        public static void RaiseInsufficientResource(ResourceType missingType)
            => SafeInvoke(() => OnInsufficientResource?.Invoke(missingType));

        public static void RaiseGlobalBuffUpdated(float cropMult, float turretMult, float secondsRemaining)
            => SafeInvoke(() => OnGlobalBuffUpdated?.Invoke(cropMult, turretMult, secondsRemaining));

        public static void RaiseNetworkPriorityChanged(NetworkPriorityMode mode)
            => SafeInvoke(() => OnNetworkPriorityChanged?.Invoke(mode));

        public static void RaiseTileChanged(Vector2Int pos, TileGroundType groundType)
            => SafeInvoke(() => OnTileChanged?.Invoke(pos, groundType));

        public static void RaiseStructurePlaced(Vector2Int pos, StructureType type, IGridEntity entity)
            => SafeInvoke(() => OnStructurePlaced?.Invoke(pos, type, entity));

        public static void RaiseStructureRemoved(Vector2Int pos, StructureType type)
            => SafeInvoke(() => OnStructureRemoved?.Invoke(pos, type));

        public static void RaiseCropPlanted(Vector2Int pos, CropType cropType)
            => SafeInvoke(() => OnCropPlanted?.Invoke(pos, cropType));

        public static void RaiseCropHarvested(Vector2Int pos, CropType cropType, ResourceType yieldResource, int amount)
            => SafeInvoke(() => OnCropHarvested?.Invoke(pos, cropType, yieldResource, amount));

        public static void RaiseWaveCountdownTick(int nextWaveNumber, float secondsLeft)
            => SafeInvoke(() => OnWaveCountdownTick?.Invoke(nextWaveNumber, secondsLeft));

        public static void RaiseWaveStarted(int waveNumber, WaveDefinition waveDef)
            => SafeInvoke(() => OnWaveStarted?.Invoke(waveNumber, waveDef));

        public static void RaiseWaveCleared(int waveNumber)
            => SafeInvoke(() => OnWaveCleared?.Invoke(waveNumber));

        public static void RaiseEnemySpawned(EnemyType type, Vector2 worldPos)
            => SafeInvoke(() => OnEnemySpawned?.Invoke(type, worldPos));

        public static void RaiseEnemyKilled(EnemyType type, Vector2 worldPos)
            => SafeInvoke(() => OnEnemyKilled?.Invoke(type, worldPos));

        public static void RaiseCoreHealthChanged(float currentHp, float maxHp)
            => SafeInvoke(() => OnCoreHealthChanged?.Invoke(currentHp, maxHp));

        public static void RaiseCoreAwakeningChanged(float progress01, float currentMutagen, float targetMutagen)
            => SafeInvoke(() => OnCoreAwakeningChanged?.Invoke(progress01, currentMutagen, targetMutagen));

        public static void RaiseCoreDefibShockwave(Vector2 centerWorld, float radius, float enemyDamage)
            => SafeInvoke(() => OnCoreDefibShockwave?.Invoke(centerWorld, radius, enemyDamage));

        public static void RaiseMiniGameRequested(MiniGameKind kind)
            => SafeInvoke(() => OnMiniGameRequested?.Invoke(kind));

        public static void RaiseMiniGameCompleted(MiniGameKind kind, bool success, int scoreTier)
            => SafeInvoke(() => OnMiniGameCompleted?.Invoke(kind, success, scoreTier));

        public static void RaiseHotbarSlotSelected(int slotIndex, HotbarSlotData slotData)
            => SafeInvoke(() => OnHotbarSlotSelected?.Invoke(slotIndex, slotData));

        public static void RaiseBannerToast(string message, Color color)
            => SafeInvoke(() => OnBannerToast?.Invoke(message, color));

        public static void RaiseWorldPopupRequested(Vector2 worldPos, string text, Color color, float scale = 1f, bool isCrit = false)
            => SafeInvoke(() => OnWorldPopupRequested?.Invoke(worldPos, text, color, scale, isCrit));

        private static void SafeInvoke(Action invokeCall)
        {
            if (invokeCall == null) return;
            try
            {
                invokeCall.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameEvents] Subscriber exception: {ex}");
            }
        }
    }
}
