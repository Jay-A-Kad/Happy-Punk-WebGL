using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Object pool for Robo enemies. Starts at <see cref="INITIAL_POOL_SIZE"/> instances
/// and expands automatically via <see cref="EnsureCapacity"/> as wave sizes grow.
/// </summary>
public class EnemyPool : MonoBehaviour
{
    private const int INITIAL_POOL_SIZE = 40;

    [Tooltip("The Robo enemy prefab to pool.")]
    public GameObject enemyPrefab;

    private readonly List<GameObject> _pool   = new(INITIAL_POOL_SIZE);
    private readonly List<GameObject> _active = new(INITIAL_POOL_SIZE);

    private void Awake()
    {
        PreWarm(INITIAL_POOL_SIZE);
    }

    /// <summary>
    /// Instantiates <paramref name="count"/> instances up front and deactivates them.
    /// </summary>
    private void PreWarm(int count)
    {
        for (int i = 0; i < count; i++)
        {
            GameObject instance = Instantiate(enemyPrefab, transform);
            instance.SetActive(false);
            _pool.Add(instance);
        }
    }

    /// <summary>
    /// Guarantees the pool can supply at least <paramref name="needed"/> enemies at once.
    /// Instantiates additional instances if the current total capacity is insufficient.
    /// </summary>
    public void EnsureCapacity(int needed)
    {
        int totalCapacity = _pool.Count + _active.Count;
        int deficit       = needed - totalCapacity;
        if (deficit > 0)
        {
            Debug.Log($"[EnemyPool] Expanding pool by {deficit} to meet wave demand of {needed}.");
            PreWarm(deficit);
        }
    }

    /// <summary>
    /// Returns an inactive enemy from the pool, positioned at the given world point.
    /// Returns null if the pool is exhausted.
    /// </summary>
    public GameObject Get(Vector3 position, Quaternion rotation)
    {
        if (_pool.Count == 0)
        {
            Debug.LogWarning("[EnemyPool] Pool exhausted — all enemies are active.");
            return null;
        }

        GameObject instance = _pool[_pool.Count - 1];
        _pool.RemoveAt(_pool.Count - 1);

        instance.transform.SetPositionAndRotation(position, rotation);
        instance.SetActive(true);
        _active.Add(instance);

        return instance;
    }

    /// <summary>
    /// Deactivates an enemy and returns it to the pool.
    /// </summary>
    public void Return(GameObject instance)
    {
        if (!_active.Remove(instance))
        {
            Debug.LogWarning($"[EnemyPool] Tried to return an object that is not tracked as active: {instance.name}");
            return;
        }

        instance.SetActive(false);

        EnemyNavAgent agent = instance.GetComponent<EnemyNavAgent>();
        if (agent != null)
        {
            agent.ResetAgent();
        }

        RoboContactDamage contact = instance.GetComponent<RoboContactDamage>();
        if (contact != null)
        {
            contact.ResetContactState();
        }

        TotalEnemiesKilled++;
        _pool.Add(instance);
    }

    /// <summary>
    /// Returns all currently active enemies back to the pool at once.
    /// </summary>
    public void ReturnAll()
    {
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            Return(_active[i]);
        }
    }

    /// <summary>Read-only snapshot of currently active enemies.</summary>
    public IReadOnlyList<GameObject> ActiveEnemies => _active;

    /// <summary>
    /// Total number of enemies returned to the pool (i.e. killed) over the session.
    /// </summary>
    public int TotalEnemiesKilled { get; private set; }
}
