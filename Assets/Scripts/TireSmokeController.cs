using UnityEngine;

/// <summary>
/// Spawns lofty tire-smoke particle systems at each rear wheel position when
/// the car is executing a hard turn. Smoke intensity scales with turn sharpness.
///
/// Attach to the MonsterTruck root alongside <see cref="CarController"/>.
/// Assign the four wheel-position transforms in the Inspector.
/// </summary>
[RequireComponent(typeof(CarController))]
public class TireSmokeController : MonoBehaviour
{
    // ── Smoke trigger thresholds ────────────────────────────────────────────────

    [Header("Turn Thresholds")]
    [Tooltip("Minimum absolute angular velocity (deg/s) before smoke begins.")]
    public float smokeStartAngularVelocity = 45f;

    [Tooltip("Angular velocity (deg/s) at which smoke reaches maximum emission.")]
    public float smokeMaxAngularVelocity   = 130f;

    [Tooltip("Minimum forward speed (units/s) required before smoke is produced.")]
    public float minimumSpeedForSmoke      = 3f;

    // ── Wheel anchors ───────────────────────────────────────────────────────────

    [Header("Wheel Positions")]
    [Tooltip("Rear-left wheel transform (smoke emitter origin).")]
    public Transform rearLeftWheel;

    [Tooltip("Rear-right wheel transform (smoke emitter origin).")]
    public Transform rearRightWheel;

    // ── Particle tuning ─────────────────────────────────────────────────────────

    [Header("Smoke Particles")]
    [Tooltip("Maximum particles emitted per second at full lock.")]
    public float maxEmissionRate = 60f;

    [Tooltip("How fast individual smoke puffs expand as they drift upward.")]
    public float smokeDriftSpeed = 0.4f;

    // ── Constants ───────────────────────────────────────────────────────────────

    // URP-compatible shader — "Particles/Standard Unlit" is Built-in RP only and
    // will not be included in WebGL URP builds.
    private const string PARTICLE_SHADER   = "Universal Render Pipeline/Particles/Unlit";

    // Resources path to the pre-built material asset — always included in builds.
    private const string PARTICLE_MAT_PATH = "FX/ParticleUnlit";

    private const float  SMOKE_LIFETIME    = 1.6f;
    private const float  SMOKE_START_SIZE  = 0.25f;
    private const float  SMOKE_END_SIZE    = 1.4f;

    // ── Cached template ─────────────────────────────────────────────────────────

    private static Material _particleMatTemplate;

    /// <summary>
    /// Returns a new instance of the URP ParticleUnlit material.
    /// Loads the template from Resources on first call and caches it.
    /// Falls back to Shader.Find so Editor play-mode still works without a build.
    /// </summary>
    private static Material CreateParticleMaterial()
    {
        if (_particleMatTemplate == null)
            _particleMatTemplate = Resources.Load<Material>(PARTICLE_MAT_PATH);

        if (_particleMatTemplate != null)
            return new Material(_particleMatTemplate);

        // Editor-only fallback
        Shader shader = Shader.Find(PARTICLE_SHADER);
        if (shader == null)
        {
            Debug.LogError($"[TireSmokeController] Shader '{PARTICLE_SHADER}' not found.");
            shader = Shader.Find("Hidden/InternalErrorShader");
        }
        return new Material(shader);
    }

    // ── Private state ───────────────────────────────────────────────────────────

    private CarController  _car;
    private ParticleSystem _smokeLeft;
    private ParticleSystem _smokeRight;

    // ── Lifecycle ───────────────────────────────────────────────────────────────

    private void Awake()
    {
        _car = GetComponent<CarController>();
    }

    private void Start()
    {
        _smokeLeft  = BuildSmokeSystem(rearLeftWheel);
        _smokeRight = BuildSmokeSystem(rearRightWheel);
    }

    private void Update()
    {
        UpdateSmoke(_smokeLeft);
        UpdateSmoke(_smokeRight);
    }

    // ── Smoke logic ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Adjusts emission rate of a smoke system based on the car's current turn rate
    /// and forward speed.
    /// </summary>
    private void UpdateSmoke(ParticleSystem ps)
    {
        if (ps == null) return;

        float angularVel = Mathf.Abs(_car.AngularVelocity);
        float speed      = _car.CurrentSpeed;

        bool isTurningHard  = angularVel > smokeStartAngularVelocity;
        bool isMoving       = speed > minimumSpeedForSmoke;

        var emission = ps.emission;

        if (isTurningHard && isMoving)
        {
            float t           = Mathf.InverseLerp(smokeStartAngularVelocity, smokeMaxAngularVelocity, angularVel);
            float rate        = Mathf.Lerp(0f, maxEmissionRate, t);
            emission.enabled  = true;
            emission.rateOverTime = rate;
        }
        else
        {
            emission.enabled      = false;
        }
    }

    // ── Particle system builder ─────────────────────────────────────────────────

    /// <summary>
    /// Constructs a looping smoke particle system parented to the given wheel transform.
    /// </summary>
    private ParticleSystem BuildSmokeSystem(Transform anchor)
    {
        if (anchor == null)
        {
            Debug.LogWarning("[TireSmokeController] A wheel anchor is not assigned.", this);
            return null;
        }

        GameObject go = new("TireSmoke");
        go.transform.SetParent(anchor, false);
        go.transform.localPosition = Vector3.zero;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // Main module
        var main              = ps.main;
        main.loop             = true;
        main.duration         = 1f;
        main.startLifetime    = SMOKE_LIFETIME;
        main.startSpeed       = smokeDriftSpeed;
        main.startSize        = SMOKE_START_SIZE;
        main.startColor       = new ParticleSystem.MinMaxGradient(
            new Color(0.85f, 0.82f, 0.78f, 0.55f),
            new Color(0.55f, 0.55f, 0.55f, 0.35f));
        main.gravityModifier  = new ParticleSystem.MinMaxCurve(-0.06f);  // slight upward drift
        main.simulationSpace  = ParticleSystemSimulationSpace.World;
        main.maxParticles     = 200;

        // Emission — starts disabled; UpdateSmoke controls rate
        var em         = ps.emission;
        em.enabled     = false;
        em.rateOverTime = 0f;

        // Shape — thin disc at ground level
        var shape       = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius    = 0.18f;
        shape.rotation  = new Vector3(90f, 0f, 0f);

        // Size grows as the puff rises and spreads
        var sizeOL    = ps.sizeOverLifetime;
        sizeOL.enabled = true;
        AnimationCurve growCurve = new();
        growCurve.AddKey(0f, SMOKE_START_SIZE / SMOKE_END_SIZE);
        growCurve.AddKey(1f, 1f);
        sizeOL.size = new ParticleSystem.MinMaxCurve(SMOKE_END_SIZE, growCurve);

        // Fade out gently — appears thick at first then dissipates
        var colOL     = ps.colorOverLifetime;
        colOL.enabled = true;
        Gradient g    = new();
        g.SetKeys(
            new GradientColorKey[]
            {
                new(new Color(0.9f, 0.88f, 0.82f), 0f),
                new(new Color(0.65f, 0.65f, 0.65f), 0.4f),
                new(new Color(0.4f, 0.4f, 0.4f), 1f)
            },
            new GradientAlphaKey[]
            {
                new(0.0f, 0f),     // puff in quickly
                new(0.55f, 0.12f),
                new(0.4f, 0.5f),
                new(0f, 1f)        // fade out cleanly
            }
        );
        colOL.color = new ParticleSystem.MinMaxGradient(g);

        // Renderer
        var rend        = go.GetComponent<ParticleSystemRenderer>();
        rend.material   = CreateParticleMaterial();
        rend.renderMode = ParticleSystemRenderMode.Billboard;
        rend.sortingOrder = 1;

        ps.Play();
        return ps;
    }
}
