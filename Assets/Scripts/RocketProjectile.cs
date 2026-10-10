using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Three-phase missile behaviour:
///   Ascending  — launches straight up until LAUNCH_HEIGHT above spawn.
///   Homing     — homes on the player with a constant turn rate.
///   Orbiting   — flight frozen; PlayerRocketInterceptor controls position.
///   Thrown     — released by the player, flies straight in a chosen direction.
///
/// Movement is driven by a _flightDirection Vector3 (the single source of truth).
/// Visual rotation = LookRotation(_flightDirection) * NOSE_CORRECTION * wobble.
/// Wobble is purely visual and never contaminates movement logic.
/// A ParticleSystem child emits a smoke/fire trail from the thruster nozzle.
/// A LineRenderer draws a fading glow trail during all active phases.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class RocketProjectile : MonoBehaviour
{
    // ── Flight tuning ───────────────────────────────────────────────────────────
    private const float LAUNCH_SPEED        = 10f;
    private const float CHASE_SPEED         = 14f;
    private const float THROWN_SPEED        = 20f;
    private const float TURN_SPEED_DEG      = 120f;  // degrees per second
    private const float LAUNCH_HEIGHT       = 6f;
    private const float EXPIRE_TIME         = 12f;
    private const float THROWN_EXPIRE_TIME  = 6f;
    private const float IMPACT_RADIUS       = 0.8f;
    private const float EXPLOSION_RADIUS    = 4f;

    // ── Trail tuning ────────────────────────────────────────────────────────────
    private const int   TRAIL_MAX_POINTS      = 60;
    private const float TRAIL_RECORD_INTERVAL = 0.04f;

    // ── Wobble tuning ───────────────────────────────────────────────────────────
    private const float WOBBLE_AMPLITUDE  = 6f;   // max sway in degrees
    private const float WOBBLE_FREQUENCY  = 1.2f; // cycles per second
    private const float WOBBLE_SEED_X     = 0f;
    private const float WOBBLE_SEED_Z     = 47.3f;

    // ── Model orientation ───────────────────────────────────────────────────────
    // The rocket mesh nose points along local +Y.
    // LookRotation aligns local +Z to the travel direction.
    // Rotating -90 on X maps local +Y to local +Z, fixing the nose.
    private static readonly Quaternion NOSE_CORRECTION = Quaternion.Euler(90f, 0f, 0f);

    // ── Tags ────────────────────────────────────────────────────────────────────
    private const string ENEMY_TAG = "Enemy";

    // ── Player knockback ─────────────────────────────────────────────────────────
    private const float PLAYER_KNOCKBACK_FORCE = 22f;

    // ── Smoke particle config ───────────────────────────────────────────────────
    private const int    SMOKE_RATE        = 30;
    private const float  SMOKE_LIFETIME    = 2.2f;
    private const float  SMOKE_START_SIZE  = 0.25f;
    private const float  SMOKE_END_SCALE   = 6f;
    private const float  SMOKE_SPEED       = 1.2f;
    private const float  SMOKE_CONE_ANGLE  = 8f;

    // Wispy secondary layer
    private const int    WISP_RATE         = 12;
    private const float  WISP_LIFETIME     = 3.0f;
    private const float  WISP_START_SIZE   = 0.15f;
    private const float  WISP_END_SCALE    = 9f;
    private const float  WISP_SPEED        = 0.4f;

    // URP-compatible particle shader — "Particles/Standard Unlit" is built-in only
    // and renders as hard-edged squares in URP. Use the URP unlit particle shader.
    private const string PARTICLE_SHADER   = "Universal Render Pipeline/Particles/Unlit";

    // Resources path to the pre-built material asset — always included in builds.
    private const string PARTICLE_MAT_PATH = "FX/ParticleUnlit";

    // ── Cached material template ────────────────────────────────────────────────
    private static Material _particleMatTemplate;

    // ── Flight phase ────────────────────────────────────────────────────────────
    private enum FlightPhase { Ascending, Homing, Orbiting, Thrown }
    private FlightPhase _phase;

    // ── References ─────────────────────────────────────────────────────────────
    private Transform      _playerTransform;
    private LineRenderer   _line;
    private ParticleSystem _smokeTrail;
    private ParticleSystem _wispTrail;

    // ── Flight state ────────────────────────────────────────────────────────────
    // _flightDirection is the ONLY source of truth for movement direction.
    // It is never read from transform.rotation, so wobble never contaminates movement.
    private Vector3 _flightDirection = Vector3.up;
    private Vector3 _spawnPoint;
    private Vector3 _throwDirection;

    // ── Wobble state ─────────────────────────────────────────────────────────────
    private float _wobbleTime;

    // ── Trail state ─────────────────────────────────────────────────────────────
    private readonly Queue<Vector3> _trailPoints = new();

    // ── Audio ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Played at the point of impact (enemy kill or player hit).
    /// Because the projectile is deactivated immediately after impact the sound is
    /// played on a detached temporary AudioSource so it is not cut short.
    /// Assign via the Inspector on the Rocket prefab.
    /// </summary>
    public AudioClip blastSound;

    /// <summary>
    /// Set by <see cref="RocketLauncher"/> immediately after instantiation.
    /// Used in <see cref="Deactivate"/> to notify the launcher directly without
    /// a scene lookup, which can fail on later waves when the scene is busier.
    /// </summary>
    [HideInInspector] public RocketLauncher launcher;

    /// <summary>
    /// Set by <see cref="RocketLauncher"/> after instantiation so <see cref="Explode"/>
    /// can return enemies to the pool without a per-impact scene lookup.
    /// </summary>
    [HideInInspector] public EnemyPool enemyPool;

    /// <summary>
    /// Cached player car reference set by <see cref="RocketLauncher"/> after instantiation.
    /// Avoids per-impact FindFirstObjectByType lookups in HitPlayer.
    /// </summary>
    [HideInInspector] public CarController playerCar;

    // ── Material helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a new instance of the URP ParticleUnlit material.
    /// Loads the pre-built asset from Resources on first call — this guarantees the
    /// shader is included in the WebGL build regardless of shader stripping settings.
    /// </summary>
    private static Material LoadParticleMaterial()
    {
        if (_particleMatTemplate == null)
            _particleMatTemplate = Resources.Load<Material>(PARTICLE_MAT_PATH);

        if (_particleMatTemplate != null)
            return new Material(_particleMatTemplate);

        // Editor-only path — Resources folder not yet built
        Shader sh = Shader.Find(PARTICLE_SHADER);
        return sh != null ? new Material(sh) : null;
    }

    private static Shader LoadParticleShader()
    {
        Material mat = LoadParticleMaterial();
        return mat != null ? mat.shader : null;
    }

    // ── Public properties ───────────────────────────────────────────────────────

    /// <summary>True while the rocket can be captured by the intercept system.</summary>
    public bool IsInterceptable => _phase == FlightPhase.Ascending || _phase == FlightPhase.Homing;

    // ── Unity lifecycle ─────────────────────────────────────────────────────────

    private void Awake()
    {
        _line = GetComponent<LineRenderer>();
        EnsureSmokeTrail();
    }

    private void Update()
    {
        switch (_phase)
        {
            case FlightPhase.Ascending: HandleAscending(); break;
            case FlightPhase.Homing:    HandleHoming();    break;
            case FlightPhase.Orbiting:  /* driven externally */ break;
            case FlightPhase.Thrown:    HandleThrown();    break;
        }

        UpdateTrailRenderer();
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Called by RocketLauncher to start normal enemy-fire behaviour.</summary>
    public void Launch(Transform playerTransform)
    {
        _playerTransform = playerTransform;
        _spawnPoint      = transform.position;
        _flightDirection = Vector3.up;
        _phase           = FlightPhase.Ascending;
        _throwDirection  = Vector3.zero;
        _wobbleTime      = Random.Range(0f, 100f);

        ResetTrail();
        SetSmokeActive(true);

        StopAllCoroutines();
        StartCoroutine(ExpireAfterDelay(EXPIRE_TIME));
        StartCoroutine(RecordTrail());
    }

    /// <summary>Freezes flight so the intercept system can orbit it.</summary>
    public void EnterOrbit()
    {
        StopAllCoroutines();
        _phase = FlightPhase.Orbiting;

        // Keep smoke and trail active — the missile is still burning while it circles.
        SetSmokeActive(true);
        ResetTrail();
        StartCoroutine(RecordTrail());
    }

    /// <summary>
    /// Called every frame by PlayerRocketInterceptor while the missile is orbiting.
    /// Keeps the nose pointing in the orbit travel direction (tangent) with wobble,
    /// so the interceptor never needs to touch transform.rotation directly.
    /// </summary>
    public void OrbitTick(Vector3 orbitTravelDirection)
    {
        if (orbitTravelDirection.sqrMagnitude < 0.001f) return;
        _flightDirection = orbitTravelDirection;
        ApplyVisualRotation(_flightDirection);
    }

    /// <summary>Releases the rocket to fly in the given direction.</summary>
    public void ThrowInDirection(Vector3 direction)
    {
        StopAllCoroutines();
        _throwDirection  = direction.normalized;
        _flightDirection = _throwDirection;
        _phase           = FlightPhase.Thrown;
        _wobbleTime      = Random.Range(0f, 100f);

        ResetTrail();
        SetSmokeActive(true);

        StartCoroutine(ExpireAfterDelay(THROWN_EXPIRE_TIME));
        StartCoroutine(RecordTrail());
    }

    // ── Phase handlers ──────────────────────────────────────────────────────────

    private void HandleAscending()
    {
        _flightDirection = Vector3.up;
        transform.position += Vector3.up * LAUNCH_SPEED * Time.deltaTime;
        ApplyVisualRotation(_flightDirection);

        if (transform.position.y >= _spawnPoint.y + LAUNCH_HEIGHT)
        {
            _phase = FlightPhase.Homing;
            if (_playerTransform != null)
            {
                _flightDirection = (_playerTransform.position - transform.position).normalized;
            }
        }
    }

    private void HandleHoming()
    {
        if (_playerTransform == null) return;

        Vector3 desired = (_playerTransform.position - transform.position).normalized;

        // Rotate the direction vector toward the target.
        // Using Vector3.RotateTowards keeps wobble completely out of the movement math
        // because movement direction comes from _flightDirection, not transform.rotation.
        _flightDirection = Vector3.RotateTowards(
            _flightDirection,
            desired,
            TURN_SPEED_DEG * Mathf.Deg2Rad * Time.deltaTime,
            0f
        );

        transform.position += _flightDirection * CHASE_SPEED * Time.deltaTime;
        ApplyVisualRotation(_flightDirection);

        if (Vector3.Distance(transform.position, _playerTransform.position) <= IMPACT_RADIUS)
        {
            HitPlayer();
        }
    }

    private void HandleThrown()
    {
        transform.position += _throwDirection * THROWN_SPEED * Time.deltaTime;
        ApplyVisualRotation(_throwDirection);
    }

    // ── Rotation helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Sets transform.rotation so the model nose (local +Y) faces <paramref name="direction"/>,
    /// then layers a Perlin-noise wobble on top. Uses a safe up-hint when direction is
    /// nearly vertical to prevent LookRotation gimbal lock.
    /// </summary>
    private void ApplyVisualRotation(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.001f) return;

        Vector3 upHint = Mathf.Abs(direction.y) > 0.95f ? Vector3.forward : Vector3.up;

        Quaternion lookRot = Quaternion.LookRotation(direction, upHint);
        transform.rotation = lookRot * NOSE_CORRECTION * ComputeWobble();
    }

    /// <summary>
    /// Returns a small Perlin-noise Quaternion to simulate in-flight instability.
    /// Advances the wobble timer — call exactly once per Update tick.
    /// </summary>
    private Quaternion ComputeWobble()
    {
        _wobbleTime += Time.deltaTime;

        float wx = (Mathf.PerlinNoise(_wobbleTime * WOBBLE_FREQUENCY, WOBBLE_SEED_X) * 2f - 1f) * WOBBLE_AMPLITUDE;
        float wz = (Mathf.PerlinNoise(_wobbleTime * WOBBLE_FREQUENCY, WOBBLE_SEED_Z) * 2f - 1f) * WOBBLE_AMPLITUDE;

        return Quaternion.Euler(wx, 0f, wz);
    }

    // ── Collision ──────────────────────────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        // Enemy kill — only when the player has thrown the rocket back.
        if (_phase == FlightPhase.Thrown && other.CompareTag(ENEMY_TAG))
        {
            Explode();
            return;
        }

        // Player hit — missile strikes the player during homing or ascent.
        if (_phase == FlightPhase.Homing || _phase == FlightPhase.Ascending)
        {
            CarController car = other.GetComponent<CarController>()
                             ?? other.GetComponentInParent<CarController>();
            if (car != null)
            {
                HitPlayer(car);
            }
        }
    }

    private void Explode()
    {
        PlayBlastSound();
        ExplosionEffect.Spawn(transform.position);

        Collider[] hits = Physics.OverlapSphere(transform.position, EXPLOSION_RADIUS);
        HashSet<GameObject> killed = new();
        foreach (Collider hit in hits)
        {
            EnemyNavAgent agent = hit.GetComponentInParent<EnemyNavAgent>();
            if (agent == null) continue;

            GameObject root = agent.gameObject;
            if (!killed.Add(root)) continue; // skip duplicates from multi-collider Robos

            if (enemyPool != null)
                enemyPool.Return(root);
            else
                Destroy(root);
        }

        Deactivate();
    }

    /// <summary>
    /// Detonates the missile on the player: spawns the explosion, then shoves
    /// the car away from the blast point and applies health damage.
    /// Falls back to the distance-check cached _playerTransform if no collider is provided.
    /// </summary>
    private void HitPlayer(CarController car = null)
    {
        PlayBlastSound();
        ExplosionEffect.Spawn(transform.position);

        if (car == null)
            car = playerCar;

        if (car != null)
        {
            Vector3 blastDir = (car.transform.position - transform.position).normalized;
            car.ApplyKnockback(blastDir, PLAYER_KNOCKBACK_FORCE);

            CarHealthSystem healthSystem = car.GetComponent<CarHealthSystem>();
            healthSystem?.ApplyRocketDamage();
        }

        Deactivate();
    }

    // ── Audio ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Spawns a temporary detached AudioSource at the impact point and plays the
    /// blast clip on it, then auto-destroys it when the clip ends.
    /// Playing on a detached host is necessary because <see cref="Deactivate"/>
    /// calls SetActive(false) immediately after impact, which would silence any
    /// AudioSource living on this GameObject.
    /// </summary>
    private void PlayBlastSound()
    {
        if (blastSound == null) return;

        GameObject host    = new("RocketBlastAudio");
        host.transform.position = transform.position;

        AudioSource src  = host.AddComponent<AudioSource>();
        src.clip         = blastSound;
        src.spatialBlend = 0f;
        src.playOnAwake  = false;
        src.loop         = false;
        src.Play();

        Destroy(host, blastSound.length + 0.1f);
    }

    // ── Smoke trail ─────────────────────────────────────────────────────────────

    private void EnsureSmokeTrail()
    {
        _smokeTrail = GetOrCreateSmokeLayer("SmokeTrail",  isCoreLayer: true);
        _wispTrail  = GetOrCreateSmokeLayer("SmokeWisps",  isCoreLayer: false);
    }

    /// <summary>
    /// Builds one smoke particle layer. <paramref name="isCoreLayer"/> true = dense
    /// hot core, false = large wispy outer layer.
    /// </summary>
    private ParticleSystem GetOrCreateSmokeLayer(string childName, bool isCoreLayer)
    {
        Transform existing = transform.Find(childName);
        if (existing != null)
        {
            ParticleSystem ps = existing.GetComponent<ParticleSystem>();
            if (ps != null) return ps;
        }

        // ── GameObject ─────────────────────────────────────────────────────────
        GameObject go = new(childName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, -0.6f, 0f);
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        ParticleSystem sys = go.AddComponent<ParticleSystem>();
        sys.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        Shader shader = LoadParticleShader();

        // ── Main ────────────────────────────────────────────────────────────────
        ParticleSystem.MainModule main = sys.main;
        main.loop            = true;
        main.playOnAwake     = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles    = isCoreLayer ? 350 : 150;
        main.duration        = 1f;

        if (isCoreLayer)
        {
            main.startLifetime  = new ParticleSystem.MinMaxCurve(SMOKE_LIFETIME * 0.7f, SMOKE_LIFETIME);
            main.startSpeed     = new ParticleSystem.MinMaxCurve(SMOKE_SPEED * 0.6f, SMOKE_SPEED);
            main.startSize      = new ParticleSystem.MinMaxCurve(SMOKE_START_SIZE * 0.5f, SMOKE_START_SIZE);
            main.startColor     = new ParticleSystem.MinMaxGradient(
                new Color(0.85f, 0.85f, 0.85f, 0.7f),  // light grey
                new Color(0.65f, 0.65f, 0.65f, 0.5f)   // mid grey
            );
            main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.06f, -0.02f);
        }
        else
        {
            main.startLifetime  = new ParticleSystem.MinMaxCurve(WISP_LIFETIME * 0.8f, WISP_LIFETIME);
            main.startSpeed     = new ParticleSystem.MinMaxCurve(0.1f, WISP_SPEED);
            main.startSize      = new ParticleSystem.MinMaxCurve(WISP_START_SIZE * 0.4f, WISP_START_SIZE);
            main.startColor     = new ParticleSystem.MinMaxGradient(
                new Color(0.80f, 0.80f, 0.80f, 0.30f),  // soft light grey
                new Color(0.70f, 0.70f, 0.70f, 0.15f)   // faint grey wisp
            );
            main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.12f, -0.04f);
        }

        // ── Emission ────────────────────────────────────────────────────────────
        ParticleSystem.EmissionModule emission = sys.emission;
        emission.enabled      = true;
        emission.rateOverTime = isCoreLayer ? SMOKE_RATE : WISP_RATE;

        // ── Shape — tight cone so smoke trails behind the nozzle ───────────────
        ParticleSystem.ShapeModule shape = sys.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle     = isCoreLayer ? SMOKE_CONE_ANGLE : 18f;
        shape.radius    = isCoreLayer ? 0.05f : 0.12f;

        // ── Size over lifetime — particles grow as they expand ─────────────────
        ParticleSystem.SizeOverLifetimeModule sizeOL = sys.sizeOverLifetime;
        sizeOL.enabled = true;
        float endScale = isCoreLayer ? SMOKE_END_SCALE : WISP_END_SCALE;
        AnimationCurve growCurve = AnimationCurve.EaseInOut(0f, 0.15f, 1f, endScale);
        sizeOL.size = new ParticleSystem.MinMaxCurve(1f, growCurve);

        // ── Colour over lifetime — fire → grey → transparent ───────────────────
        ParticleSystem.ColorOverLifetimeModule colorOL = sys.colorOverLifetime;
        colorOL.enabled = true;
        Gradient grad = new();

        if (isCoreLayer)
        {
            grad.SetKeys(
                new GradientColorKey[]
                {
                    new(new Color(0.95f, 0.95f, 0.95f), 0.00f),  // bright white puff
                    new(new Color(0.80f, 0.80f, 0.80f), 0.30f),  // light grey
                    new(new Color(0.60f, 0.60f, 0.60f), 0.65f),  // mid grey
                    new(new Color(0.45f, 0.45f, 0.45f), 1.00f),  // fading grey
                },
                new GradientAlphaKey[]
                {
                    new(0.00f, 0.00f),  // start transparent (avoids hard pop-in)
                    new(0.65f, 0.10f),  // quickly opaque
                    new(0.40f, 0.55f),
                    new(0.00f, 1.00f),
                }
            );
        }
        else
        {
            grad.SetKeys(
                new GradientColorKey[]
                {
                    new(new Color(0.90f, 0.90f, 0.90f), 0.0f),  // light wisp
                    new(new Color(0.65f, 0.65f, 0.65f), 1.0f),  // grey fade
                },
                new GradientAlphaKey[]
                {
                    new(0.00f, 0.00f),
                    new(0.25f, 0.15f),
                    new(0.10f, 0.60f),
                    new(0.00f, 1.00f),
                }
            );
        }
        colorOL.color = new ParticleSystem.MinMaxGradient(grad);

        // ── Noise — breaks up billboard edges so nothing looks square ──────────
        ParticleSystem.NoiseModule noise = sys.noise;
        noise.enabled        = true;
        noise.strength       = new ParticleSystem.MinMaxCurve(isCoreLayer ? 0.25f : 0.55f);
        noise.frequency      = isCoreLayer ? 0.6f : 0.35f;
        noise.scrollSpeed    = new ParticleSystem.MinMaxCurve(0.25f);
        noise.damping        = true;
        noise.octaveCount    = 2;
        noise.octaveMultiplier = 0.5f;
        noise.octaveScale    = 2f;

        // ── Rotation over lifetime — slow spin hides billboard seams ───────────
        ParticleSystem.RotationOverLifetimeModule rotOL = sys.rotationOverLifetime;
        rotOL.enabled = true;
        rotOL.z = new ParticleSystem.MinMaxCurve(
            -45f * Mathf.Deg2Rad,
             45f * Mathf.Deg2Rad
        );

        // ── Start rotation — random initial angle per particle ─────────────────
        ParticleSystem.MainModule mainRef = sys.main; // re-get (struct)
        mainRef.startRotation = new ParticleSystem.MinMaxCurve(
            0f,
            360f * Mathf.Deg2Rad
        );

        // ── Renderer — URP Particles/Unlit, normal alpha blending ─────────────
        ParticleSystemRenderer rend = go.GetComponent<ParticleSystemRenderer>();

        Material mat = LoadParticleMaterial();
        if (mat != null)
            rend.material = mat;

        rend.renderMode      = ParticleSystemRenderMode.Billboard;
        rend.sortingOrder    = isCoreLayer ? 2 : 1;
        rend.minParticleSize = 0f;

        return sys;
    }

    private void SetSmokeActive(bool active)
    {
        SetLayer(ref _smokeTrail, active);
        SetLayer(ref _wispTrail,  active);
    }

    private static void SetLayer(ref ParticleSystem ps, bool active)
    {
        if (ps == null) return;
        if (active)
        {
            if (!ps.isPlaying) ps.Play();
        }
        else
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    // ── LineRenderer trail ──────────────────────────────────────────────────────

    private IEnumerator RecordTrail()
    {
        WaitForSeconds wait = new(TRAIL_RECORD_INTERVAL);
        while (true)
        {
            _trailPoints.Enqueue(transform.position);
            if (_trailPoints.Count > TRAIL_MAX_POINTS) _trailPoints.Dequeue();
            yield return wait;
        }
    }

    private void UpdateTrailRenderer()
    {
        if (_line == null) return;
        Vector3[] points = _trailPoints.ToArray();
        _line.positionCount = points.Length;
        if (points.Length > 0) _line.SetPositions(points);
    }

    private void ResetTrail()
    {
        _trailPoints.Clear();
        if (_line != null) _line.positionCount = 0;
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    private IEnumerator ExpireAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        Deactivate();
    }

    private void Deactivate()
    {
        StopAllCoroutines();
        ResetTrail();
        SetSmokeActive(false);
        _phase           = FlightPhase.Ascending;
        _playerTransform = null;
        _throwDirection  = Vector3.zero;
        _flightDirection = Vector3.up;

        launcher?.OnRocketReturned(gameObject);

        gameObject.SetActive(false);
    }
}
