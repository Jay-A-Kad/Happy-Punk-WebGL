using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tracks enemy kills and drives the Kill Meter UI slider.
/// After <see cref="KILLS_TO_FILL"/> kills the meter becomes full and the
/// fill image pulses with an orange glow until the counter resets.
///
/// Kill events come from <see cref="EnemyPool"/> via <see cref="OnEnemyKilled"/>.
/// Attach to the same HUD GameObject as <see cref="HUDManager"/>.
/// </summary>
public class KillMeterController : MonoBehaviour
{
    // ── Constants ───────────────────────────────────────────────────────────────

    /// <summary>Kills required to fill the meter.</summary>
    public const int KILLS_TO_FILL = 20;

    private const float FILL_ANIMATE_SPEED = 6f;

    // ── Pulse settings ──────────────────────────────────────────────────────────

    private static readonly Color PULSE_COLOR_BRIGHT = new(1.0f, 0.50f, 0.00f, 1f); // vivid orange
    private static readonly Color PULSE_COLOR_DIM    = new(0.60f, 0.20f, 0.00f, 1f); // deep burnt orange
    private const float PULSE_SPEED = 3.5f; // cycles per second

    // ── Normal fill gradient ────────────────────────────────────────────────────

    private static readonly Color FILL_COLOR_LOW  = new(1.0f, 0.07f, 0.55f, 1f); // magenta (matches health)
    private static readonly Color FILL_COLOR_MID  = new(1.0f, 0.88f, 0.00f, 1f); // yellow
    private static readonly Color FILL_COLOR_HIGH = new(1.0f, 0.50f, 0.00f, 1f); // orange

    // ── Inspector references ────────────────────────────────────────────────────

    [Header("Kill Meter UI")]
    [Tooltip("The Slider component acting as the kill meter.")]
    public Slider killMeterSlider;

    [Tooltip("Fill Image of the kill meter slider — driven for color and pulse.")]
    public Image killMeterFillImage;

    [Tooltip("Border/BG Image to tint when meter is full.")]
    public Image killMeterBorderImage;

    [Header("Dependencies")]
    [Tooltip("EnemyPool whose TotalEnemiesKilled increments on each kill.")]
    public EnemyPool enemyPool;

    // ── Private state ───────────────────────────────────────────────────────────

    private int   _killsAtLastReset;
    private float _targetSliderValue;
    private bool  _isFull;

    /// <summary>True when the meter has reached 20 kills and an ability is ready.</summary>
    public bool IsFull => _isFull;

    /// <summary>Current fill progress in [0, 1]. Usable for threshold checks (e.g. >= 0.25).</summary>
    public float FillFraction => _isFull ? 1f : _targetSliderValue;

    // ── Lifecycle ───────────────────────────────────────────────────────────────

    private void Awake()
    {
        _killsAtLastReset  = 0;
        _targetSliderValue = 0f;
        _isFull            = false;

        if (killMeterSlider != null)
            killMeterSlider.value = 0f;

        ApplyFillColor(0f);
    }

    private void Update()
    {
        RefreshKills();
        AnimateSlider();

        if (_isFull)
            PulseFill();
    }

    // ── Kill tracking ───────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the latest kill count from <see cref="EnemyPool"/> and updates the meter.
    /// </summary>
    private void RefreshKills()
    {
        if (enemyPool == null) return;

        int killsSinceReset = enemyPool.TotalEnemiesKilled - _killsAtLastReset;
        int clamped         = Mathf.Clamp(killsSinceReset, 0, KILLS_TO_FILL);

        _targetSliderValue = (float)clamped / KILLS_TO_FILL;

        if (!_isFull && clamped >= KILLS_TO_FILL)
        {
            _isFull = true;
            _killsAtLastReset = enemyPool.TotalEnemiesKilled;
            _targetSliderValue = 0f; // reset target so it drains back on next wave
        }
    }

    // ── Slider animation ────────────────────────────────────────────────────────

    private void AnimateSlider()
    {
        if (killMeterSlider == null) return;

        killMeterSlider.value = Mathf.Lerp(
            killMeterSlider.value,
            _isFull ? 1f : _targetSliderValue,
            Time.deltaTime * FILL_ANIMATE_SPEED
        );

        if (!_isFull)
            ApplyFillColor(killMeterSlider.value);
    }

    // ── Fill color gradient ─────────────────────────────────────────────────────

    /// <summary>Evaluates magenta → yellow → orange gradient across [0, 1].</summary>
    private void ApplyFillColor(float t)
    {
        if (killMeterFillImage == null) return;

        Color color;
        if (t < 0.5f)
            color = Color.Lerp(FILL_COLOR_LOW, FILL_COLOR_MID, t * 2f);
        else
            color = Color.Lerp(FILL_COLOR_MID, FILL_COLOR_HIGH, (t - 0.5f) * 2f);

        killMeterFillImage.color = color;
    }

    // ── Pulse effect ────────────────────────────────────────────────────────────

    /// <summary>
    /// Called every frame while the meter is full. Pulses the fill image between
    /// a vivid and a deep orange using a smooth sine wave.
    /// </summary>
    private void PulseFill()
    {
        if (killMeterFillImage == null) return;

        float t = (Mathf.Sin(Time.time * PULSE_SPEED * Mathf.PI * 2f) + 1f) * 0.5f;
        killMeterFillImage.color = Color.Lerp(PULSE_COLOR_DIM, PULSE_COLOR_BRIGHT, t);

        // After the meter has visually snapped to full, queue the drain reset
        if (Mathf.Approximately(killMeterSlider.value, 1f))
        {
            _targetSliderValue = 0f;
        }
    }

    /// <summary>
    /// Manually notifies the controller that an enemy was killed.
    /// Call this from <see cref="HUDManager"/> or directly subscribe to pool events.
    /// </summary>
    public void OnEnemyKilled()
    {
        // RefreshKills() polls the pool directly each Update(), so nothing extra needed.
        // This method exists as a hook for any future event-driven integration.
    }

    /// <summary>
    /// Called by <see cref="KillMeterAbilities"/> when the player spends the charge.
    /// Immediately drives the slider back to zero and unlocks the meter for the next
    /// 20-kill cycle.
    /// </summary>
    public void ConsumeCharge()
    {
        if (!_isFull) return;
        _isFull            = false;
        _targetSliderValue = 0f;
        // Snap the slider visually rather than waiting for lerp
        if (killMeterSlider != null) killMeterSlider.value = 0f;
        ApplyFillColor(0f);
    }

    /// <summary>
    /// Reduces the current fill by <paramref name="fraction"/> of the full meter (0–1).
    /// Works whether or not the meter is currently full: it advances the kill baseline
    /// forward so <see cref="RefreshKills"/> computes a lower value next frame.
    /// Clamps so the meter never goes below zero.
    /// </summary>
    public void ReduceFill(float fraction)
    {
        int killsToRemove = Mathf.RoundToInt(KILLS_TO_FILL * fraction);

        if (_isFull)
        {
            // Meter was full — exit full state and set the slider to (1 - fraction).
            _isFull = false;

            // Place the baseline so the remaining fill equals (1 - fraction).
            int remainingKills = Mathf.Max(0, KILLS_TO_FILL - killsToRemove);
            _killsAtLastReset  = enemyPool != null
                ? enemyPool.TotalEnemiesKilled - remainingKills
                : 0;

            _targetSliderValue = (float)remainingKills / KILLS_TO_FILL;

            if (killMeterSlider != null)
                killMeterSlider.value = _targetSliderValue;

            ApplyFillColor(_targetSliderValue);
        }
        else
        {
            // Shift the baseline forward — RefreshKills will recompute fill naturally.
            _killsAtLastReset += killsToRemove;

            // Clamp so we never report negative kills.
            if (enemyPool != null)
                _killsAtLastReset = Mathf.Min(_killsAtLastReset, enemyPool.TotalEnemiesKilled);
        }
    }
}
