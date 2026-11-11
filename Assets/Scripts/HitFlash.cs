using UnityEngine;

/// <summary>
/// Flashes an object a solid colour for a split second when it takes damage.
/// Add to the zombie root (next to Health). Works with Built-in and URP materials
/// and does not create or change any materials.
/// </summary>
[RequireComponent(typeof(Health))]
public class HitFlash : MonoBehaviour
{
    [SerializeField] private Color flashColor = new Color(1f, 0.15f, 0.15f);
    [SerializeField] private float flashTime = 0.08f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP
    private static readonly int ColorId = Shader.PropertyToID("_Color");         // Built-in

    private Health health;
    private Renderer[] renderers;
    private MaterialPropertyBlock block;
    private float flashEndTime;
    private bool flashing;

    private void Awake()
    {
        health = GetComponent<Health>();
        renderers = GetComponentsInChildren<Renderer>();
        block = new MaterialPropertyBlock();
    }

    private void OnEnable()
    {
        health.OnDamaged += HandleDamaged;
        ClearFlash(); // pooled zombies must never come back still flashing
    }

    private void OnDisable()
    {
        health.OnDamaged -= HandleDamaged;
    }

    private void Update()
    {
        if (flashing && Time.time >= flashEndTime)
            ClearFlash();
    }

    private void HandleDamaged(float amount)
    {
        block.SetColor(BaseColorId, flashColor);
        block.SetColor(ColorId, flashColor);

        foreach (Renderer r in renderers)
            if (r != null) r.SetPropertyBlock(block);

        flashing = true;
        flashEndTime = Time.time + flashTime;
    }

    private void ClearFlash()
    {
        flashing = false;

        foreach (Renderer r in renderers)
            if (r != null) r.SetPropertyBlock(null);
    }
}
