using UnityEngine;

/// <summary>
/// Periodically checks all active enemies against the ground's world-space XZ bounds.
/// Any enemy that steps outside is immediately returned to <see cref="EnemyPool"/>
/// (counted as a kill and pooled for reuse).
///
/// Attach to any persistent scene object (e.g. WaveManager).
/// </summary>
public class EnemyBoundaryKiller : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────────────────────────────

    /// <summary>Seconds between boundary sweeps. Low enough to catch fast enemies.</summary>
    private const float CHECK_INTERVAL = 0.2f;

    // ── Inspector ─────────────────────────────────────────────────────────────────

    [Tooltip("The ground GameObject. Its world-space BoxCollider bounds define the kill boundary.")]
    public GameObject ground;

    [Tooltip("The EnemyPool that owns all active Robo instances.")]
    public EnemyPool enemyPool;

    // ── Private state ─────────────────────────────────────────────────────────────

    private float  _nextCheck;
    private Bounds _groundBounds;
    private bool   _boundsReady;

    // ── Lifecycle ─────────────────────────────────────────────────────────────────

    private void Start()
    {
        CacheBounds();
    }

    private void Update()
    {
        if (!_boundsReady) return;
        if (Time.time < _nextCheck) return;

        _nextCheck = Time.time + CHECK_INTERVAL;
        KillOutOfBounds();
    }

    // ── Bounds caching ────────────────────────────────────────────────────────────

    private void CacheBounds()
    {
        if (ground == null)
        {
            Debug.LogWarning("[EnemyBoundaryKiller] ground is not assigned.", this);
            return;
        }

        BoxCollider bc = ground.GetComponent<BoxCollider>();
        if (bc != null)
        {
            // bc.bounds is already in world space including the 100x100x1 scale
            _groundBounds = bc.bounds;
            _boundsReady  = true;
            return;
        }

        // Fallback: derive bounds from the Transform scale directly
        // ground scale = (100, 1, 100), pivot at centre, so XZ half-extents = 50
        Vector3 centre   = ground.transform.position;
        Vector3 halfSize = ground.transform.lossyScale * 0.5f;
        _groundBounds = new Bounds(centre, halfSize * 2f);
        _boundsReady  = true;
    }

    // ── Boundary check ────────────────────────────────────────────────────────────

    private void KillOutOfBounds()
    {
        if (enemyPool == null) return;

        // Snapshot the list — Return() mutates _active so we copy first
        var active = enemyPool.ActiveEnemies;
        int count  = active.Count;

        // Iterate backwards so indices stay valid if we were working on a copy,
        // but since we take a snapshot index we just collect then return.
        for (int i = count - 1; i >= 0; i--)
        {
            GameObject enemy = active[i];
            if (enemy == null) continue;

            Vector3 pos = enemy.transform.position;

            if (!IsInsideBounds(pos))
            {
                enemyPool.Return(enemy);
            }
        }
    }

    /// <summary>
    /// Returns true when <paramref name="pos"/> is within the ground's XZ footprint.
    /// Y is intentionally ignored — NavMesh agents stay on the surface normally,
    /// but we only care about lateral escape.
    /// </summary>
    private bool IsInsideBounds(Vector3 pos)
    {
        return pos.x >= _groundBounds.min.x
            && pos.x <= _groundBounds.max.x
            && pos.z >= _groundBounds.min.z
            && pos.z <= _groundBounds.max.z;
    }
}
