using UnityEngine;
using BioFarme.Core;
using BioFarme.Data;
using BioFarme.Grid;

namespace BioFarme.Factory
{
    /// <summary>
    /// Symbiotic vascular conduit (VeinBiomass, VeinPlasma, VeinArtery).
    /// Auto-tiles sprite and exact cardinal Z-rotation across straight, corner, T-junction, and 4-way hubs,
    /// pulses organically, and notifies VeinNetworkSystem on topology changes.
    /// </summary>
    public sealed class VeinConduitEntity : StructureBase
    {
        private static readonly Vector2Int[] CardinalOffsets =
        {
            Vector2Int.up,
            Vector2Int.right,
            Vector2Int.down,
            Vector2Int.left
        };

        private SpriteRenderer _crossNorthArmOverlay;
        private float _pulsePhase;

        protected override int GetDesiredSortingOrder()
        {
            return -48;
        }

        protected override void OnInitialized()
        {
            _pulsePhase = (GridPos.x * 0.45f + GridPos.y * 0.65f);

            // Create subtle 4th-arm overlay used only when all 4 cardinal neighbors connect (mask == 15)
            GameObject crossGo = new GameObject("CrossNorthArm");
            crossGo.transform.SetParent(VisualRoot, false);
            crossGo.transform.localPosition = Vector3.zero;
            _crossNorthArmOverlay = crossGo.AddComponent<SpriteRenderer>();
            _crossNorthArmOverlay.sortingOrder = -49;
            _crossNorthArmOverlay.gameObject.SetActive(false);

            RefreshNeighbors(propagateToCardinals: false);
        }

        protected override void OnSimulationTick(float simDt)
        {
            _pulsePhase += simDt * (2.6f + GetConduitTier() * 0.5f);
            float pulse = 1f + Mathf.Sin(_pulsePhase) * 0.025f;
            RecoilScale = new Vector3(pulse, pulse, 1f);
        }

        private int GetConduitTier()
        {
            if (Type == StructureType.VeinArtery) return 3;
            if (Type == StructureType.VeinPlasma) return 2;
            return 1;
        }

        public override void RefreshNeighbors(bool propagateToCardinals = false)
        {
            BioGridManager grid = BioGridManager.InstanceIfExists;
            if (grid == null || MainRenderer == null) return;

            int mask = grid.GetVeinNeighborMask(GridPos) & 15;
            MainRenderer.sprite = SpriteRegistry.GetVeinAutoTileSprite(Type, mask);

            float rotZ = ComputeConduitRotationDeg(mask);
            if (VisualRoot != null)
            {
                VisualRoot.localRotation = Quaternion.Euler(0f, 0f, rotZ);
            }

            if (_crossNorthArmOverlay != null)
            {
                if (mask == 15)
                {
                    _crossNorthArmOverlay.sprite = SpriteRegistry.GetVeinAutoTileSprite(Type, 5); // Vertical arm completes 4-way cross
                    _crossNorthArmOverlay.color = BaseTintColor;
                    _crossNorthArmOverlay.gameObject.SetActive(true);
                }
                else
                {
                    _crossNorthArmOverlay.gameObject.SetActive(false);
                }
            }

            VeinNetworkSystem.Instance?.MarkDirty();

            if (propagateToCardinals)
            {
                RefreshCardinalVeinsOnGrid(grid, GridPos);
            }
        }

        public static void RefreshCardinalVeinsOnGrid(BioGridManager grid, Vector2Int center)
        {
            if (grid == null) return;

            for (int i = 0; i < CardinalOffsets.Length; i++)
            {
                Vector2Int nbPos = center + CardinalOffsets[i];
                if (!grid.IsInBounds(nbPos)) continue;

                IGridEntity nb = grid.GetStructureAt(nbPos);
                if (nb is VeinConduitEntity vein)
                {
                    vein.RefreshNeighbors(propagateToCardinals: false);
                }
                else if (nb is BuiltInStructureNode builtIn && VeinNetworkSystem.IsConduitType(builtIn.Type))
                {
                    builtIn.RefreshAutoTileSprite(grid.GetVeinNeighborMask(nbPos));
                }
            }
        }

        /// <summary>
        /// Maps 4-bit cardinal mask (N=1, E=2, S=4, W=8) to exact Z rotation matching curated Vein sprites:
        /// - Vein_*_Corner native arms: West (8) + South (4) = 12 at 0 deg
        /// - Vein_*_T native arms: East (2) + South (4) + West (8) = 14 at 0 deg
        /// </summary>
        private static float ComputeConduitRotationDeg(int mask)
        {
            switch (mask)
            {
                // Corners (native = S + W = 12)
                case 12: return 0f;   // S + W
                case 6:  return 90f;  // S + E
                case 3:  return 180f; // N + E
                case 9:  return 270f; // N + W

                // T-Junctions (native = E + S + W = 14)
                case 14: return 0f;   // E + S + W
                case 7:  return 90f;  // N + E + S
                case 11: return 180f; // N + E + W
                case 13: return 270f; // N + S + W

                // Straight H (0, 2, 8, 10), Straight V (1, 4, 5), and 4-way Cross (15)
                default:
                    return 0f;
            }
        }
    }
}
