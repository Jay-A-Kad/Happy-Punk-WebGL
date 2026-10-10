using System;
using System.Collections;
using UnityEngine;
using URandom = UnityEngine.Random;

/// <summary>
/// Runs the truck-destruction sequence when the player dies.
///
/// Frame 0 (simultaneous):
///   - Big fireball + smoke + sparks + shockwave ring burst at the truck's position
///   - 22 car-piece debris chunks fly outward with physics
///   - MonsterTruck GameObject is destroyed immediately
///
/// After a short settle delay:
///   - <see cref="OnSequenceComplete"/> is fired on a persistent relay GO so
///     <see cref="GameOverManager"/> can freeze time and show the panel.
///
/// The coroutine is migrated to a separate scene-level relay GameObject
/// before <see cref="Destroy"/> is called so it is never interrupted.
/// </summary>
[RequireComponent(typeof(CarHealthSystem))]
public class CarDeathSequencer : MonoBehaviour
{
    // ── Timing ───────────────────────────────────────────────────────────────────
    /// <summary>Seconds after destruction before the game-over panel appears.</summary>
    private const float PANEL_DELAY = 2.2f;

    // ── Big explosion parameters ─────────────────────────────────────────────────
    private const int   FIRE_COUNT      = 120;
    private const float FIRE_LIFE_MIN   = 0.7f;
    private const float FIRE_LIFE_MAX   = 1.4f;
    private const float FIRE_SPEED_MIN  = 5f;
    private const float FIRE_SPEED_MAX  = 16f;
    private const float FIRE_SIZE_MIN   = 0.8f;
    private const float FIRE_SIZE_MAX   = 3.0f;

    private const int   SMOKE_COUNT     = 60;
    private const float SMOKE_LIFE_MIN  = 2.5f;
    private const float SMOKE_LIFE_MAX  = 5f;
    private const float SMOKE_SPEED_MIN = 1.5f;
    private const float SMOKE_SPEED_MAX = 5f;
    private const float SMOKE_SIZE_MIN  = 1.2f;
    private const float SMOKE_SIZE_MAX  = 4f;

    private const int   SPARK_COUNT     = 150;

    private const float SHOCKWAVE_MAX   = 14f;
    private const float SHOCKWAVE_DUR   = 0.45f;

    // ── Car-piece debris ─────────────────────────────────────────────────────────
    private const int   PIECE_COUNT     = 22;
    private const float PIECE_SIZE_MIN  = 0.18f;
    private const float PIECE_SIZE_MAX  = 0.65f;
    private const float PIECE_FORCE_MIN = 7f;
    private const float PIECE_FORCE_MAX = 22f;
    private const float PIECE_TORQUE    = 18f;
    private const float PIECE_LIFETIME  = 6f;

    // URP-compatible shaders — Built-in RP shader names are not included in WebGL URP builds.
    private const string PARTICLE_SHADER = "Universal Render Pipeline/Particles/Unlit";
    private const string LIT_SHADER      = "Universal Render Pipeline/Lit";

    // Resources paths to pre-built material assets — always included in builds.
    private const string PARTICLE_MAT_PATH = "FX/ParticleUnlit";
    private const string LIT_MAT_PATH      = "FX/Lit";

    private static Material _particleMatTemplate;
    private static Material _litMatTemplate;

    // ── Events ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired from a scene-level relay after all FX have had time to play.
    /// <see cref="GameOverManager"/> listens here to freeze time and show the panel.
    /// </summary>
    public event Action OnSequenceComplete;

    // ── Shader helper ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a new Material using the given shader name.
    /// Falls back to InternalErrorShader if the shader was stripped from the WebGL build,
    /// preventing an ArgumentNullException while logging an actionable message.
    /// </summary>
    private static Material CreateMaterial(string shaderName)
    {
        Shader shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogError($"[CarDeathSequencer] Shader '{shaderName}' not found. " +
                           "Add it to Project Settings > Graphics > Always Included Shaders.");
            shader = Shader.Find("Hidden/InternalErrorShader");
        }
        return new Material(shader);
    }

    // ── Private ──────────────────────────────────────────────────────────────────

    private CarHealthSystem _health;
    private CarController   _controller;
    private bool            _triggered;

    private void Awake()
    {
        _health     = GetComponent<CarHealthSystem>();
        _controller = GetComponent<CarController>();
    }

    private void OnEnable()
    {
        if (_health != null) _health.OnDied += HandleDied;
    }

    private void OnDisable()
    {
        if (_health != null) _health.OnDied -= HandleDied;
    }

    // ── Death entry ──────────────────────────────────────────────────────────────

    private void HandleDied()
    {
        if (_triggered) return;
        _triggered = true;

        if (_controller != null)
            _controller.enabled = false;

        Vector3 origin = transform.position + Vector3.up * 0.6f;

        // 1. Spawn all FX simultaneously — they are parented to a standalone GO
        //    that is NOT the truck, so they survive the truck being destroyed.
        SpawnExplosionBig(origin);
        SpawnCarPieceDebris(origin);

        // 2. Notify state manager so enemies stop chasing and wave spawning stops.
        GameStateManager.Instance?.NotifyPlayerDied();

        // 3. Migrate the completion-wait coroutine to a relay GO before destroying
        //    this GameObject. If we started the coroutine here and then called
        //    Destroy(gameObject), Unity would kill the coroutine immediately.
        DeathRelay relay = CreateRelay();
        relay.Run(PANEL_DELAY, OnSequenceComplete);

        // 4. Destroy the truck root immediately — all FX are already in the scene.
        Destroy(gameObject);
    }

    // ── Relay helper ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a lightweight persistent MonoBehaviour in the scene that owns the
    /// coroutine which fires <see cref="OnSequenceComplete"/> after the delay.
    /// </summary>
    private DeathRelay CreateRelay()
    {
        GameObject relayGO = new("_DeathRelay");
        return relayGO.AddComponent<DeathRelay>();
    }

    // ── Big explosion ────────────────────────────────────────────────────────────

    private void SpawnExplosionBig(Vector3 pos)
    {
        GameObject root = new("TruckExplosion");
        root.transform.position = pos;

        SpawnBigFire(root.transform);
        SpawnBigSmoke(root.transform);
        SpawnBigSparks(root.transform);
        SpawnBigShockwave(root.transform);

        Destroy(root, 8f);
    }

    private void SpawnBigFire(Transform parent)
    {
        GameObject go = new("BigFire");
        go.transform.SetParent(parent, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main            = ps.main;
        main.loop           = false;
        main.duration       = 0.3f;
        main.startLifetime  = new ParticleSystem.MinMaxCurve(FIRE_LIFE_MIN, FIRE_LIFE_MAX);
        main.startSpeed     = new ParticleSystem.MinMaxCurve(FIRE_SPEED_MIN, FIRE_SPEED_MAX);
        main.startSize      = new ParticleSystem.MinMaxCurve(FIRE_SIZE_MIN, FIRE_SIZE_MAX);
        main.startColor     = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.9f, 0.2f), new Color(1f, 0.25f, 0f));
        main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.3f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles   = FIRE_COUNT;

        var em = ps.emission;
        em.enabled      = true;
        em.rateOverTime = 0f;
        em.SetBurst(0, new ParticleSystem.Burst(0f, FIRE_COUNT));

        var shape       = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius    = 0.8f;

        var sizeOL   = ps.sizeOverLifetime;
        sizeOL.enabled = true;
        sizeOL.size   = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

        var colOL   = ps.colorOverLifetime;
        colOL.enabled = true;
        Gradient g  = new();
        g.SetKeys(
            new GradientColorKey[] {
                new(new Color(1f, 1f, 0.4f), 0f),
                new(new Color(1f, 0.35f, 0f), 0.4f),
                new(new Color(0.2f, 0.08f, 0f), 1f)
            },
            new GradientAlphaKey[] { new(1f, 0f), new(0.85f, 0.35f), new(0f, 1f) }
        );
        colOL.color = new ParticleSystem.MinMaxGradient(g);

        var rend      = go.GetComponent<ParticleSystemRenderer>();
        rend.material = CreateMaterial(PARTICLE_SHADER);
        rend.renderMode = ParticleSystemRenderMode.Billboard;

        ps.Play();
    }

    private void SpawnBigSmoke(Transform parent)
    {
        GameObject go = new("BigSmoke");
        go.transform.SetParent(parent, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main            = ps.main;
        main.loop           = false;
        main.duration       = 0.5f;
        main.startLifetime  = new ParticleSystem.MinMaxCurve(SMOKE_LIFE_MIN, SMOKE_LIFE_MAX);
        main.startSpeed     = new ParticleSystem.MinMaxCurve(SMOKE_SPEED_MIN, SMOKE_SPEED_MAX);
        main.startSize      = new ParticleSystem.MinMaxCurve(SMOKE_SIZE_MIN, SMOKE_SIZE_MAX);
        main.startColor     = new ParticleSystem.MinMaxGradient(
            new Color(0.45f, 0.4f, 0.35f, 0.95f), new Color(0.1f, 0.1f, 0.1f, 0.7f));
        main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.18f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles   = SMOKE_COUNT;

        var em = ps.emission;
        em.enabled      = true;
        em.rateOverTime = 0f;
        em.SetBurst(0, new ParticleSystem.Burst(0f, SMOKE_COUNT));

        var shape       = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius    = 1.1f;

        var sizeOL    = ps.sizeOverLifetime;
        sizeOL.enabled = true;
        sizeOL.size   = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.2f, 1f, 1f));

        var colOL   = ps.colorOverLifetime;
        colOL.enabled = true;
        Gradient g  = new();
        g.SetKeys(
            new GradientColorKey[] {
                new(new Color(0.5f, 0.4f, 0.25f), 0f),
                new(new Color(0.2f, 0.2f, 0.2f), 0.4f),
                new(new Color(0.08f, 0.08f, 0.08f), 1f)
            },
            new GradientAlphaKey[] { new(0.95f, 0f), new(0.55f, 0.5f), new(0f, 1f) }
        );
        colOL.color = new ParticleSystem.MinMaxGradient(g);

        var rend      = go.GetComponent<ParticleSystemRenderer>();
        rend.material = CreateMaterial(PARTICLE_SHADER);
        rend.renderMode = ParticleSystemRenderMode.Billboard;
        rend.sortingOrder = -1;

        ps.Play();
    }

    private void SpawnBigSparks(Transform parent)
    {
        GameObject go = new("BigSparks");
        go.transform.SetParent(parent, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main            = ps.main;
        main.loop           = false;
        main.duration       = 0.2f;
        main.startLifetime  = new ParticleSystem.MinMaxCurve(0.6f, 2f);
        main.startSpeed     = new ParticleSystem.MinMaxCurve(10f, 28f);
        main.startSize      = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
        main.startColor     = new ParticleSystem.MinMaxGradient(
            new Color(1f, 1f, 0.9f), new Color(1f, 0.6f, 0.1f));
        main.gravityModifier = new ParticleSystem.MinMaxCurve(0.8f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles   = SPARK_COUNT;

        var em = ps.emission;
        em.enabled      = true;
        em.rateOverTime = 0f;
        em.SetBurst(0, new ParticleSystem.Burst(0f, SPARK_COUNT));

        var shape       = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius    = 0.4f;

        var colOL   = ps.colorOverLifetime;
        colOL.enabled = true;
        Gradient g  = new();
        g.SetKeys(
            new GradientColorKey[] {
                new(new Color(1f, 1f, 0.9f), 0f),
                new(new Color(1f, 0.4f, 0f), 0.5f),
                new(new Color(0.2f, 0.1f, 0f), 1f)
            },
            new GradientAlphaKey[] { new(1f, 0f), new(0.7f, 0.5f), new(0f, 1f) }
        );
        colOL.color = new ParticleSystem.MinMaxGradient(g);

        var rend         = go.GetComponent<ParticleSystemRenderer>();
        Material sparMat = CreateMaterial(PARTICLE_SHADER);
        rend.material       = sparMat;
        rend.trailMaterial  = sparMat;
        rend.renderMode     = ParticleSystemRenderMode.Stretch;
        rend.lengthScale    = 4f;
        rend.sortingOrder   = 2;

        ps.Play();
    }

    private void SpawnBigShockwave(Transform parent)
    {
        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.transform.SetParent(parent, false);
        ring.transform.localPosition = Vector3.zero;
        ring.transform.localScale    = new Vector3(0.1f, 0.02f, 0.1f);
        Destroy(ring.GetComponent<Collider>());

        Renderer rend = ring.GetComponent<Renderer>();
        Material mat  = CreateMaterial(PARTICLE_SHADER);
        mat.color     = new Color(1f, 0.65f, 0.15f, 0.8f);
        rend.material = mat;

        // Run the shockwave animation on a relay so the coroutine outlives this GO.
        DeathRelay sw = CreateRelay();
        sw.RunShockwave(ring, mat, SHOCKWAVE_DUR, SHOCKWAVE_MAX);
    }

    // ── Car-piece debris ─────────────────────────────────────────────────────────

    private void SpawnCarPieceDebris(Vector3 origin)
    {
        Color[] palette =
        {
            new(0.08f, 0.06f, 0.04f),   // charred black
            new(0.90f, 0.80f, 0.00f),   // punk yellow
            new(1.00f, 0.00f, 0.43f),   // magenta accent
            new(0.30f, 0.25f, 0.15f),   // scorched brown
            new(0.15f, 0.15f, 0.15f),   // dark grey
        };

        for (int i = 0; i < PIECE_COUNT; i++)
        {
            PrimitiveType ptype = (i % 4) switch
            {
                0 => PrimitiveType.Cube,
                1 => PrimitiveType.Cylinder,
                2 => PrimitiveType.Sphere,
                _ => PrimitiveType.Cube
            };

            GameObject piece = GameObject.CreatePrimitive(ptype);

            float baseSize = URandom.Range(PIECE_SIZE_MIN, PIECE_SIZE_MAX);
            piece.transform.localScale = new Vector3(
                baseSize * URandom.Range(0.5f, 2.5f),
                baseSize * URandom.Range(0.3f, 1.2f),
                baseSize * URandom.Range(0.5f, 2.0f)
            );
            piece.transform.position = origin + URandom.insideUnitSphere * 0.5f;

            Renderer rend   = piece.GetComponent<Renderer>();
            rend.material   = CreateMaterial(LIT_SHADER);
            rend.material.color = palette[URandom.Range(0, palette.Length)];

            Rigidbody rb = piece.AddComponent<Rigidbody>();
            Vector3 dir  = (URandom.onUnitSphere + Vector3.up * 1.2f).normalized;
            float force  = URandom.Range(PIECE_FORCE_MIN, PIECE_FORCE_MAX);
            rb.AddForce(dir * force, ForceMode.Impulse);
            rb.AddTorque(URandom.onUnitSphere * PIECE_TORQUE, ForceMode.Impulse);

            Destroy(piece, PIECE_LIFETIME);
        }
    }
}

// ── Relay MonoBehaviour ───────────────────────────────────────────────────────

/// <summary>
/// A tiny self-destructing MonoBehaviour that lives at scene root and owns
/// coroutines that must outlive the MonsterTruck GameObject.
/// </summary>
internal sealed class DeathRelay : MonoBehaviour
{
    /// <summary>
    /// Waits <paramref name="delay"/> real seconds then invokes <paramref name="callback"/>,
    /// then destroys itself.
    /// </summary>
    public void Run(float delay, Action callback)
    {
        StartCoroutine(Wait(delay, callback));
    }

    /// <summary>
    /// Animates the shockwave ring expanding from 0 to <paramref name="maxScale"/>
    /// over <paramref name="duration"/> seconds.
    /// </summary>
    public void RunShockwave(GameObject ring, Material mat, float duration, float maxScale)
    {
        StartCoroutine(AnimateShockwave(ring, mat, duration, maxScale));
    }

    private IEnumerator Wait(float delay, Action callback)
    {
        yield return new WaitForSeconds(delay);
        callback?.Invoke();
        Destroy(gameObject);
    }

    private IEnumerator AnimateShockwave(GameObject ring, Material mat, float duration, float maxScale)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            if (ring != null)
                ring.transform.localScale = new Vector3(
                    Mathf.Lerp(0.1f, maxScale, t), 0.02f,
                    Mathf.Lerp(0.1f, maxScale, t));

            Color c = mat.color;
            c.a     = Mathf.Lerp(0.8f, 0f, t);
            mat.color = c;

            yield return null;
        }
        if (ring != null) Destroy(ring);
        Destroy(gameObject);
    }
}
