using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Drives the main menu: Play loads the next scene, Credits slides a panel in/out,
/// and Exit quits the application.
/// </summary>
public class MenuController : MonoBehaviour
{
    private const int GameSceneIndex = 1;

    [Header("Buttons")]
    public Button playButton;
    public Button creditsButton;
    public Button exitButton;

    [Header("Credits Panel")]
    [Tooltip("Root panel (the dim overlay). Hidden by default.")]
    public GameObject creditsPanel;

    [Tooltip("The card inside the credits panel that slides in/out.")]
    public RectTransform creditsCard;

    [Header("Animation")]
    [Tooltip("Duration of the slide animation in seconds.")]
    public float slideDuration = 0.45f;

    // How far off-screen to the left the card starts/ends (in canvas pixels).
    // Calculated at runtime so it always clears the screen edge.
    private float _offscreenX;

    // Resting (centre) position.
    private Vector2 _restPosition;

    private bool _isAnimating;
    private bool _isOpen;

    // Guard: ignore input on the exact frame the panel finishes opening
    // so the same click/key that triggered Open doesn't immediately Close.
    private bool _inputBlocked;

    private void Awake()
    {
        if (creditsCard != null)
        {
            _restPosition = creditsCard.anchoredPosition;

            // Off-screen: card width/2 + half canvas width pushes it fully left of screen
            Canvas canvas = GetComponentInParent<Canvas>();
            RectTransform canvasRect = canvas != null ? canvas.GetComponent<RectTransform>() : null;
            float canvasHalfWidth = canvasRect != null ? canvasRect.rect.width * 0.5f : 1920f * 0.5f;
            _offscreenX = -(canvasHalfWidth + creditsCard.rect.width * 0.5f + 60f);

            // Start card off-screen left, panel disabled
            creditsCard.anchoredPosition = new Vector2(_offscreenX, _restPosition.y);
        }

        if (creditsPanel != null)
            creditsPanel.SetActive(false);

        playButton?.onClick.AddListener(OnPlay);
        creditsButton?.onClick.AddListener(OnCredits);
        exitButton?.onClick.AddListener(OnExit);
    }

    private void Update()
    {
        if (!_isOpen || _isAnimating || _inputBlocked) return;

        if (Input.anyKeyDown)
            StartCoroutine(CloseCredits());
    }

    /// <summary>Loads the main game scene.</summary>
    public void OnPlay()
    {
        SceneManager.LoadScene(GameSceneIndex);
    }

    /// <summary>Slides the credits panel in from the left, or closes it back out.</summary>
    public void OnCredits()
    {
        if (creditsPanel == null || creditsCard == null || _isAnimating) return;

        if (!_isOpen)
            StartCoroutine(OpenCredits());
        else
            StartCoroutine(CloseCredits());
    }

    /// <summary>Quits the application. No-op on WebGL where quitting is not supported.</summary>
    public void OnExit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#elif !UNITY_WEBGL
        Application.Quit();
#endif
    }

    // ── Coroutines ────────────────────────────────────────────────────────────

    private IEnumerator OpenCredits()
    {
        _isAnimating = true;
        _isOpen = true;
        _inputBlocked = true;

        // Activate and ensure card is off-screen left before fading in
        creditsPanel.SetActive(true);
        creditsCard.anchoredPosition = new Vector2(_offscreenX, _restPosition.y);

        // Fade the overlay in while the card slides
        Image overlay = creditsPanel.GetComponent<Image>();
        float targetAlpha = overlay != null ? 0.72f : 0f;

        float elapsed = 0f;
        while (elapsed < slideDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / slideDuration);
            float eased = EaseOutExpo(t);

            creditsCard.anchoredPosition = new Vector2(
                Mathf.LerpUnclamped(_offscreenX, _restPosition.x, eased),
                _restPosition.y
            );

            if (overlay != null)
            {
                Color c = overlay.color;
                c.a = Mathf.Lerp(0f, targetAlpha, eased);
                overlay.color = c;
            }

            yield return null;
        }

        creditsCard.anchoredPosition = _restPosition;
        _isAnimating = false;

        // Wait one frame so the keydown/mousedown that triggered Open is gone
        yield return null;
        _inputBlocked = false;
    }

    private IEnumerator CloseCredits()
    {
        _isAnimating = true;

        Image overlay = creditsPanel.GetComponent<Image>();
        float startAlpha = overlay != null ? overlay.color.a : 0f;

        float elapsed = 0f;
        while (elapsed < slideDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / slideDuration);
            float eased = EaseInExpo(t);

            creditsCard.anchoredPosition = new Vector2(
                Mathf.LerpUnclamped(_restPosition.x, _offscreenX, eased),
                _restPosition.y
            );

            if (overlay != null)
            {
                Color c = overlay.color;
                c.a = Mathf.Lerp(startAlpha, 0f, eased);
                overlay.color = c;
            }

            yield return null;
        }

        creditsPanel.SetActive(false);
        creditsCard.anchoredPosition = new Vector2(_offscreenX, _restPosition.y);
        _isOpen = false;
        _isAnimating = false;
    }

    // ── Easing functions ──────────────────────────────────────────────────────

    /// <summary>Decelerating exponential — fast start, smooth settle.</summary>
    private static float EaseOutExpo(float t)
    {
        return t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t);
    }

    /// <summary>Accelerating exponential — slow start, snappy exit.</summary>
    private static float EaseInExpo(float t)
    {
        return t <= 0f ? 0f : Mathf.Pow(2f, 10f * t - 10f);
    }
}
