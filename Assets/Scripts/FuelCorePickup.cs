using System;
using UnityEngine;

/// <summary>
/// Represents a FuelCore dropped by a defeated Robo enemy.
/// Bobs up and down and rotates in place. When the player car drives into
/// its trigger collider the core is consumed and <see cref="OnCollected"/> fires.
/// </summary>
[RequireComponent(typeof(Collider))]
public class FuelCorePickup : MonoBehaviour
{
    // ── Visual animation ────────────────────────────────────────────────────────
    private const float BOB_AMPLITUDE  = 0.25f;   // units
    private const float BOB_FREQUENCY  = 1.8f;    // cycles per second
    private const float SPIN_SPEED_DEG = 90f;     // degrees per second
    private const float LIFETIME       = 15f;     // auto-destroy after this many seconds

    // ── Tags ────────────────────────────────────────────────────────────────────
    private const string PLAYER_TAG = "Player";

    /// <summary>Raised with this pickup when the player collects it.</summary>
    public event Action<FuelCorePickup> OnCollected;

    private Vector3 _originPosition;
    private float   _bobTime;
    private bool    _collected;

    private void Start()
    {
        // Ensure trigger so no physics response — only notification.
        Collider col = GetComponent<Collider>();
        col.isTrigger = true;

        _originPosition = transform.position;

        // Auto-despawn so uncollected cores don't litter the scene forever.
        Destroy(gameObject, LIFETIME);
    }

    private void Update()
    {
        if (_collected) return;

        _bobTime += Time.deltaTime;

        // Vertical bob
        float yOffset = Mathf.Sin(_bobTime * BOB_FREQUENCY * Mathf.PI * 2f) * BOB_AMPLITUDE;
        transform.position = new Vector3(_originPosition.x, _originPosition.y + yOffset, _originPosition.z);

        // Continuous spin around Y axis
        transform.Rotate(Vector3.up, SPIN_SPEED_DEG * Time.deltaTime, Space.World);
    }

    private void OnTriggerEnter(Collider other)
    {
        TryCollect(other);
    }

    // OnTriggerStay catches cases where the car is already overlapping (e.g. slow
    // approach, spawned inside the collider, or kinematic movement via transform.position).
    private void OnTriggerStay(Collider other)
    {
        TryCollect(other);
    }

    private void TryCollect(Collider other)
    {
        if (_collected) return;

        FuelCoreCollector collector = other.GetComponentInParent<FuelCoreCollector>();
        if (collector == null)
            collector = other.GetComponent<FuelCoreCollector>();

        if (collector != null)
        {
            Collect(collector);
            return;
        }

        // Fallback: player detected by tag or CarController without a collector present.
        if (other.CompareTag(PLAYER_TAG) || other.GetComponent<CarController>() != null
            || other.GetComponentInParent<CarController>() != null)
        {
            Collect(null);
        }
    }

    private void Collect(FuelCoreCollector collector)
    {
        _collected = true;
        collector?.OnFuelCoreCollected();
        OnCollected?.Invoke(this);
        Destroy(gameObject);
    }

    /// <summary>
    /// Forces immediate collection by an external caller (e.g. dash sweep).
    /// Safe to call even if already collected — the guard flag prevents double-collection.
    /// </summary>
    public void ForceCollect(FuelCoreCollector collector)
    {
        if (_collected) return;
        Collect(collector);
    }
}
