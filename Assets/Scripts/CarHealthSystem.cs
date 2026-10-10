using System;
using UnityEngine;

/// <summary>
/// Tracks the player car's health. Deals damage on rocket hit (via event subscription)
/// and exposes events for HUD and game-over logic.
/// Attach this to the same root GameObject as <see cref="CarController"/>.
/// </summary>
public class CarHealthSystem : MonoBehaviour
{
    private const float MAX_HEALTH = 100f;
    private const float ROCKET_DAMAGE = 25f;

    /// <summary>Raised every time health changes. Parameter is normalized 0-1 value.</summary>
    public event Action<float> OnHealthChanged;

    /// <summary>Raised once when health reaches zero.</summary>
    public event Action OnDied;

    private float _currentHealth;
    private bool _isDead;

    public float NormalizedHealth => _currentHealth / MAX_HEALTH;
    public bool IsDead => _isDead;

    private void Awake()
    {
        _currentHealth = MAX_HEALTH;
    }

    /// <summary>
    /// Apply damage to the car. Clamps to zero and fires death event once.
    /// </summary>
    public void TakeDamage(float amount)
    {
        if (_isDead) return;

        _currentHealth = Mathf.Max(0f, _currentHealth - amount);
        OnHealthChanged?.Invoke(NormalizedHealth);

        if (_currentHealth <= 0f)
        {
            _isDead = true;
            OnDied?.Invoke();
        }
    }

    /// <summary>
    /// Apply fixed rocket hit damage. Called by RocketProjectile on player impact.
    /// </summary>
    public void ApplyRocketDamage()
    {
        TakeDamage(ROCKET_DAMAGE);
    }

    /// <summary>
    /// Restore health by the given amount, capped at MAX_HEALTH.
    /// </summary>
    public void Heal(float amount)
    {
        if (_isDead) return;

        _currentHealth = Mathf.Min(MAX_HEALTH, _currentHealth + amount);
        OnHealthChanged?.Invoke(NormalizedHealth);
    }

    /// <summary>
    /// Forces an instant kill regardless of current health.
    /// Used by <see cref="FallDetector"/> when the car leaves the map.
    /// </summary>
    public void KillInstantly()
    {
        if (_isDead) return;
        _currentHealth = 0f;
        _isDead = true;
        OnHealthChanged?.Invoke(0f);
        OnDied?.Invoke();
    }
}
