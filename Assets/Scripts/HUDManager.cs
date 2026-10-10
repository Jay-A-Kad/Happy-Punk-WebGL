using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives all Canvas HUD elements:
/// - CarHealth slider with animated fill and color gradient (magenta → yellow → red).
/// - WaveClearedText: shown briefly when a wave is cleared.
/// - IncomingWaveRoot / IncomingWave_Number: shown before each new wave spawns.
/// - FuelCore count text: increments on each pickup.
///
/// Subscribes to <see cref="GameStateManager"/> and <see cref="WaveManager"/> events.
/// </summary>
public class HUDManager : MonoBehaviour
{
    // ── Timings ────────────────────────────────────────────────────────────────
    private const float WAVE_CLEAR_DISPLAY_SECONDS    = 2.5f;
    private const float INCOMING_WAVE_DISPLAY_SECONDS = 2.5f;
    private const float HEALTH_ANIMATE_SPEED          = 4f;

    // ── Health gradient stops ──────────────────────────────────────────────────
    private static readonly Color HEALTH_COLOR_FULL = new(1.0f, 0.0f,   0.431f);
    private static readonly Color HEALTH_COLOR_MID  = new(1.0f, 0.902f, 0.0f);
    private static readonly Color HEALTH_COLOR_LOW  = new(1.0f, 0.06f,  0.06f);

    // ── Inspector references ───────────────────────────────────────────────────

    [Header("Health")]
    [Tooltip("The CarHealth slider in the Canvas.")]
    public Slider carHealthSlider;

    [Tooltip("The fill Image of the CarHealth slider for color transitions.")]
    public Image carHealthFillImage;

    [Header("Wave Cleared")]
    [Tooltip("WaveTxt TextMeshProUGUI shown when a wave is defeated.")]
    public TextMeshProUGUI waveClearedText;

    [Tooltip("WaveBG Image shown behind the wave cleared text.")]
    public GameObject waveBG;

    [Tooltip("WaveBorder Image shown around the wave cleared text.")]
    public GameObject waveBorder;

    [Header("Incoming Wave")]
    [Tooltip("Root GameObject containing the incoming wave announcement UI (includes BG and border).")]
    public GameObject incomingWaveRoot;

    [Tooltip("IncomingBG Image shown behind the incoming wave text.")]
    public GameObject incomingBG;

    [Tooltip("IncomingBorder Image shown around the incoming wave text.")]
    public GameObject incomingBorder;

    [Tooltip("IncomingWave_Number TextMeshProUGUI showing the current wave index.")]
    public TextMeshProUGUI incomingWaveNumberText;

    [Header("Fuel Core")]
    [Tooltip("FuelCore TextMeshProUGUI displaying the collected count.")]
    public TextMeshProUGUI fuelCoreText;

    [Header("Dependencies")]
    public WaveManager waveManager;
    public CarHealthSystem carHealthSystem;

    // ── Private state ─────────────────────────────────────────────────────────
    private int _fuelCoreCount;
    private float _targetHealthValue = 1f;

    private Coroutine _waveClearRoutine;
    private Coroutine _incomingWaveRoutine;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        _targetHealthValue = 1f;
        if (carHealthSlider != null) carHealthSlider.value = 1f;
        UpdateHealthColor(1f);

        SetFuelCoreCount(0);

        // Hide all wave message elements on startup.
        SetWaveClearedVisible(false);
        SetIncomingWaveVisible(false);
    }

    private void Start()
    {
        if (waveManager != null)
        {
            waveManager.OnWaveCleared      += HandleWaveCleared;
            waveManager.OnAllWavesComplete += HandleAllWavesComplete;
        }

        if (carHealthSystem != null)
        {
            carHealthSystem.OnHealthChanged += HandleHealthChanged;
        }

        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnIncomingWave += HandleIncomingWave;
        }
    }

    private void OnDestroy()
    {
        if (waveManager != null)
        {
            waveManager.OnWaveCleared      -= HandleWaveCleared;
            waveManager.OnAllWavesComplete -= HandleAllWavesComplete;
        }

        if (carHealthSystem != null)
        {
            carHealthSystem.OnHealthChanged -= HandleHealthChanged;
        }

        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnIncomingWave -= HandleIncomingWave;
        }
    }

    private void Update()
    {
        if (carHealthSlider != null && !Mathf.Approximately(carHealthSlider.value, _targetHealthValue))
        {
            carHealthSlider.value = Mathf.Lerp(
                carHealthSlider.value,
                _targetHealthValue,
                Time.deltaTime * HEALTH_ANIMATE_SPEED
            );
            UpdateHealthColor(carHealthSlider.value);
        }
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Call this when the player collects a FuelCore pickup.</summary>
    public void AddFuelCore()
    {
        SetFuelCoreCount(_fuelCoreCount + 1);
    }

    /// <summary>Current number of collected fuel cores.</summary>
    public int FuelCoreCount => _fuelCoreCount;

    /// <summary>
    /// Deducts <paramref name="amount"/> fuel cores and updates the HUD.
    /// Returns true if the deduction succeeded (enough cores were available).
    /// </summary>
    public bool ConsumeFuelCores(int amount)
    {
        if (_fuelCoreCount < amount) return false;
        SetFuelCoreCount(_fuelCoreCount - amount);
        return true;
    }

    // ── Event handlers ─────────────────────────────────────────────────────────

    private void HandleHealthChanged(float normalizedHealth)
    {
        _targetHealthValue = normalizedHealth;
    }

    private void HandleWaveCleared(int waveNumber)
    {
        ShowWaveCleared();
    }

    private void HandleIncomingWave(int waveNumber)
    {
        ShowIncomingWave(waveNumber);
    }

    private void HandleAllWavesComplete()
    {
        if (waveClearedText != null)
        {
            waveClearedText.text = "// ALL WAVES DOWN //";
        }
        SetWaveClearedVisible(true);
    }

    // ── UI helpers ─────────────────────────────────────────────────────────────

    private void ShowWaveCleared()
    {
        if (waveClearedText == null) return;
        if (_waveClearRoutine != null) StopCoroutine(_waveClearRoutine);
        _waveClearRoutine = StartCoroutine(FlashWaveCleared(WAVE_CLEAR_DISPLAY_SECONDS));
    }

    private void ShowIncomingWave(int waveNumber)
    {
        if (incomingWaveRoot == null) return;
        if (incomingWaveNumberText != null)
            incomingWaveNumberText.text = waveNumber.ToString();

        if (_incomingWaveRoutine != null) StopCoroutine(_incomingWaveRoutine);
        _incomingWaveRoutine = StartCoroutine(FlashIncomingWave(INCOMING_WAVE_DISPLAY_SECONDS));
    }

    private IEnumerator FlashWaveCleared(float duration)
    {
        SetWaveClearedVisible(true);
        yield return new WaitForSeconds(duration);
        SetWaveClearedVisible(false);
    }

    private IEnumerator FlashIncomingWave(float duration)
    {
        SetIncomingWaveVisible(true);
        yield return new WaitForSeconds(duration);
        SetIncomingWaveVisible(false);
    }

    /// <summary>Toggles all wave-cleared UI elements (text + BG + border) together.</summary>
    private void SetWaveClearedVisible(bool visible)
    {
        if (waveClearedText != null) waveClearedText.gameObject.SetActive(visible);
        if (waveBG          != null) waveBG.SetActive(visible);
        if (waveBorder      != null) waveBorder.SetActive(visible);
    }

    /// <summary>Toggles all incoming-wave UI elements (text + BG + border) together.</summary>
    private void SetIncomingWaveVisible(bool visible)
    {
        if (incomingWaveRoot != null) incomingWaveRoot.SetActive(visible);
        if (incomingBG       != null) incomingBG.SetActive(visible);
        if (incomingBorder   != null) incomingBorder.SetActive(visible);
    }

    /// <summary>
    /// Shows <paramref name="target"/> for <paramref name="duration"/> seconds then hides it.
    /// </summary>
    private IEnumerator FlashText(GameObject target, float duration)
    {
        target.SetActive(true);
        yield return new WaitForSeconds(duration);
        target.SetActive(false);
    }

    private void SetFuelCoreCount(int count)
    {
        _fuelCoreCount = count;
        if (fuelCoreText != null)
            fuelCoreText.text = $"FUEL  x{_fuelCoreCount}";
    }

    /// <summary>
    /// Evaluates the health color gradient. Magenta above 60%, yellow in the mid range,
    /// red below 30%. Applies directly to the fill image.
    /// </summary>
    private void UpdateHealthColor(float normalizedHealth)
    {
        if (carHealthFillImage == null) return;

        Color color;
        if (normalizedHealth > 0.6f)
        {
            float t = Mathf.InverseLerp(1f, 0.6f, normalizedHealth);
            color = Color.Lerp(HEALTH_COLOR_FULL, HEALTH_COLOR_MID, t);
        }
        else
        {
            float t = Mathf.InverseLerp(0.6f, 0f, normalizedHealth);
            color = Color.Lerp(HEALTH_COLOR_MID, HEALTH_COLOR_LOW, t);
        }

        carHealthFillImage.color = color;
    }
}
