using UnityEngine;

/// <summary>
/// Monitors the car's Y position. When the car falls below
/// <see cref="fallThreshold"/> it is considered off the map and the player
/// is killed immediately, which triggers the existing game-over flow.
///
/// Attach to the MonsterTruck root alongside <see cref="CarHealthSystem"/>.
/// </summary>
[RequireComponent(typeof(CarHealthSystem))]
public class FallDetector : MonoBehaviour
{
    [Tooltip("Y world position below which the car is considered off the ground " +
             "(e.g. -5 gives some forgiveness for ramps or uneven terrain).")]
    public float fallThreshold = -5f;

    private CarHealthSystem _health;
    private bool _triggered;

    private void Awake()
    {
        _health = GetComponent<CarHealthSystem>();
    }

    private void Update()
    {
        if (_triggered) return;
        if (transform.position.y < fallThreshold)
        {
            _triggered = true;
            _health.KillInstantly();
        }
    }
}
