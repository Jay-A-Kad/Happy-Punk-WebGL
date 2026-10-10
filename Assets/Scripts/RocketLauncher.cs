using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fires rockets at the player from random active enemies.
///
/// Each launch cycle picks a random salvo size from <see cref="SALVO_SIZES"/>
/// (1, 2, or 4) and staggers the individual shots by <see cref="salvoStagger"/>
/// seconds so they arrive one after another rather than all at once.
///
/// A pool of <see cref="MAX_ROCKETS"/> instances is maintained so multiple
/// rockets can be in-flight simultaneously. The next salvo only starts once
/// every rocket from the previous one has returned to the pool.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class RocketLauncher : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────────────────────────────
    private const float LAUNCH_OFFSET_Y = 1f;
    private const int   MAX_ROCKETS     = 4;

    /// <summary>Possible salvo sizes chosen at random each cycle.</summary>
    private static readonly int[] SALVO_SIZES = { 1, 2, 4 };

    // ── Inspector ────────────────────────────────────────────────────────────────

    [Tooltip("Seconds between the last rocket of one salvo landing and the next salvo firing.")]
    public float fireCooldown = 4f;

    [Tooltip("Seconds between individual rockets within the same salvo.")]
    public float salvoStagger = 0.5f;

    [Tooltip("The Rocket prefab with RocketProjectile and LineRenderer.")]
    public GameObject rocketPrefab;

    [Tooltip("The Car / player transform the rocket homes in on.")]
    public Transform playerTransform;

    [Tooltip("The PlayerRocketInterceptor on the MonsterTruck. " +
             "Set here so the interceptor always has a direct reference to the " +
             "first rocket instance without scene lookups.")]
    public PlayerRocketInterceptor rocketInterceptor;

    [Header("Audio")]
    [Tooltip("Played once every time a single rocket is launched.")]
    public AudioClip launchSound;

    // ── Private state ────────────────────────────────────────────────────────────

    private EnemyPool   _pool;
    private AudioSource _audio;

    /// <summary>Pool of pre-instantiated rocket GameObjects (max 4).</summary>
    private readonly List<GameObject>       _rocketInstances   = new(MAX_ROCKETS);
    private readonly List<RocketProjectile> _rocketProjectiles = new(MAX_ROCKETS);

    /// <summary>How many rockets from the current salvo are still airborne.</summary>
    private int _rocketsInFlight;

    private Coroutine _fireCoroutine;

    // ── Unity lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        _pool  = GetComponent<EnemyPool>();
        _audio = GetComponent<AudioSource>();

        _audio.playOnAwake  = false;
        _audio.spatialBlend = 0f;
        _audio.loop         = false;
    }

    private void Start()
    {
        BuildPool();
        _fireCoroutine = StartCoroutine(FireLoop());
    }

    private void OnDestroy()
    {
        if (_fireCoroutine != null)
            StopCoroutine(_fireCoroutine);
    }

    // ── Pool construction ────────────────────────────────────────────────────────

    /// <summary>
    /// Instantiates all <see cref="MAX_ROCKETS"/> rocket GameObjects upfront and
    /// wires their direct back-references so no scene lookups are needed at runtime.
    /// </summary>
    private void BuildPool()
    {
        CarController car = playerTransform != null
            ? playerTransform.GetComponent<CarController>()
            : null;

        for (int i = 0; i < MAX_ROCKETS; i++)
        {
            GameObject go = Instantiate(rocketPrefab);
            go.SetActive(false);

            RocketProjectile proj = go.GetComponent<RocketProjectile>();
            proj.launcher  = this;
            proj.enemyPool = _pool;
            proj.playerCar = car;

            _rocketInstances.Add(go);
            _rocketProjectiles.Add(proj);
        }

        // Give the interceptor the full pool array so it can detect any rocket,
        // not just the first one, when multiple are in flight.
        if (rocketInterceptor != null)
            rocketInterceptor.rocketPool = _rocketProjectiles.ToArray();
    }

    // ── Fire loop ────────────────────────────────────────────────────────────────

    private IEnumerator FireLoop()
    {
        WaitForSeconds betweenSalvos = new(fireCooldown);

        while (true)
        {
            yield return betweenSalvos;

            // Wait until every rocket from the previous salvo has returned.
            while (_rocketsInFlight > 0)
                yield return null;

            _rocketsInFlight = 0; // clamp in case of any underflow

            if (_pool.ActiveEnemies.Count == 0) continue;

            yield return StartCoroutine(FireSalvo());
        }
    }

    /// <summary>
    /// Picks a random salvo size and fires that many rockets from different
    /// enemies, each separated by <see cref="salvoStagger"/> seconds.
    /// </summary>
    private IEnumerator FireSalvo()
    {
        int salvoSize = SALVO_SIZES[Random.Range(0, SALVO_SIZES.Length)];

        // Never fire more than what is currently available in the pool.
        salvoSize = Mathf.Min(salvoSize, AvailableRocketCount());

        if (salvoSize == 0) yield break;

        WaitForSeconds staggerWait = new(salvoStagger);

        for (int i = 0; i < salvoSize; i++)
        {
            if (_pool.ActiveEnemies.Count == 0) break;

            GameObject rocketGO = GetAvailableRocket();
            if (rocketGO == null) break;

            FireSingleRocket(rocketGO);

            // Stagger all but the last shot.
            if (i < salvoSize - 1)
                yield return staggerWait;
        }
    }

    // ── Individual rocket ────────────────────────────────────────────────────────

    /// <summary>Positions and launches one rocket from a random active enemy.</summary>
    private void FireSingleRocket(GameObject rocketGO)
    {
        IReadOnlyList<GameObject> enemies = _pool.ActiveEnemies;
        if (enemies.Count == 0) return;

        GameObject shooter  = enemies[Random.Range(0, enemies.Count)];
        Vector3    spawnPos = shooter.transform.position + Vector3.up * LAUNCH_OFFSET_Y;

        rocketGO.transform.SetPositionAndRotation(spawnPos, Quaternion.identity);
        rocketGO.SetActive(true);

        rocketGO.GetComponent<RocketProjectile>().Launch(playerTransform);

        if (launchSound != null)
            _audio.PlayOneShot(launchSound);

        _rocketsInFlight++;

        Debug.Log($"[RocketLauncher] Rocket fired from {shooter.name}. In-flight: {_rocketsInFlight}");
    }

    // ── Pool helpers ─────────────────────────────────────────────────────────────

    /// <summary>Returns the first inactive rocket from the pool, or null if all are busy.</summary>
    private GameObject GetAvailableRocket()
    {
        foreach (GameObject go in _rocketInstances)
        {
            if (!go.activeInHierarchy)
                return go;
        }
        return null;
    }

    /// <summary>How many rockets are currently inactive and available to fire.</summary>
    private int AvailableRocketCount()
    {
        int count = 0;
        foreach (GameObject go in _rocketInstances)
        {
            if (!go.activeInHierarchy)
                count++;
        }
        return count;
    }

    // ── Callback ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="RocketProjectile.Deactivate"/> when a rocket lands or expires.
    /// Decrements the in-flight counter so <see cref="FireLoop"/> knows when the
    /// pool is free for the next salvo.
    /// </summary>
    public void OnRocketReturned(GameObject rocket)
    {
        _rocketsInFlight = Mathf.Max(0, _rocketsInFlight - 1);
        Debug.Log($"[RocketLauncher] Rocket returned. In-flight: {_rocketsInFlight}");
    }
}
