using UnityEngine;
using BioFarme.Core;
using BioFarme.Data;
using BioFarme.Grid;

namespace BioFarme.Factory
{
    /// <summary>
    /// Chitin/Bone defensive wall and thorny OrganicFence entity with 4-bit cardinal auto-tiling.
    /// Automatically refreshes itself and its 4 cardinal neighbors when placed or demolished.
    /// </summary>
    public sealed class BoneWallEntity : StructureBase
    {
        private static readonly Vector2Int[] CardinalOffsets =
        {
            Vector2Int.up,
            Vector2Int.right,
            Vector2Int.down,
            Vector2Int.left
        };

        public float ThornsSlowFactor => Definition != null ? Definition.slowFactor : 0f;

        protected override void OnInitialized()
        {
            RefreshNeighbors(propagateToCardinals: false);
        }

        public override void RefreshNeighbors(bool propagateToCardinals = false)
        {
            BioGridManager grid = BioGridManager.InstanceIfExists;
            if (grid == null || MainRenderer == null) return;

            int mask = grid.GetWallNeighborMask(GridPos);
            MainRenderer.sprite = SpriteRegistry.GetWallAutoTileSprite(Type, mask);
            if (VisualRoot != null)
            {
                VisualRoot.localRotation = Quaternion.identity;
            }

            if (propagateToCardinals)
            {
                RefreshCardinalNeighborsOnGrid(grid, GridPos);
            }
        }

        public static void RefreshCardinalNeighborsOnGrid(BioGridManager grid, Vector2Int center)
        {
            if (grid == null) return;

            for (int i = 0; i < CardinalOffsets.Length; i++)
            {
                Vector2Int nbPos = center + CardinalOffsets[i];
                if (!grid.IsInBounds(nbPos)) continue;

                IGridEntity nb = grid.GetStructureAt(nbPos);
                if (nb is BoneWallEntity wall)
                {
                    wall.RefreshNeighbors(propagateToCardinals: false);
                }
                else if (nb is BuiltInStructureNode builtIn &&
                         (builtIn.Type == StructureType.BoneWall || builtIn.Type == StructureType.OrganicFence))
                {
                    builtIn.RefreshAutoTileSprite(grid.GetWallNeighborMask(nbPos));
                }
            }
        }

        protected override void OnDamaged(float amount, Vector2 hitSource)
        {
            UpdateDamageWearTint();
        }

        public override void Heal(float amount)
        {
            base.Heal(amount);
            UpdateDamageWearTint();
        }

        private void UpdateDamageWearTint()
        {
            float ratio = MaxHp > 0f ? Mathf.Clamp01(CurrentHp / MaxHp) : 1f;
            // Subtle battle-worn darkening when heavily chewed by insects
            BaseTintColor = Color.Lerp(new Color(0.76f, 0.65f, 0.64f, 1f), Color.white, ratio);
        }
    }
}
