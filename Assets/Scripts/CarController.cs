using UnityEngine;

/// <summary>
/// Realistic monster truck controller. The car always drives forward along its own
/// heading, steers toward the mouse world position using arcade-style proportional
/// steering, reduces speed on sharp turns, and tilts the body outward on corners.
/// </summary>
public class CarController : MonoBehaviour
{
    private const string GROUND_LAYER = "GROUND";

    // ── Movement ────────────────────────────────────────────────────────────────

    [Header("Movement")]
    [Tooltip("Top speed in units per second.")]
    public float maxSpeed = 12f;

    [Tooltip("Acceleration rate in units per second squared.")]
    public float acceleration = 8f;

    [Tooltip("Deceleration rate when braking or near the target.")]
    public float brakingDeceleration = 22f;

    [Tooltip("Distance at which the car considers itself arrived and brakes to a stop.")]
    public float stoppingDistance = 0.6f;

    // ── Steering ────────────────────────────────────────────────────────────────

    [Header("Steering")]
    [Tooltip("Maximum yaw rotation in degrees per second at full steering lock.")]
    public float maxSteerSpeed = 160f;

    [Tooltip("How aggressively steering hunts the target angle. Higher = tighter arcs, " +
             "lower = wider sweeping turns.")]
    public float steerResponsiveness = 3f;

    [Tooltip("Minimum steering effectiveness at zero speed. Prevents the car from being " +
             "completely stuck when stationary (0 = no turn from rest, 1 = full turn always).")]
    [Range(0f, 1f)]
    public float minSteerAtRest = 0.3f;

    // ── Body Roll ───────────────────────────────────────────────────────────────

    [Header("Body Roll")]
    [Tooltip("Maximum roll angle in degrees on the sharpest turns.")]
    public float maxRollAngle = 18f;

    [Tooltip("Maximum forward/backward pitch in degrees when accelerating or braking.")]
    public float maxPitchAngle = 5f;

    [Tooltip("Speed at which roll and pitch ease in and out.")]
    public float tiltSmoothing = 5f;

    // ── Model ───────────────────────────────────────────────────────────────────

    [Header("Model")]
    [Tooltip("Y rotation offset in degrees to align the mesh nose with the logical forward direction. " +
             "90 = mesh nose is along local +X.")]
    public float modelYawOffset = 90f;

    [Tooltip("Child transform of the visible car body that receives roll and pitch. " +
             "Assign the mesh root child so only the visual tilts, not the collision/physics root.")]
    public Transform bodyTransform;

    // ── Private State ───────────────────────────────────────────────────────────

    private Camera _mainCamera;
    private int _groundLayerMask;

    private Vector3 _targetPosition;
    private bool _hasTarget;

    /// <summary>When false the car ignores all input (used during intro and countdown).</summary>
    private bool _inputEnabled;

    private float _logicalYaw;       // current movement heading in degrees (Y-axis)
    private float _currentSpeed;     // signed forward speed (always >= 0 here)
    private float _angularVelocity;  // degrees per second, positive = left turn

    private float _currentRoll;      // smoothed body roll angle
    private float _currentPitch;     // smoothed body pitch angle
    private float _previousSpeed;    // for computing acceleration delta each frame

    private Vector3 _knockbackVelocity; // world-space impulse applied on top of steering movement
    private const float KNOCKBACK_DAMPING = 5f; // exponential decay rate (units/s²)

    /// <summary>Current forward speed in units per second.</summary>
    public float CurrentSpeed => _currentSpeed;

    /// <summary>Signed angular velocity in degrees per second (positive = left turn).</summary>
    public float AngularVelocity => _angularVelocity;

    /// <summary>
    /// World-space unit vector pointing in the logical movement direction.
    /// Use this for anything that needs to move along the car's actual heading
    /// (e.g. dash, knockback), since <c>transform.forward</c> includes the model
    /// yaw offset and does not match the movement axis.
    /// </summary>
    public Vector3 MovementForward => LogicalForward;

    /// <summary> World-space unit vector pointing in the logical movement direction. </summary>
    private Vector3 LogicalForward =>
        new Vector3(
            Mathf.Sin(_logicalYaw * Mathf.Deg2Rad),
            0f,
            Mathf.Cos(_logicalYaw * Mathf.Deg2Rad)
        );

    // ── Unity Lifecycle ─────────────────────────────────────────────────────────

    private void Awake()
    {
        _mainCamera = Camera.main;
        _groundLayerMask = LayerMask.GetMask(GROUND_LAYER);
        _targetPosition = transform.position;

        // Derive logical yaw from the initial transform, stripping the model offset.
        _logicalYaw = transform.eulerAngles.y - modelYawOffset;
        _previousSpeed = 0f;

        // Input is locked until the GameStateManager fires OnGameStarted.
        _inputEnabled = false;
    }

    private void Start()
    {
        // Subscribe after all Awake() calls have run, guaranteeing Instance is set.
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnGameStarted += EnableInput;

            // If the scene was loaded mid-game (e.g. additive loading) and the game
            // is already in the Playing state, enable input immediately.
            if (GameStateManager.Instance.CurrentState == GameStateManager.State.Playing)
                EnableInput();
        }
        else
        {
            Debug.LogError("[CarController] GameStateManager.Instance is null in Start. " +
                           "Ensure GameStateManager exists in the scene.", this);
        }
    }

    private void OnDestroy()
    {
        if (GameStateManager.Instance != null)
            GameStateManager.Instance.OnGameStarted -= EnableInput;
    }

    private void EnableInput()
    {
        _inputEnabled = true;
        // Re-cache the main camera in case it was swapped during the intro.
        _mainCamera = Camera.main;
    }

    private void Update()
    {
        if (!_inputEnabled) return;

        UpdateMouseTarget();
        Steer();
        Accelerate();
        ApplyMovement();
        ApplyKnockback();
        ApplyBodyTilt();
    }

    // ── Target ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Casts a ray from the camera through the mouse onto the GROUND layer and
    /// stores the resulting world position as the driving target.
    /// </summary>
    private void UpdateMouseTarget()
    {
        Ray ray = _mainCamera.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, _groundLayerMask))
        {
            _targetPosition = new Vector3(hit.point.x, transform.position.y, hit.point.z);
            _hasTarget = true;
        }
    }

    // ── Steering ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Rotates the logical heading toward the target using proportional steering.
    /// Steering effectiveness scales with speed so the car turns sluggishly from rest.
    /// </summary>
    private void Steer()
    {
        Vector3 toTarget = _targetPosition - transform.position;
        toTarget.y = 0f;

        if (!_hasTarget || toTarget.magnitude < stoppingDistance)
        {
            // Bleed off angular velocity so the body roll settles.
            _angularVelocity = Mathf.Lerp(_angularVelocity, 0f, Time.deltaTime * tiltSmoothing);
            return;
        }

        float targetYaw = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
        float angleDiff = Mathf.DeltaAngle(_logicalYaw, targetYaw);

        // Proportional steer, clamped to max rate.
        float rawSteer = Mathf.Clamp(angleDiff * steerResponsiveness, -maxSteerSpeed, maxSteerSpeed);

        // Reduce steer at low speed (understeer from rest), keep a minimum so the car
        // can orient itself even before rolling.
        float speedRatio = Mathf.Clamp01(_currentSpeed / maxSpeed);
        float steerFactor = Mathf.Lerp(minSteerAtRest, 1f, speedRatio);

        float previousYaw = _logicalYaw;
        _logicalYaw += rawSteer * steerFactor * Time.deltaTime;

        // Compute signed angular velocity for body roll.
        _angularVelocity = Mathf.DeltaAngle(previousYaw, _logicalYaw) / Time.deltaTime;
    }

    // ── Acceleration ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Adjusts speed: accelerates toward the target speed, slows on sharp turns,
    /// and brakes as it approaches the stopping radius.
    /// </summary>
    private void Accelerate()
    {
        if (!_hasTarget)
        {
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, 0f, brakingDeceleration * Time.deltaTime);
            return;
        }

        Vector3 toTarget = _targetPosition - transform.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;

        if (distance < stoppingDistance)
        {
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, 0f, brakingDeceleration * Time.deltaTime);
            return;
        }

        // Speed penalty proportional to how far the car is from facing the target.
        float targetYaw = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
        float angleDiff = Mathf.Abs(Mathf.DeltaAngle(_logicalYaw, targetYaw));
        float cornerPenalty = 1f - Mathf.Clamp01(angleDiff / 90f) * 0.55f;

        // Gradual brake as we close in (braking zone = 4× stopping distance).
        float approachFactor = Mathf.Clamp01(distance / (stoppingDistance * 4f));

        float desiredSpeed = maxSpeed * cornerPenalty * approachFactor;

        _currentSpeed = desiredSpeed > _currentSpeed
            ? Mathf.MoveTowards(_currentSpeed, desiredSpeed, acceleration * Time.deltaTime)
            : Mathf.MoveTowards(_currentSpeed, desiredSpeed, brakingDeceleration * Time.deltaTime);
    }

    // ── Transform Application ────────────────────────────────────────────────────

    /// <summary>
    /// Moves the car along its logical heading and applies yaw + model offset to
    /// the root transform. Tilt is applied separately to the body child.
    /// </summary>
    private void ApplyMovement()
    {
        transform.position += LogicalForward * _currentSpeed * Time.deltaTime;
        transform.rotation = Quaternion.Euler(0f, _logicalYaw + modelYawOffset, 0f);
    }

    // ── Knockback ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies a blast impulse in the given world-space direction.
    /// Called by RocketProjectile when the missile detonates near the player.
    /// </summary>
    public void ApplyKnockback(Vector3 worldDirection, float force)
    {
        Vector3 horizontal       = new Vector3(worldDirection.x, 0f, worldDirection.z).normalized;
        _knockbackVelocity       = horizontal * force;
    }

    /// <summary>
    /// Moves the car by the decaying knockback velocity each frame.
    /// Runs after ApplyMovement so the impulse stacks on top of normal steering.
    /// </summary>
    private void ApplyKnockback()
    {
        if (_knockbackVelocity.sqrMagnitude < 0.01f)
        {
            _knockbackVelocity = Vector3.zero;
            return;
        }

        transform.position += _knockbackVelocity * Time.deltaTime;
        _knockbackVelocity  = Vector3.Lerp(_knockbackVelocity, Vector3.zero, KNOCKBACK_DAMPING * Time.deltaTime);
    }

    // ── Body Tilt ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies outward body roll proportional to angular velocity and longitudinal
    /// pitch proportional to the current acceleration / braking impulse.
    /// If bodyTransform is assigned the root stays level; otherwise the whole car tilts.
    /// </summary>
    private void ApplyBodyTilt()
    {
        // ── Roll ────────────────────────────────────────────────────────────────
        // Positive angular velocity = left turn → body leans right (negative Z).
        float targetRoll = -(_angularVelocity / maxSteerSpeed) * maxRollAngle;
        targetRoll = Mathf.Clamp(targetRoll, -maxRollAngle, maxRollAngle);
        _currentRoll = Mathf.Lerp(_currentRoll, targetRoll, Time.deltaTime * tiltSmoothing);

        // ── Pitch ───────────────────────────────────────────────────────────────
        // Positive acceleration → nose dips forward (negative X pitch).
        float speedDelta = (_currentSpeed - _previousSpeed) / Time.deltaTime;
        _previousSpeed = _currentSpeed;
        float targetPitch = -(speedDelta / acceleration) * maxPitchAngle;
        targetPitch = Mathf.Clamp(targetPitch, -maxPitchAngle, maxPitchAngle);
        _currentPitch = Mathf.Lerp(_currentPitch, targetPitch, Time.deltaTime * tiltSmoothing);

        // ── Apply ───────────────────────────────────────────────────────────────
        if (bodyTransform != null)
        {
            // Only the visual body tilts; root stays flat for accurate collisions.
            bodyTransform.localEulerAngles = new Vector3(_currentPitch, 0f, _currentRoll);
        }
        else
        {
            // Bake tilt into root on top of yaw + model offset.
            Quaternion baseRotation = Quaternion.Euler(0f, _logicalYaw + modelYawOffset, 0f);
            transform.rotation = baseRotation * Quaternion.Euler(_currentPitch, 0f, _currentRoll);
        }
    }
}
