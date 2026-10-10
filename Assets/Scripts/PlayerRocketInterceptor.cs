using UnityEngine;

/// <summary>
/// Draws a detection circle around the player.
/// When an incoming rocket enters the circle, the player can press E to lock it into orbit.
/// While orbiting, a directional arrow shows where the rocket will be thrown.
/// Releasing E fires the rocket in that direction.
/// </summary>
public class PlayerRocketInterceptor : MonoBehaviour
{
    // ── Tuning ─────────────────────────────────────────────────────────────────
    private const float CIRCLE_RADIUS = 3.5f;      // drawn ring radius and exact orbit path
    private const float DETECTION_RADIUS = 5.5f;   // larger trigger zone so the rocket can reach the ring
    private const float ORBIT_HEIGHT = 1.5f;       // world-space Y the rocket orbits at
    private const float GROUND_Y = 0.52f;          // just above ground surface for circle and arrow
    private const float ORBIT_SPEED = 110f;         // degrees per second
    private const KeyCode LOCK_KEY = KeyCode.E;

    // Circle
    private const int CIRCLE_SEGMENTS = 80;
    private const float CIRCLE_WIDTH = 0.1f;

    // Arrow — chevron on the ground ring pointing in the throw direction
    private const float ARROW_HEAD_SIZE = 0.55f;
    private const float ARROW_WING_WIDTH = 0.35f;
    private const float ARROW_SHAFT_LENGTH = 0.85f;
    private const float ARROW_LINE_WIDTH = 0.12f;

    // Tolerance multiplier before leaving Available state
    private const float EXIT_TOLERANCE = 1.3f;

    // ── Colors ─────────────────────────────────────────────────────────────────
    private static readonly Color CIRCLE_IDLE      = new(0.3f, 0.85f, 1f,  0.20f);
    private static readonly Color CIRCLE_AVAILABLE = new(1f,   0.95f, 0f,  0.85f);
    private static readonly Color CIRCLE_LOCKED    = new(1f,   0.40f, 0f,  1.00f);
    private static readonly Color ARROW_COLOR      = new(1f,   0.95f, 0f,  1.00f);

    // ── Inspector references ────────────────────────────────────────────────────
    [Tooltip("LineRenderer child used to draw the detection circle.")]
    public LineRenderer circleRenderer;

    [Tooltip("LineRenderer child used to draw the directional arrow.")]
    public LineRenderer arrowRenderer;

    [Tooltip("Visual centre of the truck model. Assign the TruckCenter child. " +
             "Falls back to this transform when not set.")]
    public Transform centerPoint;

    [Tooltip("Direct reference to all RocketProjectile instances in the pool. " +
             "Populated at runtime by RocketLauncher.BuildPool(). " +
             "TryDetectRocket picks whichever one is closest and interceptable.")]
    public RocketProjectile[] rocketPool;

    /// <summary>
    /// Convenience property kept for backward compatibility with RocketLauncher which
    /// sets the first-slot reference. Setting this also fills slot 0 of the pool array.
    /// </summary>
    public RocketProjectile trackedRocketRef
    {
        get => rocketPool != null && rocketPool.Length > 0 ? rocketPool[0] : null;
        set
        {
            if (rocketPool == null || rocketPool.Length == 0)
                rocketPool = new RocketProjectile[1];
            rocketPool[0] = value;
        }
    }

    private Vector3 Center => centerPoint != null ? centerPoint.position : transform.position;

    // ── State machine ───────────────────────────────────────────────────────────
    private enum InterceptState { Idle, Available, Locked }
    private InterceptState _state = InterceptState.Idle;

    private RocketProjectile _trackedRocket;
    private float _orbitAngle;

    // ── Unity lifecycle ─────────────────────────────────────────────────────────

    private void Start()
    {
        InitCircle();
        InitArrow();
    }

    private void Update()
    {
        DrawCircle();

        switch (_state)
        {
            case InterceptState.Idle:
                SetCircleColor(CIRCLE_IDLE);
                SetArrowVisible(false);
                TryDetectRocket();
                break;

            case InterceptState.Available:
                SetCircleColor(CIRCLE_AVAILABLE);
                SetArrowVisible(false);
                ValidateTrackedRocket();
                if (_state != InterceptState.Available) break;
                if (Input.GetKeyDown(LOCK_KEY))
                {
                    BeginOrbit();
                }
                break;

            case InterceptState.Locked:
                SetCircleColor(CIRCLE_LOCKED);
                if (_trackedRocket == null || !_trackedRocket.gameObject.activeInHierarchy)
                {
                    ReturnToIdle();
                    break;
                }
                StepOrbit();
                DrawArrow();
                if (Input.GetKeyUp(LOCK_KEY))
                {
                    ThrowRocket();
                }
                break;
        }
    }

    // ── Detection ───────────────────────────────────────────────────────────────

    private void TryDetectRocket()
    {
        if (rocketPool == null) return;

        // Pick the closest interceptable rocket inside the detection radius.
        RocketProjectile best     = null;
        float            bestDist = float.MaxValue;

        foreach (RocketProjectile rocket in rocketPool)
        {
            if (rocket == null) continue;
            if (!rocket.gameObject.activeInHierarchy) continue;
            if (!rocket.IsInterceptable) continue;

            float dist = Vector3.Distance(Center, rocket.transform.position);
            if (dist <= DETECTION_RADIUS && dist < bestDist)
            {
                best     = rocket;
                bestDist = dist;
            }
        }

        if (best == null) return;

        _trackedRocket = best;
        _state         = InterceptState.Available;
    }

    private void ValidateTrackedRocket()
    {
        if (_trackedRocket == null || !_trackedRocket.gameObject.activeInHierarchy ||
            !_trackedRocket.IsInterceptable)
        {
            ReturnToIdle();
            return;
        }

        float dist = Vector3.Distance(Center, _trackedRocket.transform.position);
        if (dist > DETECTION_RADIUS * EXIT_TOLERANCE)
        {
            ReturnToIdle();
        }
    }

    // ── Orbit ───────────────────────────────────────────────────────────────────

    private void BeginOrbit()
    {
        // Double-check the tracked rocket is still valid and interceptable before
        // committing — guards against the same frame where detection and input both fire.
        if (_trackedRocket == null
            || !_trackedRocket.gameObject.activeInHierarchy
            || !_trackedRocket.IsInterceptable)
        {
            ReturnToIdle();
            return;
        }

        Vector3 toRocket = _trackedRocket.transform.position - Center;
        _orbitAngle = Mathf.Atan2(toRocket.z, toRocket.x) * Mathf.Rad2Deg;

        _trackedRocket.EnterOrbit();
        _state = InterceptState.Locked;
        SetArrowVisible(true);
    }

    private void StepOrbit()
    {
        _orbitAngle += ORBIT_SPEED * Time.deltaTime;
        float rad = _orbitAngle * Mathf.Deg2Rad;

        Vector3 orbitPos = new(
            Center.x + Mathf.Cos(rad) * CIRCLE_RADIUS,
            ORBIT_HEIGHT,
            Center.z + Mathf.Sin(rad) * CIRCLE_RADIUS
        );

        _trackedRocket.transform.position = orbitPos;

        // Pass the tangent (the actual travel direction around the circle) to the
        // rocket so it can apply its own nose correction and wobble.
        // Never write transform.rotation here — RocketProjectile owns all rotation.
        _trackedRocket.OrbitTick(GetTangent(rad));
    }

    private void ThrowRocket()
    {
        float rad = _orbitAngle * Mathf.Deg2Rad;
        _trackedRocket.ThrowInDirection(GetRadialOut(rad));
        ReturnToIdle();
    }

    private void ReturnToIdle()
    {
        _trackedRocket = null;
        _state = InterceptState.Idle;
        SetArrowVisible(false);
    }

    // ── Circle drawing ──────────────────────────────────────────────────────────

    private void InitCircle()
    {
        if (circleRenderer == null) return;
        circleRenderer.positionCount = CIRCLE_SEGMENTS;
        circleRenderer.loop = true;
        circleRenderer.useWorldSpace = true;
        circleRenderer.startWidth = CIRCLE_WIDTH;
        circleRenderer.endWidth = CIRCLE_WIDTH;
        SetCircleColor(CIRCLE_IDLE);
    }

    private void DrawCircle()
    {
        if (circleRenderer == null) return;

        for (int i = 0; i < CIRCLE_SEGMENTS; i++)
        {
            float angle = (float)i / CIRCLE_SEGMENTS * Mathf.PI * 2f;
            circleRenderer.SetPosition(i, new Vector3(
                Center.x + Mathf.Cos(angle) * CIRCLE_RADIUS,
                GROUND_Y,
                Center.z + Mathf.Sin(angle) * CIRCLE_RADIUS
            ));
        }
    }

    private void SetCircleColor(Color color)
    {
        if (circleRenderer == null) return;
        circleRenderer.startColor = color;
        circleRenderer.endColor = color;
    }

    // ── Arrow drawing ───────────────────────────────────────────────────────────

    private void InitArrow()
    {
        if (arrowRenderer == null) return;
        // 5-point arrow: rightWing → tip → leftWing → neck → shaftBase
        arrowRenderer.positionCount = 5;
        arrowRenderer.loop = false;
        arrowRenderer.useWorldSpace = true;
        arrowRenderer.startWidth = ARROW_LINE_WIDTH;
        arrowRenderer.endWidth = ARROW_LINE_WIDTH;
        arrowRenderer.startColor = ARROW_COLOR;
        arrowRenderer.endColor = ARROW_COLOR;
        SetArrowVisible(false);
    }

    private void DrawArrow()
    {
        if (arrowRenderer == null) return;

        float rad = _orbitAngle * Mathf.Deg2Rad;

        // Arrow sits on the ground ring at the current orbit angle
        Vector3 center = new(
            Center.x + Mathf.Cos(rad) * CIRCLE_RADIUS,
            GROUND_Y,
            Center.z + Mathf.Sin(rad) * CIRCLE_RADIUS
        );

        Vector3 forward = GetRadialOut(rad);                           // points away from player centre
        Vector3 right   = GetTangent(rad);                              // tangent, used for wing spread

        Vector3 tip       = center + forward * ARROW_HEAD_SIZE;
        Vector3 rightWing = center + right   * ARROW_WING_WIDTH;
        Vector3 leftWing  = center - right   * ARROW_WING_WIDTH;
        Vector3 neck      = center;
        Vector3 shaftBase = center - forward * ARROW_SHAFT_LENGTH;

        // Line: rightWing → tip → leftWing → neck → shaftBase
        arrowRenderer.SetPosition(0, rightWing);
        arrowRenderer.SetPosition(1, tip);
        arrowRenderer.SetPosition(2, leftWing);
        arrowRenderer.SetPosition(3, neck);
        arrowRenderer.SetPosition(4, shaftBase);
    }

    private void SetArrowVisible(bool visible)
    {
        if (arrowRenderer != null) arrowRenderer.enabled = visible;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>Unit vector pointing radially outward from the player at the given orbit angle.</summary>
    private static Vector3 GetRadialOut(float rad)
    {
        return new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
    }

    /// <summary>Unit tangent to the orbit circle at the given angle — used only for wing spread.</summary>
    private static Vector3 GetTangent(float rad)
    {
        return new Vector3(-Mathf.Sin(rad), 0f, Mathf.Cos(rad));
    }
}
