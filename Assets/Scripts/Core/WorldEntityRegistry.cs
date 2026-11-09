using System.Collections.Generic;
using UnityEngine;
using BioFarme.Data;

namespace BioFarme.Core
{
    /// <summary>
    /// Lightweight spatial & entity registry in BioFarme.Core so Combat, Player Sprayer,
    /// Turrets, Crops, and Minimap UI can query live entities deterministically without
    /// relying on fragile TagManager layers or cross-assembly references.
    /// </summary>
    public static class WorldEntityRegistry
    {
        private static readonly List<IDamageable> _activeEnemies = new List<IDamageable>(128);
        private static readonly Dictionary<Vector2Int, ICropEntity> _cropsByCell = new Dictionary<Vector2Int, ICropEntity>(128);

        public static Transform PlayerTransform { get; private set; }
        public static Vector2 PlayerWorldPosition => PlayerTransform != null ? (Vector2)PlayerTransform.position : new Vector2(18.5f, 9.5f);
        public static float PlayerStaminaNormalized { get; set; } = 1f;
        public static float PlayerHealthNormalized { get; set; } = 1f;

        public static IReadOnlyList<IDamageable> ActiveEnemies => _activeEnemies;
        public static IReadOnlyDictionary<Vector2Int, ICropEntity> CropsByCell => _cropsByCell;

        public static void RegisterPlayer(Transform playerTransform)
        {
            PlayerTransform = playerTransform;
        }

        public static void RegisterEnemy(IDamageable enemy)
        {
            if (enemy != null && !_activeEnemies.Contains(enemy))
            {
                _activeEnemies.Add(enemy);
            }
        }

        public static void UnregisterEnemy(IDamageable enemy)
        {
            if (enemy != null)
            {
                _activeEnemies.Remove(enemy);
            }
        }

        public static void PruneDeadEnemies()
        {
            for (int i = _activeEnemies.Count - 1; i >= 0; i--)
            {
                var e = _activeEnemies[i];
                if (e == null || (e is Object unityObj && unityObj == null) || !e.IsAlive)
                {
                    _activeEnemies.RemoveAt(i);
                }
            }
        }

        public static IDamageable GetClosestEnemy(Vector2 center, float maxRange)
        {
            PruneDeadEnemies();
            float bestDistSq = maxRange * maxRange;
            IDamageable best = null;
            for (int i = 0; i < _activeEnemies.Count; i++)
            {
                var e = _activeEnemies[i];
                float dSq = (e.WorldPosition - center).sqrMagnitude;
                if (dSq <= bestDistSq)
                {
                    bestDistSq = dSq;
                    best = e;
                }
            }
            return best;
        }

        public static int GetEnemiesInRadius(Vector2 center, float radius, List<IDamageable> results)
        {
            if (results == null) return 0;
            results.Clear();
            PruneDeadEnemies();
            float rSq = radius * radius;
            for (int i = 0; i < _activeEnemies.Count; i++)
            {
                var e = _activeEnemies[i];
                if ((e.WorldPosition - center).sqrMagnitude <= rSq)
                {
                    results.Add(e);
                }
            }
            return results.Count;
        }

        public static void RegisterCrop(Vector2Int cell, ICropEntity crop)
        {
            if (crop != null)
            {
                _cropsByCell[cell] = crop;
            }
        }

        public static void UnregisterCrop(Vector2Int cell)
        {
            _cropsByCell.Remove(cell);
        }

        public static ICropEntity GetCropAt(Vector2Int cell)
        {
            return _cropsByCell.TryGetValue(cell, out var crop) ? crop : null;
        }

        public static void ClearAll()
        {
            _activeEnemies.Clear();
            _cropsByCell.Clear();
            PlayerTransform = null;
            PlayerStaminaNormalized = 1f;
            PlayerHealthNormalized = 1f;
        }
    }
}
