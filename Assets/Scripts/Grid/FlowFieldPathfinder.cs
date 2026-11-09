using System;
using UnityEngine;
using BioFarme.Core;
using BioFarme.Data;

namespace BioFarme.Grid
{
    /// <summary>
    /// Zero-GC weighted FlowField pathfinder across the 36x24 cavern grid targeting the 3x3 Living Heart Core.
    /// Open soil cost = 1 (10 fp), BoneWall cost = 18 (180 fp), CavernRock = impassable.
    /// Enemies naturally funnel through wall gaps; if completely walled off, they march to the weakest BoneWall and chew through!
    /// </summary>
    public sealed class FlowFieldPathfinder : MonoBehaviour
    {
        private const int TotalCells = BioGridManager.Width * BioGridManager.Height;
        private const int UnreachableCost = 100000000;

        // Fixed-point 10x costs (open = 10 -> 1.0 tile, diagonal = 14 -> 1.4 tiles, BoneWall = 180 -> 18.0 tiles)
        private const int OpenCardinalCost = 10;
        private const int OpenDiagonalCost = 14;
        private const int BoneWallPenalty = 180;
        private const int OrganicFencePenalty = 110;
        private const int StructureBlockPenalty = 140;

        private static FlowFieldPathfinder _instance;

        public static FlowFieldPathfinder Instance
        {
            get
            {
                if (_instance == null && BioGridManager.InstanceIfExists != null)
                {
                    _instance = BioGridManager.InstanceIfExists.GetComponent<FlowFieldPathfinder>();
                    if (_instance == null)
                    {
                        _instance = BioGridManager.InstanceIfExists.gameObject.AddComponent<FlowFieldPathfinder>();
                    }
                }
                return _instance;
            }
        }

        private readonly int[] _distField = new int[TotalCells];
        private readonly Vector2[] _flowDirs = new Vector2[TotalCells];
        private readonly Vector2Int[] _nextCells = new Vector2Int[TotalCells];
        private readonly bool[] _closed = new bool[TotalCells];

        // Lightweight binary min-heap for 864 cells (zero allocations per recompute!)
        private readonly int[] _heapCellIndices = new int[TotalCells + 1];
        private readonly int[] _heapPriorities = new int[TotalCells + 1];
        private int _heapCount;

        private bool _isDirty = true;

        private static readonly Vector2Int[] NeighborOffsets =
        {
            new Vector2Int(0, 1),   // N
            new Vector2Int(1, 0),   // E
            new Vector2Int(0, -1),  // S
            new Vector2Int(-1, 0),  // W
            new Vector2Int(1, 1),   // NE
            new Vector2Int(1, -1),  // SE
            new Vector2Int(-1, -1), // SW
            new Vector2Int(-1, 1)   // NW
        };

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
            }
            _isDirty = true;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void LateUpdate()
        {
            if (_isDirty)
            {
                RecomputeNow();
            }
        }

        public void MarkDirty()
        {
            _isDirty = true;
        }

        public void RecomputeNow()
        {
            BioGridManager grid = BioGridManager.InstanceIfExists;
            if (grid == null) return;

            _isDirty = false;
            _heapCount = 0;

            for (int i = 0; i < TotalCells; i++)
            {
                _distField[i] = UnreachableCost;
                _closed[i] = false;
                _flowDirs[i] = Vector2.zero;
                _nextCells[i] = new Vector2Int(i % BioGridManager.Width, i / BioGridManager.Width);
            }

            // Seed all 9 cells of the 3x3 Living Heart Core (17..19, 11..13) with cost 0
            Vector2Int coreAnchor = grid.HeartCoreAnchor;
            for (int dx = 0; dx < 3; dx++)
            {
                for (int dy = 0; dy < 3; dy++)
                {
                    int cx = coreAnchor.x + dx;
                    int cy = coreAnchor.y + dy;
                    if (!grid.IsInBounds(cx, cy)) continue;

                    int idx = ToIndex(cx, cy);
                    _distField[idx] = 0;
                    HeapPush(idx, 0);
                }
            }

            // Dijkstra propagation
            while (_heapCount > 0)
            {
                int currentIdx = HeapPop();
                if (_closed[currentIdx]) continue;
                _closed[currentIdx] = true;

                int cx = currentIdx % BioGridManager.Width;
                int cy = currentIdx / BioGridManager.Width;
                int baseDist = _distField[currentIdx];

                for (int n = 0; n < NeighborOffsets.Length; n++)
                {
                    Vector2Int offset = NeighborOffsets[n];
                    int nx = cx + offset.x;
                    int ny = cy + offset.y;
                    if (!grid.IsInBounds(nx, ny)) continue;

                    int nIdx = ToIndex(nx, ny);
                    if (_closed[nIdx]) continue;

                    bool isDiagonal = offset.x != 0 && offset.y != 0;
                    if (isDiagonal)
                    {
                        // Disallow diagonal corner-cutting adjacent to rock or blocking structures
                        if (!IsOpenForDiagonal(grid, cx + offset.x, cy) || !IsOpenForDiagonal(grid, cx, cy + offset.y))
                        {
                            continue;
                        }
                    }

                    int stepCost = EvaluateCellTraversalCost(grid, nx, ny, isDiagonal);
                    if (stepCost >= UnreachableCost) continue;

                    int candidateDist = baseDist + stepCost;
                    if (candidateDist < _distField[nIdx])
                    {
                        _distField[nIdx] = candidateDist;
                        HeapPush(nIdx, candidateDist);
                    }
                }
            }

            // Build smooth gradient / best neighbor vector for every cell
            Vector2 coreCenter = new Vector2(18.5f, 12.5f);
            for (int y = 0; y < BioGridManager.Height; y++)
            {
                for (int x = 0; x < BioGridManager.Width; x++)
                {
                    int idx = ToIndex(x, y);
                    int myDist = _distField[idx];

                    if (myDist == 0)
                    {
                        _nextCells[idx] = new Vector2Int(x, y);
                        _flowDirs[idx] = Vector2.zero;
                        continue;
                    }

                    if (myDist >= UnreachableCost)
                    {
                        Vector2 fallback = (coreCenter - new Vector2(x + 0.5f, y + 0.5f));
                        _flowDirs[idx] = fallback.sqrMagnitude > 0.001f ? fallback.normalized : Vector2.right;
                        _nextCells[idx] = new Vector2Int(x, y);
                        continue;
                    }

                    int bestDist = myDist;
                    Vector2Int bestCell = new Vector2Int(x, y);

                    for (int n = 0; n < NeighborOffsets.Length; n++)
                    {
                        Vector2Int off = NeighborOffsets[n];
                        int nx = x + off.x;
                        int ny = y + off.y;
                        if (!grid.IsInBounds(nx, ny)) continue;

                        bool isDiagonal = off.x != 0 && off.y != 0;
                        if (isDiagonal && (!IsOpenForDiagonal(grid, x + off.x, y) || !IsOpenForDiagonal(grid, x, y + off.y)))
                        {
                            continue;
                        }

                        int nd = _distField[ToIndex(nx, ny)];
                        if (nd < bestDist)
                        {
                            bestDist = nd;
                            bestCell = new Vector2Int(nx, ny);
                        }
                    }

                    _nextCells[idx] = bestCell;
                    Vector2 dir = new Vector2(bestCell.x - x, bestCell.y - y);
                    if (dir.sqrMagnitude > 0.001f)
                    {
                        _flowDirs[idx] = dir.normalized;
                    }
                    else
                    {
                        Vector2 toCore = coreCenter - new Vector2(x + 0.5f, y + 0.5f);
                        _flowDirs[idx] = toCore.sqrMagnitude > 0.001f ? toCore.normalized : Vector2.zero;
                    }
                }
            }
        }

        private static bool IsOpenForDiagonal(BioGridManager grid, int x, int y)
        {
            if (!grid.IsInBounds(x, y)) return false;
            Vector2Int p = new Vector2Int(x, y);
            TileGroundType ground = grid.GetGroundType(p);
            if (ground == TileGroundType.CavernRock) return false;

            IGridEntity ent = grid.GetStructureAt(p);
            if (ent != null)
            {
                StructureDefinition def = BalanceDatabase.GetStructure(ent.Type);
                if (def != null && def.blocksEnemies) return false;
            }
            return true;
        }

        private static int EvaluateCellTraversalCost(BioGridManager grid, int x, int y, bool isDiagonal)
        {
            Vector2Int p = new Vector2Int(x, y);
            TileGroundType ground = grid.GetGroundType(p);
            if (ground == TileGroundType.CavernRock)
            {
                return UnreachableCost;
            }

            int baseMove = isDiagonal ? OpenDiagonalCost : OpenCardinalCost;
            IGridEntity entity = grid.GetStructureAt(p);
            if (entity == null)
            {
                return baseMove;
            }

            if (entity.Type == StructureType.LivingHeartCore)
            {
                return 0;
            }

            StructureDefinition def = BalanceDatabase.GetStructure(entity.Type);
            if (def == null || !def.blocksEnemies)
            {
                return baseMove; // Veins & passable conduits cost 1.0 just like open soil
            }

            if (entity.Type == StructureType.BoneWall)
            {
                return BoneWallPenalty + (isDiagonal ? 6 : 0);
            }
            if (entity.Type == StructureType.OrganicFence)
            {
                return OrganicFencePenalty + (isDiagonal ? 4 : 0);
            }
            return StructureBlockPenalty + (isDiagonal ? 5 : 0);
        }

        // --- Public Query Methods ---

        public Vector2 GetFlowDirection(Vector2 worldPos)
        {
            EnsureFresh();
            BioGridManager grid = BioGridManager.InstanceIfExists;
            if (grid == null) return Vector2.zero;

            Vector2Int cell = grid.WorldToGrid(worldPos);
            if (!grid.IsInBounds(cell))
            {
                Vector2 toCenter = (Vector2)grid.HeartCoreCenterWorld - worldPos;
                return toCenter.sqrMagnitude > 0.001f ? toCenter.normalized : Vector2.zero;
            }

            int idx = ToIndex(cell.x, cell.y);
            Vector2Int nextCell = _nextCells[idx];
            if (nextCell != cell)
            {
                // Steer smoothly toward the center of nextCell so enemies never snag on corridor corners!
                Vector2 targetWorld = grid.GridToWorldCenter(nextCell);
                Vector2 steer = targetWorld - worldPos;
                if (steer.sqrMagnitude > 0.0001f)
                {
                    return steer.normalized;
                }
            }

            return _flowDirs[idx];
        }

        public Vector2 GetFlowDirection(Vector2Int cell)
        {
            EnsureFresh();
            BioGridManager grid = BioGridManager.InstanceIfExists;
            if (grid == null || !grid.IsInBounds(cell)) return Vector2.zero;
            return _flowDirs[ToIndex(cell.x, cell.y)];
        }

        public Vector2Int GetNextGridStep(Vector2Int cell)
        {
            EnsureFresh();
            BioGridManager grid = BioGridManager.InstanceIfExists;
            if (grid == null || !grid.IsInBounds(cell)) return cell;
            return _nextCells[ToIndex(cell.x, cell.y)];
        }

        public float GetDistanceToCore(Vector2Int cell)
        {
            EnsureFresh();
            BioGridManager grid = BioGridManager.InstanceIfExists;
            if (grid == null || !grid.IsInBounds(cell)) return 9999f;
            int raw = _distField[ToIndex(cell.x, cell.y)];
            return raw >= UnreachableCost ? 9999f : raw * 0.1f;
        }

        /// <summary>
        /// Checks if a blocking colony structure (BoneWall, Fence, Turret, or LivingHeartCore) sits in front of the enemy
        /// so enemy AI can immediately stop and chew through it.
        /// </summary>
        public IGridEntity GetBlockingStructureAhead(Vector2 worldPos, float lookAheadDist = 0.58f)
        {
            EnsureFresh();
            BioGridManager grid = BioGridManager.InstanceIfExists;
            if (grid == null) return null;

            Vector2 flow = GetFlowDirection(worldPos);
            Vector2 probePoint = worldPos + flow * Mathf.Max(0.15f, lookAheadDist);
            Vector2Int probeCell = grid.WorldToGrid(probePoint);

            IGridEntity hit = CheckBlockingAt(grid, probeCell);
            if (hit != null) return hit;

            Vector2Int currentCell = grid.WorldToGrid(worldPos);
            Vector2Int nextCell = GetNextGridStep(currentCell);
            return CheckBlockingAt(grid, nextCell);
        }

        private static IGridEntity CheckBlockingAt(BioGridManager grid, Vector2Int cell)
        {
            if (!grid.IsInBounds(cell)) return null;
            IGridEntity ent = grid.GetStructureAt(cell);
            if (ent == null) return null;

            StructureDefinition def = BalanceDatabase.GetStructure(ent.Type);
            if (def != null && !def.blocksEnemies) return null;
            return ent;
        }

        private void EnsureFresh()
        {
            if (_isDirty)
            {
                RecomputeNow();
            }
        }

        private static int ToIndex(int x, int y) => y * BioGridManager.Width + x;

        private void HeapPush(int cellIndex, int priority)
        {
            int i = ++_heapCount;
            while (i > 1)
            {
                int parent = i >> 1;
                if (_heapPriorities[parent] <= priority) break;
                _heapCellIndices[i] = _heapCellIndices[parent];
                _heapPriorities[i] = _heapPriorities[parent];
                i = parent;
            }
            _heapCellIndices[i] = cellIndex;
            _heapPriorities[i] = priority;
        }

        private int HeapPop()
        {
            int topCell = _heapCellIndices[1];
            int lastCell = _heapCellIndices[_heapCount];
            int lastPrio = _heapPriorities[_heapCount];
            _heapCount--;

            int i = 1;
            int half = _heapCount >> 1;
            while (i <= half)
            {
                int left = i << 1;
                int right = left + 1;
                int smallest = (right <= _heapCount && _heapPriorities[right] < _heapPriorities[left]) ? right : left;
                if (lastPrio <= _heapPriorities[smallest]) break;
                _heapCellIndices[i] = _heapCellIndices[smallest];
                _heapPriorities[i] = _heapPriorities[smallest];
                i = smallest;
            }

            if (_heapCount > 0)
            {
                _heapCellIndices[i] = lastCell;
                _heapPriorities[i] = lastPrio;
            }
            return topCell;
        }
    }
}
