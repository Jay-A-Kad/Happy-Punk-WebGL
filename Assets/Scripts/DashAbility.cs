using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Grants the player a SHIFT dash that destroys Robos on contact.
///
/// Two activation modes — evaluated in priority order each time SHIFT is pressed:
///
///   1. KILL-METER MODE  — requires kill meter >= 25 % fill AND enough speed.
///      Free to use; does NOT consume the meter charge.
///
///   2. FUEL-CORE MODE   — fallback when the meter is below 25 %.
///      Requires >= <see cref="FUEL_CORE_COST"/> fuel cores AND enough speed.
///      Deducts <see cref="FUEL_CORE_COST"/> cores from the HUD on activation.
///
/// During the dash the car travels at <see cref="dashSpeed"/> along its current
/// heading for <see cref="dashDuration"/> seconds. Any active Robo that the car's
/// collider overlaps during the dash is immediately killed via <see cref="EnemyPool.Return"/>.
///
/// Attach to the MonsterTruck root alongside <see cref="CarController"/>.
/// </summary>
[RequireComponent(typeof(CarController))]
[RequireComponent(typeof(CarHealthSystem))]
public class DashAbility : MonoBehaviour
{
    // ── Tuning constants ─────────────────────────────────────────────────────────

    private const KeyCode   DASH_KEY              = KeyCode.LeftShift;

    /// <summary>Minimum forward speed (units/s) before the dash is allowed.</summary>
    private const float     MIN_DASH_SPEED        = 4f;

    /// <summary>Kill-meter fill fraction required for the free-mode dash (0.25 = 25 %).</summary>
    private const float     METER_THRESHOLD       = 0.25f;

    /// <summary>Fuel cores deducted when dashing in fuel-core mode.</summary>
    private const int       FUEL_CORE_COST        = 10;

    private const float     DASH_DURATION         = 0.35f;   // seconds
    private const float     DASH_SPEED            = 28f;     // units / s during the dash
    private const float     DASH_COOLDOWN         = 1.2f;    // seconds between dashes

    // ── Collision detection ──────────────────────────────────────────────────────

    private const float     OVERLAP_RADIUS        = 2.2f;    // sphere radius for enemy detection
    private const string    ENEMY_TAG             = "Enemy";

    // ── Trail FX ────────────────────────────────────────────────────────────────

    // URP-compatible shader — "Particles/Standard Unlit" is Built-in RP only and
    // will not be included in WebGL URP builds.
    private const string    PARTICLE_SHADER       = "Universal Render Pipeline/Particles/Unlit";

    // Resources path to pre-built material asset — always included in builds.
    private const string    PARTICLE_MAT_PATH     = "FX/ParticleUnlit";

    private static Material _particleMatTemplate;
    private const float     TRAIL_LIFETIME        = 0.28f;
    private const float     TRAIL_EMIT_DURATION   = 0.15f;   // only burst at dash start

    // ── Inspector ────────────────────────────────────────────────────────────────

    [Header("Dependencies")]
    public KillMeterController killMeter;
    public HUDManager          hudManager;
    public EnemyPool           enemyPool;

    [Header("Dash Settings")]
    [Tooltip("Speed in units/s applied during the dash override.")]
    public float dashSpeed    = DASH_SPEED;

    [Tooltip("How long the dash lasts in seconds.")]
    public float dashDuration = DASH_DURATION;

    [Tooltip("Cooldown in seconds before the dash can be used again.")]
    public float dashCooldown = DASH_COOLDOWN;

    [Tooltip("Minimum forward speed (units/s) needed to trigger the dash.")]
    public float minDashSpeed = MIN_DASH_SPEED;

    // ── Private state ────────────────────────────────────────────────────────────

    private CarController   _car;
    private CarHealthSystem _health;
    private FuelCoreCollector _fuelCoreCollector;

    private bool  _dashing;
    private float _cooldownRemaining;

    // Track enemies already killed this dash so overlapping across multiple frames
    // doesn't try to return the same enemy twice.
    private readonly HashSet<GameObject> _killedThisDash = new();

    // ── Lifecycle ────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _car                = GetComponent<CarController>();
        _health             = GetComponent<CarHealthSystem>();
        _fuelCoreCollector  = GetComponent<FuelCoreCollector>();
    }

    private void Update()
    {
        if (_cooldownRemaining > 0f)
            _cooldownRemaining -= Time.deltaTime;

        if (_dashing || _health.IsDead) return;

        if (Input.GetKeyDown(DASH_KEY))
            TryDash();
    }

    // ── Dash activation ──────────────────────────────────────────────────────────

    /// <summary>
    /// Determines the activation mode and starts the dash coroutine if all
    /// conditions are met; otherwise silently does nothing.
    /// </summary>
    private void TryDash()
    {
        if (_cooldownRemaining > 0f)  return;
        if (_car.CurrentSpeed < minDashSpeed) return;

        // Priority 1 — kill-meter mode (meter >= 25 %, costs 20 % fill).
        if (killMeter != null && killMeter.FillFraction >= METER_THRESHOLD)
        {
            killMeter.ReduceFill(0.20f);
            StartCoroutine(PerformDash(meterMode: true));
            return;
        }

        // Priority 2 — fuel-core mode (meter < 25 %, costs 10 cores).
        if (hudManager != null && hudManager.FuelCoreCount >= FUEL_CORE_COST)
        {
            hudManager.ConsumeFuelCores(FUEL_CORE_COST);
            StartCoroutine(PerformDash(meterMode: false));
            return;
        }

        // Neither condition met — do nothing (no feedback sound/FX per spec).
    }

    // ── Dash coroutine ────────────────────────────────────────────────────────────

    /// <summary>
    /// Overrides the car's position each frame for <see cref="dashDuration"/> seconds,
    /// detects and kills any Robos touched during the dash.
    /// </summary>
    private IEnumerator PerformDash(bool meterMode)
    {
        _dashing   = true;
        _cooldownRemaining = dashCooldown;
        _killedThisDash.Clear();

        // Capture the heading at dash start so the car travels in a straight line.
        Vector3 dashDir = _car.MovementForward;

        SpawnDashFX(meterMode);

        float elapsed = 0f;
        while (elapsed < dashDuration)
        {
            float dt = Time.deltaTime;

            // Move the car manually this frame, bypassing CarController's own movement.
            transform.position += dashDir * dashSpeed * dt;

            KillEnemiesInOverlapSphere();
            CollectFuelCoresInOverlapSphere();

            elapsed += dt;
            yield return null;
        }

        _dashing = false;
    }

    // ── Enemy kill detection ──────────────────────────────────────────────────────

    /// <summary>
    /// Checks a sphere around the car for active Robos and returns them to the pool.
    /// Uses <see cref="EnemyNavAgent"/> as the definitive Robo marker — avoids relying
    /// on tags or transform.root, both of which break when enemies are parented under
    /// the pool/wave manager hierarchy at runtime.
    /// </summary>
    private void KillEnemiesInOverlapSphere()
    {
        if (enemyPool == null) return;

        // Include trigger colliders so we catch Robos regardless of their collider type.
        Collider[] hits = Physics.OverlapSphere(
            transform.position, OVERLAP_RADIUS,
            Physics.AllLayers,
            QueryTriggerInteraction.Collide);

        foreach (Collider hit in hits)
        {
            // Walk up to find the EnemyNavAgent — works at any depth in the hierarchy.
            EnemyNavAgent agent = hit.GetComponentInParent<EnemyNavAgent>();
            if (agent == null) continue;

            GameObject enemyRoot = agent.gameObject;

            if (_killedThisDash.Contains(enemyRoot)) continue;
            if (!IsActiveEnemy(enemyRoot))           continue;

            _killedThisDash.Add(enemyRoot);
            enemyPool.Return(enemyRoot);
        }
    }

    /// <summary>
    /// Returns true if <paramref name="go"/> is currently tracked as an active enemy
    /// in the pool's ActiveEnemies list.
    /// </summary>
    private bool IsActiveEnemy(GameObject go)
    {
        IReadOnlyList<GameObject> active = enemyPool.ActiveEnemies;
        for (int i = 0; i < active.Count; i++)
        {
            if (active[i] == go) return true;
        }
        return false;
    }

    // ── Fuel core collection during dash ─────────────────────────────────────────

    /// <summary>
    /// Sweeps for <see cref="FuelCorePickup"/> components each frame of the dash.
    /// Because the car moves via <c>transform.position</c> during a dash the physics
    /// engine does not generate OnTriggerEnter events reliably, so we query manually.
    /// </summary>
    private void CollectFuelCoresInOverlapSphere()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, OVERLAP_RADIUS,
            Physics.AllLayers, QueryTriggerInteraction.Collide);

        foreach (Collider hit in hits)
        {
            FuelCorePickup pickup = hit.GetComponent<FuelCorePickup>();
            if (pickup == null)
                pickup = hit.GetComponentInParent<FuelCorePickup>();

            if (pickup == null) continue;

            // Let FuelCorePickup handle its own collection logic (guard flag + HUD update).
            // We fake an overlap by calling the car's own BoxCollider as the "other".
            // Instead, directly trigger the pickup via FuelCoreCollector on the car root.
            FuelCoreCollector collector = _fuelCoreCollector;
            pickup.ForceCollect(collector);
        }
    }

    // ── Visual FX ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Spawns a brief directional trail burst behind the car.
    /// Orange for meter-mode, cyan for fuel-core mode.
    /// </summary>
    private void SpawnDashFX(bool meterMode)
    {
        Color baseColor = meterMode
            ? new Color(1f, 0.5f, 0f, 0.9f)      // vivid orange — meter mode
            : new Color(0.3f, 0.9f, 1f, 0.9f);    // electric cyan — fuel-core mode

        Color dimColor = meterMode
            ? new Color(0.8f, 0.2f, 0f, 0.5f)
            : new Color(0.1f, 0.5f, 0.9f, 0.5f);

        GameObject root = new("DashFX");
        root.transform.position = transform.position;
        root.transform.rotation = transform.rotation;

        // Stretched streak particles that shoot behind the car.
        GameObject go = new("DashTrail");
        go.transform.SetParent(root.transform, false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main              = ps.main;
        main.loop             = false;
        main.duration         = TRAIL_EMIT_DURATION;
        main.startLifetime    = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
        main.startSpeed       = new ParticleSystem.MinMaxCurve(5f, 14f);
        main.startSize        = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
        main.startColor       = new ParticleSystem.MinMaxGradient(baseColor, dimColor);
        main.gravityModifier  = new ParticleSystem.MinMaxCurve(-0.05f);
        main.simulationSpace  = ParticleSystemSimulationSpace.World;
        main.maxParticles     = 80;
        // Particles shoot backward (opposite car forward).
        main.startRotation3D  = false;

        var em = ps.emission;
        em.enabled      = true;
        em.rateOverTime = 200f;

        // Cone pointing backward along local -Z.
        var shape       = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle     = 22f;
        shape.radius    = 0.6f;
        shape.rotation  = new Vector3(0f, 180f, 0f); // face backward

        var vel                = ps.velocityOverLifetime;
        vel.enabled            = true;
        vel.space              = ParticleSystemSimulationSpace.Local;
        vel.speedModifier      = new ParticleSystem.MinMaxCurve(
            1f,
            AnimationCurve.EaseInOut(0f, 1f, 1f, 0f)
        );

        var colOL     = ps.colorOverLifetime;
        colOL.enabled = true;
        Gradient g    = new();
        g.SetKeys(
            new GradientColorKey[]
            {
                new(baseColor, 0f),
                new(dimColor,  0.6f),
                new(Color.black, 1f)
            },
            new GradientAlphaKey[] { new(1f, 0f), new(0.7f, 0.4f), new(0f, 1f) }
        );
        colOL.color = new ParticleSystem.MinMaxGradient(g);

        var rend         = go.GetComponent<ParticleSystemRenderer>();
        if (_particleMatTemplate == null)
            _particleMatTemplate = Resources.Load<Material>(PARTICLE_MAT_PATH);
        Shader shader    = _particleMatTemplate != null
            ? _particleMatTemplate.shader
            : Shader.Find(PARTICLE_SHADER);
        if (shader == null)
        {
            Debug.LogError($"[DashAbility] Shader '{PARTICLE_SHADER}' not found.");
            shader = Shader.Find("Hidden/InternalErrorShader");
        }
        rend.material    = new Material(shader);
        rend.renderMode  = ParticleSystemRenderMode.Stretch;
        rend.lengthScale = 4f;
        rend.sortingOrder = 2;

        ps.Play();
        Object.Destroy(root, 1.5f);
    }
}
