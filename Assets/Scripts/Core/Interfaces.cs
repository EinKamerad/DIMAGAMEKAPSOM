using UnityEngine;
using BioFarme.Data;

namespace BioFarme.Core
{
    /// <summary>
    /// Implemented by enemies, structures, walls, turrets, and the Living Heart Core.
    /// </summary>
    public interface IDamageable
    {
        float CurrentHp { get; }
        float MaxHp { get; }
        Vector2 WorldPosition { get; }
        bool IsAlive { get; }
        void TakeDamage(float amount, Vector2 hitSource);
        void Heal(float amount);
    }

    /// <summary>
    /// Implemented by any structure, wall, turret, conduit, or core occupying grid cell(s).
    /// </summary>
    public interface IGridEntity
    {
        Vector2Int GridPos { get; }
        StructureType Type { get; }
    }

    /// <summary>
    /// Implemented by structures that receive resources from VeinNetworkSystem
    /// (turrets, reactors, mend tendrils, mutagen incubators, Living Heart Core).
    /// </summary>
    public interface IResourceConsumer : IGridEntity
    {
        bool CanAcceptResource(ResourceType resource);
        float GetResourceNeedWeight(NetworkPriorityMode priorityMode);
        float ReceiveResource(ResourceType resource, float amount);
    }

    /// <summary>
    /// Implemented by structures that push resources into VeinNetworkSystem
    /// (PulseBulbGenerator, SymbioticHarvester, BioReactor, LivingHeartCore).
    /// </summary>
    public interface IResourceProducer : IGridEntity
    {
        ResourceType OutputResourceType { get; }
        float StoredOutputAmount { get; }
        float ExtractOutput(float maxAmount);
    }

    /// <summary>
    /// Implemented by world objects that respond to player [E] / click interaction
    /// (e.g., MutagenIncubator for Gene-Splicing, LivingHeartCore for Defibrillation/Awakening, Mature Crops).
    /// </summary>
    public interface IInteractable
    {
        string GetInteractionPrompt();
        bool CanInteract();
        void Interact();
    }

    /// <summary>
    /// Optional interface for cultivated crops so SymbioticHarvester and grid tools can auto-harvest cleanly.
    /// </summary>
    public interface ICropEntity
    {
        Vector2Int GridPos { get; }
        CropType CropType { get; }
        bool IsMature { get; }
        int Harvest();
    }
}
