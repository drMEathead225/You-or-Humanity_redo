using UnityEngine;

/// <summary>
/// Put on a child collider of a zombie (for example a small sphere at the head) to give that
/// body part a damage multiplier. Shots that hit it do more damage and show a gold hit marker.
/// The Health component stays on the zombie's root, and Weapon finds it automatically.
/// </summary>
[RequireComponent(typeof(Collider))]
public class HitZone : MonoBehaviour
{
    [SerializeField] private float damageMultiplier = 2.5f;
    [Tooltip("Headshots trigger the gold hit marker. Untick for other zones such as legs (use a multiplier below 1).")]
    [SerializeField] private bool isHeadshot = true;

    public float DamageMultiplier => damageMultiplier;
    public bool IsHeadshot => isHeadshot;
}
