using UnityEngine;

/// <summary>
/// Deals periodic damage to the player car when a Robo is in contact with it.
///
/// The Robo uses a NavMeshAgent with no Rigidbody, which means OnCollisionEnter
/// callbacks are never fired. Instead this component adds a slightly larger trigger
/// collider on the Robo to detect overlap with the car, then ticks damage on a
/// cooldown so the car takes one hit per contact interval rather than every frame.
///
/// Attach this to the Robo prefab root alongside the existing CapsuleCollider.
/// </summary>
[RequireComponent(typeof(CapsuleCollider))]
public class RoboContactDamage : MonoBehaviour
{
    private const string CAR_TAG         = "CAR";
    private const float  DAMAGE_AMOUNT   = 10f;   // damage per contact tick
    private const float  DAMAGE_COOLDOWN = 0.8f;  // seconds between damage ticks per Robo

    private float _lastDamageTime = float.NegativeInfinity;

    /// <summary>Trigger radius is set slightly wider than the CapsuleCollider so
    /// the overlap fires before the NavMeshAgent is visually clipping the car.</summary>
    private const float TRIGGER_RADIUS_OFFSET = 0.15f;

    private CapsuleCollider _triggerCollider;

    private void Awake()
    {
        // Add a second CapsuleCollider configured as a trigger for contact detection.
        // We duplicate the shape of the existing solid collider and widen it slightly.
        CapsuleCollider solid = GetComponent<CapsuleCollider>();

        _triggerCollider             = gameObject.AddComponent<CapsuleCollider>();
        _triggerCollider.isTrigger   = true;
        _triggerCollider.center      = solid.center;
        _triggerCollider.radius      = solid.radius + TRIGGER_RADIUS_OFFSET;
        _triggerCollider.height      = solid.height;
        _triggerCollider.direction   = solid.direction;
    }

    private void OnTriggerStay(Collider other)
    {
        if (!other.CompareTag(CAR_TAG))
            return;

        if (Time.time < _lastDamageTime + DAMAGE_COOLDOWN)
            return;

        CarHealthSystem health = other.GetComponent<CarHealthSystem>()
                              ?? other.GetComponentInParent<CarHealthSystem>();

        if (health == null)
            return;

        health.TakeDamage(DAMAGE_AMOUNT);
        _lastDamageTime = Time.time;
    }

    /// <summary>Resets the damage cooldown when this Robo is returned to the pool
    /// so it does not carry stale timing into its next activation.</summary>
    public void ResetContactState()
    {
        _lastDamageTime = float.NegativeInfinity;
    }
}
