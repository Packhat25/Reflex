using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class TemporaryGameOverUI : MonoBehaviour
{
    private static TemporaryGameOverUI _instance;
    private const string DefaultLobbySceneName = "Lobby";

    [Header("Navigation")]
    [SerializeField] private string lobbySceneName = DefaultLobbySceneName;
    [SerializeField] private bool generateFreshRunOnReturn = true;

    private PlayerManager _observedPlayer;
    private TemporaryGameOverCanvasView _authoredCanvasView;
    private CanvasGroup _canvasGroup;
    private TextMeshProUGUI _detailsText;
    private Button _returnToLobbyButton;
    private bool _isShowing;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (FindFirstObjectByType<TemporaryGameOverUI>() != null)
        {
            return;
        }

        GameObject managerObject = new GameObject("TemporaryGameOverUI");
        managerObject.AddComponent<TemporaryGameOverUI>();
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
        ResolveCanvasBindings();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        TryBindPlayer();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        UnbindPlayer();
    }

    private void Update()
    {
        if (_observedPlayer == null)
        {
            TryBindPlayer();
        }
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (_isShowing)
        {
            return;
        }

        ResolveCanvasBindings();
        TryBindPlayer();
    }

    private void TryBindPlayer()
    {
        PlayerManager player = FindFirstObjectByType<PlayerManager>();
        if (player == null || _observedPlayer == player)
        {
            return;
        }

        UnbindPlayer();
        _observedPlayer = player;
        _observedPlayer.PlayerDied += HandlePlayerDied;
    }

    private void UnbindPlayer()
    {
        if (_observedPlayer == null)
        {
            return;
        }

        _observedPlayer.PlayerDied -= HandlePlayerDied;
        _observedPlayer = null;
    }

    private void HandlePlayerDied(PlayerManager deadPlayer)
    {
        ShowGameOver(deadPlayer);
    }

    private void ShowGameOver(PlayerManager deadPlayer)
    {
        if (_isShowing)
        {
            return;
        }

        ResolveCanvasBindings();
        if (_canvasGroup == null || _detailsText == null)
        {
            return;
        }

        RunRewardSummary summary = BuildFallbackSummary();
        if (RewardManager.Instance != null &&
            RewardManager.Instance.TryGetRunRewardSummary(out RunRewardSummary runSummary))
        {
            summary = runSummary;
        }

        int otherEssence = Mathf.Max(0, summary.totalEssenceEarned - summary.stageRewardEssence - summary.composureBonusEssence);
        _detailsText.text =
            "GAME OVER\n\n" +
            $"Runtime: {FormatRuntime(summary.runtimeSeconds)}\n" +
            $"Floor Cleared: {summary.floorReached}\n" +
            $"Stage Cleared: {summary.stageReached}\n" +
            $"Enemies Killed: {summary.enemiesDefeated}\n\n" +
            $"Total Soul Essence Earned: {summary.totalEssenceEarned}\n" +
            "Calculation:\n" +
            $"- Kill Essence: {summary.enemiesDefeated} x {summary.essencePerKill} = {summary.rawKillEssence}\n" +
            $"- Base Clear Essence: {summary.rawBaseEssence}\n" +
            $"- Floor Depth Essence: {summary.rawFloorEssence}\n" +
            $"- Raw Subtotal: {summary.rawEssenceBeforeMultipliers}\n" +
            $"- Effective Run Multiplier: x{summary.effectiveCombinedMultiplier:0.00}\n" +
            $"- Stage Reward Total: {summary.stageRewardEssence}\n" +
            $"- Composure Bonus: {summary.composureBonusEssence}\n" +
            $"- Other Bonus Sources: {otherEssence}";

        _canvasGroup.alpha = 1f;
        _canvasGroup.interactable = true;
        _canvasGroup.blocksRaycasts = true;
        if (_returnToLobbyButton != null)
        {
            _returnToLobbyButton.interactable = true;
        }

        _isShowing = true;
        Time.timeScale = 0f;
    }

    private RunRewardSummary BuildFallbackSummary()
    {
        RunRewardSummary fallback = new RunRewardSummary
        {
            runtimeSeconds = 0f,
            floorReached = 0,
            stageReached = 0,
            stagesCleared = 0,
            enemiesDefeated = 0,
            essencePerKill = 0,
            totalEssenceEarned = 0,
            stageRewardEssence = 0,
            composureBonusEssence = 0,
            rawBaseEssence = 0,
            rawKillEssence = 0,
            rawFloorEssence = 0,
            rawEssenceBeforeMultipliers = 0,
            effectiveCombinedMultiplier = 1f
        };

        if (LevelRunManager.HasInstance)
        {
            LevelClearContext context = LevelRunManager.Instance.LastClearContext;
            if (context.floorDepth > 0)
            {
                int stagesPerFloor = Mathf.Max(1, LevelRunManager.Instance.StagesPerFloor);
                fallback.floorReached = ((context.floorDepth - 1) / stagesPerFloor) + 1;
                fallback.stageReached = ((context.floorDepth - 1) % stagesPerFloor) + 1;
            }
        }

        return fallback;
    }

    private string FormatRuntime(float runtimeSeconds)
    {
        TimeSpan runTime = TimeSpan.FromSeconds(Mathf.Max(0f, runtimeSeconds));
        return runTime.TotalHours >= 1
            ? runTime.ToString(@"hh\:mm\:ss")
            : runTime.ToString(@"mm\:ss");
    }

    private void ResolveCanvasBindings()
    {
        if (TryBindAuthoredCanvas())
        {
            return;
        }

        BuildRuntimeUIIfNeeded();
    }

    private bool TryBindAuthoredCanvas()
    {
        if (_authoredCanvasView != null)
        {
            if (_authoredCanvasView.TryGetBindings(out CanvasGroup group, out TextMeshProUGUI details, out Button returnButton))
            {
                _canvasGroup = group;
                _detailsText = details;
                BindReturnToLobbyButton(returnButton);
                _authoredCanvasView.HideImmediate();
                return true;
            }
        }

        TemporaryGameOverCanvasView discoveredView = FindFirstObjectByType<TemporaryGameOverCanvasView>(FindObjectsInactive.Include);
        if (discoveredView == null)
        {
            return false;
        }

        if (!discoveredView.TryGetBindings(out CanvasGroup discoveredGroup, out TextMeshProUGUI discoveredDetails, out Button discoveredReturnButton))
        {
            Debug.LogWarning("TemporaryGameOverCanvasView found but missing CanvasGroup or details text reference.");
            return false;
        }

        _authoredCanvasView = discoveredView;
        _canvasGroup = discoveredGroup;
        _detailsText = discoveredDetails;
        BindReturnToLobbyButton(discoveredReturnButton);
        _authoredCanvasView.HideImmediate();
        return true;
    }

    private void BuildRuntimeUIIfNeeded()
    {
        if (_canvasGroup != null && _detailsText != null && _returnToLobbyButton != null)
        {
            return;
        }

        GameObject canvasObject = new GameObject(
            "Temporary Game Over Canvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(CanvasGroup));

        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 2000;
        canvas.pixelPerfect = false;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        _canvasGroup = canvasObject.GetComponent<CanvasGroup>();
        _canvasGroup.alpha = 0f;
        _canvasGroup.interactable = false;
        _canvasGroup.blocksRaycasts = false;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.anchorMin = Vector2.zero;
        canvasRect.anchorMax = Vector2.one;
        canvasRect.offsetMin = Vector2.zero;
        canvasRect.offsetMax = Vector2.zero;

        CreateImage("Dim", canvasRect, new Color(0f, 0f, 0f, 0.86f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Vector2.one * 0.5f);
        RectTransform panel = CreateImage("Panel", canvasRect, new Color(0.08f, 0.08f, 0.1f, 0.98f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000f, 760f), Vector2.one * 0.5f);

        _detailsText = CreateText(
            "Details",
            panel,
            "GAME OVER",
            34f,
            FontStyles.Bold,
            new Color(0.98f, 0.98f, 0.98f, 1f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            new Vector2(920f, 690f),
            new Vector2(0.5f, 0.5f),
            TextAlignmentOptions.TopLeft);

        RectTransform buttonRect = CreateImage(
            "Return To Lobby Button",
            panel,
            new Color(0.2f, 0.28f, 0.45f, 1f),
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0f, 58f),
            new Vector2(360f, 72f),
            new Vector2(0.5f, 0.5f));

        Button button = buttonRect.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.92f, 0.95f, 1f, 1f);
        colors.pressedColor = new Color(0.8f, 0.85f, 1f, 1f);
        button.colors = colors;

        CreateText(
            "Label",
            buttonRect,
            "Return To Lobby",
            30f,
            FontStyles.Bold,
            new Color(0.95f, 0.98f, 1f, 1f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            new Vector2(340f, 56f),
            new Vector2(0.5f, 0.5f),
            TextAlignmentOptions.Center);

        BindReturnToLobbyButton(button);
    }

    private void BindReturnToLobbyButton(Button button)
    {
        if (_returnToLobbyButton != null)
        {
            _returnToLobbyButton.onClick.RemoveListener(HandleReturnToLobbyPressed);
        }

        _returnToLobbyButton = button;
        if (_returnToLobbyButton == null)
        {
            return;
        }

        _returnToLobbyButton.onClick.RemoveListener(HandleReturnToLobbyPressed);
        _returnToLobbyButton.onClick.AddListener(HandleReturnToLobbyPressed);
    }

    private void HandleReturnToLobbyPressed()
    {
        Time.timeScale = 1f;
        _isShowing = false;

        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
        }

        PlayerManager player = _observedPlayer != null ? _observedPlayer : FindFirstObjectByType<PlayerManager>();
        if (player != null)
        {
            player.RespawnForRunStart();
        }

        if (generateFreshRunOnReturn && LevelRunManager.HasInstance)
        {
            LevelRunManager.Instance.GenerateNewRun();
        }

        string targetScene = string.IsNullOrWhiteSpace(lobbySceneName) ? DefaultLobbySceneName : lobbySceneName;
        if (!TemporaryLoadingUI.LoadSceneWithOverlay(targetScene, LoadSceneMode.Single))
        {
            SceneManager.LoadScene(targetScene, LoadSceneMode.Single);
        }
    }

    private RectTransform CreateImage(
        string name,
        RectTransform parent,
        Color color,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        Vector2 sizeDelta,
        Vector2 pivot)
    {
        GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;

        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        return rect;
    }

    private TextMeshProUGUI CreateText(
        string name,
        RectTransform parent,
        string value,
        float fontSize,
        FontStyles fontStyle,
        Color color,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        Vector2 sizeDelta,
        Vector2 pivot,
        TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.font = TMP_Settings.defaultFontAsset;
        return text;
    }
}

[DisallowMultipleComponent]
public class TemporaryGameOverCanvasView : MonoBehaviour
{
    [Header("Required References")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TextMeshProUGUI detailsText;
    [SerializeField] private Button returnToLobbyButton;

    [Header("Runtime Behavior")]
    [SerializeField] private bool hideOnAwake = true;

    private void Awake()
    {
        if (hideOnAwake)
        {
            HideImmediate();
        }
    }

    public bool TryGetBindings(out CanvasGroup group, out TextMeshProUGUI details, out Button returnButton)
    {
        group = canvasGroup;
        details = detailsText;
        returnButton = returnToLobbyButton;
        return group != null && details != null;
    }

    public void HideImmediate()
    {
        if (canvasGroup == null)
        {
            return;
        }

        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    private void Reset()
    {
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }

        if (detailsText == null)
        {
            detailsText = GetComponentInChildren<TextMeshProUGUI>(true);
        }

        if (returnToLobbyButton == null)
        {
            returnToLobbyButton = GetComponentInChildren<Button>(true);
        }
    }
}

[DefaultExecutionOrder(-950)]
public class TemporaryLoadingUI : MonoBehaviour
{
    private static TemporaryLoadingUI _instance;

    [Header("Startup")]
    [SerializeField] private bool showStartupShaderWarmup = true;

    [Header("Runtime Labels")]
    [SerializeField] private string loadingTitle = "LOADING";
    [SerializeField] private string loadingAssetsLabel = "Loading assets...";
    [SerializeField] private string compilingShadersLabel = "Compiling shaders...";
    [SerializeField] private string finalizingLabel = "Finalizing...";

    private TemporaryLoadingCanvasView _authoredCanvasView;
    private CanvasGroup _canvasGroup;
    private TextMeshProUGUI _titleText;
    private TextMeshProUGUI _statusText;
    private Image _progressFill;
    private Coroutine _activeRoutine;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (FindFirstObjectByType<TemporaryLoadingUI>() != null)
        {
            return;
        }

        GameObject managerObject = new GameObject("TemporaryLoadingUI");
        managerObject.AddComponent<TemporaryLoadingUI>();
    }

    public static bool LoadSceneWithOverlay(string sceneName, LoadSceneMode mode = LoadSceneMode.Single)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogError($"{nameof(TemporaryLoadingUI)} cannot load an empty scene name.");
            return false;
        }

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"{nameof(TemporaryLoadingUI)} cannot load scene '{sceneName}'. Add it to Build Settings.");
            return false;
        }

        TemporaryLoadingUI loader = EnsureInstance();
        return loader != null && loader.TryStartSceneLoad(sceneName, mode);
    }

    private static TemporaryLoadingUI EnsureInstance()
    {
        if (_instance != null)
        {
            return _instance;
        }

        _instance = FindFirstObjectByType<TemporaryLoadingUI>();
        if (_instance != null)
        {
            return _instance;
        }

        GameObject managerObject = new GameObject("TemporaryLoadingUI");
        _instance = managerObject.AddComponent<TemporaryLoadingUI>();
        return _instance;
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
        ResolveCanvasBindings();
        HideImmediate();
    }

    private void Start()
    {
        if (showStartupShaderWarmup)
        {
            TryStartRoutine(RunStartupShaderWarmup());
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (_activeRoutine != null)
        {
            return;
        }

        HideImmediate();
    }

    private bool TryStartSceneLoad(string sceneName, LoadSceneMode mode)
    {
        if (_activeRoutine != null)
        {
            Debug.LogWarning($"{nameof(TemporaryLoadingUI)} ignored scene load to '{sceneName}' because another loading routine is already active.");
            return false;
        }

        return TryStartRoutine(RunSceneLoad(sceneName, mode));
    }

    private bool TryStartRoutine(IEnumerator routine)
    {
        if (routine == null || _activeRoutine != null)
        {
            return false;
        }

        _activeRoutine = StartCoroutine(RunTrackedRoutine(routine));
        return true;
    }

    private IEnumerator RunTrackedRoutine(IEnumerator routine)
    {
        yield return routine;
        _activeRoutine = null;
    }

    private IEnumerator RunStartupShaderWarmup()
    {
        ResolveCanvasBindings();
        SetOverlayState(true);
        UpdateOverlayText(loadingTitle, compilingShadersLabel);
        SetProgress(0.15f);
        yield return null;

        Shader.WarmupAllShaders();

        UpdateOverlayText(loadingTitle, finalizingLabel);
        SetProgress(1f);
        yield return null;
        HideImmediate();
    }

    private IEnumerator RunSceneLoad(string sceneName, LoadSceneMode mode)
    {
        ResolveCanvasBindings();
        SetOverlayState(true);
        UpdateOverlayText(loadingTitle, loadingAssetsLabel);
        SetProgress(0f);
        yield return null;

        AsyncOperation loadOperation = SceneManager.LoadSceneAsync(sceneName, mode);
        if (loadOperation == null)
        {
            HideImmediate();
            yield break;
        }

        loadOperation.allowSceneActivation = false;

        while (loadOperation.progress < 0.9f)
        {
            float normalizedLoad = Mathf.Clamp01(loadOperation.progress / 0.9f);
            SetProgress(Mathf.Lerp(0f, 0.8f, normalizedLoad));
            yield return null;
        }

        UpdateOverlayText(loadingTitle, compilingShadersLabel);
        SetProgress(0.86f);
        yield return null;

        Shader.WarmupAllShaders();

        UpdateOverlayText(loadingTitle, finalizingLabel);
        SetProgress(0.94f);
        loadOperation.allowSceneActivation = true;

        while (!loadOperation.isDone)
        {
            SetProgress(Mathf.Clamp01(Mathf.Max(0.94f, loadOperation.progress)));
            yield return null;
        }

        SetProgress(1f);
        yield return null;
        HideImmediate();
    }

    private void ResolveCanvasBindings()
    {
        if (TryBindAuthoredCanvas())
        {
            return;
        }

        BuildRuntimeUIIfNeeded();
    }

    private bool TryBindAuthoredCanvas()
    {
        if (_authoredCanvasView != null &&
            _authoredCanvasView.TryGetBindings(out CanvasGroup group, out TextMeshProUGUI title, out TextMeshProUGUI status, out Image progress))
        {
            _canvasGroup = group;
            _titleText = title;
            _statusText = status;
            _progressFill = progress;
            _authoredCanvasView.HideImmediate();
            return true;
        }

        TemporaryLoadingCanvasView discovered = FindFirstObjectByType<TemporaryLoadingCanvasView>(FindObjectsInactive.Include);
        if (discovered == null)
        {
            return false;
        }

        if (!discovered.TryGetBindings(out CanvasGroup discoveredGroup, out TextMeshProUGUI discoveredTitle, out TextMeshProUGUI discoveredStatus, out Image discoveredProgress))
        {
            Debug.LogWarning("TemporaryLoadingCanvasView found but missing required references.");
            return false;
        }

        _authoredCanvasView = discovered;
        _canvasGroup = discoveredGroup;
        _titleText = discoveredTitle;
        _statusText = discoveredStatus;
        _progressFill = discoveredProgress;
        _authoredCanvasView.HideImmediate();
        return true;
    }

    private void BuildRuntimeUIIfNeeded()
    {
        if (_canvasGroup != null && _titleText != null && _statusText != null && _progressFill != null)
        {
            return;
        }

        GameObject canvasObject = new GameObject(
            "Temporary Loading Canvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(CanvasGroup));

        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 2100;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        _canvasGroup = canvasObject.GetComponent<CanvasGroup>();
        _canvasGroup.alpha = 0f;
        _canvasGroup.interactable = false;
        _canvasGroup.blocksRaycasts = false;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.anchorMin = Vector2.zero;
        canvasRect.anchorMax = Vector2.one;
        canvasRect.offsetMin = Vector2.zero;
        canvasRect.offsetMax = Vector2.zero;

        CreateImage("Dim", canvasRect, new Color(0f, 0f, 0f, 0.86f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Vector2.one * 0.5f);
        RectTransform panel = CreateImage("Panel", canvasRect, new Color(0.08f, 0.08f, 0.1f, 0.98f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 360f), new Vector2(0.5f, 0.5f));

        _titleText = CreateText(
            "Title",
            panel,
            loadingTitle,
            54f,
            FontStyles.Bold,
            new Color(0.97f, 0.98f, 1f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0f, -90f),
            new Vector2(780f, 90f),
            new Vector2(0.5f, 0.5f),
            TextAlignmentOptions.Center);

        _statusText = CreateText(
            "Status",
            panel,
            loadingAssetsLabel,
            32f,
            FontStyles.Normal,
            new Color(0.9f, 0.94f, 1f, 1f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0f, -6f),
            new Vector2(780f, 74f),
            new Vector2(0.5f, 0.5f),
            TextAlignmentOptions.Center);

        RectTransform trackRect = CreateImage(
            "Progress Track",
            panel,
            new Color(0.16f, 0.2f, 0.28f, 1f),
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0f, 62f),
            new Vector2(780f, 26f),
            new Vector2(0.5f, 0.5f));

        RectTransform fillRect = CreateImage(
            "Progress Fill",
            trackRect,
            new Color(0.32f, 0.53f, 0.95f, 1f),
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero,
            new Vector2(0f, 0.5f));

        _progressFill = fillRect.GetComponent<Image>();
        _progressFill.type = Image.Type.Filled;
        _progressFill.fillMethod = Image.FillMethod.Horizontal;
        _progressFill.fillOrigin = 0;
        _progressFill.fillAmount = 0f;
    }

    private void SetOverlayState(bool visible)
    {
        if (_canvasGroup == null)
        {
            return;
        }

        _canvasGroup.alpha = visible ? 1f : 0f;
        _canvasGroup.interactable = visible;
        _canvasGroup.blocksRaycasts = visible;
    }

    private void HideImmediate()
    {
        SetOverlayState(false);
        SetProgress(0f);
    }

    private void UpdateOverlayText(string title, string status)
    {
        if (_titleText != null)
        {
            _titleText.text = string.IsNullOrWhiteSpace(title) ? loadingTitle : title;
        }

        if (_statusText != null)
        {
            _statusText.text = status;
        }
    }

    private void SetProgress(float value)
    {
        if (_progressFill != null)
        {
            _progressFill.fillAmount = Mathf.Clamp01(value);
        }
    }

    private RectTransform CreateImage(
        string name,
        RectTransform parent,
        Color color,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        Vector2 sizeDelta,
        Vector2 pivot)
    {
        GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;

        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        return rect;
    }

    private TextMeshProUGUI CreateText(
        string name,
        RectTransform parent,
        string value,
        float fontSize,
        FontStyles fontStyle,
        Color color,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        Vector2 sizeDelta,
        Vector2 pivot,
        TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.font = TMP_Settings.defaultFontAsset;
        return text;
    }
}

[DisallowMultipleComponent]
public class TemporaryLoadingCanvasView : MonoBehaviour
{
    [Header("Required References")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private Image progressFillImage;

    [Header("Runtime Behavior")]
    [SerializeField] private bool hideOnAwake = true;

    private void Awake()
    {
        if (hideOnAwake)
        {
            HideImmediate();
        }
    }

    public bool TryGetBindings(out CanvasGroup group, out TextMeshProUGUI title, out TextMeshProUGUI status, out Image progressFill)
    {
        group = canvasGroup;
        title = titleText;
        status = statusText;
        progressFill = progressFillImage;
        return group != null && title != null && status != null && progressFill != null;
    }

    public void HideImmediate()
    {
        if (canvasGroup == null)
        {
            return;
        }

        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    private void Reset()
    {
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }

        if (titleText == null || statusText == null)
        {
            TextMeshProUGUI[] texts = GetComponentsInChildren<TextMeshProUGUI>(true);
            if (texts.Length > 0 && titleText == null)
            {
                titleText = texts[0];
            }

            if (texts.Length > 1 && statusText == null)
            {
                statusText = texts[1];
            }
        }

        if (progressFillImage == null)
        {
            progressFillImage = GetComponentInChildren<Image>(true);
        }
    }
}
