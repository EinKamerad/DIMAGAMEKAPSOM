using System;
using System.Collections.Generic;
using UnityEngine;

namespace BioFarme.Data
{
    /// <summary>
    /// Self-initializing in-memory balance table for "BioFarme: Сердце Колонии".
    /// Zero fragile ScriptableObject .asset inspector dependencies!
    /// Supports both static calls (BalanceDatabase.GetCrop(...)) and singleton calls (BalanceDatabase.Instance.GetCrop(...)).
    /// </summary>
    public sealed class BalanceDatabase
    {
        private static BalanceDatabase _instance;
        public static BalanceDatabase Instance => _instance ?? (_instance = new BalanceDatabase());

        public const int TotalWaves = 5;
        public const float TargetMutagenForVictory = 10f;
        public const float TargetHeartAwakeningPercent = 100f;
        public const float DeconstructRefundFraction = 0.70f;

        private readonly Dictionary<ResourceType, float> _startingResources = new Dictionary<ResourceType, float>();
        private readonly Dictionary<CropType, CropDefinition> _crops = new Dictionary<CropType, CropDefinition>();
        private readonly Dictionary<StructureType, StructureDefinition> _structures = new Dictionary<StructureType, StructureDefinition>();
        private readonly Dictionary<EnemyType, EnemyDefinition> _enemies = new Dictionary<EnemyType, EnemyDefinition>();
        private readonly Dictionary<ToolType, ToolDefinition> _tools = new Dictionary<ToolType, ToolDefinition>();
        private readonly Dictionary<WeatherType, WeatherDefinition> _weathers = new Dictionary<WeatherType, WeatherDefinition>();
        private readonly List<WaveDefinition> _waves = new List<WaveDefinition>();
        private readonly List<CropDefinition> _cropList = new List<CropDefinition>();
        private readonly List<StructureDefinition> _buildableStructures = new List<StructureDefinition>();
        private readonly List<ToolDefinition> _toolList = new List<ToolDefinition>();
        private readonly HotbarSlotData[] _defaultHotbar;

        private BalanceDatabase()
        {
            _instance = this;
            InitStartingResources();
            InitTools();
            InitCrops();
            InitStructures();
            InitEnemies();
            InitWeathers();
            InitWaves();
            _defaultHotbar = BuildDefaultHotbar();
        }

        private void InitStartingResources()
        {
            _startingResources[ResourceType.Biomass] = 250f;
            _startingResources[ResourceType.Spores] = 60f;
            _startingResources[ResourceType.BioPlasma] = 30f;
            _startingResources[ResourceType.BoneChitin] = 25f;
            _startingResources[ResourceType.Mutagen] = 0f;
        }

        private void InitTools()
        {
            RegisterTool(new ToolDefinition
            {
                toolType = ToolType.CultivatorHoe,
                displayName = "Культиватор-мотыга",
                description = "Рыхлит плоть-почву и мшистый грунт под грядки, а также собирает созревший урожай.",
                useRange = 2.6f,
                cooldownSec = 0.20f,
                combatDps = 8f,
                repairAmount = 0f,
                iconKey = "Icon_Tool_Cultivator"
            });

            RegisterTool(new ToolDefinition
            {
                toolType = ToolType.BioSprayer,
                displayName = "Био-распылитель",
                description = "Орошает грядки питательным раствором и обжигает рой едким аэрозолем вблизи.",
                useRange = 3.2f,
                cooldownSec = 0.10f,
                combatDps = 28f,
                repairAmount = 0f,
                iconKey = "Icon_Tool_BioSprayer"
            });

            RegisterTool(new ToolDefinition
            {
                toolType = ToolType.BoneHammer,
                displayName = "Костяной молот",
                description = "Чинит повреждённые био-постройки (ЛКМ) или разбирает их с возвратом 70% ресурсов (ПКМ).",
                useRange = 2.8f,
                cooldownSec = 0.28f,
                combatDps = 22f,
                repairAmount = 35f,
                iconKey = "Icon_Tool_BioAxe"
            });
        }

        private void RegisterTool(ToolDefinition def)
        {
            _tools[def.toolType] = def;
            _toolList.Add(def);
        }

        private void InitCrops()
        {
            RegisterCrop(new CropDefinition
            {
                cropType = CropType.Sporecap,
                displayName = "Спорошляпка",
                description = "Быстрорастущий симбиотический гриб. Преобразует базовую биомассу в облака спор.",
                seedCost = new ResourceCost(ResourceType.Biomass, 6f),
                seedCosts = new[] { new ResourceCost(ResourceType.Biomass, 6f) },
                growthDurationSec = 13f,
                waterNeedRate = 0.032f,
                fertileBonusMultiplier = 1.30f,
                harvestResource = ResourceType.Spores,
                harvestMin = 12,
                harvestMax = 18,
                spriteKeys = new[]
                {
                    "Crop_Sporecap_Stage0",
                    "Crop_Sporecap_Stage1",
                    "Crop_Sporecap_Stage2",
                    "Crop_Sporecap_Stage3"
                },
                iconKey = "Icon_Crop_Sporecap"
            });

            RegisterCrop(new CropDefinition
            {
                cropType = CropType.CrimsonPod,
                displayName = "Багровый стручок",
                description = "Мясистый сосудистый плод, дающий обильный урожай органической биомассы.",
                seedCost = new ResourceCost(ResourceType.Spores, 6f),
                seedCosts = new[] { new ResourceCost(ResourceType.Spores, 6f) },
                growthDurationSec = 15f,
                waterNeedRate = 0.038f,
                fertileBonusMultiplier = 1.25f,
                harvestResource = ResourceType.Biomass,
                harvestMin = 30,
                harvestMax = 44,
                spriteKeys = new[]
                {
                    "Crop_CrimsonPod_Stage0",
                    "Crop_CrimsonPod_Stage1",
                    "Crop_CrimsonPod_Stage2",
                    "Crop_CrimsonPod_Stage3"
                },
                iconKey = "Icon_Crop_CrimsonPod"
            });

            RegisterCrop(new CropDefinition
            {
                cropType = CropType.PlasmaBulb,
                displayName = "Плазмолуковица",
                description = "Биолюминесцентный клубень, накапливающий высокоэнергетическую биоплазму.",
                seedCost = new ResourceCost(ResourceType.Spores, 10f),
                seedCosts = new[]
                {
                    new ResourceCost(ResourceType.Spores, 10f),
                    new ResourceCost(ResourceType.Biomass, 10f)
                },
                growthDurationSec = 21f,
                waterNeedRate = 0.045f,
                fertileBonusMultiplier = 1.30f,
                harvestResource = ResourceType.BioPlasma,
                harvestMin = 11,
                harvestMax = 17,
                spriteKeys = new[]
                {
                    "Crop_PlasmaBulb_Stage0",
                    "Crop_PlasmaBulb_Stage1",
                    "Crop_PlasmaBulb_Stage2",
                    "Crop_PlasmaBulb_Stage3"
                },
                iconKey = "Icon_Crop_PlasmaBulb"
            });

            RegisterCrop(new CropDefinition
            {
                cropType = CropType.OssuaryRoot,
                displayName = "Костяной корень",
                description = "Минерализованный корнеплод для выращивания прочного костяного хитина.",
                seedCost = new ResourceCost(ResourceType.Spores, 8f),
                seedCosts = new[]
                {
                    new ResourceCost(ResourceType.Spores, 8f),
                    new ResourceCost(ResourceType.Biomass, 12f)
                },
                growthDurationSec = 23f,
                waterNeedRate = 0.036f,
                fertileBonusMultiplier = 1.25f,
                harvestResource = ResourceType.BoneChitin,
                harvestMin = 9,
                harvestMax = 15,
                spriteKeys = new[]
                {
                    "Crop_OssuaryRoot_Stage0",
                    "Crop_OssuaryRoot_Stage1",
                    "Crop_OssuaryRoot_Stage2",
                    "Crop_OssuaryRoot_Stage3"
                },
                iconKey = "Icon_Crop_OssuaryRoot"
            });
        }

        private void RegisterCrop(CropDefinition def)
        {
            _crops[def.cropType] = def;
            _cropList.Add(def);
        }

        private void InitStructures()
        {
            RegisterStructure(new StructureDefinition
            {
                structureType = StructureType.LivingHeartCore,
                displayName = "Живое Сердце Колонии",
                description = "Главный нейронно-сосудистый узел (3x3). Защищайте его любой ценой и пробудите 10 ед. Мутагена!",
                category = StructureCategory.Core,
                categoryLabel = "Ядро",
                costs = Array.Empty<ResourceCost>(),
                maxHp = 1500f,
                range = 5.0f,
                damage = 0f,
                fireRate = 0f,
                powerOrAmmoResource = ResourceType.Biomass,
                ammoPerShot = 0f,
                producedResource = ResourceType.Biomass,
                outputPerCycle = 2f,
                cycleIntervalSec = 4f,
                footprintSize = new Vector2Int(3, 3),
                spriteKey = "Core_LivingHeart",
                iconKey = "Core_LivingHeart",
                isPassable = false,
                blocksEnemies = true,
                isConduit = false,
                conduitTier = 0,
                canDeconstruct = false
            }, isBuildable: false);

            RegisterStructure(new StructureDefinition
            {
                structureType = StructureType.BoneWall,
                displayName = "Костяная стена",
                description = "Массивная хитиново-костная преграда. Направляет поток жуков в огневые коридоры.",
                category = StructureCategory.DefenseWall,
                categoryLabel = "Укрепления",
                costs = new[]
                {
                    new ResourceCost(ResourceType.Biomass, 10f),
                    new ResourceCost(ResourceType.BoneChitin, 4f)
                },
                maxHp = 340f,
                footprintSize = Vector2Int.one,
                spriteKey = "Wall_Bone_Hub",
                iconKey = "Icon_Wall_Bone",
                isPassable = false,
                blocksEnemies = true,
                isConduit = false,
                canDeconstruct = true
            });

            RegisterStructure(new StructureDefinition
            {
                structureType = StructureType.OrganicFence,
                displayName = "Хитиновая изгородь",
                description = "Лёгкий колючий барьер из живых сухожилий. Задерживает мелких тварей и стоит дёшево.",
                category = StructureCategory.DefenseWall,
                categoryLabel = "Укрепления",
                costs = new[]
                {
                    new ResourceCost(ResourceType.Biomass, 12f),
                    new ResourceCost(ResourceType.Spores, 2f)
                },
                maxHp = 165f,
                slowFactor = 0.45f,
                footprintSize = Vector2Int.one,
                spriteKey = "Fence_OrganicWood",
                iconKey = "Icon_Fence_Wood",
                isPassable = false,
                blocksEnemies = true,
                isConduit = false,
                canDeconstruct = true
            });

            RegisterStructure(new StructureDefinition
            {
                structureType = StructureType.VeinBiomass,
                displayName = "Био-вена",
                description = "Базовый сосудистый канал. Перекачивает биомассу и споры между узлами и турелями.",
                category = StructureCategory.Conduit,
                categoryLabel = "Био-сеть",
                costs = new[]
                {
                    new ResourceCost(ResourceType.Biomass, 4f)
                },
                maxHp = 70f,
                footprintSize = Vector2Int.one,
                spriteKey = "Vein_Biomass_H",
                iconKey = "Icon_Vein_Biomass",
                isPassable = true,
                blocksEnemies = false,
                isConduit = true,
                conduitTier = 1,
                canDeconstruct = true
            });

            RegisterStructure(new StructureDefinition
            {
                structureType = StructureType.VeinPlasma,
                displayName = "Плазмо-вена",
                description = "Усиленный сосуд для всех ресурсов. Автоматически увлажняет соседние грядки (3x3)!",
                category = StructureCategory.Conduit,
                categoryLabel = "Био-сеть",
                costs = new[]
                {
                    new ResourceCost(ResourceType.Biomass, 6f),
                    new ResourceCost(ResourceType.BioPlasma, 2f)
                },
                maxHp = 95f,
                range = 1.5f,
                footprintSize = Vector2Int.one,
                spriteKey = "Vein_Plasma_H",
                iconKey = "Icon_Vein_Plasma",
                isPassable = true,
                blocksEnemies = false,
                isConduit = true,
                conduitTier = 2,
                canDeconstruct = true
            });

            RegisterStructure(new StructureDefinition
            {
                structureType = StructureType.VeinArtery,
                displayName = "Магистральная артерия",
                description = "Скоростная артерия высокого давления. Мгновенно снабжает тяжёлые орудия и орошает почву.",
                category = StructureCategory.Conduit,
                categoryLabel = "Био-сеть",
                costs = new[]
                {
                    new ResourceCost(ResourceType.Biomass, 10f),
                    new ResourceCost(ResourceType.BioPlasma, 3f),
                    new ResourceCost(ResourceType.BoneChitin, 2f)
                },
                maxHp = 135f,
                range = 1.5f,
                footprintSize = Vector2Int.one,
                spriteKey = "Vein_Artery_H",
                iconKey = "Icon_Vein_Artery",
                isPassable = true,
                blocksEnemies = false,
                isConduit = true,
                conduitTier = 3,
                canDeconstruct = true
            });

            RegisterStructure(new StructureDefinition
            {
                structureType = StructureType.FlytrapTurret,
                displayName = "Мухоловка-страж",
                description = "Скорострельная кислотная турель ближнего и среднего боя. Питание по венам ускоряет стрельбу.",
                category = StructureCategory.Turret,
                categoryLabel = "Оборона",
                costs = new[]
                {
                    new ResourceCost(ResourceType.Biomass, 35f),
                    new ResourceCost(ResourceType.Spores, 10f)
                },
                maxHp = 190f,
                range = 4.2f,
                damage = 22f,
                fireRate = 1.45f,
                powerOrAmmoResource = ResourceType.Biomass,
                ammoPerShot = 0.4f,
                projectileSpeed = 11f,
                footprintSize = Vector2Int.one,
                spriteKey = "Turret_Flytrap_Idle",
                iconKey = "Icon_Turret_Flytrap",
                isPassable = false,
                blocksEnemies = true,
                isConduit = false,
                canDeconstruct = true
            });

            RegisterStructure(new StructureDefinition
            {
                structureType = StructureType.SporeFlowerTurret,
                displayName = "Спороцвет-миномёт",
                description = "Дальнобойный био-миномёт. Выпускает разрывные споровые капсулы, замедляющие толпы врагов.",
                category = StructureCategory.Turret,
                categoryLabel = "Оборона",
                costs = new[]
                {
                    new ResourceCost(ResourceType.Biomass, 45f),
                    new ResourceCost(ResourceType.Spores, 20f),
                    new ResourceCost(ResourceType.BoneChitin, 6f)
                },
                maxHp = 175f,
                range = 6.2f,
                damage = 26f,
                fireRate = 0.75f,
                powerOrAmmoResource = ResourceType.Spores,
                ammoPerShot = 0.5f,
                projectileSpeed = 8.5f,
                splashRadius = 1.45f,
                slowFactor = 0.40f,
                footprintSize = Vector2Int.one,
                spriteKey = "Turret_SporeFlower_Idle",
                iconKey = "Icon_Turret_SporeFlower",
                isPassable = false,
                blocksEnemies = true,
                isConduit = false,
                canDeconstruct = true
            });

            RegisterStructure(new StructureDefinition
            {
                structureType = StructureType.BileCannonTurret,
                displayName = "Желчная пушка",
                description = "Тяжёлое плазменно-желчное орудие против панцирных крабов и Альфа-Матки.",
                category = StructureCategory.Turret,
                categoryLabel = "Оборона",
                costs = new[]
                {
                    new ResourceCost(ResourceType.Biomass, 75f),
                    new ResourceCost(ResourceType.BioPlasma, 20f),
                    new ResourceCost(ResourceType.BoneChitin, 15f)
                },
                maxHp = 270f,
                range = 7.6f,
                damage = 72f,
                fireRate = 0.55f,
                powerOrAmmoResource = ResourceType.BioPlasma,
                ammoPerShot = 0.75f,
                projectileSpeed = 12.5f,
                splashRadius = 1.15f,
                footprintSize = Vector2Int.one,
                spriteKey = "Turret_BileCannon_Idle",
                iconKey = "Icon_Turret_BileCannon",
                isPassable = false,
                blocksEnemies = true,
                isConduit = false,
                canDeconstruct = true
            });

            RegisterStructure(new StructureDefinition
            {
                structureType = StructureType.PulseBulbGenerator,
                displayName = "Пульс-луковица",
                description = "Органический генератор. Синтезирует биоплазму и подаёт импульсы в подключённые вены.",
                category = StructureCategory.Production,
                categoryLabel = "Синтез",
                costs = new[]
                {
                    new ResourceCost(ResourceType.Biomass, 40f),
                    new ResourceCost(ResourceType.Spores, 12f),
                    new ResourceCost(ResourceType.BioPlasma, 6f)
                },
                maxHp = 160f,
                range = 2.0f,
                producedResource = ResourceType.BioPlasma,
                outputPerCycle = 3f,
                cycleIntervalSec = 5.0f,
                footprintSize = Vector2Int.one,
                spriteKey = "Turret_PulseBulb_Idle",
                iconKey = "Icon_Turret_PulseBulb",
                isPassable = false,
                blocksEnemies = true,
                isConduit = false,
                canDeconstruct = true
            });

            RegisterStructure(new StructureDefinition
            {
                structureType = StructureType.BioReactor,
                displayName = "Био-реактор",
                description = "Перерабатывает избыток биомассы в концентрированную биоплазму высокого давления.",
                category = StructureCategory.Production,
                categoryLabel = "Синтез",
                costs = new[]
                {
                    new ResourceCost(ResourceType.Biomass, 60f),
                    new ResourceCost(ResourceType.Spores, 15f),
                    new ResourceCost(ResourceType.BoneChitin, 10f)
                },
                maxHp = 230f,
                range = 2.5f,
                powerOrAmmoResource = ResourceType.Biomass,
                ammoPerShot = 6f,
                producedResource = ResourceType.BioPlasma,
                outputPerCycle = 6f,
                cycleIntervalSec = 5.5f,
                footprintSize = Vector2Int.one,
                spriteKey = "Building_BioReactor",
                iconKey = "Icon_Building_BioReactor",
                isPassable = false,
                blocksEnemies = true,
                isConduit = false,
                canDeconstruct = true
            });

            RegisterStructure(new StructureDefinition
            {
                structureType = StructureType.SymbioticHarvester,
                displayName = "Симбиот-сборщик",
                description = "Автоматически собирает созревшие культуры вокруг (радиус 2.5) и качает биомассу в сеть вен.",
                category = StructureCategory.Support,
                categoryLabel = "Поддержка",
                costs = new[]
                {
                    new ResourceCost(ResourceType.Biomass, 55f),
                    new ResourceCost(ResourceType.BioPlasma, 12f),
                    new ResourceCost(ResourceType.BoneChitin, 10f)
                },
                maxHp = 180f,
                range = 2.6f,
                producedResource = ResourceType.Biomass,
                outputPerCycle = 6f,
                cycleIntervalSec = 4.5f,
                footprintSize = Vector2Int.one,
                spriteKey = "Building_SymbioticHarvester",
                iconKey = "Icon_Building_Harvester",
                isPassable = false,
                blocksEnemies = true,
                isConduit = false,
                canDeconstruct = true
            });

            RegisterStructure(new StructureDefinition
            {
                structureType = StructureType.MendTendril,
                displayName = "Целительное щупальце",
                description = "Регенерирует повреждённые стены, турели и Живое Сердце в радиусе 4 клеток.",
                category = StructureCategory.Support,
                categoryLabel = "Поддержка",
                costs = new[]
                {
                    new ResourceCost(ResourceType.Biomass, 45f),
                    new ResourceCost(ResourceType.BioPlasma, 12f),
                    new ResourceCost(ResourceType.Spores, 8f)
                },
                maxHp = 165f,
                range = 4.2f,
                damage = -20f, // Healing pulse amount
                fireRate = 0.65f,
                powerOrAmmoResource = ResourceType.Biomass,
                ammoPerShot = 1.0f,
                footprintSize = Vector2Int.one,
                spriteKey = "Building_MendTendril",
                iconKey = "Icon_Building_MendTendril",
                isPassable = false,
                blocksEnemies = true,
                isConduit = false,
                canDeconstruct = true
            });

            RegisterStructure(new StructureDefinition
            {
                structureType = StructureType.MutagenIncubator,
                displayName = "Инкубатор мутагена",
                description = "Лаборатория гено-сплайсинга! Синтезирует Мутаген и открывает мини-игру усиления колонии [E].",
                category = StructureCategory.Production,
                categoryLabel = "Синтез",
                costs = new[]
                {
                    new ResourceCost(ResourceType.Biomass, 85f),
                    new ResourceCost(ResourceType.Spores, 25f),
                    new ResourceCost(ResourceType.BioPlasma, 20f),
                    new ResourceCost(ResourceType.BoneChitin, 12f)
                },
                maxHp = 290f,
                range = 3.0f,
                powerOrAmmoResource = ResourceType.BioPlasma,
                ammoPerShot = 4f,
                producedResource = ResourceType.Mutagen,
                outputPerCycle = 1.0f,
                cycleIntervalSec = 16.0f,
                footprintSize = Vector2Int.one,
                spriteKey = "Building_MutagenIncubator",
                iconKey = "Icon_Building_Incubator",
                isPassable = false,
                blocksEnemies = true,
                isConduit = false,
                canDeconstruct = true
            });
        }

        private void RegisterStructure(StructureDefinition def, bool isBuildable = true)
        {
            _structures[def.structureType] = def;
            if (isBuildable && def.structureType != StructureType.None)
            {
                _buildableStructures.Add(def);
            }
        }

        private void InitEnemies()
        {
            _enemies[EnemyType.SwarmMite] = new EnemyDefinition
            {
                enemyType = EnemyType.SwarmMite,
                displayName = "Клещ роя",
                description = "Юркий пещерный паразит, атакующий стаями по запаху биомассы.",
                maxHp = 55f,
                moveSpeed = 2.25f,
                attackDamage = 8f,
                attackInterval = 0.90f,
                attackRange = 0.80f,
                dropResource = ResourceType.Biomass,
                dropAmount = 6,
                bonusDropResource = ResourceType.Spores,
                bonusDropAmount = 1,
                isBoss = false,
                visualScale = 0.85f,
                spriteKeys = new[] { "Enemy_SwarmMite_01", "Enemy_SwarmMite_02" },
                iconKey = "Enemy_SwarmMite_01"
            };

            _enemies[EnemyType.MantisBug] = new EnemyDefinition
            {
                enemyType = EnemyType.MantisBug,
                displayName = "Богомол-жнец",
                description = "Хищный инсектоид с серповидными жвалами, рассекающий живые ткани.",
                maxHp = 140f,
                moveSpeed = 1.85f,
                attackDamage = 18f,
                attackInterval = 1.00f,
                attackRange = 0.95f,
                dropResource = ResourceType.Biomass,
                dropAmount = 10,
                bonusDropResource = ResourceType.Spores,
                bonusDropAmount = 3,
                isBoss = false,
                visualScale = 1.05f,
                spriteKeys = new[]
                {
                    "Enemy_MantisBug_01", "Enemy_MantisBug_02", "Enemy_MantisBug_03",
                    "Enemy_MantisBug_04", "Enemy_MantisBug_05", "Enemy_MantisBug_06", "Enemy_MantisBug_07"
                },
                iconKey = "Enemy_MantisBug_01"
            };

            _enemies[EnemyType.CarapaceCrab] = new EnemyDefinition
            {
                enemyType = EnemyType.CarapaceCrab,
                displayName = "Панцирный краб",
                description = "Тяжелобронированный таран улья, проламывающий костяные стены.",
                maxHp = 350f,
                moveSpeed = 1.05f,
                attackDamage = 34f,
                attackInterval = 1.35f,
                attackRange = 1.05f,
                dropResource = ResourceType.BoneChitin,
                dropAmount = 7,
                bonusDropResource = ResourceType.Biomass,
                bonusDropAmount = 12,
                isBoss = false,
                visualScale = 1.25f,
                spriteKeys = new[]
                {
                    "Enemy_CarapaceCrab_01", "Enemy_CarapaceCrab_02", "Enemy_CarapaceCrab_03",
                    "Enemy_CarapaceCrab_04", "Enemy_CarapaceCrab_05", "Enemy_CarapaceCrab_06"
                },
                iconKey = "Enemy_CarapaceCrab_01"
            };

            _enemies[EnemyType.BroodAlphaBoss] = new EnemyDefinition
            {
                enemyType = EnemyType.BroodAlphaBoss,
                displayName = "Альфа-Матка Улья",
                description = "Древний колосс глубин! Сокрушает бастионы и хранит чистейший Мутаген.",
                maxHp = 2200f,
                moveSpeed = 0.82f,
                attackDamage = 68f,
                attackInterval = 1.45f,
                attackRange = 1.55f,
                dropResource = ResourceType.Mutagen,
                dropAmount = 5,
                bonusDropResource = ResourceType.Biomass,
                bonusDropAmount = 120,
                isBoss = true,
                visualScale = 1.95f,
                spriteKeys = new[] { "Enemy_BroodAlphaBoss_01", "Enemy_BroodAlphaBoss_02" },
                iconKey = "Enemy_BroodAlphaBoss_01"
            };
        }

        private void InitWeathers()
        {
            _weathers[WeatherType.CalmSymbiosis] = new WeatherDefinition
            {
                weatherType = WeatherType.CalmSymbiosis,
                displayName = "Спокойный симбиоз",
                description = "Стабильный микроклимат пещеры. Стандартный рост культур и расход влаги.",
                hydrationDeltaPerSec = -0.018f,
                cropGrowthMultiplier = 1.0f,
                enemySpeedMultiplier = 1.0f,
                ambientTint = new Color(1.0f, 0.98f, 1.0f, 1f),
                iconKey = "Icon_Weather_CalmSymbiosis"
            };

            _weathers[WeatherType.AcidSporeRain] = new WeatherDefinition
            {
                weatherType = WeatherType.AcidSporeRain,
                displayName = "Споровый конденсат",
                description = "Питательный кислотный туман автоматически увлажняет все грядки и ускоряет рост на 20%!",
                hydrationDeltaPerSec = 0.065f,
                cropGrowthMultiplier = 1.20f,
                enemySpeedMultiplier = 0.95f,
                ambientTint = new Color(0.86f, 1.0f, 0.92f, 1f),
                iconKey = "Icon_Weather_AcidSporeRain"
            };

            _weathers[WeatherType.BioDrought] = new WeatherDefinition
            {
                weatherType = WeatherType.BioDrought,
                displayName = "Тепловой жар недр",
                description = "Горячие испарения сушат грядки вдвое быстрее. Используйте распылитель или плазмо-вены!",
                hydrationDeltaPerSec = -0.042f,
                cropGrowthMultiplier = 0.90f,
                enemySpeedMultiplier = 1.05f,
                ambientTint = new Color(1.0f, 0.90f, 0.84f, 1f),
                iconKey = "Icon_Weather_BioDrought"
            };

            _weathers[WeatherType.ParasiteSwarm] = new WeatherDefinition
            {
                weatherType = WeatherType.ParasiteSwarm,
                displayName = "Феромонная буря",
                description = "Споры улья возбуждают инсектоидов (+15% к скорости врагов), но стимулируют мутации.",
                hydrationDeltaPerSec = -0.022f,
                cropGrowthMultiplier = 1.10f,
                enemySpeedMultiplier = 1.15f,
                ambientTint = new Color(0.98f, 0.86f, 1.0f, 1f),
                iconKey = "Icon_Weather_ParasiteSwarm"
            };
        }

        private void InitWaves()
        {
            // Wave 1: Scout Mites (West + East)
            _waves.Add(new WaveDefinition
            {
                waveNumber = 1,
                displayName = "Волна 1: Разведчики роя",
                warningSubtitle = "Клещи роя движутся с западного и восточного туннелей!",
                prepDurationSec = 45f,
                waveWeather = WeatherType.CalmSymbiosis,
                entries = new[]
                {
                    new WaveSpawnEntry { enemyType = EnemyType.SwarmMite, count = 5, spawnInterval = 1.3f, startDelaySec = 0f, preferredBurrowIndex = 0 },
                    new WaveSpawnEntry { enemyType = EnemyType.SwarmMite, count = 4, spawnInterval = 1.4f, startDelaySec = 2.5f, preferredBurrowIndex = 1 }
                },
                completionRewards = new[]
                {
                    new ResourceCost(ResourceType.Biomass, 40f),
                    new ResourceCost(ResourceType.Spores, 15f)
                }
            });

            // Wave 2: Cave Hunters (West + East + North)
            _waves.Add(new WaveDefinition
            {
                waveNumber = 2,
                displayName = "Волна 2: Охотники пещер",
                warningSubtitle = "Богомолы-жнецы присоединились к стае!",
                prepDurationSec = 42f,
                waveWeather = WeatherType.AcidSporeRain,
                entries = new[]
                {
                    new WaveSpawnEntry { enemyType = EnemyType.SwarmMite, count = 7, spawnInterval = 1.05f, startDelaySec = 0f, preferredBurrowIndex = 0 },
                    new WaveSpawnEntry { enemyType = EnemyType.MantisBug, count = 3, spawnInterval = 1.8f, startDelaySec = 3.0f, preferredBurrowIndex = 1 },
                    new WaveSpawnEntry { enemyType = EnemyType.SwarmMite, count = 5, spawnInterval = 1.1f, startDelaySec = 5.0f, preferredBurrowIndex = 2 },
                    new WaveSpawnEntry { enemyType = EnemyType.MantisBug, count = 2, spawnInterval = 2.0f, startDelaySec = 7.0f, preferredBurrowIndex = 2 }
                },
                completionRewards = new[]
                {
                    new ResourceCost(ResourceType.Biomass, 55f),
                    new ResourceCost(ResourceType.BioPlasma, 15f),
                    new ResourceCost(ResourceType.BoneChitin, 10f)
                }
            });

            // Wave 3: Carapace Breach
            _waves.Add(new WaveDefinition
            {
                waveNumber = 3,
                displayName = "Волна 3: Панцирный прорыв",
                warningSubtitle = "Панцирные крабы идут на таран укреплений!",
                prepDurationSec = 42f,
                waveWeather = WeatherType.BioDrought,
                entries = new[]
                {
                    new WaveSpawnEntry { enemyType = EnemyType.SwarmMite, count = 8, spawnInterval = 0.90f, startDelaySec = 0f, preferredBurrowIndex = 0 },
                    new WaveSpawnEntry { enemyType = EnemyType.CarapaceCrab, count = 2, spawnInterval = 2.6f, startDelaySec = 2.0f, preferredBurrowIndex = 1 },
                    new WaveSpawnEntry { enemyType = EnemyType.MantisBug, count = 5, spawnInterval = 1.4f, startDelaySec = 4.0f, preferredBurrowIndex = 2 },
                    new WaveSpawnEntry { enemyType = EnemyType.CarapaceCrab, count = 2, spawnInterval = 2.8f, startDelaySec = 7.5f, preferredBurrowIndex = 3 }
                },
                completionRewards = new[]
                {
                    new ResourceCost(ResourceType.Biomass, 75f),
                    new ResourceCost(ResourceType.BioPlasma, 20f),
                    new ResourceCost(ResourceType.BoneChitin, 15f),
                    new ResourceCost(ResourceType.Mutagen, 1f)
                }
            });

            // Wave 4: Frenzy of all 4 Burrows
            _waves.Add(new WaveDefinition
            {
                waveNumber = 4,
                displayName = "Волна 4: Неистовый рой",
                warningSubtitle = "Все четыре логова открыты! Проверьте снабжение турелей!",
                prepDurationSec = 40f,
                waveWeather = WeatherType.ParasiteSwarm,
                entries = new[]
                {
                    new WaveSpawnEntry { enemyType = EnemyType.SwarmMite, count = 10, spawnInterval = 0.75f, startDelaySec = 0f, preferredBurrowIndex = 0 },
                    new WaveSpawnEntry { enemyType = EnemyType.MantisBug, count = 5, spawnInterval = 1.25f, startDelaySec = 1.5f, preferredBurrowIndex = 1 },
                    new WaveSpawnEntry { enemyType = EnemyType.CarapaceCrab, count = 3, spawnInterval = 2.2f, startDelaySec = 3.5f, preferredBurrowIndex = 2 },
                    new WaveSpawnEntry { enemyType = EnemyType.SwarmMite, count = 8, spawnInterval = 0.80f, startDelaySec = 5.0f, preferredBurrowIndex = 3 },
                    new WaveSpawnEntry { enemyType = EnemyType.MantisBug, count = 4, spawnInterval = 1.3f, startDelaySec = 7.5f, preferredBurrowIndex = 3 },
                    new WaveSpawnEntry { enemyType = EnemyType.CarapaceCrab, count = 2, spawnInterval = 2.4f, startDelaySec = 9.0f, preferredBurrowIndex = 0 }
                },
                completionRewards = new[]
                {
                    new ResourceCost(ResourceType.Biomass, 100f),
                    new ResourceCost(ResourceType.BioPlasma, 30f),
                    new ResourceCost(ResourceType.BoneChitin, 20f),
                    new ResourceCost(ResourceType.Mutagen, 2f)
                }
            });

            // Wave 5: Awakening of the Brood Alpha Boss
            _waves.Add(new WaveDefinition
            {
                waveNumber = 5,
                displayName = "Волна 5: Пробуждение Альфа-Матки",
                warningSubtitle = "ТРЕВОГА! Альфа-Матка Улья ведёт финальный штурм на Живое Сердце!",
                prepDurationSec = 45f,
                waveWeather = WeatherType.ParasiteSwarm,
                entries = new[]
                {
                    new WaveSpawnEntry { enemyType = EnemyType.SwarmMite, count = 10, spawnInterval = 0.70f, startDelaySec = 0f, preferredBurrowIndex = 0 },
                    new WaveSpawnEntry { enemyType = EnemyType.MantisBug, count = 6, spawnInterval = 1.15f, startDelaySec = 1.5f, preferredBurrowIndex = 1 },
                    new WaveSpawnEntry { enemyType = EnemyType.CarapaceCrab, count = 3, spawnInterval = 2.1f, startDelaySec = 3.0f, preferredBurrowIndex = 2 },
                    new WaveSpawnEntry { enemyType = EnemyType.BroodAlphaBoss, count = 1, spawnInterval = 1.0f, startDelaySec = 5.5f, preferredBurrowIndex = 1 },
                    new WaveSpawnEntry { enemyType = EnemyType.MantisBug, count = 4, spawnInterval = 1.25f, startDelaySec = 8.0f, preferredBurrowIndex = 3 },
                    new WaveSpawnEntry { enemyType = EnemyType.CarapaceCrab, count = 2, spawnInterval = 2.4f, startDelaySec = 10.0f, preferredBurrowIndex = 0 }
                },
                completionRewards = new[]
                {
                    new ResourceCost(ResourceType.Biomass, 150f),
                    new ResourceCost(ResourceType.Mutagen, 3f)
                }
            });
        }

        private HotbarSlotData[] BuildDefaultHotbar()
        {
            return new[]
            {
                CreateToolSlot(0, "1", ToolType.CultivatorHoe),
                CreateToolSlot(1, "2", ToolType.BioSprayer),
                CreateToolSlot(2, "3", ToolType.BoneHammer),
                CreateSeedSlot(3, "4", CropType.Sporecap),
                CreateSeedSlot(4, "5", CropType.CrimsonPod),
                CreateSeedSlot(5, "6", CropType.PlasmaBulb),
                CreateSeedSlot(6, "7", CropType.OssuaryRoot),
                CreateStructureSlot(7, "8", StructureType.BoneWall),
                CreateStructureSlot(8, "9", StructureType.VeinBiomass),
                CreateStructureSlot(9, "0", StructureType.FlytrapTurret)
            };
        }

        public static HotbarSlotData CreateToolSlot(int index, string keyLabel, ToolType toolType)
        {
            ToolDefinition def = GetTool(toolType);
            return new HotbarSlotData
            {
                slotIndex = index,
                keyLabel = keyLabel,
                category = HotbarItemCategory.Tool,
                toolType = toolType,
                displayName = def != null ? def.displayName : toolType.ToString(),
                description = def != null ? def.description : string.Empty,
                iconKey = def != null ? def.iconKey : $"Icon_Tool_{toolType}",
                costs = Array.Empty<ResourceCost>()
            };
        }

        public static HotbarSlotData CreateSeedSlot(int index, string keyLabel, CropType cropType)
        {
            CropDefinition def = GetCrop(cropType);
            return new HotbarSlotData
            {
                slotIndex = index,
                keyLabel = keyLabel,
                category = HotbarItemCategory.Seed,
                cropType = cropType,
                displayName = def != null ? $"Семена: {def.displayName}" : cropType.ToString(),
                description = def != null ? def.description : string.Empty,
                iconKey = def != null ? def.iconKey : $"Icon_Seed_{cropType}",
                costs = def != null ? def.GetSeedCosts() : Array.Empty<ResourceCost>()
            };
        }

        public static HotbarSlotData CreateStructureSlot(int index, string keyLabel, StructureType structureType)
        {
            StructureDefinition def = GetStructure(structureType);
            return new HotbarSlotData
            {
                slotIndex = index,
                keyLabel = keyLabel,
                category = HotbarItemCategory.Structure,
                structureType = structureType,
                displayName = def != null ? def.displayName : structureType.ToString(),
                description = def != null ? def.description : string.Empty,
                iconKey = def != null ? def.iconKey : $"Icon_Structure_{structureType}",
                costs = def != null && def.costs != null ? def.costs : Array.Empty<ResourceCost>()
            };
        }

        // --- Public Instance & Static Accessors ---

        public static IReadOnlyDictionary<ResourceType, float> StartingResources => Instance._startingResources;
        public static float GetStartingResource(ResourceType type)
            => Instance._startingResources.TryGetValue(type, out float val) ? val : 0f;

        public static CropDefinition GetCrop(CropType type)
            => Instance._crops.TryGetValue(type, out CropDefinition def) ? def : null;

        public static IReadOnlyList<CropDefinition> AllCrops => Instance._cropList;

        public static StructureDefinition GetStructure(StructureType type)
            => Instance._structures.TryGetValue(type, out StructureDefinition def) ? def : null;

        public static IReadOnlyList<StructureDefinition> BuildableStructures => Instance._buildableStructures;

        public static EnemyDefinition GetEnemy(EnemyType type)
            => Instance._enemies.TryGetValue(type, out EnemyDefinition def) ? def : null;

        public static ToolDefinition GetTool(ToolType type)
            => Instance._tools.TryGetValue(type, out ToolDefinition def) ? def : null;

        public static IReadOnlyList<ToolDefinition> AllTools => Instance._toolList;

        public static WeatherDefinition GetWeather(WeatherType type)
            => Instance._weathers.TryGetValue(type, out WeatherDefinition def) ? def : Instance._weathers[WeatherType.CalmSymbiosis];

        public static WaveDefinition GetWave(int oneBasedWaveNumber)
        {
            if (Instance._waves.Count == 0) return null;
            int idx = Mathf.Clamp(oneBasedWaveNumber - 1, 0, Instance._waves.Count - 1);
            return Instance._waves[idx];
        }

        public static IReadOnlyList<WaveDefinition> AllWaves => Instance._waves;

        public static HotbarSlotData[] GetDefaultHotbarCopy()
        {
            HotbarSlotData[] src = Instance._defaultHotbar;
            HotbarSlotData[] copy = new HotbarSlotData[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                HotbarSlotData s = src[i];
                copy[i] = new HotbarSlotData
                {
                    slotIndex = s.slotIndex,
                    keyLabel = s.keyLabel,
                    category = s.category,
                    toolType = s.toolType,
                    cropType = s.cropType,
                    structureType = s.structureType,
                    displayName = s.displayName,
                    description = s.description,
                    iconKey = s.iconKey,
                    costs = s.costs
                };
            }
            return copy;
        }

        public static string GetResourceNameRu(ResourceType type)
        {
            switch (type)
            {
                case ResourceType.Biomass: return "Биомасса";
                case ResourceType.Spores: return "Споры";
                case ResourceType.BioPlasma: return "Биоплазма";
                case ResourceType.BoneChitin: return "Костяной хитин";
                case ResourceType.Mutagen: return "Мутаген";
                default: return type.ToString();
            }
        }

        public static Color GetResourceColor(ResourceType type)
        {
            switch (type)
            {
                case ResourceType.Biomass: return new Color(0.35f, 0.92f, 0.42f, 1f);
                case ResourceType.Spores: return new Color(0.98f, 0.80f, 0.25f, 1f);
                case ResourceType.BioPlasma: return new Color(0.22f, 0.90f, 1.00f, 1f);
                case ResourceType.BoneChitin: return new Color(0.93f, 0.89f, 0.78f, 1f);
                case ResourceType.Mutagen: return new Color(0.96f, 0.28f, 0.88f, 1f);
                default: return Color.white;
            }
        }

        public static string GetCategoryNameRu(StructureCategory category)
        {
            switch (category)
            {
                case StructureCategory.Core: return "Ядро";
                case StructureCategory.DefenseWall: return "Укрепления";
                case StructureCategory.Conduit: return "Био-сеть";
                case StructureCategory.Turret: return "Оборона";
                case StructureCategory.Production: return "Синтез";
                case StructureCategory.Support: return "Поддержка";
                default: return category.ToString();
            }
        }
    }
}
