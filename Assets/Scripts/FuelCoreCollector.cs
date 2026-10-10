using UnityEngine;

/// <summary>
/// Attach to the player car root. Sweeps for nearby <see cref="FuelCorePickup"/>
/// objects every frame via <see cref="Physics.OverlapSphere"/> and collects them.
///
/// Using an active sweep instead of relying on Unity trigger callbacks because the
/// car (layer: CAR) and fuel cores (layer: Default) are configured in the Physics
/// collision matrix to not interact, which silently blocks OnTriggerEnter events
/// between them regardless of isTrigger settings.
/// </summary>
public class FuelCoreCollector : MonoBehaviour
{
    private const float COLLECT_RADIUS = 1.8f; // world-space pickup radius around the car centre

    [Tooltip("Reference to the HUDManager to increment the fuel core counter.")]
    public HUDManager hudManager;

    private void Update()
    {
        // Include trigger colliders so the pickup's trigger sphere is detected.
        Collider[] hits = Physics.OverlapSphere(
            transform.position,
            COLLECT_RADIUS,
            Physics.AllLayers,
            QueryTriggerInteraction.Collide);

        foreach (Collider hit in hits)
        {
            FuelCorePickup pickup = hit.GetComponent<FuelCorePickup>()
                                 ?? hit.GetComponentInParent<FuelCorePickup>();

            pickup?.ForceCollect(this);
        }
    }

    /// <summary>Called by FuelCorePickup when the player collects it.</summary>
    public void OnFuelCoreCollected()
    {
        hudManager?.AddFuelCore();
    }
}
