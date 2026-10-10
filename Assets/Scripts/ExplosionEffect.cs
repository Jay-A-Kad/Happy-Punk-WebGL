using System.Collections;
using UnityEngine;

/// <summary>
/// Self-contained explosion visual. Spawned at a world position; auto-destroys
/// once all effects have faded. Contains:
///   - Fire burst       — bright orange/yellow sphere burst
///   - Smoke puff       — expanding grey billowing cloud
///   - Sparks           — fast hot particles with stretch-render trails
///   - Shockwave ring   — rapidly expanding flat disc at ground level
///   - Debris chunks    — primitive GameObjects launched outward by physics
/// </summary>
public class ExplosionEffect : MonoBehaviour
{
    // ── Lifetime ─────────────────────────────────────────────────────────────────
    private const float TOTAL_LIFETIME   = 4f;

    // ── Debris ───────────────────────────────────────────────────────────────────
    private const int   DEBRIS_COUNT     = 14;
    private const float DEBRIS_FORCE_MIN = 5f;
    private const float DEBRIS_FORCE_MAX = 12f;
    private const float DEBRIS_TORQUE    = 8f;
    private const float DEBRIS_SIZE_MIN  = 0.06f;
    private const float DEBRIS_SIZE_MAX  = 0.22f;
    private const float DEBRIS_LIFETIME  = 3f;

    // ── Fire ─────────────────────────────────────────────────────────────────────
    private const int   FIRE_COUNT       = 70;
    private const float FIRE_LIFETIME    = 0.9f;
    private const float FIRE_SPEED_MIN   = 3f;
    private const float FIRE_SPEED_MAX   = 9f;
    private const float FIRE_SIZE_MIN    = 0.4f;
    private const float FIRE_SIZE_MAX    = 1.2f;

    // ── Smoke ────────────────────────────────────────────────────────────────────
    private const int   SMOKE_COUNT      = 35;
    private const float SMOKE_LIFETIME   = 2.8f;
    private const float SMOKE_SPEED_MIN  = 0.8f;
    private const float SMOKE_SPEED_MAX  = 3.5f;
    private const float SMOKE_SIZE_MIN   = 0.6f;
    private const float SMOKE_SIZE_MAX   = 1.8f;

    // ── Sparks ───────────────────────────────────────────────────────────────────
    private const int   SPARK_COUNT      = 90;
    private const float SPARK_LIFETIME   = 1.3f;
    private const float SPARK_SPEED_MIN  = 6f;
    private const float SPARK_SPEED_MAX  = 18f;
    private const float SPARK_SIZE       = 0.08f;

    // ── Shockwave ────────────────────────────────────────────────────────────────
    private const float SHOCKWAVE_DURATION  = 0.35f;
    private const float SHOCKWAVE_MAX_SCALE = 6f;

    // URP-compatible shader — "Particles/Standard Unlit" is Built-in RP only and
    // will not be included in WebGL URP builds.
    private const string PARTICLE_SHADER = "Universal Render Pipeline/Particles/Unlit";

    // Resources path to the pre-built material asset — always included in builds.
    private const string PARTICLE_MAT_PATH = "FX/ParticleUnlit";

    private static Material _particleMatTemplate;

    // ── Shader helper ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a new instance of the URP ParticleUnlit material loaded from Resources.
    /// This guarantees the shader is included in the WebGL build.
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
            Debug.LogError($"[ExplosionEffect] Shader '{PARTICLE_SHADER}' not found.");
            shader = Shader.Find("Hidden/InternalErrorShader");
        }
        return new Material(shader);
    }

    // ── Static factory ───────────────────────────────────────────────────────────

    /// <summary>Spawns a full explosion at the given world position.</summary>
    public static void Spawn(Vector3 position)
    {
        GameObject go = new("Explosion");
        go.transform.position = position;
        ExplosionEffect fx = go.AddComponent<ExplosionEffect>();
        fx.Begin();
    }

    // ── Initialisation ───────────────────────────────────────────────────────────

    private void Begin()
    {
        SpawnFireBurst();
        SpawnSmokePuff();
        SpawnSparks();
        SpawnShockwave();
        SpawnDebris();
        StartCoroutine(SelfDestruct(TOTAL_LIFETIME));
    }

    // ── Fire ─────────────────────────────────────────────────────────────────────

    private void SpawnFireBurst()
    {
        GameObject go = new("Fire");
        go.transform.SetParent(transform, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = ps.main;
        main.loop            = false;
        main.duration        = 0.25f;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(FIRE_LIFETIME * 0.5f, FIRE_LIFETIME);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(FIRE_SPEED_MIN, FIRE_SPEED_MAX);
        main.startSize       = new ParticleSystem.MinMaxCurve(FIRE_SIZE_MIN, FIRE_SIZE_MAX);
        main.startColor      = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.9f, 0.2f),
            new Color(1f, 0.3f, 0f)
        );
        main.gravityModifier   = new ParticleSystem.MinMaxCurve(-0.25f);
        main.simulationSpace   = ParticleSystemSimulationSpace.World;
        main.maxParticles      = FIRE_COUNT;

        ParticleSystem.EmissionModule em = ps.emission;
        em.enabled       = true;
        em.rateOverTime  = 0f;
        em.SetBurst(0, new ParticleSystem.Burst(0f, FIRE_COUNT));

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled    = true;
        shape.shapeType  = ParticleSystemShapeType.Sphere;
        shape.radius     = 0.35f;

        ParticleSystem.SizeOverLifetimeModule sizeOL = ps.sizeOverLifetime;
        sizeOL.enabled   = true;
        sizeOL.size      = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

        ParticleSystem.ColorOverLifetimeModule colOL = ps.colorOverLifetime;
        colOL.enabled    = true;
        Gradient g       = new();
        g.SetKeys(
            new GradientColorKey[]
            {
                new(new Color(1f, 1f, 0.4f),   0f),
                new(new Color(1f, 0.4f, 0f),   0.35f),
                new(new Color(0.25f, 0.1f, 0f), 1f),
            },
            new GradientAlphaKey[]
            {
                new(1f,   0f),
                new(0.9f, 0.3f),
                new(0f,   1f),
            }
        );
        colOL.color = new ParticleSystem.MinMaxGradient(g);

        ParticleSystemRenderer rend = go.GetComponent<ParticleSystemRenderer>();
        rend.material   = CreateParticleMaterial();
        rend.renderMode = ParticleSystemRenderMode.Billboard;

        ps.Play();
    }

    // ── Smoke ─────────────────────────────────────────────────────────────────────

    private void SpawnSmokePuff()
    {
        GameObject go = new("Smoke");
        go.transform.SetParent(transform, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = ps.main;
        main.loop            = false;
        main.duration        = 0.4f;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(SMOKE_LIFETIME * 0.6f, SMOKE_LIFETIME);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(SMOKE_SPEED_MIN, SMOKE_SPEED_MAX);
        main.startSize       = new ParticleSystem.MinMaxCurve(SMOKE_SIZE_MIN, SMOKE_SIZE_MAX);
        main.startColor      = new ParticleSystem.MinMaxGradient(
            new Color(0.5f, 0.45f, 0.4f, 0.9f),
            new Color(0.15f, 0.15f, 0.15f, 0.6f)
        );
        main.gravityModifier   = new ParticleSystem.MinMaxCurve(-0.1f);
        main.simulationSpace   = ParticleSystemSimulationSpace.World;
        main.maxParticles      = SMOKE_COUNT;

        ParticleSystem.EmissionModule em = ps.emission;
        em.enabled       = true;
        em.rateOverTime  = 0f;
        em.SetBurst(0, new ParticleSystem.Burst(0f, SMOKE_COUNT));

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled    = true;
        shape.shapeType  = ParticleSystemShapeType.Sphere;
        shape.radius     = 0.5f;

        ParticleSystem.SizeOverLifetimeModule sizeOL = ps.sizeOverLifetime;
        sizeOL.enabled   = true;
        AnimationCurve grow = new();
        grow.AddKey(0f, 0.25f);
        grow.AddKey(1f, 1f);
        sizeOL.size = new ParticleSystem.MinMaxCurve(1f, grow);

        ParticleSystem.ColorOverLifetimeModule colOL = ps.colorOverLifetime;
        colOL.enabled = true;
        Gradient g    = new();
        g.SetKeys(
            new GradientColorKey[]
            {
                new(new Color(0.55f, 0.45f, 0.3f), 0f),
                new(new Color(0.25f, 0.25f, 0.25f), 0.4f),
                new(new Color(0.12f, 0.12f, 0.12f), 1f),
            },
            new GradientAlphaKey[]
            {
                new(0.9f, 0f),
                new(0.5f, 0.5f),
                new(0f,   1f),
            }
        );
        colOL.color = new ParticleSystem.MinMaxGradient(g);

        ParticleSystemRenderer rend = go.GetComponent<ParticleSystemRenderer>();
        rend.material   = CreateParticleMaterial();
        rend.renderMode = ParticleSystemRenderMode.Billboard;
        rend.sortingOrder = -1; // render behind fire

        ps.Play();
    }

    // ── Sparks ───────────────────────────────────────────────────────────────────

    private void SpawnSparks()
    {
        GameObject go = new("Sparks");
        go.transform.SetParent(transform, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = ps.main;
        main.loop            = false;
        main.duration        = 0.15f;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(SPARK_LIFETIME * 0.4f, SPARK_LIFETIME);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(SPARK_SPEED_MIN, SPARK_SPEED_MAX);
        main.startSize       = new ParticleSystem.MinMaxCurve(SPARK_SIZE * 0.5f, SPARK_SIZE);
        main.startColor      = new ParticleSystem.MinMaxGradient(
            new Color(1f, 1f, 0.9f),
            new Color(1f, 0.7f, 0.1f)
        );
        main.gravityModifier   = new ParticleSystem.MinMaxCurve(0.6f);
        main.simulationSpace   = ParticleSystemSimulationSpace.World;
        main.maxParticles      = SPARK_COUNT;

        ParticleSystem.EmissionModule em = ps.emission;
        em.enabled       = true;
        em.rateOverTime  = 0f;
        em.SetBurst(0, new ParticleSystem.Burst(0f, SPARK_COUNT));

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled    = true;
        shape.shapeType  = ParticleSystemShapeType.Sphere;
        shape.radius     = 0.15f;

        ParticleSystem.ColorOverLifetimeModule colOL = ps.colorOverLifetime;
        colOL.enabled = true;
        Gradient g    = new();
        g.SetKeys(
            new GradientColorKey[]
            {
                new(new Color(1f, 1f, 0.9f),   0f),
                new(new Color(1f, 0.4f, 0f),   0.5f),
                new(new Color(0.2f, 0.1f, 0f), 1f),
            },
            new GradientAlphaKey[]
            {
                new(1f,   0f),
                new(0.8f, 0.5f),
                new(0f,   1f),
            }
        );
        colOL.color = new ParticleSystem.MinMaxGradient(g);

        ParticleSystemRenderer rend = go.GetComponent<ParticleSystemRenderer>();
        Material mat        = CreateParticleMaterial();
        rend.material       = mat;
        rend.trailMaterial  = mat;
        rend.renderMode     = ParticleSystemRenderMode.Stretch;
        rend.lengthScale    = 3f;
        rend.sortingOrder   = 2;

        ps.Play();
    }

    // ── Shockwave ring ───────────────────────────────────────────────────────────

    private void SpawnShockwave()
    {
        // Use a flat cylinder that rapidly expands then disappears.
        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.transform.SetParent(transform, false);
        ring.transform.localPosition = Vector3.zero;
        ring.transform.localScale    = new Vector3(0.1f, 0.02f, 0.1f);

        // Remove the collider so the ring doesn't interfere with physics.
        Destroy(ring.GetComponent<Collider>());

        Renderer rend    = ring.GetComponent<Renderer>();
        Material mat     = CreateParticleMaterial();
        mat.color        = new Color(1f, 0.7f, 0.2f, 0.7f);
        rend.material    = mat;

        StartCoroutine(AnimateShockwave(ring, mat));
    }

    private IEnumerator AnimateShockwave(GameObject ring, Material mat)
    {
        float elapsed = 0f;
        while (elapsed < SHOCKWAVE_DURATION)
        {
            elapsed  += Time.deltaTime;
            float t   = elapsed / SHOCKWAVE_DURATION;
            float s   = Mathf.Lerp(0.1f, SHOCKWAVE_MAX_SCALE, t);
            ring.transform.localScale = new Vector3(s, 0.02f, s);

            Color c   = mat.color;
            c.a       = Mathf.Lerp(0.7f, 0f, t);
            mat.color = c;

            yield return null;
        }
        Destroy(ring);
    }

    // ── Debris ───────────────────────────────────────────────────────────────────

    private void SpawnDebris()
    {
        for (int i = 0; i < DEBRIS_COUNT; i++)
        {
            PrimitiveType type = (i % 3 == 0) ? PrimitiveType.Sphere : PrimitiveType.Cube;
            GameObject chunk   = GameObject.CreatePrimitive(type);

            float size = Random.Range(DEBRIS_SIZE_MIN, DEBRIS_SIZE_MAX);
            chunk.transform.position   = transform.position + Random.insideUnitSphere * 0.25f;
            chunk.transform.localScale = Vector3.one * size;

            // Charred metallic colour.
            Renderer rend = chunk.GetComponent<Renderer>();
            rend.material.color = Color.Lerp(
                new Color(0.35f, 0.25f, 0.1f),
                new Color(0.1f,  0.1f,  0.1f),
                Random.value
            );

            Rigidbody rb = chunk.AddComponent<Rigidbody>();
            // Bias upward so pieces arc through the air.
            Vector3 dir = (Random.onUnitSphere + Vector3.up * 0.7f).normalized;
            float force = Random.Range(DEBRIS_FORCE_MIN, DEBRIS_FORCE_MAX);
            rb.AddForce(dir * force, ForceMode.Impulse);
            rb.AddTorque(Random.onUnitSphere * DEBRIS_TORQUE, ForceMode.Impulse);

            Destroy(chunk, DEBRIS_LIFETIME);
        }
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────────

    private IEnumerator SelfDestruct(float delay)
    {
        yield return new WaitForSeconds(delay);
        Destroy(gameObject);
    }
}
