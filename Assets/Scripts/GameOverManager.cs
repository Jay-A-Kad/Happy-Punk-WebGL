using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Shows the Game Over panel after <see cref="CarDeathSequencer.OnSequenceComplete"/>
/// fires (i.e. once the truck explosion has finished and the GO is destroyed).
///
/// Just before showing the panel it sets <see cref="Time.timeScale"/> to 0 so all
/// remaining enemies, particles, and projectiles freeze in place.
///
/// Stats displayed:
///   - Robots destroyed  (from <see cref="EnemyPool.TotalEnemiesKilled"/>)
///   - Survived the crowd (total seconds from game-start to death, formatted mm:ss)
///
/// Buttons: Play Again (reload current scene) and Return to Menu (load scene index 0).
/// </summary>
public class GameOverManager : MonoBehaviour
{
    // ── Inspector ────────────────────────────────────────────────────────────────

    [Header("Panel Root")]
    [Tooltip("The root Game Over panel GameObject. Hidden at startup.")]
    public GameObject gameOverPanel;

    [Header("Stat Labels")]
    [Tooltip("Shows the number of robots destroyed.")]
    public TextMeshProUGUI robotsDestroyedText;

    [Tooltip("Shows the total time survived, formatted as mm:ss.")]
    public TextMeshProUGUI timeSurvivedText;

    [Header("Buttons")]
    [Tooltip("Reloads the current scene.")]
    public Button playAgainButton;

    [Tooltip("Loads the main menu (scene index 0).")]
    public Button returnToMenuButton;

    [Header("Dependencies")]
    [Tooltip("CarDeathSequencer on the MonsterTruck — wired at runtime via CarHealthSystem.")]
    public CarDeathSequencer carDeathSequencer;
    public EnemyPool enemyPool;

    // ── Private state ─────────────────────────────────────────────────────────
    private float _gameStartTime;
    private bool  _tracking;
    private float _deathTime;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);
    }

    private void OnEnable()
    {
        if (carDeathSequencer != null)
            carDeathSequencer.OnSequenceComplete += HandleSequenceComplete;

        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnGameStarted += HandleGameStarted;
            GameStateManager.Instance.OnPlayerDied  += HandlePlayerDied;
        }
    }

    private void OnDisable()
    {
        // carDeathSequencer may already be destroyed — guard the unsubscribe.
        if (carDeathSequencer != null)
            carDeathSequencer.OnSequenceComplete -= HandleSequenceComplete;

        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnGameStarted -= HandleGameStarted;
            GameStateManager.Instance.OnPlayerDied  -= HandlePlayerDied;
        }
    }

    private void Start()
    {
        if (playAgainButton    != null) playAgainButton.onClick.AddListener(OnPlayAgain);
        if (returnToMenuButton != null) returnToMenuButton.onClick.AddListener(OnReturnToMenu);
    }

    // ── Event handlers ─────────────────────────────────────────────────────────

    private void HandleGameStarted()
    {
        _gameStartTime = Time.time;
        _tracking = true;
    }

    private void HandlePlayerDied()
    {
        if (_tracking)
        {
            _deathTime = Time.time - _gameStartTime;
            _tracking  = false;
        }
    }

    /// <summary>
    /// Called by <see cref="CarDeathSequencer"/> once the truck explosion is done.
    /// Freezes all remaining gameplay via <see cref="Time.timeScale"/> then shows the panel.
    /// </summary>
    private void HandleSequenceComplete()
    {
        // Freeze the world — enemies, projectiles, and any lingering physics stop cold.
        Time.timeScale = 0f;

        PopulateStats(_deathTime);

        if (gameOverPanel != null)
            gameOverPanel.SetActive(true);
    }

    // ── Stat population ───────────────────────────────────────────────────────

    private void PopulateStats(float survivedSeconds)
    {
        if (robotsDestroyedText != null && enemyPool != null)
            robotsDestroyedText.text = enemyPool.TotalEnemiesKilled.ToString();

        if (timeSurvivedText != null)
        {
            int minutes = Mathf.FloorToInt(survivedSeconds / 60f);
            int seconds = Mathf.FloorToInt(survivedSeconds % 60f);
            timeSurvivedText.text = $"{minutes:00}:{seconds:00}";
        }
    }

    // ── Button callbacks ──────────────────────────────────────────────────────

    /// <summary>Reloads the active scene, resetting all state.</summary>
    private void OnPlayAgain()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    /// <summary>Loads scene index 0 (main menu).</summary>
    private void OnReturnToMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(0);
    }
}
