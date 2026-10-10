using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Drives the opening sequence:
///   1. Activates <see cref="introCamera"/> and deactivates the main camera.
///   2. Pans the intro camera from the truck's tire level up to the roof over
///      <see cref="panDuration"/> seconds using a smooth curve.
///   3. Cuts back to the main camera and notifies <see cref="GameStateManager"/>.
///   4. Runs the 3 → 2 → 1 → "LET'S ROCK!" countdown on screen.
///   5. Fires <see cref="GameStateManager.NotifyGameStarted"/> so gameplay begins.
/// </summary>
public class IntroSequencer : MonoBehaviour
{
    // ── Inspector ────────────────────────────────────────────────────────────────

    [Header("Cameras")]
    [Tooltip("The intro camera that performs the pan shot.")]
    public Camera introCamera;

    [Tooltip("The main gameplay camera that takes over after the intro.")]
    public Camera mainCamera;

    [Header("Pan Targets")]
    [Tooltip("World-space point at tire/ground level to start the pan.")]
    public Transform panStart;

    [Tooltip("World-space point at roof level to end the pan.")]
    public Transform panEnd;

    [Header("Timing")]
    [Tooltip("Seconds taken to pan from start to end.")]
    public float panDuration = 2.5f;

    [Tooltip("Seconds to hold at the end of the pan before cutting to gameplay camera.")]
    public float holdAfterPan = 0.4f;

    [Tooltip("Seconds between each countdown beat (3 → 2 → 1).")]
    public float countdownInterval = 1f;

    [Tooltip("Seconds to display 'LET'S ROCK!' before gameplay begins.")]
    public float letsRockDuration = 1f;

    [Tooltip("Seconds to wait after countdown before the first wave spawns.")]
    public float delayBeforeFirstWave = 0.3f;

    [Header("Countdown UI")]
    [Tooltip("TextMeshPro element that shows the countdown number / text.")]
    public TextMeshProUGUI countdownText;

    // ── Private ──────────────────────────────────────────────────────────────────

    private void Start()
    {
        // Begin with only the intro camera active.
        if (introCamera != null) introCamera.gameObject.SetActive(true);
        if (mainCamera  != null) mainCamera.gameObject.SetActive(false);
        if (countdownText != null) countdownText.gameObject.SetActive(false);

        StartCoroutine(RunIntroSequence());
    }

    // ── Sequence ─────────────────────────────────────────────────────────────────

    private IEnumerator RunIntroSequence()
    {
        yield return StartCoroutine(PanCamera());
        yield return new WaitForSeconds(holdAfterPan);

        SwitchToMainCamera();
        GameStateManager.Instance?.NotifyIntroComplete();

        yield return StartCoroutine(RunCountdown());
        GameStateManager.Instance?.NotifyGameStarted();
    }

    /// <summary>
    /// Smoothly moves the intro camera from <see cref="panStart"/> to <see cref="panEnd"/>
    /// over <see cref="panDuration"/> seconds. The camera always looks toward the
    /// midpoint of both targets so the truck stays in frame throughout.
    /// </summary>
    private IEnumerator PanCamera()
    {
        if (introCamera == null || panStart == null || panEnd == null)
        {
            yield break;
        }

        float elapsed = 0f;

        while (elapsed < panDuration)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / panDuration);

            Vector3 pos = Vector3.Lerp(panStart.position, panEnd.position, t);
            introCamera.transform.position = pos;

            // Look at the midpoint between both targets so the truck stays framed.
            Vector3 lookTarget = Vector3.Lerp(panStart.position, panEnd.position, 0.5f);
            introCamera.transform.LookAt(lookTarget);

            elapsed += Time.deltaTime;
            yield return null;
        }

        // Snap to end.
        introCamera.transform.position = panEnd.position;
        introCamera.transform.LookAt(Vector3.Lerp(panStart.position, panEnd.position, 0.5f));
    }

    private void SwitchToMainCamera()
    {
        // Disable the Camera component only — deactivating the whole GameObject
        // would kill this coroutine because IntroSequencer lives on the same object.
        if (introCamera != null) introCamera.enabled = false;
        if (mainCamera  != null) mainCamera.gameObject.SetActive(true);
    }

    /// <summary>
    /// Shows 3, 2, 1, "LET'S ROCK!" on <see cref="countdownText"/>.
    /// </summary>
    private IEnumerator RunCountdown()
    {
        if (countdownText == null) yield break;

        countdownText.gameObject.SetActive(true);

        string[] beats = { "3", "2", "1" };
        foreach (string beat in beats)
        {
            countdownText.text = beat;
            yield return new WaitForSeconds(countdownInterval);
        }

        countdownText.text = "LET'S ROCK!";
        yield return new WaitForSeconds(letsRockDuration);

        countdownText.gameObject.SetActive(false);
        yield return new WaitForSeconds(delayBeforeFirstWave);
    }
}
