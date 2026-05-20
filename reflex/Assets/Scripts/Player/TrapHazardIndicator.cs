using UnityEngine;
using UnityEngine.Tilemaps;

[ExecuteAlways]
public class TrapHazardIndicator : MonoBehaviour
{
    [Header("Glow")]
    public Color warningColor = new Color(1f, 0.35f, 0.04f, 1f);
    public Color emissionColor = new Color(1f, 0.12f, 0.02f, 1f);
    public float emissionIntensity = 4f;
    [Range(0f, 1f)] public float minimumPulse = 0.45f;
    public float pulseSpeed = 2f;

    [Header("Targets")]
    public bool tintOnlyNamedGlowParts = true;
    public string glowNameFilter = "glass";
    [Range(0f, 1f)] public float floorTintStrength = 0.65f;
    public bool includeInactiveChildren = true;

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private readonly MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
    private Renderer[] targetRenderers = new Renderer[0];
    private Tilemap[] tilemaps = new Tilemap[0];

    private void OnEnable()
    {
        CacheTargets();
        ApplyIndicator(1f);
    }

    private void OnValidate()
    {
        emissionIntensity = Mathf.Max(0f, emissionIntensity);
        pulseSpeed = Mathf.Max(0f, pulseSpeed);
        floorTintStrength = Mathf.Clamp01(floorTintStrength);
        CacheTargets();
        ApplyIndicator(1f);
    }

    private void Update()
    {
        float pulse = pulseSpeed > 0f
            ? Mathf.Lerp(minimumPulse, 1f, (Mathf.Sin(Time.realtimeSinceStartup * pulseSpeed * Mathf.PI * 2f) + 1f) * 0.5f)
            : 1f;

        ApplyIndicator(pulse);
    }

    private void CacheTargets()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(includeInactiveChildren);

        if (!tintOnlyNamedGlowParts || string.IsNullOrWhiteSpace(glowNameFilter))
        {
            targetRenderers = renderers;
        }
        else
        {
            targetRenderers = System.Array.FindAll(renderers, RendererMatchesGlowFilter);
            if (targetRenderers.Length == 0)
            {
                targetRenderers = renderers;
            }
        }

        tilemaps = GetComponentsInChildren<Tilemap>(includeInactiveChildren);
    }

    private bool RendererMatchesGlowFilter(Renderer renderer)
    {
        return renderer.name.IndexOf(glowNameFilter, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void ApplyIndicator(float pulse)
    {
        Color surfaceColor = Color.Lerp(Color.white, warningColor, pulse);
        Color emission = emissionColor * (emissionIntensity * pulse);

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            Renderer targetRenderer = targetRenderers[i];
            if (targetRenderer == null)
            {
                continue;
            }

            targetRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(ColorId, surfaceColor);
            propertyBlock.SetColor(BaseColorId, surfaceColor);
            propertyBlock.SetColor(EmissionColorId, emission);
            targetRenderer.SetPropertyBlock(propertyBlock);
        }

        Color floorColor = Color.Lerp(Color.white, warningColor, floorTintStrength * pulse);
        for (int i = 0; i < tilemaps.Length; i++)
        {
            if (tilemaps[i] != null)
            {
                tilemaps[i].color = floorColor;
            }
        }
    }
}
