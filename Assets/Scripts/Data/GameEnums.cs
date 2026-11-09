using System;

namespace BioFarme.Data
{
    /// <summary>
    /// High-level state of the game loop.
    /// </summary>
    public enum GameState
    {
        MainMenu = 0,
        Playing = 1,
        TacticalPause = 2,
        MiniGame = 3,
        Paused = 4,
        Victory = 5,
        Defeat = 6
    }

    /// <summary>
    /// The exact 5 symbiotic colony resources.
    /// </summary>
    public enum ResourceType
    {
        Biomass = 0,
        Spores = 1,
        BioPlasma = 2,
        BoneChitin = 3,
        Mutagen = 4
    }

    /// <summary>
    /// Ground terrain and cultivated soil states on the 36x24 cavern grid.
    /// </summary>
    public enum TileGroundType
    {
        CavernRock = 0,
        FleshSoil = 1,
        MossFertile = 2,
        TilledBed = 3,
        WateredBed = 4,
        CoreFootprint = 5,
        HiveBurrow = 6
    }

    /// <summary>
    /// Cultivated biopunk flora species.
    /// </summary>
    public enum CropType
    {
        None = 0,
        Sporecap = 1,
        PlasmaBulb = 2,
        OssuaryRoot = 3,
        CrimsonPod = 4
    }

    /// <summary>
    /// Symbiotic colony buildings, walls, turrets, conduits, and the Living Heart Core.
    /// </summary>
    public enum StructureType
    {
        None = 0,
        LivingHeartCore = 1,
        BoneWall = 2,
        OrganicFence = 3,
        VeinBiomass = 4,
        VeinPlasma = 5,
        VeinArtery = 6,
        FlytrapTurret = 7,
        SporeFlowerTurret = 8,
        PulseBulbGenerator = 9,
        BileCannonTurret = 10,
        BioReactor = 11,
        SymbioticHarvester = 12,
        MendTendril = 13,
        MutagenIncubator = 14
    }

    /// <summary>
    /// Functional grouping for buildable colony structures.
    /// </summary>
    public enum StructureCategory
    {
        Core = 0,
        DefenseWall = 1,
        Conduit = 2,
        Turret = 3,
        Production = 4,
        Support = 5
    }

    /// <summary>
    /// Item classification inside the player's action hotbar.
    /// </summary>
    public enum HotbarItemCategory
    {
        Tool = 0,
        Seed = 1,
        Structure = 2
    }

    /// <summary>
    /// Hand-held bio-tools operated by the colonist.
    /// </summary>
    public enum ToolType
    {
        None = 0,
        CultivatorHoe = 1,
        BioSprayer = 2,
        BoneHammer = 3
    }

    /// <summary>
    /// Hostile subterranean hive creatures and Wave 5 Alpha Boss.
    /// </summary>
    public enum EnemyType
    {
        SwarmMite = 0,
        MantisBug = 1,
        CarapaceCrab = 2,
        BroodAlphaBoss = 3
    }

    /// <summary>
    /// Subterranean biosphere atmospheric conditions affecting crops and combat.
    /// </summary>
    public enum WeatherType
    {
        CalmSymbiosis = 0,
        AcidSporeRain = 1,
        BioDrought = 2,
        ParasiteSwarm = 3
    }

    /// <summary>
    /// Routing priority for resource packets flowing through the VeinNetworkSystem.
    /// </summary>
    public enum NetworkPriorityMode
    {
        Balanced = 0,
        PrioritizeTurrets = 1,
        PrioritizeReactors = 2,
        PrioritizeCore = 3
    }

    /// <summary>
    /// Interactive laboratory / emergency resuscitation mini-games.
    /// </summary>
    public enum MiniGameKind
    {
        GeneSplicing = 0,
        CoreDefibrillation = 1
    }

    /// <summary>
    /// 4-bit cardinal bitmask used for BoneWall and Vein auto-tiling (0..15).
    /// </summary>
    [Flags]
    public enum CardinalNeighborMask
    {
        None = 0,
        North = 1 << 0, // +Y (1)
        East = 1 << 1,  // +X (2)
        South = 1 << 2, // -Y (4)
        West = 1 << 3   // -X (8)
    }
}
