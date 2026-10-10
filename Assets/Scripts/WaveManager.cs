using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Manages infinite enemy waves. Wave 1 starts at <see cref="FIRST_WAVE_COUNT"/> enemies;
/// each subsequent wave adds <see cref="ENEMIES_PER_WAVE_INCREMENT"/> more.
/// Waves continue until the player dies.
/// Fires events so the HUD can react without coupling.
/// </summary>
[RequireComponent(typeof(EnemyPool))]
public class WaveManager : MonoBehaviour
{
    private const int   FIRST_WAVE_COUNT          = 20;
    private const int   ENEMIES_PER_WAVE_INCREMENT = 15;

    private const float SPAWN_RADIUS        = 25f;
    private const float SPAWN_DELAY_SECONDS = 0.05f;

    [Tooltip("The Car / player transform enemies will chase.")]
    public Transform playerTransform;

    [Tooltip("Seconds to display the Wave Cleared banner before announcing the next wave.")]
    public float waveClearPauseDuration = 3f;

    [Tooltip("Seconds the 'Incoming Wave X' banner is visible before enemies spawn.")]
    public float incomingWaveAnnounceDuration = 2.5f;

    // ── Events ─────────────────────────────────────────────────────────────────

    /// <summary>Raised when a new wave begins. Parameter is the 1-based wave number.</summary>
    public event Action<int> OnWaveStarted;

    /// <summary>Raised when all enemies of the current wave are defeated.</summary>
    public event Action<int> OnWaveCleared;

    /// <summary>Never raised in infinite mode; kept for API compatibility.</summary>
    public event Action OnAllWavesComplete;

    // ── Private state ───────────────────────────────────────────────────────────

    private EnemyPool _pool;
    private int _currentWaveIndex = -1;
    private bool _waveInProgress;
    private bool _gameStarted;
    private Coroutine _spawnCoroutine;
    private Coroutine _progressionCoroutine;

    public int CurrentWave      => _currentWaveIndex + 1;
    /// <summary>Always false in infinite mode — waves never end on their own.</summary>
    public bool AllWavesComplete => false;

    /// <summary>Returns how many enemies spawn in the given 0-based wave index.</summary>
    private int EnemiesForWave(int index) => FIRST_WAVE_COUNT + index * ENEMIES_PER_WAVE_INCREMENT;

    // ── Lifecycle ───────────────────────────────────────────────────────────────

    private void Awake()
    {
        _pool = GetComponent<EnemyPool>();
    }

    private void Start()
    {
        // Subscribe in Start so all Awake() calls are done and Instance is guaranteed set.
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnGameStarted += HandleGameStarted;
            GameStateManager.Instance.OnPlayerDied  += HandlePlayerDied;

            // Safety: if the game was already started before this object enabled, kick off wave 1.
            if (GameStateManager.Instance.CurrentState == GameStateManager.State.Playing && !_gameStarted)
                HandleGameStarted();
        }
        else
        {
            Debug.LogError("[WaveManager] GameStateManager.Instance is null in Start. " +
                           "Ensure GameStateManager exists in the scene.", this);
        }
    }

    private void OnDestroy()
    {
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnGameStarted -= HandleGameStarted;
            GameStateManager.Instance.OnPlayerDied  -= HandlePlayerDied;
        }

        if (_spawnCoroutine       != null) StopCoroutine(_spawnCoroutine);
        if (_progressionCoroutine != null) StopCoroutine(_progressionCoroutine);
    }

    private bool _spawningComplete;

    private void Update()
    {
        if (!_gameStarted || !_waveInProgress || !_spawningComplete) return;

        if (_pool.ActiveEnemies.Count == 0)
        {
            HandleWaveCleared();
        }
    }

    // ── Game-state handlers ─────────────────────────────────────────────────────

    private void HandleGameStarted()
    {
        _gameStarted = true;
        LaunchNextWave();
    }

    private void HandlePlayerDied()
    {
        // Cancel any in-progress spawn or between-wave coroutine immediately.
        if (_spawnCoroutine      != null) { StopCoroutine(_spawnCoroutine);      _spawnCoroutine      = null; }
        if (_progressionCoroutine != null) { StopCoroutine(_progressionCoroutine); _progressionCoroutine = null; }
        _waveInProgress = false;
    }

    // ── Wave flow ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Advances to the next wave index then immediately begins spawning.
    /// Called on game start and after each between-wave pause.
    /// </summary>
    private void LaunchNextWave()
    {
        _currentWaveIndex++;

        int count = EnemiesForWave(_currentWaveIndex);
        Debug.Log($"[WaveManager] Starting wave {CurrentWave} — spawning {count} enemies.");

        _pool.EnsureCapacity(count);

        _spawningComplete = false;
        _waveInProgress   = true;
        _spawnCoroutine   = StartCoroutine(SpawnWave(count));

        OnWaveStarted?.Invoke(CurrentWave);
        GameStateManager.Instance?.NotifyWaveStarted();
    }

    private void HandleWaveCleared()
    {
        int clearedWave = CurrentWave;
        _waveInProgress = false;
        Debug.Log($"[WaveManager] Wave {clearedWave} cleared.");

        DespawnAllFuelCores();

        OnWaveCleared?.Invoke(clearedWave);
        GameStateManager.Instance?.NotifyWaveCleared(clearedWave);

        // Always start the next wave — infinite mode.
        _progressionCoroutine = StartCoroutine(BetweenWavesPause(clearedWave + 1));
    }

    /// <summary>
    /// Destroys every <see cref="FuelCorePickup"/> currently alive in the scene
    /// so stale cores do not carry over into the next wave.
    /// </summary>
    private void DespawnAllFuelCores()
    {
        FuelCorePickup[] cores = FindObjectsByType<FuelCorePickup>(FindObjectsSortMode.None);
        foreach (FuelCorePickup core in cores)
            Destroy(core.gameObject);

        if (cores.Length > 0)
            Debug.Log($"[WaveManager] Despawned {cores.Length} leftover fuel core(s) on wave clear.");
    }

    /// <summary>
    /// Waits for the wave-clear banner to show, then announces the next wave,
    /// then spawns it.
    /// </summary>
    private IEnumerator BetweenWavesPause(int nextWaveNumber)
    {
        // Let the "Wave Cleared" banner breathe.
        yield return new WaitForSeconds(waveClearPauseDuration);

        // Signal HUD / state that the next wave is incoming.
        GameStateManager.Instance?.NotifyIncomingWave(nextWaveNumber);

        // Show the incoming-wave banner for a moment before enemies arrive.
        yield return new WaitForSeconds(incomingWaveAnnounceDuration);

        LaunchNextWave();
    }

    // ── Spawning ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Spawns enemies one-by-one with a small stagger to avoid a single-frame spike.
    /// </summary>
    private IEnumerator SpawnWave(int count)
    {
        WaitForSeconds wait = new(SPAWN_DELAY_SECONDS);

        for (int i = 0; i < count; i++)
        {
            SpawnEnemy();
            yield return wait;
        }

        _spawningComplete = true;
    }

    /// <summary>
    /// Gets an enemy from the pool, places it at a random spawn point on the arena
    /// perimeter (uniformly distributed across all compass directions), and initialises
    /// its NavMeshAgent.
    /// </summary>
    private void SpawnEnemy()
    {
        Vector3 spawnPosition = GetRandomPerimeterPosition();
        GameObject instance = _pool.Get(spawnPosition, Quaternion.identity);

        if (instance == null) return;

        EnemyNavAgent agent = instance.GetComponent<EnemyNavAgent>();
        if (agent != null)
        {
            agent.Initialise(playerTransform, _pool);
        }
    }

    /// <summary>
    /// Returns a spawn point on a circle of radius <see cref="SPAWN_RADIUS"/> centred
    /// on the arena origin (not the player) so enemies come from truly random directions
    /// rather than always being equidistant from the truck's current position.
    /// </summary>
    private Vector3 GetRandomPerimeterPosition()
    {
        float angle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
        float cx = playerTransform != null ? playerTransform.position.x : 0f;
        float cz = playerTransform != null ? playerTransform.position.z : 0f;

        // Scatter the radius slightly (±20 %) so enemies don't land on a perfect ring.
        float radius = SPAWN_RADIUS * UnityEngine.Random.Range(0.8f, 1.2f);

        return new Vector3(
            cx + Mathf.Cos(angle) * radius,
            0f,
            cz + Mathf.Sin(angle) * radius
        );
    }

    // ── Cleanup ─────────────────────────────────────────────────────────────────
}

