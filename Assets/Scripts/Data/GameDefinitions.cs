using System;
using UnityEngine;

namespace BioFarme.Data
{
    /// <summary>
    /// Represents an amount of a specific colony resource used for costs, drops, or rewards.
    /// </summary>
    [Serializable]
    public struct ResourceCost
    {
        public ResourceType resource;
        public float amount;

        public ResourceType ResourceType => resource;
        public int IntAmount => Mathf.RoundToInt(amount);

        public ResourceCost(ResourceType resource, float amount)
        {
            this.resource = resource;
            this.amount = amount;
        }

        public override string ToString()
        {
            return $"{resource}: {amount:0.#}";
        }
    }

    /// <summary>
    /// Static balance & visual metadata for a cultivable biopunk crop.
    /// </summary>
    [Serializable]
    public class CropDefinition
    {
        public CropType cropType;
        public string displayName;
        public string description;
        public ResourceCost seedCost;
        public ResourceCost[] seedCosts;
        public float growthDurationSec;
        public float waterNeedRate;
        public float fertileBonusMultiplier = 1.25f;
        public ResourceType harvestResource;
        public int harvestMin;
        public int harvestMax;
        public string[] spriteKeys; // Length 4: stage 0 (seedling) -> stage 3 (mature)
        public string iconKey;

        public ResourceCost[] GetSeedCosts()
        {
            if (seedCosts != null && seedCosts.Length > 0)
            {
                return seedCosts;
            }
            if (seedCost.amount > 0f)
            {
                return new[] { seedCost };
            }
            return Array.Empty<ResourceCost>();
        }

        public string GetStageSpriteKey(int stage)
        {
            if (spriteKeys == null || spriteKeys.Length == 0)
            {
                return iconKey ?? string.Empty;
            }
            int clamped = Mathf.Clamp(stage, 0, spriteKeys.Length - 1);
            return spriteKeys[clamped];
        }

        public int RollHarvestAmount()
        {
            int min = Mathf.Max(1, harvestMin);
            int max = Mathf.Max(min, harvestMax);
            return UnityEngine.Random.Range(min, max + 1);
        }
    }

    /// <summary>
    /// Static balance & visual metadata for a symbiotic colony structure, conduit, turret, or core.
    /// </summary>
    [Serializable]
    public class StructureDefinition
    {
        public StructureType structureType;
        public string displayName;
        public string description;
        public StructureCategory category;
        public string categoryLabel;
        public ResourceCost[] costs;
        public float maxHp;
        public float range;
        public float damage;
        public float fireRate;
        public ResourceType powerOrAmmoResource;
        public float ammoPerShot;
        public ResourceType producedResource;
        public float outputPerCycle;
        public float cycleIntervalSec;
        public Vector2Int footprintSize = Vector2Int.one;
        public string spriteKey;
        public string iconKey;
        public bool isPassable;
        public bool blocksEnemies;
        public bool isConduit;
        public int conduitTier;
        public bool canDeconstruct = true;
        public float projectileSpeed = 9f;
        public float splashRadius;
        public float slowFactor;
    }

    /// <summary>
    /// Static balance & visual metadata for a hostile hive organism.
    /// </summary>
    [Serializable]
    public class EnemyDefinition
    {
        public EnemyType enemyType;
        public string displayName;
        public string description;
        public float maxHp;
        public float moveSpeed;
        public float attackDamage;
        public float attackInterval;
        public float attackRange = 0.85f;
        public ResourceType dropResource;
        public int dropAmount;
        public ResourceType bonusDropResource;
        public int bonusDropAmount;
        public bool isBoss;
        public float visualScale = 1f;
        public string[] spriteKeys;
        public string iconKey;

        public string GetFrameSpriteKey(int frameIndex)
        {
            if (spriteKeys == null || spriteKeys.Length == 0)
            {
                return iconKey ?? string.Empty;
            }
            int idx = Mathf.Abs(frameIndex) % spriteKeys.Length;
            return spriteKeys[idx];
        }
    }

    /// <summary>
    /// Single sub-group of enemies spawned within a wave.
    /// </summary>
    [Serializable]
    public class WaveSpawnEntry
    {
        public EnemyType enemyType;
        public int count;
        public float spawnInterval = 1.1f;
        public float startDelaySec;
        public int preferredBurrowIndex = -1; // -1 = round-robin / random burrow, 0=W, 1=E, 2=N, 3=S
    }

    /// <summary>
    /// Full specification of an assault wave (Waves 1..5).
    /// </summary>
    [Serializable]
    public class WaveDefinition
    {
        public int waveNumber;
        public string displayName;
        public string warningSubtitle;
        public float prepDurationSec = 40f;
        public WeatherType waveWeather = WeatherType.CalmSymbiosis;
        public WaveSpawnEntry[] entries;
        public ResourceCost[] completionRewards;

        public WaveSpawnEntry[] SpawnEntries => entries;

        public int GetTotalEnemyCount()
        {
            if (entries == null) return 0;
            int total = 0;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] != null)
                {
                    total += Mathf.Max(0, entries[i].count);
                }
            }
            return total;
        }
    }

    /// <summary>
    /// Metadata for a hand-held colonist tool.
    /// </summary>
    [Serializable]
    public class ToolDefinition
    {
        public ToolType toolType;
        public string displayName;
        public string description;
        public float useRange = 2.6f;
        public float cooldownSec = 0.22f;
        public float combatDps;
        public float repairAmount;
        public string iconKey;
    }

    /// <summary>
    /// Metadata and simulation modifiers for subterranean cavern weather.
    /// </summary>
    [Serializable]
    public class WeatherDefinition
    {
        public WeatherType weatherType;
        public string displayName;
        public string description;
        public float hydrationDeltaPerSec; // Positive hydrates soil, negative dries soil faster
        public float cropGrowthMultiplier = 1f;
        public float enemySpeedMultiplier = 1f;
        public Color ambientTint = Color.white;
        public string iconKey;
    }

    /// <summary>
    /// Represents a selectable item in the player's hotbar or build bar.
    /// </summary>
    [Serializable]
    public class HotbarSlotData
    {
        public int slotIndex;
        public string keyLabel;
        public HotbarItemCategory category;
        public ToolType toolType;
        public CropType cropType;
        public StructureType structureType;
        public string displayName;
        public string description;
        public string iconKey;
        public ResourceCost[] costs;

        public bool IsEmpty =>
            (category == HotbarItemCategory.Tool && toolType == ToolType.None) ||
            (category == HotbarItemCategory.Seed && cropType == CropType.None) ||
            (category == HotbarItemCategory.Structure && structureType == StructureType.None);
    }
}
