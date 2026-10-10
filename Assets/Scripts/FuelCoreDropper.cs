using UnityEngine;

/// <summary>
/// Attach this to the Robo enemy prefab. When the enemy is disabled (returned to the pool)
/// it spawns a <see cref="FuelCorePickup"/> at its last known world position.
/// Uses OnDisable so it works correctly with the EnemyPool pattern.
/// </summary>
public class FuelCoreDropper : MonoBehaviour
{
    private const float DROP_HEIGHT_OFFSET = 1.3f;

    [Tooltip("The FuelCore prefab to instantiate on enemy death.")]
    public GameObject fuelCorePrefab;

    /// <summary>
    /// Called by Unity when the GameObject is deactivated (returned to pool).
    /// Spawns the fuel core at the enemy's last position.
    /// </summary>
    private void OnDisable()
    {
        // Only drop when the game is running — not on scene teardown.
        if (!Application.isPlaying) return;
        if (fuelCorePrefab == null) return;

        Vector3 dropPosition = new(
            transform.position.x,
            DROP_HEIGHT_OFFSET,
            transform.position.z
        );

        Instantiate(fuelCorePrefab, dropPosition, Quaternion.identity);
    }
}
