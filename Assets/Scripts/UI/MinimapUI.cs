using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using BioFarme.Core;
using BioFarme.Data;
using BioFarme.Grid;

namespace BioFarme.UI
{
    /// <summary>
    /// Real-time 72x48 pixel-art subterranean minimap framed inside UI/UI_Frame_Minimap (~260x278 px).
    /// Updates 5 times/sec from BioGridManager, WorldEntityRegistry.ActiveEnemies, and PlayerWorldPosition,
    /// renders a crisp white camera viewport rectangle, and supports click/drag panning during TacticalPause.
    /// </summary>
    public sealed class MinimapUI : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        private const int TexWidth = 72;   // 36 grid cells * 2 px
        private const int TexHeight = 48;  // 24 grid cells * 2 px
        private const float RefreshIntervalSec = 0.20f; // 5 Hz

        // Pixel-art biopunk map palette matching ref_gameplay.jpg
        private static readonly Color ColCavernRock = new Color32(20, 16, 20, 255);
        private static readonly Color ColFleshSoil = new Color32(62, 38, 36, 255);
        private static readonly Color ColMossFertile = new Color32(47, 72, 41, 255);
        private static readonly Color ColTilledDry = new Color32(92, 58, 41, 255);
        private static readonly Color ColWateredBed = new Color32(44, 92, 76, 255);
        private static readonly Color ColHiveBurrow = new Color32(168, 36, 78, 255);
        private static readonly Color ColCoreArea = new Color32(48, 224, 255, 255);
        private static readonly Color ColCoreHeartCenter = new Color32(255, 62, 112, 255);

        private static readonly Color ColCropGrowing = new Color32(106, 214, 58, 255);
        private static readonly Color ColCropMature = new Color32(216, 255, 84, 255);

        private static readonly Color ColBoneWall = new Color32(216, 205, 180, 255);
        private static readonly Color ColFence = new Color32(158, 126, 92, 255);
        private static readonly Color ColVeinBiomass = new Color32(62, 192, 120, 255);
        private static readonly Color ColVeinPlasma = new Color32(56, 210, 248, 255);
        private static readonly Color ColVeinArtery = new Color32(236, 76, 162, 255);
        private static readonly Color ColTurret = new Color32(148, 255, 64, 255);
        private static readonly Color ColProduction = new Color32(198, 98, 255, 255);

        private static readonly Color ColEnemy = new Color32(255, 46, 46, 255);
        private static readonly Color ColBoss = new Color32(255, 128, 24, 255);
        private static readonly Color ColPlayer = new Color32(255, 244, 84, 255);
        private static readonly Color ColViewportRect = new Color32(255, 255, 255, 235);

        private RectTransform _mapContainer;
        private RawImage _mapRawImage;
        private RectTransform _mapImageRect;
        private GameObject _collapsedRestoreButton;

        private Texture2D _minimapTex;
        private readonly Color32[] _pixelBuffer = new Color32[TexWidth * TexHeight];
        private float _refreshTimer;
        private bool _isCollapsed;
        private float _lastPanHintTime = -10f;

        public bool IsCollapsed => _isCollapsed;

        public void Build(Transform parent)
        {
            // Root container anchored bottom-right (~260x278 matching 400x428 UI_Frame_Minimap aspect)
            RectTransform host = BiopunkUiBuilder.CreateRect(
                "MinimapCluster", parent,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-18f, 16f), new Vector2(260f, 278f));

            _mapContainer = BiopunkUiBuilder.CreateStretchRect("ExpandedMinimap", host);

            // Recessed dark background behind the transparent interior of UI_Frame_Minimap
            // Note: UI_Frame_Minimap has its top-right [X] tab occupying the top ~12% height,
            // while the square frame interior spans roughly X=[6%..94%], Y=[5%..86%].
            RectTransform innerBackdrop = BiopunkUiBuilder.CreateRect(
                "MapBackdrop", _mapContainer,
                new Vector2(0.06f, 0.05f), new Vector2(0.94f, 0.86f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            innerBackdrop.offsetMin = Vector2.zero;
            innerBackdrop.offsetMax = Vector2.zero;

            Image darkFill = innerBackdrop.gameObject.AddComponent<Image>();
            darkFill.sprite = SpriteRegistry.GetWhiteSquareSprite();
            darkFill.color = ColCavernRock;
            darkFill.raycastTarget = false;

            // Live 72x48 Texture2D RawImage
            _minimapTex = new Texture2D(TexWidth, TexHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "RuntimeMinimap_72x48"
            };

            RectTransform rawRt = BiopunkUiBuilder.CreateStretchRect("LiveMinimapRaw", innerBackdrop, 2f, 2f, 2f, 2f);
            _mapImageRect = rawRt;
            _mapRawImage = rawRt.gameObject.AddComponent<RawImage>();
            _mapRawImage.texture = _minimapTex;
            _mapRawImage.raycastTarget = true;

            // Forward pointer clicks on RawImage to this MinimapUI component
            MinimapPointerRelay relay = rawRt.gameObject.AddComponent<MinimapPointerRelay>();
            relay.Owner = this;

            // Crisp UI/UI_Frame_Minimap foreground overlay
            Image frameImg = BiopunkUiBuilder.CreateSpriteImage(
                _mapContainer, "FrameOverlay", "UI/UI_Frame_Minimap",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, preserveAspect: false, raycastTarget: false);
            BiopunkUiBuilder.Stretch(frameImg.rectTransform);

            // Subtle coordinate/hint footer strip
            Text mapLabel = BiopunkUiBuilder.CreateText(
                _mapContainer, "SectorLabel", "СЕКТОР 36×24",
                15, FontStyle.Bold, BiopunkUiBuilder.TextSecondary, TextAnchor.MiddleLeft);
            mapLabel.rectTransform.anchorMin = new Vector2(0.08f, 0.87f);
            mapLabel.rectTransform.anchorMax = new Vector2(0.76f, 0.98f);
            mapLabel.rectTransform.offsetMin = Vector2.zero;
            mapLabel.rectTransform.offsetMax = Vector2.zero;

            // Invisible clickable hotspot right over the baked [X] button in top-right of UI_Frame_Minimap
            RectTransform closeHotspot = BiopunkUiBuilder.CreateRect(
                "BtnCollapseMinimap", _mapContainer,
                new Vector2(0.80f, 0.86f), new Vector2(0.96f, 1.00f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            closeHotspot.offsetMin = Vector2.zero;
            closeHotspot.offsetMax = Vector2.zero;

            Image transparentHit = closeHotspot.gameObject.AddComponent<Image>();
            transparentHit.sprite = SpriteRegistry.GetWhiteSquareSprite();
            transparentHit.color = new Color(1f, 1f, 1f, 0.01f);
            Button collapseBtn = closeHotspot.gameObject.AddComponent<Button>();
            collapseBtn.targetGraphic = transparentHit;
            collapseBtn.onClick.AddListener(() =>
            {
                BiopunkUiBuilder.PlayClickSfx();
                SetCollapsed(true);
            });
            BiopunkUiBuilder.AttachTooltip(
                closeHotspot.gameObject,
                "Свернуть мини-карту [N]",
                "Нажмите, чтобы временно скрыть радар пещеры.");

            BiopunkUiBuilder.AttachTooltip(
                rawRt.gameObject,
                "Радар Пещеры (36×24)",
                "• Жёлтая точка — Колонист | Голубое ядро — Живое Сердце\n" +
                "• Красные точки — Рой Улья | Белая рамка — Камера\n" +
                "• В Тактической паузе [Space] клик перемещает камеру!");

            // Collapsed restore button in bottom-right corner
            Button restoreBtn = BiopunkUiBuilder.CreateButton(
                host, "BtnRestoreMinimap", "РАДАР [N]",
                new Vector2(156f, 44f),
                () => SetCollapsed(false),
                fontSize: 18,
                baseTint: new Color(0.78f, 0.94f, 0.76f, 1f));
            restoreBtn.image.rectTransform.anchorMin = new Vector2(1f, 0f);
            restoreBtn.image.rectTransform.anchorMax = new Vector2(1f, 0f);
            restoreBtn.image.rectTransform.pivot = new Vector2(1f, 0f);
            restoreBtn.image.rectTransform.anchoredPosition = Vector2.zero;
            _collapsedRestoreButton = restoreBtn.gameObject;
            _collapsedRestoreButton.SetActive(false);

            RenderMinimapNow();
        }

        private void OnDestroy()
        {
            if (_minimapTex != null)
            {
                Destroy(_minimapTex);
                _minimapTex = null;
            }
        }

        private void Update()
        {
            if (_isCollapsed || _minimapTex == null) return;

            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = RefreshIntervalSec;
                RenderMinimapNow();
            }
        }

        public void ToggleCollapsed()
        {
            SetCollapsed(!_isCollapsed);
        }

        public void SetCollapsed(bool collapsed)
        {
            _isCollapsed = collapsed;
            if (_mapContainer != null) _mapContainer.gameObject.SetActive(!collapsed);
            if (_collapsedRestoreButton != null) _collapsedRestoreButton.SetActive(collapsed);
            if (!collapsed)
            {
                RenderMinimapNow();
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            HandleMapPointer(eventData, isInitialClick: true);
        }

        public void OnDrag(PointerEventData eventData)
        {
            HandleMapPointer(eventData, isInitialClick: false);
        }

        internal void HandleMapPointer(PointerEventData eventData, bool isInitialClick)
        {
            if (_mapImageRect == null || eventData == null) return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _mapImageRect, eventData.position, eventData.pressEventCamera, out Vector2 localPt))
            {
                return;
            }

            Rect rect = _mapImageRect.rect;
            if (rect.width <= 1f || rect.height <= 1f) return;

            float u = Mathf.Clamp01((localPt.x - rect.xMin) / rect.width);
            float v = Mathf.Clamp01((localPt.y - rect.yMin) / rect.height);

            GameManager gm = GameManager.InstanceIfExists;
            if (gm != null && gm.IsTacticalPaused)
            {
                PanCameraToNormalized(u, v);
            }
            else if (isInitialClick && Time.unscaledTime - _lastPanHintTime > 2.5f)
            {
                _lastPanHintTime = Time.unscaledTime;
                GameEvents.RaiseBannerToast(
                    "Нажмите [Space] (Тактическая пауза), чтобы перемещать камеру кликом по мини-карте!",
                    BiopunkUiBuilder.AccentCyan);
            }
        }

        private static void PanCameraToNormalized(float u, float v)
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            float worldW = BioGridManager.Width * BioGridManager.CellSize;
            float worldH = BioGridManager.Height * BioGridManager.CellSize;

            float targetX = u * worldW;
            float targetY = v * worldH;

            if (cam.orthographic)
            {
                float halfH = cam.orthographicSize;
                float halfW = halfH * cam.aspect;
                float minX = Mathf.Min(worldW * 0.5f, halfW * 0.65f);
                float maxX = Mathf.Max(worldW * 0.5f, worldW - halfW * 0.65f);
                float minY = Mathf.Min(worldH * 0.5f, halfH * 0.65f);
                float maxY = Mathf.Max(worldH * 0.5f, worldH - halfH * 0.65f);

                targetX = Mathf.Clamp(targetX, minX, maxX);
                targetY = Mathf.Clamp(targetY, minY, maxY);
            }

            Vector3 cur = cam.transform.position;
            cam.transform.position = new Vector3(targetX, targetY, cur.z);
        }

        public void RenderMinimapNow()
        {
            if (_minimapTex == null) return;

            BioGridManager grid = BioGridManager.InstanceIfExists;
            float pulse = 0.78f + 0.22f * Mathf.Sin(Time.unscaledTime * 5f);

            if (grid == null)
            {
                Color32 fallbackCol = ColCavernRock;
                for (int i = 0; i < _pixelBuffer.Length; i++) _pixelBuffer[i] = fallbackCol;
                _minimapTex.SetPixels32(_pixelBuffer);
                _minimapTex.Apply(false, false);
                return;
            }

            // 1. Sample 36x24 grid terrain, crops, and structures (each cell = 2x2 texels)
            for (int gy = 0; gy < BioGridManager.Height; gy++)
            {
                for (int gx = 0; gx < BioGridManager.Width; gx++)
                {
                    Vector2Int cell = new Vector2Int(gx, gy);
                    Color cellColor = ResolveCellColor(grid, cell, pulse);

                    Color32 c32 = cellColor;
                    int px = gx * 2;
                    int py = gy * 2;
                    SetPixelSafe(px, py, c32);
                    SetPixelSafe(px + 1, py, c32);
                    SetPixelSafe(px, py + 1, c32);
                    SetPixelSafe(px + 1, py + 1, c32);
                }
            }

            // Highlight Living Heart Core 3x3 center with pulsing crimson-cyan heart beacon
            int coreCx = BioGridManager.HeartCenterCell.x * 2;
            int coreCy = BioGridManager.HeartCenterCell.y * 2;
            Color32 heartBeacon = Color.Lerp(ColCoreArea, ColCoreHeartCenter, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f));
            StampSquare(coreCx, coreCy, 2, heartBeacon);

            // 2. Plot live hostile Hive organisms
            IReadOnlyList<IDamageable> enemies = WorldEntityRegistry.ActiveEnemies;
            if (enemies != null)
            {
                for (int i = 0; i < enemies.Count; i++)
                {
                    IDamageable enemy = enemies[i];
                    if (enemy == null || !enemy.IsAlive) continue;

                    Vector2 pos = enemy.WorldPosition;
                    int ex = Mathf.Clamp(Mathf.RoundToInt((pos.x / (BioGridManager.Width * BioGridManager.CellSize)) * (TexWidth - 1)), 0, TexWidth - 1);
                    int ey = Mathf.Clamp(Mathf.RoundToInt((pos.y / (BioGridManager.Height * BioGridManager.CellSize)) * (TexHeight - 1)), 0, TexHeight - 1);

                    bool isBoss = enemy.MaxHp >= 1000f;
                    StampSquare(ex, ey, isBoss ? 3 : 2, isBoss ? (Color32)ColBoss : (Color32)ColEnemy);
                }
            }

            // 3. Plot live Hazmat Colonist marker
            Vector2 playerPos = WorldEntityRegistry.PlayerWorldPosition;
            int plx = Mathf.Clamp(Mathf.RoundToInt((playerPos.x / (BioGridManager.Width * BioGridManager.CellSize)) * (TexWidth - 1)), 1, TexWidth - 2);
            int ply = Mathf.Clamp(Mathf.RoundToInt((playerPos.y / (BioGridManager.Height * BioGridManager.CellSize)) * (TexHeight - 1)), 1, TexHeight - 2);
            StampSquare(plx - 1, ply - 1, 3, ColPlayer);

            // 4. Draw crisp 1px white Camera Viewport outline
            DrawCameraViewportOutline();

            _minimapTex.SetPixels32(_pixelBuffer);
            _minimapTex.Apply(false, false);
        }

        private static Color ResolveCellColor(BioGridManager grid, Vector2Int cell, float pulse)
        {
            StructureType st = grid.GetStructureTypeAt(cell);
            if (st != StructureType.None)
            {
                switch (st)
                {
                    case StructureType.LivingHeartCore:
                        return ColCoreArea * pulse;
                    case StructureType.BoneWall:
                        return ColBoneWall;
                    case StructureType.OrganicFence:
                        return ColFence;
                    case StructureType.VeinBiomass:
                        return ColVeinBiomass;
                    case StructureType.VeinPlasma:
                        return ColVeinPlasma;
                    case StructureType.VeinArtery:
                        return ColVeinArtery;
                    case StructureType.FlytrapTurret:
                    case StructureType.SporeFlowerTurret:
                    case StructureType.BileCannonTurret:
                        return ColTurret;
                    default:
                        return ColProduction;
                }
            }

            if (grid.HasCropAt(cell))
            {
                ICropEntity crop = grid.GetCropAt(cell) ?? WorldEntityRegistry.GetCropAt(cell);
                return (crop != null && crop.IsMature) ? ColCropMature : ColCropGrowing;
            }

            TileGroundType ground = grid.GetGroundType(cell);
            switch (ground)
            {
                case TileGroundType.CavernRock:
                    return ColCavernRock;
                case TileGroundType.FleshSoil:
                    return ColFleshSoil;
                case TileGroundType.MossFertile:
                    return ColMossFertile;
                case TileGroundType.TilledBed:
                    return ColTilledDry;
                case TileGroundType.WateredBed:
                    return ColWateredBed;
                case TileGroundType.CoreFootprint:
                    return ColCoreArea;
                case TileGroundType.HiveBurrow:
                    return ColHiveBurrow * pulse;
                default:
                    return ColFleshSoil;
            }
        }

        private void DrawCameraViewportOutline()
        {
            Camera cam = Camera.main;
            if (cam == null || !cam.orthographic) return;

            float worldW = BioGridManager.Width * BioGridManager.CellSize;
            float worldH = BioGridManager.Height * BioGridManager.CellSize;

            float halfH = cam.orthographicSize;
            float halfW = halfH * cam.aspect;
            Vector3 cPos = cam.transform.position;

            int xMin = Mathf.Clamp(Mathf.RoundToInt(((cPos.x - halfW) / worldW) * (TexWidth - 1)), 0, TexWidth - 1);
            int xMax = Mathf.Clamp(Mathf.RoundToInt(((cPos.x + halfW) / worldW) * (TexWidth - 1)), 0, TexWidth - 1);
            int yMin = Mathf.Clamp(Mathf.RoundToInt(((cPos.y - halfH) / worldH) * (TexHeight - 1)), 0, TexHeight - 1);
            int yMax = Mathf.Clamp(Mathf.RoundToInt(((cPos.y + halfH) / worldH) * (TexHeight - 1)), 0, TexHeight - 1);

            Color32 col = ColViewportRect;
            for (int x = xMin; x <= xMax; x++)
            {
                SetPixelSafe(x, yMin, col);
                SetPixelSafe(x, yMax, col);
            }
            for (int y = yMin; y <= yMax; y++)
            {
                SetPixelSafe(xMin, y, col);
                SetPixelSafe(xMax, y, col);
            }
        }

        private void StampSquare(int startX, int startY, int size, Color32 color)
        {
            for (int dy = 0; dy < size; dy++)
            {
                for (int dx = 0; dx < size; dx++)
                {
                    SetPixelSafe(startX + dx, startY + dy, color);
                }
            }
        }

        private void SetPixelSafe(int x, int y, Color32 color)
        {
            if (x < 0 || x >= TexWidth || y < 0 || y >= TexHeight) return;
            _pixelBuffer[y * TexWidth + x] = color;
        }
    }

    /// <summary>
    /// Relays uGUI pointer events from the child RawImage directly to MinimapUI.
    /// </summary>
    internal sealed class MinimapPointerRelay : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public MinimapUI Owner;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (Owner != null) Owner.HandleMapPointer(eventData, isInitialClick: true);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (Owner != null) Owner.HandleMapPointer(eventData, isInitialClick: false);
        }
    }
}
