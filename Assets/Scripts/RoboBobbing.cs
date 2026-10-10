using UnityEngine;

/// <summary>
/// Smoothly bobs the attached GameObject up and down using a sine wave.
/// Attach to each Robo root object in the Menu scene.
/// </summary>
public class RoboBobbing : MonoBehaviour
{
    [Header("Bobbing Settings")]
    [Tooltip("Vertical distance the robot travels from its rest position.")]
    public float amplitude = 0.18f;

    [Tooltip("Full cycles per second (higher = faster bob).")]
    public float frequency = 0.8f;

    [Tooltip("Phase offset in radians so robots don't all bob in sync.")]
    public float phaseOffset = 0f;

    private Vector3 _originPosition;

    private void Start()
    {
        _originPosition = transform.position;
    }

    private void Update()
    {
        float yOffset = amplitude * Mathf.Sin((Time.time * frequency * Mathf.PI * 2f) + phaseOffset);
        transform.position = _originPosition + new Vector3(0f, yOffset, 0f);
    }
}
