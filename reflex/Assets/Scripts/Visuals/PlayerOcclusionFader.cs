using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class PlayerOcclusionFader : MonoBehaviour
{
    [Header("Reveal Area")]
    [SerializeField, Min(0.1f)] private float revealRadius = 2.25f;
    [SerializeField, Range(0.05f, 0.95f)] private float revealedAlpha = 0.28f;
    [SerializeField, Min(0.01f)] private float scanInterval = 0.08f;
    [SerializeField, Min(0.1f)] private float fadeSpeed = 5f;

    [Header("Filtering")]
    [SerializeField] private LayerMask occluderLayers = ~0;
    [SerializeField] private bool useDefaultLayerExclusions = true;
    [SerializeField] private float verticalPaddingBelowPlayer = 1.25f;
    [SerializeField] private float verticalPaddingAbovePlayer = 6f;

    private readonly Dictionary<Renderer, FadedRenderer> fadedRenderers = new Dictionary<Renderer, FadedRenderer>();
    private readonly HashSet<Renderer> currentRevealTargets = new HashSet<Renderer>();
    private readonly List<Renderer> removeBuffer = new List<Renderer>();

    private float nextScanTime;
    private int playerLayer = -1;
    private int enemyLayer = -1;
    private int terrainLayer = -1;
    private int uiLayer = -1;
    private int ignoreRaycastLayer = -1;
    private int dashingPlayerLayer = -1;

    private void Awake()
    {
        playerLayer = LayerMask.NameToLayer("Player");
        enemyLayer = LayerMask.NameToLayer("Enemy");
        terrainLayer = LayerMask.NameToLayer("Terrain");
        uiLayer = LayerMask.NameToLayer("UI");
        ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
        dashingPlayerLayer = LayerMask.NameToLayer("DashingPlayer");
    }

    private void OnDisable()
    {
        foreach (FadedRenderer fadedRenderer in fadedRenderers.Values)
        {
            fadedRenderer.Restore();
        }

        fadedRenderers.Clear();
        currentRevealTargets.Clear();
        removeBuffer.Clear();
    }

    private void Update()
    {
        if (Time.time >= nextScanTime)
        {
            nextScanTime = Time.time + scanInterval;
            RefreshRevealTargets();
        }

        UpdateFades();
    }

    private void RefreshRevealTargets()
    {
        currentRevealTargets.Clear();
        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);

        foreach (Renderer renderer in renderers)
        {
            if (!IsEligibleOccluder(renderer) || !BoundsOverlapRevealCircle(renderer.bounds))
            {
                continue;
            }

            currentRevealTargets.Add(renderer);
            if (!fadedRenderers.TryGetValue(renderer, out FadedRenderer fadedRenderer))
            {
                fadedRenderer = new FadedRenderer(renderer);
                fadedRenderers.Add(renderer, fadedRenderer);
            }

            fadedRenderer.TargetAlpha = revealedAlpha;
        }

        foreach (KeyValuePair<Renderer, FadedRenderer> entry in fadedRenderers)
        {
            if (!currentRevealTargets.Contains(entry.Key))
            {
                entry.Value.TargetAlpha = 1f;
            }
        }
    }

    private void UpdateFades()
    {
        removeBuffer.Clear();

        foreach (KeyValuePair<Renderer, FadedRenderer> entry in fadedRenderers)
        {
            Renderer renderer = entry.Key;
            FadedRenderer fadedRenderer = entry.Value;

            if (renderer == null)
            {
                fadedRenderer.Dispose();
                removeBuffer.Add(renderer);
                continue;
            }

            if (fadedRenderer.Step(Time.deltaTime, fadeSpeed))
            {
                removeBuffer.Add(renderer);
            }
        }

        foreach (Renderer renderer in removeBuffer)
        {
            fadedRenderers.Remove(renderer);
        }
    }

    private bool IsEligibleOccluder(Renderer renderer)
    {
        if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
        {
            return false;
        }

        if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
        {
            return false;
        }

        if (renderer.transform == transform || renderer.transform.IsChildOf(transform))
        {
            return false;
        }

        int layerMask = 1 << renderer.gameObject.layer;
        if ((occluderLayers.value & layerMask) == 0 || IsDefaultExcludedLayer(renderer.gameObject.layer))
        {
            return false;
        }

        return !HasExcludedParent(renderer.transform);
    }

    private bool IsDefaultExcludedLayer(int layer)
    {
        if (!useDefaultLayerExclusions)
        {
            return false;
        }

        return layer == playerLayer ||
               layer == enemyLayer ||
               layer == terrainLayer ||
               layer == uiLayer ||
               layer == ignoreRaycastLayer ||
               layer == dashingPlayerLayer;
    }

    private bool HasExcludedParent(Transform candidate)
    {
        for (Transform current = candidate; current != null; current = current.parent)
        {
            if (current == transform || current.CompareTag("Player") || current.CompareTag("Enemy"))
            {
                return true;
            }

            int layer = current.gameObject.layer;
            if (layer == playerLayer || layer == enemyLayer || layer == dashingPlayerLayer)
            {
                return true;
            }
        }

        return false;
    }

    private bool BoundsOverlapRevealCircle(Bounds bounds)
    {
        Vector3 playerPosition = transform.position;

        if (bounds.max.y < playerPosition.y - verticalPaddingBelowPlayer ||
            bounds.min.y > playerPosition.y + verticalPaddingAbovePlayer)
        {
            return false;
        }

        float closestX = Mathf.Clamp(playerPosition.x, bounds.min.x, bounds.max.x);
        float closestZ = Mathf.Clamp(playerPosition.z, bounds.min.z, bounds.max.z);
        float deltaX = playerPosition.x - closestX;
        float deltaZ = playerPosition.z - closestZ;

        return (deltaX * deltaX) + (deltaZ * deltaZ) <= revealRadius * revealRadius;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.25f, 0.8f, 1f, 0.35f);
        Vector3 center = transform.position;
        const int segments = 48;
        Vector3 previous = center + Vector3.forward * revealRadius;

        for (int i = 1; i <= segments; i++)
        {
            float angle = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 next = center + new Vector3(Mathf.Sin(angle) * revealRadius, 0f, Mathf.Cos(angle) * revealRadius);
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }

    private sealed class FadedRenderer
    {
        private readonly Renderer renderer;
        private readonly Material[] originalMaterials;
        private readonly ShadowCastingMode originalShadowCastingMode;
        private readonly bool originalReceiveShadows;

        private Material[] fadeMaterials;
        private MaterialFadeState[] materialStates;
        private bool fadeMaterialsAssigned;
        private float currentAlpha = 1f;

        public float TargetAlpha { get; set; } = 1f;

        public FadedRenderer(Renderer renderer)
        {
            this.renderer = renderer;
            originalMaterials = renderer.sharedMaterials;
            originalShadowCastingMode = renderer.shadowCastingMode;
            originalReceiveShadows = renderer.receiveShadows;
        }

        public bool Step(float deltaTime, float fadeSpeed)
        {
            currentAlpha = Mathf.MoveTowards(currentAlpha, TargetAlpha, fadeSpeed * deltaTime);

            if (currentAlpha < 0.999f)
            {
                EnsureFadeMaterials();
                ApplyAlpha(currentAlpha);
                return false;
            }

            if (TargetAlpha >= 0.999f)
            {
                Restore();
                return true;
            }

            return false;
        }

        public void Restore()
        {
            if (renderer != null)
            {
                renderer.sharedMaterials = originalMaterials;
                renderer.shadowCastingMode = originalShadowCastingMode;
                renderer.receiveShadows = originalReceiveShadows;
            }

            Dispose();
            currentAlpha = 1f;
            fadeMaterialsAssigned = false;
        }

        public void Dispose()
        {
            if (fadeMaterials == null)
            {
                return;
            }

            foreach (Material material in fadeMaterials)
            {
                if (material == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Object.Destroy(material);
                }
                else
                {
                    Object.DestroyImmediate(material);
                }
            }

            fadeMaterials = null;
            materialStates = null;
        }

        private void EnsureFadeMaterials()
        {
            if (fadeMaterials == null)
            {
                fadeMaterials = new Material[originalMaterials.Length];
                materialStates = new MaterialFadeState[originalMaterials.Length];

                for (int i = 0; i < originalMaterials.Length; i++)
                {
                    Material source = originalMaterials[i];
                    if (source == null)
                    {
                        continue;
                    }

                    Material fadeMaterial = new Material(source)
                    {
                        name = $"{source.name} (Player Reveal Fade)",
                        hideFlags = HideFlags.DontSave
                    };

                    fadeMaterials[i] = fadeMaterial;
                    materialStates[i] = new MaterialFadeState(fadeMaterial);
                    ConfigureTransparentMaterial(fadeMaterial);
                }
            }

            if (!fadeMaterialsAssigned && renderer != null)
            {
                renderer.sharedMaterials = fadeMaterials;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                fadeMaterialsAssigned = true;
            }
        }

        private void ApplyAlpha(float alpha)
        {
            if (fadeMaterials == null || materialStates == null)
            {
                return;
            }

            for (int i = 0; i < fadeMaterials.Length; i++)
            {
                if (fadeMaterials[i] == null)
                {
                    continue;
                }

                materialStates[i].ApplyAlpha(alpha);
            }
        }

        private static void ConfigureTransparentMaterial(Material material)
        {
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
            }

            if (material.HasProperty("_Blend"))
            {
                material.SetFloat("_Blend", 0f);
            }

            if (material.HasProperty("_SrcBlend"))
            {
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            }

            if (material.HasProperty("_DstBlend"))
            {
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            }

            if (material.HasProperty("_SrcBlendAlpha"))
            {
                material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            }

            if (material.HasProperty("_DstBlendAlpha"))
            {
                material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            }

            if (material.HasProperty("_ZWrite"))
            {
                material.SetFloat("_ZWrite", 0f);
            }

            if (material.HasProperty("_AlphaClip"))
            {
                material.SetFloat("_AlphaClip", 0f);
            }

            if (material.HasProperty("_Mode"))
            {
                material.SetFloat("_Mode", 2f);
            }

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
        }
    }

    private readonly struct MaterialFadeState
    {
        private readonly Material material;
        private readonly bool hasBaseColor;
        private readonly bool hasColor;
        private readonly Color originalBaseColor;
        private readonly Color originalColor;

        public MaterialFadeState(Material material)
        {
            this.material = material;
            hasBaseColor = material.HasProperty("_BaseColor");
            hasColor = material.HasProperty("_Color");
            originalBaseColor = hasBaseColor ? material.GetColor("_BaseColor") : Color.white;
            originalColor = hasColor ? material.GetColor("_Color") : Color.white;
        }

        public void ApplyAlpha(float alpha)
        {
            if (hasBaseColor)
            {
                Color color = originalBaseColor;
                color.a *= alpha;
                material.SetColor("_BaseColor", color);
            }

            if (hasColor)
            {
                Color color = originalColor;
                color.a *= alpha;
                material.SetColor("_Color", color);
            }
        }
    }
}
