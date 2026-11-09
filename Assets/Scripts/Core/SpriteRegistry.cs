using System;
using System.Collections.Generic;
using UnityEngine;
using BioFarme.Data;

namespace BioFarme.Core
{
    /// <summary>
    /// Central sprite loader and procedural pixel-art fallback generator.
    /// Guarantees SpriteRegistry.Get(key) NEVER returns null even if an art asset is missing or imported as Texture2D.
    /// </summary>
    public static class SpriteRegistry
    {
        public const float DefaultPixelsPerUnit = 128f;

        private static bool _initialized;
        private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Sprite> _normalizedResourceSprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Texture2D> _normalizedResourceTextures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string[]> _aliasTable = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        private static Sprite _whitePixelSprite;
        private static Sprite _softGlowSprite;

        public static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            BuildAliasTable();
            IndexResourcesFolder("Sprites");
        }

        /// <summary>
        /// Forces a re-scan of Resources/Sprites (useful if assets were hot-loaded in Editor).
        /// </summary>
        public static void RefreshIndex()
        {
            _initialized = false;
            _cache.Clear();
            _normalizedResourceSprites.Clear();
            _normalizedResourceTextures.Clear();
            EnsureInitialized();
        }

        private static void IndexResourcesFolder(string rootFolder)
        {
            try
            {
                Sprite[] sprites = Resources.LoadAll<Sprite>(rootFolder);
                if (sprites != null)
                {
                    for (int i = 0; i < sprites.Length; i++)
                    {
                        Sprite s = sprites[i];
                        if (s == null) continue;
                        if (s.texture != null)
                        {
                            s.texture.filterMode = FilterMode.Point;
                        }
                        _cache[s.name] = s;
                        string norm = NormalizeKey(s.name);
                        if (!string.IsNullOrEmpty(norm) && !_normalizedResourceSprites.ContainsKey(norm))
                        {
                            _normalizedResourceSprites[norm] = s;
                        }

                        if (s.name.EndsWith("_0", StringComparison.Ordinal) && s.texture != null && !string.IsNullOrEmpty(s.texture.name))
                        {
                            if (!_cache.ContainsKey(s.texture.name))
                            {
                                _cache[s.texture.name] = s;
                            }
                            string texNorm = NormalizeKey(s.texture.name);
                            if (!string.IsNullOrEmpty(texNorm) && !_normalizedResourceSprites.ContainsKey(texNorm))
                            {
                                _normalizedResourceSprites[texNorm] = s;
                            }
                        }
                    }
                }

                Texture2D[] textures = Resources.LoadAll<Texture2D>(rootFolder);
                if (textures != null)
                {
                    for (int i = 0; i < textures.Length; i++)
                    {
                        Texture2D tex = textures[i];
                        if (tex == null) continue;
                        tex.filterMode = FilterMode.Point;
                        tex.wrapMode = TextureWrapMode.Clamp;

                        string norm = NormalizeKey(tex.name);
                        if (!string.IsNullOrEmpty(norm) && !_normalizedResourceTextures.ContainsKey(norm))
                        {
                            _normalizedResourceTextures[norm] = tex;
                        }

                        if (!_cache.ContainsKey(tex.name))
                        {
                            _cache[tex.name] = CreateSpriteFromTexture(tex, DefaultPixelsPerUnit);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SpriteRegistry] Resources scan warning: {ex.Message}");
            }
        }

        private static void BuildAliasTable()
        {
            _aliasTable.Clear();

            // Ground tiles -> exact curated PNGs in Resources/Sprites/Tiles/
            RegisterAliases("Tile_CavernRock", "Tile_CavernBorder_01", "CavernRock");
            RegisterAliases("Tile_FleshSoil", "Tile_FleshSoil_01", "Tile_FleshSoil_02", "FleshSoil");
            RegisterAliases("Tile_MossFertile", "Tile_MossGround_01", "Tile_MossGround_02", "MossFertile");
            RegisterAliases("Tile_TilledBed", "Tile_TilledBed_Dry", "TilledBed");
            RegisterAliases("Tile_WateredBed", "Tile_TilledBed_Watered", "WateredBed");
            RegisterAliases("Tile_CoreFootprint", "Tile_FleshSoil_02", "Tile_TilledBed_MutagenRich");
            RegisterAliases("Tile_HiveBurrow", "Tile_TilledBed_MutagenRich", "HiveBurrow");

            // Crops -> exact curated PNGs in Resources/Sprites/Crops/
            for (int stg = 0; stg <= 3; stg++)
            {
                RegisterAliases($"Crop_Sporecap_{stg}", $"Crop_Sporecap_Stage{stg}");
                RegisterAliases($"Crop_CrimsonPod_{stg}", $"Crop_CrimsonPod_Stage{stg}");
                RegisterAliases($"Crop_PlasmaBulb_{stg}", $"Crop_PlasmaBulb_Stage{stg}");
                RegisterAliases($"Crop_OssuaryRoot_{stg}", $"Crop_OssuaryRoot_Stage{stg}");
            }

            // Structures & Turrets -> exact curated PNGs in Resources/Sprites/Structures/ & Turrets/
            RegisterAliases("Structure_LivingHeartCore", "Core_LivingHeart", "LivingHeartCore");
            RegisterAliases("Structure_BoneWall", "Wall_Bone_Hub", "Wall_Bone_Horizontal", "BoneWall");
            RegisterAliases("Structure_OrganicFence", "Fence_OrganicWood", "OrganicFence");
            RegisterAliases("Structure_VeinBiomass", "Vein_Biomass_H", "Vein_Biomass_Extractor");
            RegisterAliases("Structure_VeinPlasma", "Vein_Plasma_H");
            RegisterAliases("Structure_VeinArtery", "Vein_Artery_H");
            RegisterAliases("Structure_FlytrapTurret", "Turret_Flytrap_Idle", "Turret_Flytrap_Attack");
            RegisterAliases("Structure_SporeFlowerTurret", "Turret_SporeFlower_Idle", "Turret_SporeFlower_Attack");
            RegisterAliases("Structure_PulseBulbGenerator", "Turret_PulseBulb_Idle", "Turret_PulseBulb_Active");
            RegisterAliases("Structure_BileCannonTurret", "Turret_BileCannon_Idle");
            RegisterAliases("Structure_BioReactor", "Building_BioReactor");
            RegisterAliases("Structure_SymbioticHarvester", "Building_SymbioticHarvester");
            RegisterAliases("Structure_MendTendril", "Building_MendTendril");
            RegisterAliases("Structure_MutagenIncubator", "Building_MutagenIncubator");

            // Resources -> exact curated PNGs in Resources/Sprites/UI/
            RegisterAliases("Icon_Resource_Biomass", "Icon_Biomass", "Biomass");
            RegisterAliases("Icon_Resource_Spores", "Icon_Spores", "Spores");
            RegisterAliases("Icon_Resource_BioPlasma", "Icon_BioPlasma", "BioPlasma");
            RegisterAliases("Icon_Resource_BoneChitin", "Icon_BoneChitin", "BoneChitin");
            RegisterAliases("Icon_Resource_Mutagen", "Icon_MutagenRed", "Icon_SerumGreen", "Icon_Mutagen");

            // Tools -> exact curated PNGs in Resources/Sprites/UI/
            RegisterAliases("Icon_Tool_CultivatorHoe", "Icon_Tool_Cultivator", "Icon_Tool_Shovel");
            RegisterAliases("Icon_Tool_BioSprayer", "Icon_Tool_BioSprayer");
            RegisterAliases("Icon_Tool_BoneHammer", "Icon_Tool_BioAxe");

            // Seed Icons -> exact curated PNGs in Resources/Sprites/UI/
            RegisterAliases("Icon_Seed_Sporecap", "Icon_Crop_Sporecap");
            RegisterAliases("Icon_Seed_CrimsonPod", "Icon_Crop_CrimsonPod");
            RegisterAliases("Icon_Seed_PlasmaBulb", "Icon_Crop_PlasmaBulb");
            RegisterAliases("Icon_Seed_OssuaryRoot", "Icon_Crop_OssuaryRoot");

            // Structure Icons -> exact curated PNGs in Resources/Sprites/UI/
            RegisterAliases("Icon_Structure_LivingHeartCore", "Core_LivingHeart");
            RegisterAliases("Icon_Structure_BoneWall", "Icon_Wall_Bone");
            RegisterAliases("Icon_Structure_OrganicFence", "Icon_Fence_Wood");
            RegisterAliases("Icon_Structure_VeinBiomass", "Icon_Vein_Biomass");
            RegisterAliases("Icon_Structure_VeinPlasma", "Icon_Vein_Plasma");
            RegisterAliases("Icon_Structure_VeinArtery", "Icon_Vein_Artery");
            RegisterAliases("Icon_Structure_FlytrapTurret", "Icon_Turret_Flytrap");
            RegisterAliases("Icon_Structure_SporeFlowerTurret", "Icon_Turret_SporeFlower");
            RegisterAliases("Icon_Structure_PulseBulbGenerator", "Icon_Turret_PulseBulb");
            RegisterAliases("Icon_Structure_BileCannonTurret", "Icon_Turret_BileCannon");
            RegisterAliases("Icon_Structure_BioReactor", "Icon_Building_BioReactor");
            RegisterAliases("Icon_Structure_SymbioticHarvester", "Icon_Building_Harvester");
            RegisterAliases("Icon_Structure_MendTendril", "Icon_Building_MendTendril");
            RegisterAliases("Icon_Structure_MutagenIncubator", "Icon_Building_Incubator");

            // Projectiles
            RegisterAliases("Projectile_FlytrapTurret", "Proj_AcidGlob", "Proj_AcidSplash");
            RegisterAliases("Projectile_SporeFlowerTurret", "Proj_AcidSplash", "Proj_AcidGlob");
            RegisterAliases("Projectile_BileCannonTurret", "Proj_BileSlug", "Proj_PlasmaBolt");
            RegisterAliases("Projectile_PulseBulbGenerator", "Proj_PlasmaBolt");
        }

        private static void RegisterAliases(string canonicalKey, params string[] aliases)
        {
            _aliasTable[canonicalKey] = aliases;
        }

        /// <summary>
        /// Retrieves a Sprite by key from cache, Resources/Sprites, or generates a crisp procedural biopunk fallback.
        /// NEVER returns null.
        /// </summary>
        public static Sprite Get(string key, float pixelsPerUnit = DefaultPixelsPerUnit)
        {
            EnsureInitialized();

            if (string.IsNullOrWhiteSpace(key))
            {
                key = "Fallback_Default";
            }

            if (_cache.TryGetValue(key, out Sprite cached) && cached != null)
            {
                return cached;
            }

            Sprite loaded = TryLoadFromResources(key, pixelsPerUnit);
            if (loaded != null)
            {
                _cache[key] = loaded;
                return loaded;
            }

            // Try explicit aliases
            if (_aliasTable.TryGetValue(key, out string[] aliases) && aliases != null)
            {
                for (int i = 0; i < aliases.Length; i++)
                {
                    loaded = TryLoadFromResources(aliases[i], pixelsPerUnit);
                    if (loaded != null)
                    {
                        _cache[key] = loaded;
                        return loaded;
                    }
                }
            }

            // Try normalized stripped-prefix matching
            string stripped = StripKnownPrefixes(key);
            if (!string.Equals(stripped, key, StringComparison.OrdinalIgnoreCase))
            {
                loaded = TryLoadFromResources(stripped, pixelsPerUnit);
                if (loaded != null)
                {
                    _cache[key] = loaded;
                    return loaded;
                }
            }

            // Generate crisp procedural pixel-art fallback so SpriteRegistry.Get(key) NEVER returns null
            Sprite fallback = GenerateProceduralSprite(key, pixelsPerUnit);
            _cache[key] = fallback;
            return fallback;
        }

        private static Sprite TryLoadFromResources(string candidateKey, float pixelsPerUnit)
        {
            if (string.IsNullOrEmpty(candidateKey)) return null;

            if (_cache.TryGetValue(candidateKey, out Sprite hit) && hit != null)
            {
                return hit;
            }

            string norm = NormalizeKey(candidateKey);
            if (_normalizedResourceSprites.TryGetValue(norm, out Sprite indexedSprite) && indexedSprite != null)
            {
                return indexedSprite;
            }
            if (_normalizedResourceTextures.TryGetValue(norm, out Texture2D indexedTex) && indexedTex != null)
            {
                return CreateSpriteFromTexture(indexedTex, pixelsPerUnit);
            }

            // Direct path probes (in case placed in common subfolders)
            string[] paths =
            {
                $"Sprites/{candidateKey}",
                candidateKey,
                $"Sprites/Tiles/{candidateKey}",
                $"Sprites/Crops/{candidateKey}",
                $"Sprites/Structures/{candidateKey}",
                $"Sprites/Turrets/{candidateKey}",
                $"Sprites/Enemies/{candidateKey}",
                $"Sprites/Player/{candidateKey}",
                $"Sprites/Icons/{candidateKey}",
                $"Sprites/UI/{candidateKey}"
            };

            for (int i = 0; i < paths.Length; i++)
            {
                Sprite s = Resources.Load<Sprite>(paths[i]);
                if (s != null)
                {
                    if (s.texture != null) s.texture.filterMode = FilterMode.Point;
                    return s;
                }

                Texture2D tex = Resources.Load<Texture2D>(paths[i]);
                if (tex != null)
                {
                    return CreateSpriteFromTexture(tex, pixelsPerUnit);
                }
            }

            return null;
        }

        private static Sprite CreateSpriteFromTexture(Texture2D tex, float pixelsPerUnit)
        {
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            Sprite s = Sprite.Create(
                tex,
                new Rect(0f, 0f, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit > 0f ? pixelsPerUnit : DefaultPixelsPerUnit,
                0u,
                SpriteMeshType.FullRect);
            s.name = tex.name;
            return s;
        }

        private static string NormalizeKey(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            char[] buf = new char[raw.Length];
            int len = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (char.IsLetterOrDigit(c))
                {
                    buf[len++] = char.ToLowerInvariant(c);
                }
            }
            return new string(buf, 0, len);
        }

        private static string StripKnownPrefixes(string key)
        {
            string[] prefixes =
            {
                "Icon_Structure_", "Icon_Seed_", "Icon_Tool_", "Icon_Resource_", "Icon_Enemy_", "Icon_Weather_",
                "Icon_", "Structure_", "Tile_", "Ground_", "Crop_", "Enemy_"
            };
            for (int i = 0; i < prefixes.Length; i++)
            {
                if (key.StartsWith(prefixes[i], StringComparison.OrdinalIgnoreCase))
                {
                    return key.Substring(prefixes[i].Length);
                }
            }
            return key;
        }

        // --- Standardized Key & Sprite Helpers ---

        public static string GetGroundTileKey(TileGroundType groundType, int variantSeed = 0)
        {
            switch (groundType)
            {
                case TileGroundType.CavernRock:
                    return "Tile_CavernBorder_01";
                case TileGroundType.FleshSoil:
                    return (Mathf.Abs(variantSeed) % 2 == 0) ? "Tile_FleshSoil_01" : "Tile_FleshSoil_02";
                case TileGroundType.MossFertile:
                    return (Mathf.Abs(variantSeed) % 2 == 0) ? "Tile_MossGround_01" : "Tile_MossGround_02";
                case TileGroundType.TilledBed:
                    return "Tile_TilledBed_Dry";
                case TileGroundType.WateredBed:
                    return "Tile_TilledBed_Watered";
                case TileGroundType.CoreFootprint:
                    return "Tile_TilledBed_BoneRich";
                case TileGroundType.HiveBurrow:
                    return "Tile_TilledBed_MutagenRich";
                default:
                    return $"Tile_{groundType}";
            }
        }

        public static Sprite GetGroundTileSprite(TileGroundType groundType, int variantSeed = 0)
            => Get(GetGroundTileKey(groundType, variantSeed));

        public static string GetCropStageKey(CropType cropType, int stage)
            => $"Crop_{cropType}_Stage{Mathf.Clamp(stage, 0, 3)}";

        public static Sprite GetCropStageSprite(CropType cropType, int stage)
            => Get(GetCropStageKey(cropType, stage));

        public static string GetStructureKey(StructureType type)
        {
            StructureDefinition def = BalanceDatabase.GetStructure(type);
            if (def != null && !string.IsNullOrEmpty(def.spriteKey))
            {
                return def.spriteKey;
            }
            return $"Structure_{type}";
        }

        public static Sprite GetStructureSprite(StructureType type)
            => Get(GetStructureKey(type));

        public static string GetVeinAutoTileKey(StructureType veinType, int neighborMask)
            => $"Vein_{veinType}_{neighborMask & 15}";

        public static Sprite GetVeinAutoTileSprite(StructureType veinType, int neighborMask)
        {
            EnsureInitialized();
            int mask = neighborMask & 15;
            string cacheKey = GetVeinAutoTileKey(veinType, mask);
            if (_cache.TryGetValue(cacheKey, out Sprite cached) && cached != null)
            {
                return cached;
            }

            string prefix = veinType == StructureType.VeinArtery
                ? "Vein_Artery"
                : (veinType == StructureType.VeinPlasma ? "Vein_Plasma" : "Vein_Biomass");

            string shapeSuffix;
            if (mask == 1 || mask == 4 || mask == 5)
            {
                shapeSuffix = "V";
            }
            else if (mask == 3 || mask == 6 || mask == 9 || mask == 12)
            {
                shapeSuffix = "Corner";
            }
            else if (mask == 7 || mask == 11 || mask == 13 || mask == 14 || mask == 15)
            {
                shapeSuffix = "T";
            }
            else
            {
                shapeSuffix = "H";
            }

            Sprite curated = TryLoadFromResources($"{prefix}_{shapeSuffix}", DefaultPixelsPerUnit);
            if (curated != null)
            {
                _cache[cacheKey] = curated;
                return curated;
            }

            Sprite generated = GenerateVeinMaskSprite(veinType, mask);
            _cache[cacheKey] = generated;
            return generated;
        }

        public static string GetBoneWallAutoTileKey(int neighborMask)
            => $"BoneWall_{neighborMask & 15}";

        public static Sprite GetBoneWallAutoTileSprite(int neighborMask)
            => GetWallAutoTileSprite(StructureType.BoneWall, neighborMask);

        public static Sprite GetWallAutoTileSprite(StructureType wallType, int neighborMask)
        {
            EnsureInitialized();
            int mask = neighborMask & 15;
            string key = $"{wallType}_{mask}";
            if (_cache.TryGetValue(key, out Sprite cached) && cached != null)
            {
                return cached;
            }

            if (wallType == StructureType.OrganicFence)
            {
                Sprite fenceRes = TryLoadFromResources("Fence_OrganicWood", DefaultPixelsPerUnit);
                if (fenceRes != null)
                {
                    _cache[key] = fenceRes;
                    return fenceRes;
                }
            }
            else
            {
                string boneAssetName;
                switch (mask)
                {
                    case 2:
                    case 8:
                    case 10:
                        boneAssetName = "Wall_Bone_Horizontal";
                        break;
                    case 1:
                    case 4:
                    case 5:
                        boneAssetName = "Wall_Bone_Vertical";
                        break;
                    case 3: // N + E
                        boneAssetName = "Wall_Bone_CornerBL";
                        break;
                    case 9: // N + W
                        boneAssetName = "Wall_Bone_CornerBR";
                        break;
                    case 6: // S + E
                        boneAssetName = "Wall_Bone_CornerTL";
                        break;
                    case 12: // S + W
                        boneAssetName = "Wall_Bone_CornerTR";
                        break;
                    default:
                        boneAssetName = "Wall_Bone_Hub";
                        break;
                }

                Sprite boneRes = TryLoadFromResources(boneAssetName, DefaultPixelsPerUnit);
                if (boneRes != null)
                {
                    _cache[key] = boneRes;
                    return boneRes;
                }
            }

            Sprite wallSprite = GenerateWallMaskSprite(wallType, mask);
            _cache[key] = wallSprite;
            return wallSprite;
        }

        public static string GetEnemyFrameKey(EnemyType enemyType, int frame = 0)
        {
            EnemyDefinition def = BalanceDatabase.GetEnemy(enemyType);
            if (def != null && def.spriteKeys != null && def.spriteKeys.Length > 0)
            {
                return def.GetFrameSpriteKey(frame);
            }
            return $"Enemy_{enemyType}_0{(Mathf.Abs(frame) % 2) + 1}";
        }

        public static Sprite GetEnemyFrameSprite(EnemyType enemyType, int frame = 0)
        {
            string frameKey = GetEnemyFrameKey(enemyType, frame);
            return Get(frameKey);
        }

        public static string GetPlayerDirectionalKey(Vector2 direction, int frame = 0, bool isShooting = false)
        {
            int f = (Mathf.Abs(frame) % 2) + 1;
            if (direction.sqrMagnitude < 0.01f)
            {
                return isShooting ? "Player_Hazmat_ShootRight" : "Player_Hazmat_IdleDown";
            }

            if (direction.y > 0.35f && Mathf.Abs(direction.x) > 0.25f)
            {
                return isShooting ? "Player_Hazmat_ShootUpRight" : $"Player_Hazmat_WalkUpRight{f}";
            }

            if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.y))
            {
                return isShooting ? "Player_Hazmat_ShootRight" : $"Player_Hazmat_WalkRight{f}";
            }

            return direction.y >= 0f ? "Player_Hazmat_IdleUp" : "Player_Hazmat_IdleDown";
        }

        public static Sprite GetPlayerDirectionalSprite(Vector2 direction, int frame = 0, bool isShooting = false)
        {
            string key = GetPlayerDirectionalKey(direction, frame, isShooting);
            return Get(key);
        }

        public static string GetResourceIconKey(ResourceType type) => $"Icon_Resource_{type}";
        public static Sprite GetResourceIcon(ResourceType type) => Get(GetResourceIconKey(type), 64f);

        public static string GetToolIconKey(ToolType type)
        {
            ToolDefinition def = BalanceDatabase.GetTool(type);
            return def != null && !string.IsNullOrEmpty(def.iconKey) ? def.iconKey : $"Icon_Tool_{type}";
        }
        public static Sprite GetToolIcon(ToolType type) => Get(GetToolIconKey(type), 64f);

        public static string GetCropIconKey(CropType type)
        {
            CropDefinition def = BalanceDatabase.GetCrop(type);
            return def != null && !string.IsNullOrEmpty(def.iconKey) ? def.iconKey : $"Icon_Seed_{type}";
        }
        public static Sprite GetCropIcon(CropType type) => Get(GetCropIconKey(type), 64f);

        public static string GetStructureIconKey(StructureType type)
        {
            StructureDefinition def = BalanceDatabase.GetStructure(type);
            return def != null && !string.IsNullOrEmpty(def.iconKey) ? def.iconKey : $"Icon_Structure_{type}";
        }
        public static Sprite GetStructureIcon(StructureType type) => Get(GetStructureIconKey(type), 64f);

        public static string GetWeatherIconKey(WeatherType type) => $"Icon_Weather_{type}";
        public static Sprite GetWeatherIcon(WeatherType type) => Get(GetWeatherIconKey(type), 64f);

        public static Sprite GetProjectileSprite(StructureType turretType)
            => Get($"Projectile_{turretType}", 128f);

        public static Sprite GetPacketSprite(ResourceType resourceType)
            => Get($"Packet_{resourceType}", 128f);

        public static Sprite GetWhiteSquareSprite()
        {
            if (_whitePixelSprite != null) return _whitePixelSprite;
            Texture2D tex = new Texture2D(8, 8, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "Generated_WhiteSquare"
            };
            Color[] px = new Color[64];
            for (int i = 0; i < px.Length; i++) px[i] = Color.white;
            tex.SetPixels(px);
            tex.Apply(false, false);
            _whitePixelSprite = Sprite.Create(tex, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f), 8f);
            return _whitePixelSprite;
        }

        public static Sprite GetSoftCircleSprite()
        {
            if (_softGlowSprite != null) return _softGlowSprite;
            const int size = 32;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "Generated_SoftCircle"
            };
            Color[] px = new Color[size * size];
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - dist);
                    px[y * size + x] = new Color(1f, 1f, 1f, a * a);
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, false);
            _softGlowSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 32f);
            return _softGlowSprite;
        }

        // --- Procedural Biopunk Pixel-Art Generators ---

        private static Sprite GenerateVeinMaskSprite(StructureType veinType, int mask)
        {
            const int size = 128;
            Color outerWall;
            Color midTissue;
            Color innerLumen;
            int halfThickness;

            switch (veinType)
            {
                case StructureType.VeinPlasma:
                    outerWall = new Color(0.10f, 0.28f, 0.38f, 0.96f);
                    midTissue = new Color(0.14f, 0.58f, 0.76f, 1f);
                    innerLumen = new Color(0.52f, 0.96f, 1.00f, 1f);
                    halfThickness = 15;
                    break;
                case StructureType.VeinArtery:
                    outerWall = new Color(0.32f, 0.08f, 0.24f, 0.98f);
                    midTissue = new Color(0.78f, 0.16f, 0.46f, 1f);
                    innerLumen = new Color(1.00f, 0.55f, 0.88f, 1f);
                    halfThickness = 19;
                    break;
                default: // VeinBiomass
                    outerWall = new Color(0.26f, 0.10f, 0.16f, 0.95f);
                    midTissue = new Color(0.56f, 0.18f, 0.26f, 1f);
                    innerLumen = new Color(0.42f, 0.88f, 0.46f, 1f);
                    halfThickness = 13;
                    break;
            }

            Color[] px = new Color[size * size];
            int center = size / 2;

            bool north = (mask & (int)CardinalNeighborMask.North) != 0;
            bool east = (mask & (int)CardinalNeighborMask.East) != 0;
            bool south = (mask & (int)CardinalNeighborMask.South) != 0;
            bool west = (mask & (int)CardinalNeighborMask.West) != 0;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Quantize to 4x4 pixel-art blocks for crisp aesthetic
                    int qx = (x / 4) * 4 + 2;
                    int qy = (y / 4) * 4 + 2;

                    float minDist = Vector2.Distance(new Vector2(qx, qy), new Vector2(center, center));

                    // Organic sine wave along arms
                    float waveX = Mathf.Sin(qy * 0.12f) * 2.5f;
                    float waveY = Mathf.Sin(qx * 0.12f) * 2.5f;

                    if (north && qy >= center)
                        minDist = Mathf.Min(minDist, Mathf.Abs(qx - (center + waveX)));
                    if (south && qy <= center)
                        minDist = Mathf.Min(minDist, Mathf.Abs(qx - (center + waveX)));
                    if (east && qx >= center)
                        minDist = Mathf.Min(minDist, Mathf.Abs(qy - (center + waveY)));
                    if (west && qx <= center)
                        minDist = Mathf.Min(minDist, Mathf.Abs(qy - (center + waveY)));

                    float hubRadius = (mask == 0) ? halfThickness + 6f : halfThickness + 3f;
                    float centerDist = Vector2.Distance(new Vector2(qx, qy), new Vector2(center, center));
                    if (centerDist < hubRadius)
                    {
                        minDist = Mathf.Min(minDist, centerDist * (halfThickness / hubRadius));
                    }

                    if (minDist <= halfThickness)
                    {
                        if (minDist < halfThickness * 0.38f)
                            px[y * size + x] = innerLumen;
                        else if (minDist < halfThickness * 0.72f)
                            px[y * size + x] = midTissue;
                        else
                            px[y * size + x] = outerWall;
                    }
                }
            }

            return BakeTexture(px, size, size, $"Generated_Vein_{veinType}_{mask}", DefaultPixelsPerUnit);
        }

        private static Sprite GenerateWallMaskSprite(StructureType wallType, int mask)
        {
            const int size = 128;
            Color[] px = new Color[size * size];

            bool isFence = wallType == StructureType.OrganicFence;
            Color border = isFence ? new Color(0.20f, 0.12f, 0.10f, 1f) : new Color(0.22f, 0.18f, 0.16f, 1f);
            Color body = isFence ? new Color(0.46f, 0.30f, 0.22f, 1f) : new Color(0.82f, 0.77f, 0.66f, 1f);
            Color highlight = isFence ? new Color(0.62f, 0.78f, 0.32f, 1f) : new Color(0.95f, 0.92f, 0.84f, 1f);
            Color flesh = new Color(0.52f, 0.16f, 0.24f, 1f);

            int center = size / 2;
            int halfArm = isFence ? 16 : 28;
            int hubHalf = isFence ? 26 : 38;

            bool north = (mask & 1) != 0;
            bool east = (mask & 2) != 0;
            bool south = (mask & 4) != 0;
            bool west = (mask & 8) != 0;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int qx = (x / 4) * 4 + 2;
                    int qy = (y / 4) * 4 + 2;

                    bool inside = Mathf.Abs(qx - center) <= hubHalf && Mathf.Abs(qy - center) <= hubHalf;
                    if (north && qy >= center && Mathf.Abs(qx - center) <= halfArm) inside = true;
                    if (south && qy <= center && Mathf.Abs(qx - center) <= halfArm) inside = true;
                    if (east && qx >= center && Mathf.Abs(qy - center) <= halfArm) inside = true;
                    if (west && qx <= center && Mathf.Abs(qy - center) <= halfArm) inside = true;

                    if (!inside) continue;

                    bool isEdge =
                        (Mathf.Abs(qx - center) >= hubHalf - 6 && Mathf.Abs(qy - center) <= hubHalf && !((east && qx > center && Mathf.Abs(qy - center) < halfArm - 4) || (west && qx < center && Mathf.Abs(qy - center) < halfArm - 4))) ||
                        (Mathf.Abs(qy - center) >= hubHalf - 6 && Mathf.Abs(qx - center) <= hubHalf && !((north && qy > center && Mathf.Abs(qx - center) < halfArm - 4) || (south && qy < center && Mathf.Abs(qx - center) < halfArm - 4))) ||
                        (qy > center + hubHalf && Mathf.Abs(qx - center) >= halfArm - 5) ||
                        (qy < center - hubHalf && Mathf.Abs(qx - center) >= halfArm - 5) ||
                        (qx > center + hubHalf && Mathf.Abs(qy - center) >= halfArm - 5) ||
                        (qx < center - hubHalf && Mathf.Abs(qy - center) >= halfArm - 5);

                    int hash = ((qx * 73856093) ^ (qy * 19349663)) & 0xFF;
                    if (isEdge)
                    {
                        px[y * size + x] = border;
                    }
                    else if (Mathf.Abs(qx - center) < 12 && Mathf.Abs(qy - center) < 12)
                    {
                        px[y * size + x] = flesh;
                    }
                    else if (hash > 205)
                    {
                        px[y * size + x] = highlight;
                    }
                    else
                    {
                        px[y * size + x] = body;
                    }
                }
            }

            return BakeTexture(px, size, size, $"Generated_Wall_{wallType}_{mask}", DefaultPixelsPerUnit);
        }

        private static Sprite GenerateProceduralSprite(string key, float pixelsPerUnit)
        {
            string lower = key.ToLowerInvariant();

            // 1. Living Heart Core (3x3 = 384x384 at 128 PPU)
            if (lower.Contains("livingheartcore") && !lower.StartsWith("icon_"))
            {
                return GenerateHeartCoreSprite();
            }

            // 2. Ground Tiles (128x128 full opaque cell)
            if (lower.StartsWith("tile_"))
            {
                return GenerateGroundTileProcedural(lower);
            }

            // 3. Crops (128x128 stage-aware flora)
            if (lower.StartsWith("crop_"))
            {
                return GenerateCropProcedural(lower);
            }

            // 4. Enemies (128x128 or 256x256 for Alpha Boss)
            if (lower.StartsWith("enemy_"))
            {
                return GenerateEnemyProcedural(lower);
            }

            // 5. Player colonist
            if (lower.StartsWith("player_"))
            {
                return GeneratePlayerProcedural(lower);
            }

            // 6. Projectiles & Vein Packets (64x64)
            if (lower.StartsWith("projectile_") || lower.StartsWith("packet_"))
            {
                return GenerateOrbProcedural(lower);
            }

            // 7. UI Icons (64x64)
            if (lower.StartsWith("icon_"))
            {
                return GenerateIconProcedural(lower, pixelsPerUnit > 0f ? pixelsPerUnit : 64f);
            }

            // 8. Structures (128x128)
            return GenerateStructureProcedural(lower, pixelsPerUnit);
        }

        private static Sprite GenerateHeartCoreSprite()
        {
            const int size = 384;
            Color[] px = new Color[size * size];
            float cx = size * 0.5f;
            float cy = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int qx = (x / 4) * 4 + 2;
                    int qy = (y / 4) * 4 + 2;
                    float dx = (qx - cx) / 160f;
                    float dy = (qy - cy) / 160f;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float angle = Mathf.Atan2(dy, dx);
                    float lobe = 0.88f + 0.10f * Mathf.Sin(angle * 5f) + 0.05f * Mathf.Cos(angle * 3f);

                    if (dist <= lobe)
                    {
                        if (dist > lobe - 0.08f)
                            px[y * size + x] = new Color(0.22f, 0.05f, 0.14f, 1f); // Chitin pericardium rim
                        else if (dist < 0.28f)
                            px[y * size + x] = new Color(1.0f, 0.38f, 0.76f, 1f); // Glowing mutagenic nucleus
                        else if (dist < 0.52f)
                            px[y * size + x] = new Color(0.86f, 0.14f, 0.36f, 1f); // Crimson myocardium
                        else
                        {
                            float vein = Mathf.Abs(Mathf.Sin(angle * 8f + dist * 6f));
                            px[y * size + x] = vein < 0.18f
                                ? new Color(0.30f, 0.92f, 0.96f, 1f)
                                : new Color(0.54f, 0.12f, 0.26f, 1f);
                        }
                    }
                }
            }
            return BakeTexture(px, size, size, "Generated_LivingHeartCore", DefaultPixelsPerUnit);
        }

        private static Sprite GenerateGroundTileProcedural(string lowerKey)
        {
            const int size = 128;
            Color[] px = new Color[size * size];

            Color cBase;
            Color cAccent;
            Color cDetail;

            if (lowerKey.Contains("cavernrock"))
            {
                cBase = new Color(0.11f, 0.09f, 0.14f, 1f);
                cAccent = new Color(0.17f, 0.14f, 0.22f, 1f);
                cDetail = new Color(0.06f, 0.05f, 0.09f, 1f);
            }
            else if (lowerKey.Contains("mossfertile"))
            {
                cBase = new Color(0.14f, 0.25f, 0.19f, 1f);
                cAccent = new Color(0.20f, 0.42f, 0.28f, 1f);
                cDetail = new Color(0.32f, 0.65f, 0.42f, 1f);
            }
            else if (lowerKey.Contains("tilledbed"))
            {
                cBase = new Color(0.25f, 0.14f, 0.15f, 1f);
                cAccent = new Color(0.18f, 0.09f, 0.10f, 1f);
                cDetail = new Color(0.34f, 0.20f, 0.19f, 1f);
            }
            else if (lowerKey.Contains("wateredbed"))
            {
                cBase = new Color(0.15f, 0.11f, 0.16f, 1f);
                cAccent = new Color(0.10f, 0.22f, 0.28f, 1f);
                cDetail = new Color(0.22f, 0.52f, 0.62f, 1f);
            }
            else if (lowerKey.Contains("corefootprint"))
            {
                cBase = new Color(0.28f, 0.10f, 0.18f, 1f);
                cAccent = new Color(0.42f, 0.14f, 0.24f, 1f);
                cDetail = new Color(0.65f, 0.20f, 0.38f, 1f);
            }
            else if (lowerKey.Contains("hiveburrow"))
            {
                cBase = new Color(0.16f, 0.08f, 0.14f, 1f);
                cAccent = new Color(0.05f, 0.02f, 0.06f, 1f);
                cDetail = new Color(0.95f, 0.52f, 0.15f, 1f);
            }
            else // FleshSoil
            {
                cBase = new Color(0.20f, 0.12f, 0.16f, 1f);
                cAccent = new Color(0.25f, 0.15f, 0.19f, 1f);
                cDetail = new Color(0.15f, 0.09f, 0.12f, 1f);
            }

            bool isBed = lowerKey.Contains("tilledbed") || lowerKey.Contains("wateredbed");
            bool isBurrow = lowerKey.Contains("hiveburrow");

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int bx = x / 4;
                    int by = y / 4;
                    int hash = ((bx * 374761393) ^ (by * 668265263)) & 0xFF;

                    if (isBurrow)
                    {
                        float d = Vector2.Distance(new Vector2(x, y), new Vector2(64f, 64f));
                        if (d < 36f)
                        {
                            // Glowing insect eyes in the dark burrow pit
                            bool eye = (Mathf.Abs(x - 52) < 4 && Mathf.Abs(y - 68) < 4) ||
                                       (Mathf.Abs(x - 76) < 4 && Mathf.Abs(y - 62) < 4);
                            px[y * size + x] = eye ? cDetail : cAccent;
                            continue;
                        }
                        if (d < 48f)
                        {
                            px[y * size + x] = new Color(0.42f, 0.28f, 0.22f, 1f);
                            continue;
                        }
                    }

                    if (isBed && x >= 10 && x <= 118 && y >= 10 && y <= 118)
                    {
                        int furrow = (y / 12) % 2;
                        px[y * size + x] = furrow == 0 ? cAccent : (hash > 215 ? cDetail : cBase);
                        continue;
                    }

                    // Subtle grid border shade
                    if (x < 2 || y < 2 || x >= size - 2 || y >= size - 2)
                    {
                        px[y * size + x] = Color.Lerp(cBase, Color.black, 0.16f);
                    }
                    else if (hash > 228)
                    {
                        px[y * size + x] = cDetail;
                    }
                    else if (hash > 150)
                    {
                        px[y * size + x] = cAccent;
                    }
                    else
                    {
                        px[y * size + x] = cBase;
                    }
                }
            }

            return BakeTexture(px, size, size, $"Generated_{lowerKey}", DefaultPixelsPerUnit);
        }

        private static Sprite GenerateCropProcedural(string lowerKey)
        {
            const int size = 128;
            Color[] px = new Color[size * size];

            int stage = 3;
            if (lowerKey.EndsWith("_0")) stage = 0;
            else if (lowerKey.EndsWith("_1")) stage = 1;
            else if (lowerKey.EndsWith("_2")) stage = 2;

            Color stemColor = new Color(0.22f, 0.56f, 0.30f, 1f);
            Color capColor = new Color(0.95f, 0.78f, 0.24f, 1f);
            Color glowColor = new Color(1.0f, 0.95f, 0.55f, 1f);

            if (lowerKey.Contains("crimsonpod"))
            {
                stemColor = new Color(0.35f, 0.16f, 0.22f, 1f);
                capColor = new Color(0.86f, 0.18f, 0.32f, 1f);
                glowColor = new Color(0.45f, 0.95f, 0.52f, 1f);
            }
            else if (lowerKey.Contains("plasmabulb"))
            {
                stemColor = new Color(0.16f, 0.42f, 0.48f, 1f);
                capColor = new Color(0.18f, 0.82f, 0.98f, 1f);
                glowColor = new Color(0.75f, 1.0f, 1.0f, 1f);
            }
            else if (lowerKey.Contains("ossuaryroot"))
            {
                stemColor = new Color(0.45f, 0.38f, 0.32f, 1f);
                capColor = new Color(0.88f, 0.84f, 0.74f, 1f);
                glowColor = new Color(0.98f, 0.96f, 0.90f, 1f);
            }

            int heightPx = 24 + stage * 24; // 24 -> 48 -> 72 -> 96
            int bulbRadius = 10 + stage * 9; // 10 -> 19 -> 28 -> 37
            int baseY = 16;

            for (int y = baseY; y < baseY + heightPx && y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int stemHalf = 5 + stage * 2;
                    if (Mathf.Abs(x - 64) <= stemHalf && y < baseY + heightPx * 0.65f)
                    {
                        px[y * size + x] = Mathf.Abs(x - 64) == stemHalf ? Color.Lerp(stemColor, Color.black, 0.45f) : stemColor;
                    }
                }
            }

            Vector2 bulbCenter = new Vector2(64f, baseY + heightPx * 0.72f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2((x / 3) * 3, (y / 3) * 3), bulbCenter);
                    if (d <= bulbRadius)
                    {
                        if (d > bulbRadius - 4f)
                            px[y * size + x] = Color.Lerp(capColor, Color.black, 0.5f);
                        else if (stage >= 3 && d < bulbRadius * 0.42f)
                            px[y * size + x] = glowColor;
                        else
                            px[y * size + x] = capColor;
                    }
                }
            }

            return BakeTexture(px, size, size, $"Generated_{lowerKey}", DefaultPixelsPerUnit);
        }

        private static Sprite GenerateEnemyProcedural(string lowerKey)
        {
            bool isBoss = lowerKey.Contains("broodalphaboss");
            int size = isBoss ? 256 : 128;
            Color[] px = new Color[size * size];

            Color shell = new Color(0.75f, 0.22f, 0.26f, 1f);
            Color eye = new Color(1.0f, 0.88f, 0.20f, 1f);
            if (lowerKey.Contains("mantisbug"))
            {
                shell = new Color(0.45f, 0.78f, 0.22f, 1f);
                eye = new Color(1.0f, 0.25f, 0.35f, 1f);
            }
            else if (lowerKey.Contains("carapacecrab"))
            {
                shell = new Color(0.56f, 0.42f, 0.34f, 1f);
                eye = new Color(0.25f, 0.95f, 1.0f, 1f);
            }
            else if (isBoss)
            {
                shell = new Color(0.62f, 0.14f, 0.52f, 1f);
                eye = new Color(0.35f, 1.0f, 0.55f, 1f);
            }

            int frame = lowerKey.EndsWith("_1") ? 1 : 0;
            float half = size * 0.5f;
            float bodyRad = size * 0.32f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int qx = (x / 4) * 4 + 2;
                    int qy = (y / 4) * 4 + 2;
                    float dx = qx - half;
                    float dy = qy - half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy * 0.8f);

                    // Insect legs
                    float legWave = Mathf.Sin((qy + frame * 16) * 0.22f) * (size * 0.14f);
                    bool isLeg = Mathf.Abs(dy) < bodyRad * 0.85f &&
                                 Mathf.Abs(Mathf.Abs(dx) - (bodyRad + Mathf.Abs(legWave))) < (size * 0.045f);

                    if (d <= bodyRad)
                    {
                        if (d > bodyRad - 5f)
                            px[y * size + x] = Color.Lerp(shell, Color.black, 0.55f);
                        else if (dy > bodyRad * 0.25f && Mathf.Abs(Mathf.Abs(dx) - bodyRad * 0.38f) < size * 0.06f)
                            px[y * size + x] = eye;
                        else
                            px[y * size + x] = shell;
                    }
                    else if (isLeg)
                    {
                        px[y * size + x] = Color.Lerp(shell, Color.black, 0.35f);
                    }
                }
            }

            return BakeTexture(px, size, size, $"Generated_{lowerKey}", DefaultPixelsPerUnit);
        }

        private static Sprite GeneratePlayerProcedural(string lowerKey)
        {
            const int size = 128;
            Color[] px = new Color[size * size];

            Color suit = new Color(0.24f, 0.68f, 0.62f, 1f);
            Color visor = new Color(0.65f, 0.98f, 1.0f, 1f);
            Color pack = new Color(0.32f, 0.88f, 0.42f, 1f);
            Color outline = new Color(0.08f, 0.14f, 0.18f, 1f);

            for (int y = 16; y < 112; y++)
            {
                for (int x = 32; x < 96; x++)
                {
                    float dx = (x - 64f) / 26f;
                    float dy = (y - 60f) / 42f;
                    float d = dx * dx + dy * dy;
                    if (d > 1f) continue;

                    if (d > 0.78f)
                        px[y * size + x] = outline;
                    else if (y > 72 && y < 92 && x > 48 && x < 80 && !lowerKey.Contains("up"))
                        px[y * size + x] = visor;
                    else if (y >= 40 && y <= 72 && (x < 44 || x > 84))
                        px[y * size + x] = pack;
                    else
                        px[y * size + x] = suit;
                }
            }

            return BakeTexture(px, size, size, $"Generated_{lowerKey}", DefaultPixelsPerUnit);
        }

        private static Sprite GenerateOrbProcedural(string lowerKey)
        {
            const int size = 64;
            Color[] px = new Color[size * size];
            Color c = new Color(0.40f, 0.95f, 0.45f, 1f);
            if (lowerKey.Contains("bioplasma") || lowerKey.Contains("bile"))
                c = new Color(0.25f, 0.92f, 1.0f, 1f);
            else if (lowerKey.Contains("spores") || lowerKey.Contains("sporeflower"))
                c = new Color(1.0f, 0.82f, 0.24f, 1f);
            else if (lowerKey.Contains("bonechitin"))
                c = new Color(0.94f, 0.90f, 0.80f, 1f);
            else if (lowerKey.Contains("mutagen"))
                c = new Color(0.98f, 0.30f, 0.90f, 1f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(32f, 32f));
                    if (d <= 18f)
                    {
                        px[y * size + x] = d < 8f ? Color.white : c;
                    }
                }
            }

            return BakeTexture(px, size, size, $"Generated_{lowerKey}", DefaultPixelsPerUnit);
        }

        private static Sprite GenerateIconProcedural(string lowerKey, float ppu)
        {
            const int size = 64;
            Color[] px = new Color[size * size];
            Color accent = new Color(0.38f, 0.90f, 0.48f, 1f);

            if (lowerKey.Contains("spore")) accent = new Color(0.98f, 0.80f, 0.25f, 1f);
            else if (lowerKey.Contains("plasma") || lowerKey.Contains("sprayer") || lowerKey.Contains("rain")) accent = new Color(0.22f, 0.90f, 1.0f, 1f);
            else if (lowerKey.Contains("bone") || lowerKey.Contains("ossuary") || lowerKey.Contains("hammer")) accent = new Color(0.92f, 0.88f, 0.76f, 1f);
            else if (lowerKey.Contains("mutagen") || lowerKey.Contains("incubator") || lowerKey.Contains("swarm")) accent = new Color(0.96f, 0.28f, 0.88f, 1f);
            else if (lowerKey.Contains("crimson") || lowerKey.Contains("heart") || lowerKey.Contains("artery")) accent = new Color(0.92f, 0.22f, 0.38f, 1f);

            Color bg = new Color(0.14f, 0.10f, 0.18f, 0.95f);
            Color rim = Color.Lerp(accent, Color.black, 0.35f);

            for (int y = 4; y < size - 4; y++)
            {
                for (int x = 4; x < size - 4; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(32f, 32f));
                    if (x < 8 || y < 8 || x >= size - 8 || y >= size - 8)
                        px[y * size + x] = rim;
                    else if (d < 16f)
                        px[y * size + x] = d < 7f ? Color.Lerp(accent, Color.white, 0.55f) : accent;
                    else
                        px[y * size + x] = bg;
                }
            }

            return BakeTexture(px, size, size, $"Generated_{lowerKey}", ppu);
        }

        private static Sprite GenerateStructureProcedural(string lowerKey, float ppu)
        {
            const int size = 128;
            Color[] px = new Color[size * size];

            Color baseRim = new Color(0.18f, 0.12f, 0.18f, 1f);
            Color body = new Color(0.36f, 0.68f, 0.36f, 1f);
            Color core = new Color(0.75f, 0.98f, 0.45f, 1f);

            if (lowerKey.Contains("sporeflower"))
            {
                body = new Color(0.78f, 0.56f, 0.18f, 1f);
                core = new Color(1.0f, 0.90f, 0.35f, 1f);
            }
            else if (lowerKey.Contains("bilecannon"))
            {
                body = new Color(0.28f, 0.54f, 0.62f, 1f);
                core = new Color(0.35f, 0.98f, 0.85f, 1f);
            }
            else if (lowerKey.Contains("pulsebulb") || lowerKey.Contains("bioreactor"))
            {
                body = new Color(0.16f, 0.56f, 0.78f, 1f);
                core = new Color(0.55f, 0.96f, 1.0f, 1f);
            }
            else if (lowerKey.Contains("mutagenincubator"))
            {
                body = new Color(0.56f, 0.18f, 0.64f, 1f);
                core = new Color(1.0f, 0.45f, 0.95f, 1f);
            }
            else if (lowerKey.Contains("harvester"))
            {
                body = new Color(0.58f, 0.44f, 0.24f, 1f);
                core = new Color(0.45f, 0.94f, 0.52f, 1f);
            }
            else if (lowerKey.Contains("mendtendril"))
            {
                body = new Color(0.18f, 0.62f, 0.46f, 1f);
                core = new Color(0.62f, 1.0f, 0.78f, 1f);
            }

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int qx = (x / 4) * 4 + 2;
                    int qy = (y / 4) * 4 + 2;
                    float d = Vector2.Distance(new Vector2(qx, qy), new Vector2(64f, 64f));
                    if (d <= 48f)
                    {
                        if (d > 41f) px[y * size + x] = baseRim;
                        else if (d < 18f) px[y * size + x] = core;
                        else px[y * size + x] = body;
                    }
                }
            }

            return BakeTexture(px, size, size, $"Generated_{lowerKey}", ppu > 0f ? ppu : DefaultPixelsPerUnit);
        }

        private static Sprite BakeTexture(Color[] pixels, int width, int height, string texName, float pixelsPerUnit)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = texName
            };
            tex.SetPixels(pixels);
            tex.Apply(false, false);

            Sprite sprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit > 0f ? pixelsPerUnit : DefaultPixelsPerUnit,
                0u,
                SpriteMeshType.FullRect);
            sprite.name = texName;
            return sprite;
        }
    }
}
