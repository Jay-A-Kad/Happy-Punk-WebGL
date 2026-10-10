using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Drives a Robo enemy toward the player every frame using the NavMeshAgent.
/// Disable this component (via the pool) to pause pursuit when the enemy is inactive.
///
/// Also monitors the Robo's world position: if it falls below
/// <see cref="FALL_THRESHOLD"/> (off the arena) it is immediately returned to
/// the pool, mirroring the behaviour of <see cref="FallDetector"/> on the car.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyNavAgent : MonoBehaviour
{
    /// <summary>
    /// Y world position below which a Robo is considered off the ground.
    /// The arena floor surface sits at Y ≈ 0.5 (ground cube at Y=0, scale=1).
    /// A threshold of -3 gives a small buffer for uneven terrain without
    /// letting Robos float around off-screen.
    /// </summary>
    private const float FALL_THRESHOLD = -3f;

    private NavMeshAgent _agent;
    private Transform    _playerTransform;
    private EnemyPool    _pool;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
    }

    /// <summary>
    /// Initialises the agent with the player transform to chase and the pool
    /// it belongs to so it can self-return when it leaves the arena.
    /// Call this once after spawning from the pool.
    /// </summary>
    public void Initialise(Transform playerTransform, EnemyPool pool)
    {
        _playerTransform = playerTransform;
        _pool            = pool;

        // Explicitly snap the agent to the nearest NavMesh point so it is
        // guaranteed to be on the surface regardless of spawn Y.
        _agent.Warp(transform.position);
    }

    private void Update()
    {
        // Out-of-bounds check: return to pool if the Robo has left the arena.
        if (transform.position.y < FALL_THRESHOLD)
        {
            _pool?.Return(gameObject);
            return;
        }

        if (_playerTransform == null || !_agent.isOnNavMesh)
            return;

        // Freeze pursuit once the player is dead so enemies stop mid-track.
        if (GameStateManager.Instance != null &&
            GameStateManager.Instance.CurrentState == GameStateManager.State.PlayerDead)
        {
            _agent.ResetPath();
            _agent.velocity = Vector3.zero;
            return;
        }

        _agent.SetDestination(_playerTransform.position);
    }

    /// <summary>
    /// Resets agent state so it is ready to be reused from the pool.
    /// </summary>
    public void ResetAgent()
    {
        if (_agent.isOnNavMesh)
            _agent.ResetPath();

        _playerTransform = null;
        _pool            = null;
    }
}
