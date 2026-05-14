using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerOcclusionFader : MonoBehaviour
{
    private const string SilhouetteResourceName = "Player Occlusion Silhouette";
    private const string SilhouetteShaderName = "Hidden/Reflex/PlayerOcclusionSilhouette";
    private const string SilhouetteObjectPrefix = "OcclusionSilhouette";
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");

    [Header("Line Of Sight")]
    [SerializeField] private Camera occlusionCamera;
    [SerializeField] private Vector3 fallbackViewTargetOffset = new Vector3(0f, 1f, 0f);
    [SerializeField, Min(0f)] private float lineOfSightPadding = 0.18f;
    [SerializeField, Range(0.1f, 1f)] private float playerWidthSampleScale = 0.75f;
    [SerializeField, Range(0.1f, 1f)] private float playerHeightSampleScale = 0.35f;
    [SerializeField, Min(0f)] private float playerBackPadding = 0.15f;
    [SerializeField, Min(0.01f)] private float scanInterval = 0.08f;

    [Header("Silhouette")]
    [SerializeField] private Material silhouetteMaterial;
    [SerializeField] private Color silhouetteFillColor = new Color(0.02f, 0.025f, 0.035f, 0.62f);
    [SerializeField] private Color silhouetteOutlineColor = new Color(1f, 0.74f, 0.22f, 0.95f);
    [SerializeField, Min(1f)] private float outlineScale = 1.12f;
    [SerializeField, Min(0.1f)] private float silhouetteFadeSpeed = 7f;
    [SerializeField] private int silhouetteSortingOrderOffset = 250;
    [SerializeField, Min(0.05f)] private float sourceRefreshInterval = 0.5f;

    [Header("Filtering")]
    [SerializeField] private LayerMask occluderLayers = ~0;
    [SerializeField] private bool useDefaultLayerExclusions = true;

    private readonly List<SilhouetteSprite> silhouetteSprites = new List<SilhouetteSprite>();
    private readonly HashSet<SpriteRenderer> knownSilhouetteSources = new HashSet<SpriteRenderer>();
    private readonly List<Collider> occluderColliders = new List<Collider>();
    private readonly Plane[] cameraFrustumPlanes = new Plane[6];
    private readonly Vector3[] playerTargetPoints = new Vector3[5];

    private CharacterController playerController;
    private Material runtimeSilhouetteMaterial;
    private MaterialPropertyBlock fillPropertyBlock;
    private MaterialPropertyBlock outlinePropertyBlock;
    private bool ownsRuntimeSilhouetteMaterial;
    private bool playerOccluded;
    private float silhouetteAmount;
    private float nextScanTime;
    private float nextSourceRefreshTime;
    private int playerLayer = -1;
    private int enemyLayer = -1;
    private int terrainLayer = -1;
    private int uiLayer = -1;
    private int ignoreRaycastLayer = -1;
    private int dashingPlayerLayer = -1;

    private void Awake()
    {
        playerController = GetComponent<CharacterController>();
        fillPropertyBlock = new MaterialPropertyBlock();
        outlinePropertyBlock = new MaterialPropertyBlock();

        playerLayer = LayerMask.NameToLayer("Player");
        enemyLayer = LayerMask.NameToLayer("Enemy");
        terrainLayer = LayerMask.NameToLayer("Terrain");
        uiLayer = LayerMask.NameToLayer("UI");
        ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
        dashingPlayerLayer = LayerMask.NameToLayer("DashingPlayer");

        ResolveSilhouetteMaterial();
    }

    private void Start()
    {
        RefreshSilhouetteSources();
    }

    private void OnDisable()
    {
        playerOccluded = false;
        silhouetteAmount = 0f;
        UpdateSilhouetteVisuals();
    }

    private void OnDestroy()
    {
        for (int i = 0; i < silhouetteSprites.Count; i++)
        {
            silhouetteSprites[i].Destroy();
        }

        silhouetteSprites.Clear();
        knownSilhouetteSources.Clear();

        if (ownsRuntimeSilhouetteMaterial && runtimeSilhouetteMaterial != null)
        {
            if (Application.isPlaying)
            {
                Destroy(runtimeSilhouetteMaterial);
            }
            else
            {
                DestroyImmediate(runtimeSilhouetteMaterial);
            }
        }
    }

    private void Update()
    {
        if (Time.time >= nextScanTime)
        {
            nextScanTime = Time.time + scanInterval;
            playerOccluded = IsPlayerOccluded();
        }

        float targetAmount = playerOccluded ? 1f : 0f;
        silhouetteAmount = Mathf.MoveTowards(silhouetteAmount, targetAmount, silhouetteFadeSpeed * Time.deltaTime);
    }

    private void LateUpdate()
    {
        if (Time.time >= nextSourceRefreshTime)
        {
            nextSourceRefreshTime = Time.time + sourceRefreshInterval;
            RefreshSilhouetteSources();
        }

        UpdateSilhouetteVisuals();
    }

    private bool IsPlayerOccluded()
    {
        Camera activeCamera = ResolveCamera();
        if (activeCamera == null)
        {
            return false;
        }

        BuildPlayerTargetPoints(activeCamera);
        GeometryUtility.CalculateFrustumPlanes(activeCamera, cameraFrustumPlanes);
        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);

        foreach (Renderer renderer in renderers)
        {
            if (!IsEligibleOccluder(renderer) ||
                !GeometryUtility.TestPlanesAABB(cameraFrustumPlanes, renderer.bounds) ||
                !RendererBlocksPlayerLineOfSight(renderer, activeCamera))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private Camera ResolveCamera()
    {
        if (occlusionCamera != null && occlusionCamera.isActiveAndEnabled)
        {
            return occlusionCamera;
        }

        occlusionCamera = Camera.main;
        return occlusionCamera;
    }

    private void BuildPlayerTargetPoints(Camera activeCamera)
    {
        Vector3 center = playerController != null
            ? transform.TransformPoint(playerController.center)
            : transform.TransformPoint(fallbackViewTargetOffset);

        float halfWidth = playerController != null
            ? playerController.radius * playerWidthSampleScale
            : 0.35f * playerWidthSampleScale;

        float halfHeight = playerController != null
            ? playerController.height * playerHeightSampleScale
            : 1f * playerHeightSampleScale;

        Vector3 cameraRight = activeCamera.transform.right * halfWidth;
        Vector3 worldUp = Vector3.up * halfHeight;

        playerTargetPoints[0] = center;
        playerTargetPoints[1] = center + cameraRight;
        playerTargetPoints[2] = center - cameraRight;
        playerTargetPoints[3] = center + worldUp;
        playerTargetPoints[4] = center - worldUp;
    }

    private bool IsEligibleOccluder(Renderer renderer)
    {
        if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
        {
            return false;
        }

        if (renderer is SpriteRenderer || (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)))
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

    private bool RendererBlocksPlayerLineOfSight(Renderer renderer, Camera activeCamera)
    {
        Bounds paddedBounds = renderer.bounds;
        paddedBounds.Expand(lineOfSightPadding * 2f);
        bool hasPreciseCollider = TryCollectBlockingColliders(renderer);

        Vector3 cameraPosition = activeCamera.transform.position;

        for (int i = 0; i < playerTargetPoints.Length; i++)
        {
            Vector3 toPlayer = playerTargetPoints[i] - cameraPosition;
            float playerDistance = toPlayer.magnitude;

            if (playerDistance <= Mathf.Epsilon)
            {
                continue;
            }

            Ray cameraRay = new Ray(cameraPosition, toPlayer / playerDistance);
            float maxDistance = playerDistance - playerBackPadding;
            if (maxDistance <= 0f ||
                !paddedBounds.IntersectRay(cameraRay, out float hitDistance) ||
                hitDistance >= maxDistance)
            {
                continue;
            }

            if (!hasPreciseCollider || CollidersBlockRay(cameraRay, maxDistance))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryCollectBlockingColliders(Renderer renderer)
    {
        occluderColliders.Clear();
        renderer.GetComponents(occluderColliders);

        for (int i = occluderColliders.Count - 1; i >= 0; i--)
        {
            Collider occluderCollider = occluderColliders[i];
            if (occluderCollider == null ||
                !occluderCollider.enabled ||
                !occluderCollider.gameObject.activeInHierarchy)
            {
                occluderColliders.RemoveAt(i);
            }
        }

        return occluderColliders.Count > 0;
    }

    private bool CollidersBlockRay(Ray ray, float maxDistance)
    {
        for (int i = 0; i < occluderColliders.Count; i++)
        {
            if (occluderColliders[i].Raycast(ray, out RaycastHit hit, maxDistance))
            {
                return true;
            }
        }

        return false;
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

    private void ResolveSilhouetteMaterial()
    {
        runtimeSilhouetteMaterial = silhouetteMaterial != null
            ? silhouetteMaterial
            : Resources.Load<Material>(SilhouetteResourceName);

        if (runtimeSilhouetteMaterial != null)
        {
            return;
        }

        Shader silhouetteShader = Shader.Find(SilhouetteShaderName);
        if (silhouetteShader == null)
        {
            silhouetteShader = Shader.Find("Sprites/Default");
        }

        if (silhouetteShader == null)
        {
            Debug.LogWarning($"{nameof(PlayerOcclusionFader)} could not find a silhouette shader.");
            return;
        }

        runtimeSilhouetteMaterial = new Material(silhouetteShader)
        {
            name = "Runtime Player Occlusion Silhouette",
            hideFlags = HideFlags.DontSave
        };
        ownsRuntimeSilhouetteMaterial = true;
    }

    private void RefreshSilhouetteSources()
    {
        RemoveMissingSilhouetteSources();

        if (runtimeSilhouetteMaterial == null)
        {
            ResolveSilhouetteMaterial();
            if (runtimeSilhouetteMaterial == null)
            {
                return;
            }
        }

        SpriteRenderer[] sources = GetComponentsInChildren<SpriteRenderer>(true);
        foreach (SpriteRenderer source in sources)
        {
            if (!IsEligibleSilhouetteSource(source) || knownSilhouetteSources.Contains(source))
            {
                continue;
            }

            silhouetteSprites.Add(new SilhouetteSprite(source, runtimeSilhouetteMaterial));
            knownSilhouetteSources.Add(source);
        }
    }

    private void RemoveMissingSilhouetteSources()
    {
        for (int i = silhouetteSprites.Count - 1; i >= 0; i--)
        {
            SilhouetteSprite silhouetteSprite = silhouetteSprites[i];
            if (silhouetteSprite.HasSource)
            {
                continue;
            }

            knownSilhouetteSources.Remove(silhouetteSprite.Source);
            silhouetteSprite.Destroy();
            silhouetteSprites.RemoveAt(i);
        }
    }

    private bool IsEligibleSilhouetteSource(SpriteRenderer source)
    {
        if (source == null || source.transform == transform || IsSilhouetteProxy(source.transform))
        {
            return false;
        }

        if (NameContains(source.transform, "Hitbox"))
        {
            return false;
        }

        return source.transform.IsChildOf(transform);
    }

    private bool IsSilhouetteProxy(Transform candidate)
    {
        for (Transform current = candidate; current != null && current != transform; current = current.parent)
        {
            if (current.name.StartsWith(SilhouetteObjectPrefix))
            {
                return true;
            }
        }

        return false;
    }

    private bool NameContains(Transform candidate, string text)
    {
        for (Transform current = candidate; current != null && current != transform; current = current.parent)
        {
            if (current.name.IndexOf(text, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private void UpdateSilhouetteVisuals()
    {
        Color fillColor = silhouetteFillColor;
        Color outlineColor = silhouetteOutlineColor;
        fillColor.a *= silhouetteAmount;
        outlineColor.a *= silhouetteAmount;
        fillPropertyBlock.SetColor(ColorProperty, fillColor);
        outlinePropertyBlock.SetColor(ColorProperty, outlineColor);

        for (int i = 0; i < silhouetteSprites.Count; i++)
        {
            silhouetteSprites[i].Sync(
                silhouetteAmount,
                outlineScale,
                silhouetteSortingOrderOffset,
                fillPropertyBlock,
                outlinePropertyBlock);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Camera activeCamera = occlusionCamera != null ? occlusionCamera : Camera.main;
        if (activeCamera == null)
        {
            return;
        }

        BuildPlayerTargetPoints(activeCamera);
        Gizmos.color = new Color(0.25f, 0.8f, 1f, 0.35f);

        for (int i = 0; i < playerTargetPoints.Length; i++)
        {
            Gizmos.DrawLine(activeCamera.transform.position, playerTargetPoints[i]);
        }
    }

    private sealed class SilhouetteSprite
    {
        private readonly SpriteRenderer source;
        private readonly GameObject root;
        private readonly SpriteRenderer outlineRenderer;
        private readonly SpriteRenderer fillRenderer;

        public SpriteRenderer Source => source;
        public bool HasSource => source != null;

        public SilhouetteSprite(SpriteRenderer source, Material material)
        {
            this.source = source;

            root = new GameObject($"{SilhouetteObjectPrefix} ({source.name})")
            {
                hideFlags = HideFlags.DontSave
            };
            root.transform.SetParent(source.transform, false);

            outlineRenderer = CreateRenderer("OcclusionSilhouette Outline", root.transform, material);
            fillRenderer = CreateRenderer("OcclusionSilhouette Fill", root.transform, material);
            root.SetActive(false);
        }

        public void Sync(
            float amount,
            float outlineScale,
            int sortingOrderOffset,
            MaterialPropertyBlock fillPropertyBlock,
            MaterialPropertyBlock outlinePropertyBlock)
        {
            if (source == null)
            {
                return;
            }

            bool visible = amount > 0.001f &&
                           source.enabled &&
                           source.gameObject.activeInHierarchy &&
                           source.sprite != null;

            root.SetActive(visible);
            if (!visible)
            {
                return;
            }

            CopySpriteState(source, outlineRenderer, sortingOrderOffset, outlinePropertyBlock);
            CopySpriteState(source, fillRenderer, sortingOrderOffset + 1, fillPropertyBlock);
            outlineRenderer.transform.localScale = Vector3.one * outlineScale;
            fillRenderer.transform.localScale = Vector3.one;
        }

        public void Destroy()
        {
            if (root == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(root);
            }
            else
            {
                Object.DestroyImmediate(root);
            }
        }

        private static SpriteRenderer CreateRenderer(string name, Transform parent, Material material)
        {
            GameObject rendererObject = new GameObject(name)
            {
                hideFlags = HideFlags.DontSave
            };
            rendererObject.transform.SetParent(parent, false);

            SpriteRenderer renderer = rendererObject.AddComponent<SpriteRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        private static void CopySpriteState(
            SpriteRenderer source,
            SpriteRenderer target,
            int sortingOrderOffset,
            MaterialPropertyBlock propertyBlock)
        {
            target.sprite = source.sprite;
            target.flipX = source.flipX;
            target.flipY = source.flipY;
            target.drawMode = source.drawMode;
            target.size = source.size;
            target.tileMode = source.tileMode;
            target.maskInteraction = SpriteMaskInteraction.None;
            target.sortingLayerID = source.sortingLayerID;
            target.sortingOrder = source.sortingOrder + sortingOrderOffset;
            target.color = new Color(1f, 1f, 1f, source.color.a);
            target.SetPropertyBlock(propertyBlock);
        }
    }
}
