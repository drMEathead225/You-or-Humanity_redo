using System.Collections;
using UnityEngine;

/// <summary>
/// When the player dies, the camera rolls onto its side and drops toward the floor.
/// Add to the Main Camera. GameManager disables the controller on death, so nothing fights this.
/// </summary>
public class DeathCamera : MonoBehaviour
{
    [SerializeField] private float duration = 0.9f;
    [Tooltip("Sideways roll in degrees (direction is random).")]
    [SerializeField] private float rollAngle = 65f;
    [Tooltip("Extra downward pitch in degrees.")]
    [SerializeField] private float pitchDown = 20f;
    [Tooltip("How far the camera falls, in metres.")]
    [SerializeField] private float dropDistance = 1.1f;

    private Health playerHealth;

    private void Start()
    {
        if (GameManager.Instance != null)
            playerHealth = GameManager.Instance.PlayerHealth;

        if (playerHealth != null)
            playerHealth.OnDeath += HandleDeath;
    }

    private void OnDestroy()
    {
        if (playerHealth != null)
            playerHealth.OnDeath -= HandleDeath;
    }

    private void HandleDeath()
    {
        StartCoroutine(FallRoutine());
    }

    private IEnumerator FallRoutine()
    {
        Quaternion startRotation = transform.localRotation;
        Vector3 startPosition = transform.localPosition;

        float roll = rollAngle * (Random.value < 0.5f ? -1f : 1f);
        Quaternion endRotation = startRotation * Quaternion.Euler(pitchDown, 0f, roll);
        Vector3 endPosition = startPosition + Vector3.down * dropDistance;

        // Unscaled time: the game is in slow motion while the player dies.
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float eased = Mathf.SmoothStep(0f, 1f, t / duration);
            transform.localRotation = Quaternion.Slerp(startRotation, endRotation, eased);
            transform.localPosition = Vector3.Lerp(startPosition, endPosition, eased);
            yield return null;
        }

        transform.localRotation = endRotation;
        transform.localPosition = endPosition;
    }
}
