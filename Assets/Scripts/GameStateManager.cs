using System;
using UnityEngine;

/// <summary>
/// Central game-state machine. Other systems subscribe to events and react
/// without depending on each other directly.
///
/// State flow:
///   Intro → Countdown → Playing → WaveCleared → BetweenWaves → Playing → … → AllWavesComplete
/// </summary>
public class GameStateManager : MonoBehaviour
{
    public static GameStateManager Instance { get; private set; }

    // ── Events ──────────────────────────────────────────────────────────────────

    /// <summary>Fired once the camera intro pan finishes.</summary>
    public event Action OnIntroComplete;

    /// <summary>Fired when the countdown finishes and the player can move.</summary>
    public event Action OnGameStarted;

    /// <summary>Fired when a wave is cleared. Parameter is the 1-based wave number.</summary>
    public event Action<int> OnWaveCleared;

    /// <summary>Fired when the next wave is about to begin. Parameter is the incoming wave number.</summary>
    public event Action<int> OnIncomingWave;

    /// <summary>Fired when all waves are complete.</summary>
    public event Action OnAllWavesComplete;

    /// <summary>Fired when the player's health reaches zero.</summary>
    public event Action OnPlayerDied;

    // ── State ───────────────────────────────────────────────────────────────────

    public enum State
    {
        Intro,
        Countdown,
        Playing,
        WaveCleared,
        BetweenWaves,
        AllWavesComplete,
        PlayerDead
    }

    public State CurrentState { get; private set; } = State.Intro;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    // ── State transition API (called by other systems) ──────────────────────────

    /// <summary>Called by <see cref="IntroSequencer"/> when the pan finishes.</summary>
    public void NotifyIntroComplete()
    {
        if (CurrentState != State.Intro) return;
        CurrentState = State.Countdown;
        OnIntroComplete?.Invoke();
    }

    /// <summary>Called by <see cref="IntroSequencer"/> after the countdown ends.</summary>
    public void NotifyGameStarted()
    {
        if (CurrentState != State.Countdown) return;
        CurrentState = State.Playing;
        OnGameStarted?.Invoke();
    }

    /// <summary>Called by <see cref="WaveManager"/> when a wave is defeated.</summary>
    public void NotifyWaveCleared(int waveNumber)
    {
        if (CurrentState != State.Playing) return;
        CurrentState = State.WaveCleared;
        OnWaveCleared?.Invoke(waveNumber);
    }

    /// <summary>Called by <see cref="WaveManager"/> during the between-wave delay.</summary>
    public void NotifyIncomingWave(int waveNumber)
    {
        if (CurrentState != State.WaveCleared && CurrentState != State.BetweenWaves) return;
        CurrentState = State.BetweenWaves;
        OnIncomingWave?.Invoke(waveNumber);
    }

    /// <summary>Called by <see cref="WaveManager"/> when the next wave's spawn begins.</summary>
    public void NotifyWaveStarted()
    {
        if (CurrentState != State.BetweenWaves && CurrentState != State.Countdown) return;
        CurrentState = State.Playing;
    }

    /// <summary>Called by <see cref="WaveManager"/> when all waves are done.</summary>
    public void NotifyAllWavesComplete()
    {
        CurrentState = State.AllWavesComplete;
        OnAllWavesComplete?.Invoke();
    }

    /// <summary>Called by <see cref="CarHealthSystem"/> (via subscriber) when the player dies.</summary>
    public void NotifyPlayerDied()
    {
        if (CurrentState == State.PlayerDead) return;
        CurrentState = State.PlayerDead;
        OnPlayerDied?.Invoke();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
