using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Grants the player two kill-meter abilities when <see cref="KillMeterController.IsFull"/>.
///
///   H — Heal 25 % of max health + plays Health-sound.
///   T — Shockwave: kills every Robo within <see cref="shockwaveRadius"/>,
///       spawns electric lightning arcs + comic-book text labels on each target,
///       expanding orange ring, radial sparks, and plays Shockwave-sound.
///
/// Either ability immediately consumes the charge via <see cref="KillMeterController.ConsumeCharge"/>.
/// Attach to the MonsterTruck root alongside <see cref="CarHealthSystem"/>.
/// </summary>
[RequireComponent(typeof(CarHealthSystem))]
[RequireComponent(typeof(AudioSource))]
public class KillMeterAbilities : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────────────────────────────

    private const float  HEAL_FRACTION      = 0.25f;
    private const float  MAX_HEALTH         = 100f;
    private const KeyCode HEAL_KEY          = KeyCode.H;
    private const KeyCode SHOCKWAVE_KEY     = KeyCode.T;
    // URP-compatible shaders — Built-in RP shader names are not included in WebGL URP builds.
    private const string PARTICLE_SHADER    = "Universal Render Pipeline/Particles/Unlit";
    private const string SPRITE_SHADER      = "Universal Render Pipeline/Particles/Unlit";

    // Resources path to pre-built material asset — always included in builds.
    private const string PARTICLE_MAT_PATH  = "FX/ParticleUnlit";

    private static Material _particleMatTemplate;

    // ── Shockwave ring ───────────────────────────────────────────────────────────

    private const float SHOCKWAVE_DURATION  = 0.55f;
    private const int   SPARK_COUNT         = 90;

    // ── Electric arcs ────────────────────────────────────────────────────────────

    private const int   ARC_SEGMENT_COUNT   = 18;    // line renderer points per arc
    private const float ARC_JITTER          = 0.45f; // max XZ offset per mid-point
    private const float ARC_LIFETIME        = 0.35f; // seconds each arc lives
    private const int   ARCS_PER_ENEMY      = 4;     // simultaneous arc strands

    // ── Comic labels ─────────────────────────────────────────────────────────────

    private static readonly string[] COMIC_WORDS = { "SHOCK!!!", "ZAP!", "BOOM!", "KR-ZAP!", "WHAM!" };
    private const float LABEL_HEIGHT            = 2.6f;   // units above enemy pivot
    private const float LABEL_SCALE             = 0.012f; // TMP world-space scale
    private const float LABEL_LIFETIME          = 0.95f;
    private const float LABEL_RISE_SPEED        = 1.8f;   // units/s upward drift

    // ── Inspector ────────────────────────────────────────────────────────────────

    [Header("Dependencies")]
    public KillMeterController killMeter;
    public EnemyPool           enemyPool;

    [Header("Shockwave")]
    [Tooltip("World-space kill radius.")]
    public float shockwaveRadius = 12f;

    [Header("Audio")]
    [Tooltip("Drag Assets/Music/Health-sound.mp3 here.")]
    public AudioClip healSound;

    [Tooltip("Drag Assets/Music/Shockwave-sound.mp3 here.")]
    public AudioClip shockwaveSound;

    // ── Private ──────────────────────────────────────────────────────────────────

    private CarHealthSystem _health;
    private AudioSource     _audio;
    private Camera          _mainCamera;

    // ── Shader helper ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a new instance of the URP ParticleUnlit material loaded from Resources.
    /// The shaderName parameter is kept for signature compatibility but is only used
    /// as an Editor-only fallback — Resources.Load is the WebGL-safe primary path.
    /// </summary>
    private static Material CreateParticleMaterial(string shaderName)
    {
        if (_particleMatTemplate == null)
            _particleMatTemplate = Resources.Load<Material>(PARTICLE_MAT_PATH);

        if (_particleMatTemplate != null)
            return new Material(_particleMatTemplate);

        // Editor-only fallback
        Shader shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogError($"[KillMeterAbilities] Shader '{shaderName}' not found.");
            shader = Shader.Find("Hidden/InternalErrorShader");
        }
        return new Material(shader);
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _health      = GetComponent<CarHealthSystem>();
        _audio       = GetComponent<AudioSource>();
        _mainCamera  = Camera.main;

        _audio.playOnAwake  = false;
        _audio.spatialBlend = 0f; // 2D — sounds like UI feedback, not 3D world audio
    }

    private void Update()
    {
        if (killMeter == null || !killMeter.IsFull) return;
        if (_health == null  || _health.IsDead)    return;

        if (Input.GetKeyDown(HEAL_KEY))
            UseHeal();
        else if (Input.GetKeyDown(SHOCKWAVE_KEY))
            UseShockwave();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // H — HEAL
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Restores 25 % of max health, plays sound, and consumes the charge.</summary>
    private void UseHeal()
    {
        _health.Heal(MAX_HEALTH * HEAL_FRACTION);
        killMeter.ConsumeCharge();
        PlaySound(healSound);
        SpawnHealFlash();
    }

    private void SpawnHealFlash()
    {
        Vector3    origin = transform.position + Vector3.up * 0.8f;
        GameObject root   = new("HealFX");
        root.transform.position = origin;

        GameObject go = new("HealBurst");
        go.transform.SetParent(root.transform, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main             = ps.main;
        main.loop            = false;
        main.duration        = 0.2f;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(6f, 14f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.12f, 0.32f);
        main.startColor      = new ParticleSystem.MinMaxGradient(
            new Color(0.22f, 1f,   0.08f, 1f),
            new Color(0f,    0.85f, 0.3f, 0.8f));
        main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.2f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles    = 60;

        var em = ps.emission;
        em.enabled      = true;
        em.rateOverTime = 0f;
        em.SetBurst(0, new ParticleSystem.Burst(0f, 60));

        var shape       = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius    = 0.5f;

        var colOL     = ps.colorOverLifetime;
        colOL.enabled = true;
        Gradient g    = new();
        g.SetKeys(
            new GradientColorKey[]
            {
                new(new Color(0.6f, 1f,   0.3f), 0f),
                new(new Color(0.1f, 0.8f, 0.2f), 0.5f),
                new(new Color(0f,   0.4f, 0.1f), 1f)
            },
            new GradientAlphaKey[] { new(1f, 0f), new(0.6f, 0.5f), new(0f, 1f) }
        );
        colOL.color = new ParticleSystem.MinMaxGradient(g);

        go.GetComponent<ParticleSystemRenderer>().material =
            CreateParticleMaterial(PARTICLE_SHADER);

        ps.Play();
        Destroy(root, 2f);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // T — SHOCKWAVE
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Kills all Robos in radius, spawns per-enemy electric + comic FX,
    /// plays shockwave ring + sparks, plays sound, consumes the charge.
    /// </summary>
    private void UseShockwave()
    {
        List<GameObject> killed = CollectEnemiesInRadius();

        // Per-enemy FX before they are returned to the pool.
        foreach (GameObject enemy in killed)
            SpawnEnemyShockFX(enemy.transform.position);

        // Return them now (removes from active list).
        foreach (GameObject enemy in killed)
            enemyPool.Return(enemy);

        killMeter.ConsumeCharge();
        PlaySound(shockwaveSound);
        SpawnShockwaveFX();
    }

    /// <summary>
    /// Returns a snapshot of all active enemies within <see cref="shockwaveRadius"/>
    /// without modifying the pool yet.
    /// </summary>
    private List<GameObject> CollectEnemiesInRadius()
    {
        var result   = new List<GameObject>();
        if (enemyPool == null) return result;

        Vector3 origin   = transform.position;
        float   radiusSq = shockwaveRadius * shockwaveRadius;

        foreach (GameObject enemy in enemyPool.ActiveEnemies)
        {
            if (enemy == null) continue;
            Vector3 delta = enemy.transform.position - origin;
            delta.y = 0f;
            if (delta.sqrMagnitude <= radiusSq)
                result.Add(enemy);
        }
        return result;
    }

    // ── Per-enemy shock FX ───────────────────────────────────────────────────────

    /// <summary>
    /// Spawns electric arc strands + a comic-book word label at the given world position.
    /// </summary>
    private void SpawnEnemyShockFX(Vector3 worldPos)
    {
        GameObject root = new("EnemyShockFX");
        root.transform.position = worldPos;

        for (int i = 0; i < ARCS_PER_ENEMY; i++)
            SpawnLightningArc(root.transform);

        SpawnElectricParticles(root.transform);
        SpawnComicLabel(root.transform);

        Destroy(root, LABEL_LIFETIME + 0.3f);
    }

    // ── Lightning arc ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a jagged lightning-arc LineRenderer that randomly resamples its path
    /// each frame to simulate electric crackling, then fades out.
    /// </summary>
    private void SpawnLightningArc(Transform parent)
    {
        GameObject go = new("Arc");
        go.transform.SetParent(parent, false);

        LineRenderer lr = go.AddComponent<LineRenderer>();

        // Material — additive blend so arcs glow brightly over any background.
        Material mat = CreateParticleMaterial(SPRITE_SHADER);
        mat.SetInt("_SrcBlend",  (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend",  (int)UnityEngine.Rendering.BlendMode.One);   // additive
        mat.SetInt("_ZWrite",    0);
        mat.color = new Color(0.55f, 0.85f, 1f, 1f); // electric cyan-blue

        lr.material        = mat;
        lr.positionCount   = ARC_SEGMENT_COUNT;
        lr.startWidth      = 0.06f;
        lr.endWidth        = 0.02f;
        lr.useWorldSpace   = false;
        lr.numCapVertices  = 3;

        // Random base direction for this arc strand.
        Vector3 arcDir = new(
            Random.Range(-1f, 1f),
            Random.Range(0.6f, 1.4f),
            Random.Range(-1f, 1f)
        );
        arcDir.Normalize();
        float arcLen = Random.Range(1.0f, 2.2f);

        StartCoroutine(AnimateLightningArc(go, lr, arcDir, arcLen));
    }

    private IEnumerator AnimateLightningArc(
        GameObject go, LineRenderer lr, Vector3 dir, float length)
    {
        float elapsed = 0f;

        while (elapsed < ARC_LIFETIME)
        {
            if (lr == null) yield break;

            float fade = 1f - (elapsed / ARC_LIFETIME);

            // Resample the jagged path every frame for a crackling effect.
            for (int i = 0; i < ARC_SEGMENT_COUNT; i++)
            {
                float t = (float)i / (ARC_SEGMENT_COUNT - 1);
                Vector3 straight = dir * (t * length);
                Vector3 jitter   = Vector3.zero;
                if (i > 0 && i < ARC_SEGMENT_COUNT - 1)
                {
                    jitter = new Vector3(
                        Random.Range(-ARC_JITTER, ARC_JITTER),
                        Random.Range(-ARC_JITTER * 0.4f, ARC_JITTER * 0.4f),
                        Random.Range(-ARC_JITTER, ARC_JITTER)
                    );
                }
                lr.SetPosition(i, straight + jitter);
            }

            // Tint: arc flickers between cyan-blue and white.
            float flicker = Random.value;
            Color c       = Color.Lerp(
                new Color(0.55f, 0.85f, 1f),
                new Color(1f,    1f,    1f),
                flicker
            );
            c.a              = fade;
            lr.startColor    = c;
            lr.endColor      = new Color(c.r, c.g, c.b, c.a * 0.4f);

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (go != null) Destroy(go);
    }

    // ── Electric particle burst ────────────────────────────────────────────────────

    private void SpawnElectricParticles(Transform parent)
    {
        GameObject go = new("ElectricSparks");
        go.transform.SetParent(parent, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main             = ps.main;
        main.loop            = false;
        main.duration        = 0.15f;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.2f, 0.55f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(3f, 9f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
        main.startColor      = new ParticleSystem.MinMaxGradient(
            new Color(0.6f, 0.9f, 1f,  1f),
            new Color(1f,   1f,   0.5f, 1f));
        main.gravityModifier = new ParticleSystem.MinMaxCurve(0.15f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles    = 40;

        var em = ps.emission;
        em.enabled      = true;
        em.rateOverTime = 0f;
        em.SetBurst(0, new ParticleSystem.Burst(0f, 40));

        var shape       = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius    = 0.3f;

        var colOL     = ps.colorOverLifetime;
        colOL.enabled = true;
        Gradient g    = new();
        g.SetKeys(
            new GradientColorKey[]
            {
                new(new Color(1f,   1f,   1f),   0f),
                new(new Color(0.5f, 0.8f, 1f),   0.4f),
                new(new Color(0.2f, 0.4f, 0.8f), 1f)
            },
            new GradientAlphaKey[] { new(1f, 0f), new(0.8f, 0.3f), new(0f, 1f) }
        );
        colOL.color = new ParticleSystem.MinMaxGradient(g);

        Material sparMat   = CreateParticleMaterial(PARTICLE_SHADER);
        var rend            = go.GetComponent<ParticleSystemRenderer>();
        rend.material       = sparMat;
        rend.trailMaterial  = sparMat;
        rend.renderMode     = ParticleSystemRenderMode.Stretch;
        rend.lengthScale    = 2.5f;
        rend.sortingOrder   = 3;

        ps.Play();
    }

    // ── Comic-book label ─────────────────────────────────────────────────────────

    /// <summary>
    /// Spawns a world-space TextMeshPro label above the enemy that billboards toward
    /// the main camera, drifts upward, and fades out — classic comic-book hit text.
    /// </summary>
    private void SpawnComicLabel(Transform parent)
    {
        string word = COMIC_WORDS[Random.Range(0, COMIC_WORDS.Length)];

        GameObject go = new("ComicLabel");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.up * LABEL_HEIGHT;

        // Random tilt for that hand-drawn feel.
        float tiltZ = Random.Range(-18f, 18f);
        go.transform.localRotation = Quaternion.Euler(0f, 0f, tiltZ);
        go.transform.localScale    = Vector3.one * LABEL_SCALE;

        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        tmp.text         = word;
        tmp.fontSize     = 420;
        tmp.fontStyle    = FontStyles.Bold | FontStyles.Italic | FontStyles.UpperCase;
        tmp.alignment    = TextAlignmentOptions.Center;
        tmp.characterSpacing = 5f;

        // Outline for the punchy comic-book look.
        tmp.outlineWidth = 0.35f;
        tmp.outlineColor = new Color32(30, 0, 60, 255);  // deep purple outline

        // Pick a random hot color: yellow, orange, or white.
        Color[] palette =
        {
            new Color(1f,   0.95f, 0.05f),  // electric yellow
            new Color(1f,   0.45f, 0f),     // vivid orange
            new Color(0.55f,0.88f, 1f),     // shock cyan
            new Color(1f,   1f,    1f),     // pure white
        };
        tmp.color = palette[Random.Range(0, palette.Length)];

        // Animate: billboard toward camera, drift up, fade out.
        StartCoroutine(AnimateComicLabel(go, tmp));
    }

    private IEnumerator AnimateComicLabel(GameObject go, TextMeshPro tmp)
    {
        if (_mainCamera == null) _mainCamera = Camera.main;

        // Pop-in scale punch.
        float punchDuration = 0.12f;
        float elapsed       = 0f;
        Vector3 baseScale   = go.transform.localScale;

        while (elapsed < punchDuration)
        {
            float t = elapsed / punchDuration;
            go.transform.localScale = baseScale * Mathf.Lerp(1.6f, 1f, t);
            elapsed += Time.deltaTime;
            yield return null;
        }
        go.transform.localScale = baseScale;

        // Hold then drift + fade.
        elapsed = 0f;
        Vector3 startLocalPos = go.transform.localPosition;

        while (elapsed < LABEL_LIFETIME)
        {
            if (go == null) yield break;

            float t = elapsed / LABEL_LIFETIME;

            // Billboard toward camera.
            if (_mainCamera != null)
            {
                Vector3 camDir = go.transform.position - _mainCamera.transform.position;
                if (camDir != Vector3.zero)
                    go.transform.rotation = Quaternion.LookRotation(-camDir, Vector3.up);
            }

            // Drift upward in local space — LABEL_SCALE is already baked into localScale,
            // so we move in local units directly (do NOT divide by scale here).
            go.transform.localPosition = startLocalPos + Vector3.up * (LABEL_RISE_SPEED * elapsed);

            // Fade out in the last 40 % of lifetime.
            if (tmp != null)
            {
                float alpha = t < 0.6f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.6f) / 0.4f);
                Color c = tmp.color;
                c.a     = alpha;
                tmp.color = c;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (go != null) Destroy(go);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Shockwave ring + sparks
    // ═══════════════════════════════════════════════════════════════════════════

    private void SpawnShockwaveFX()
    {
        Vector3    origin = transform.position + Vector3.up * 0.05f;
        GameObject root   = new("ShockwaveFX");
        root.transform.position = origin;

        SpawnShockwaveRing(root.transform);
        SpawnShockwaveSparks(root.transform);

        Destroy(root, 3f);
    }

    private void SpawnShockwaveRing(Transform parent)
    {
        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.transform.SetParent(parent, false);
        ring.transform.localPosition = Vector3.zero;
        ring.transform.localScale    = new Vector3(0.05f, 0.015f, 0.05f);
        Destroy(ring.GetComponent<Collider>());

        Material mat = CreateParticleMaterial(PARTICLE_SHADER);
        mat.color    = new Color(1f, 0.50f, 0f, 0.85f);
        ring.GetComponent<Renderer>().material = mat;

        StartCoroutine(AnimateRing(ring, mat, SHOCKWAVE_DURATION, shockwaveRadius * 2f));
    }

    private IEnumerator AnimateRing(GameObject ring, Material mat, float duration, float targetScale)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float t     = elapsed / duration;
            float scale = Mathf.Lerp(0.05f, targetScale, t);
            if (ring != null)
                ring.transform.localScale = new Vector3(scale, 0.015f, scale);
            if (mat != null)
                mat.color = new Color(1f, 0.50f, 0f, Mathf.Lerp(0.85f, 0f, t));
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (ring != null) Destroy(ring);
    }

    private void SpawnShockwaveSparks(Transform parent)
    {
        GameObject go = new("ShockwaveSparks");
        go.transform.SetParent(parent, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main             = ps.main;
        main.loop            = false;
        main.duration        = 0.15f;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.5f, 1.4f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(shockwaveRadius * 0.55f,
                                                              shockwaveRadius * 1.1f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.07f, 0.18f);
        main.startColor      = new ParticleSystem.MinMaxGradient(
            new Color(1f, 1f, 0.5f), new Color(1f, 0.45f, 0f));
        main.gravityModifier = new ParticleSystem.MinMaxCurve(0.55f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles    = SPARK_COUNT;

        var em = ps.emission;
        em.enabled      = true;
        em.rateOverTime = 0f;
        em.SetBurst(0, new ParticleSystem.Burst(0f, SPARK_COUNT));

        var shape       = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius    = 0.3f;
        shape.rotation  = new Vector3(90f, 0f, 0f);

        var colOL     = ps.colorOverLifetime;
        colOL.enabled = true;
        Gradient g    = new();
        g.SetKeys(
            new GradientColorKey[]
            {
                new(new Color(1f,   1f,   0.6f), 0f),
                new(new Color(1f,   0.5f, 0f),   0.4f),
                new(new Color(0.3f, 0.1f, 0f),   1f)
            },
            new GradientAlphaKey[] { new(1f, 0f), new(0.75f, 0.4f), new(0f, 1f) }
        );
        colOL.color = new ParticleSystem.MinMaxGradient(g);

        Material sparMat   = CreateParticleMaterial(PARTICLE_SHADER);
        var rend            = go.GetComponent<ParticleSystemRenderer>();
        rend.material       = sparMat;
        rend.trailMaterial  = sparMat;
        rend.renderMode     = ParticleSystemRenderMode.Stretch;
        rend.lengthScale    = 3.5f;
        rend.sortingOrder   = 2;

        ps.Play();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Audio
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Plays a clip once via the component's AudioSource (2D, non-spatial).</summary>
    private void PlaySound(AudioClip clip)
    {
        if (clip == null || _audio == null) return;
        _audio.PlayOneShot(clip);
    }
}
