using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class InGameUIManager : MonoBehaviour
{
    public static InGameUIManager Instance { get; private set; }

    private const string DefaultLobbySceneName = "Lobby";
    private const string GameOverCanvasName = "Game Over Canvas";
    private const string AuthoredTitlePath = "Main Box/Header";
    private const string AuthoredReturnToLobbyButtonPath = "Lobby btn";

    [Header("Health Bar References")]
    [SerializeField] private Image greenHPBarFill;
    [SerializeField] private Image redHPBarFill;
    [SerializeField] private TextMeshProUGUI hpText;

    [Header("Weapon Icon")]
    public Image weaponIcon;

    [Header("HP Bar Settings")]
    [SerializeField] private float redLerpSpeed = 5f; // Speed of the health bar animation
    [SerializeField] private float greenLerpSpeed = 5f; // Speed of the health bar animation

    [Header("Canvas References")]
    [SerializeField] private CanvasGroup inGameUICanvasGroup;
    [SerializeField] private CanvasGroup PauseUICanvasGroup;

    [Header("Game Over Screen")]
    [SerializeField] private string lobbySceneName = DefaultLobbySceneName;
    [SerializeField] private bool generateFreshRunOnReturn = true;
    [SerializeField] private RectTransform gameOverCanvasRoot;
    [SerializeField] private CanvasGroup gameOverCanvasGroup;
    [SerializeField] private TextMeshProUGUI gameOverDetailsText;
    [SerializeField] private Button returnToLobbyButton;
    [SerializeField] private TextMeshProUGUI gameOverTitleText;

    [Header("Game Over Summary Fields")]
    [SerializeField] private TextMeshProUGUI runtimeValueText;
    [SerializeField] private TextMeshProUGUI floorsClearedValueText;
    [SerializeField] private TextMeshProUGUI stagesClearedValueText;
    [SerializeField] private TextMeshProUGUI enemiesKilledValueText;
    [SerializeField] private TextMeshProUGUI killEssenceValueText;
    [SerializeField] private TextMeshProUGUI baseClearValueText;
    [SerializeField] private TextMeshProUGUI floorDepthValueText;
    [SerializeField] private TextMeshProUGUI rawSubtotalValueText;
    [SerializeField] private TextMeshProUGUI runMultiplierValueText;
    [SerializeField] private TextMeshProUGUI rewardTotalValueText;
    [SerializeField] private TextMeshProUGUI composureBonusValueText;
    [SerializeField] private TextMeshProUGUI otherBonusValueText;
    [SerializeField] private TextMeshProUGUI soulEssenceEarnedValueText;

    [Header("Status Messaging")]
    [SerializeField] private TextMeshProUGUI statusMessageText;
    [SerializeField] private CanvasGroup statusMessageCanvasGroup;
    [SerializeField] private float statusMessageFadeInDuration = 0.1f;
    [SerializeField] private float statusMessageHoldDuration = 1.8f;
    [SerializeField] private float statusMessageFadeOutDuration = 0.35f;

    [Header("Pop-up Text")]
    [SerializeField] private TextMeshProUGUI popupText;
    [SerializeField] private CanvasGroup popupCanvasGroup;
    [SerializeField] private float popupFadeInDuration = 0.1f;
    [SerializeField] private float popupFadeOutDuration = 0.35f;

    private Coroutine healthAnimationCoroutine;
    private Coroutine statusMessageCoroutine;
    private PlayerManager observedPlayer;
    private Vector3 gameOverCanvasVisibleScale = Vector3.one;
    private bool useScaleVisibilityForGameOverCanvas;
    private bool isGameOverShowing;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        ResolveGameOverBindings();
        SetGameOverCanvasVisible(false);
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

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (observedPlayer == null)
        {
            TryBindPlayer();
        }
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!isGameOverShowing)
        {
            ResolveGameOverBindings();
            SetGameOverCanvasVisible(false);
        }

        TryBindPlayer();
    }

    private void TryBindPlayer()
    {
        PlayerManager player = FindFirstObjectByType<PlayerManager>();
        if (player == null || observedPlayer == player)
        {
            return;
        }

        UnbindPlayer();
        observedPlayer = player;
        observedPlayer.PlayerDied += HandlePlayerDied;
    }

    private void UnbindPlayer()
    {
        if (observedPlayer == null)
        {
            return;
        }

        observedPlayer.PlayerDied -= HandlePlayerDied;
        observedPlayer = null;
    }

    private void HandlePlayerDied(PlayerManager deadPlayer)
    {
        ShowGameOver(deadPlayer);
    }

    /// <summary>
    /// Centralized public method to safely update health. 
    /// Call this directly from external scripts instead of starting a coroutine there.
    /// </summary>
    public void UpdateHealth(float currentHp, float maxHp)
    {
        UpdateHPText(currentHp, maxHp);

        // Stop any currently running health bar animations to prevent overlapping race conditions
        if (healthAnimationCoroutine != null)
        {
            StopCoroutine(healthAnimationCoroutine);
        }

        // Start the managed animation routine on this persistent UI manager
        healthAnimationCoroutine = StartCoroutine(HealthBarRoutine(currentHp, maxHp));
    }

    public void UpdateWeaponIcon(Sprite newIcon)
    {
        if (weaponIcon != null)
        {
            weaponIcon.sprite = newIcon;
            weaponIcon.SetNativeSize();
        }
    }

    // display popup text canvas group with fade in transition
    public void ShowPopupText(string text)
    {
        if (popupText == null || popupCanvasGroup == null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        popupText.text = text;
        StartCoroutine(OnPopupTextRoutine());
    }

    public void HidePopupText()
    {
        if (popupCanvasGroup == null)
        {
            return;
        }

        StartCoroutine(OffPopupTextRoutine());
    }

    private IEnumerator OnPopupTextRoutine()
    {
        // Fade in
        float elapsed = 0f;
        while (elapsed < popupFadeInDuration)
        {
            popupCanvasGroup.alpha = Mathf.Lerp(0f, 1f, elapsed / popupFadeInDuration);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        popupCanvasGroup.alpha = 1f;
        popupCanvasGroup.interactable = true;
        popupCanvasGroup.blocksRaycasts = true;
    }

    public IEnumerator OffPopupTextRoutine()
    {
        popupCanvasGroup.interactable = false;
        popupCanvasGroup.blocksRaycasts = false;
        // Fade out
        float elapsed = 0f;
        while (elapsed < popupFadeOutDuration)
        {
            popupCanvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsed / popupFadeOutDuration);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        popupCanvasGroup.alpha = 0f;
    }


    public void SetHealthImmediate(float currentHp, float maxHp)
    {
        float safeMaxHp = Mathf.Max(0.01f, maxHp);
        float targetFill = Mathf.Clamp01(currentHp / safeMaxHp);

        if (healthAnimationCoroutine != null)
        {
            StopCoroutine(healthAnimationCoroutine);
            healthAnimationCoroutine = null;
        }

        if (greenHPBarFill != null)
        {
            greenHPBarFill.fillAmount = targetFill;
        }

        if (redHPBarFill != null)
        {
            redHPBarFill.fillAmount = targetFill;
        }

        UpdateHPText(currentHp, safeMaxHp);
    }

    private IEnumerator HealthBarRoutine(float currentHp, float maxHp)
    {
        float targetFill = Mathf.Clamp01(currentHp / Mathf.Max(0.01f, maxHp));

        // 1. Smoothly transition the green bar to the target fill
        // Using Time.unscaledDeltaTime ensures it moves even if the game pauses/slows down on death
        while (greenHPBarFill != null && Mathf.Abs(greenHPBarFill.fillAmount - targetFill) > 0.001f)
        {
            greenHPBarFill.fillAmount = Mathf.Lerp(greenHPBarFill.fillAmount, targetFill, Time.unscaledDeltaTime * greenLerpSpeed);
            yield return null;
        }

        if (greenHPBarFill != null)
        {
            greenHPBarFill.fillAmount = targetFill;
        }

        // If health drops to 0, skip the 1.5s delay so the red bar drains immediately 
        if (targetFill > 0f)
        {
            // Wait 1.5 seconds using unscaled real time before the red bar catches up
            yield return new WaitForSecondsRealtime(1.5f);
        }

        // 2. Smoothly transition the red bar to match the target fill
        while (redHPBarFill != null && Mathf.Abs(redHPBarFill.fillAmount - targetFill) > 0.001f)
        {
            redHPBarFill.fillAmount = Mathf.MoveTowards(redHPBarFill.fillAmount, targetFill, redLerpSpeed * Time.unscaledDeltaTime);
            yield return null;
        }

        if (redHPBarFill != null)
        {
            redHPBarFill.fillAmount = targetFill;
        }
    }

    public void UpdateHPText(float currentHp, float maxHp)
    {
        if (hpText == null) return;
        // Clamp currentHp to 0 so it doesn't display negative values if overkill damage happens
        hpText.text = $"{Mathf.RoundToInt(Mathf.Max(0, currentHp))}/{Mathf.RoundToInt(maxHp)}";
    }

    public void ShowPauseUI()
    {
        if (isGameOverShowing)
        {
            return;
        }

        SetCanvasGroupVisible(inGameUICanvasGroup, false);
        SetCanvasGroupVisible(PauseUICanvasGroup, true);
    }

    public void HidePauseUI()
    {
        if (isGameOverShowing)
        {
            return;
        }

        SetCanvasGroupVisible(inGameUICanvasGroup, true);
        SetCanvasGroupVisible(PauseUICanvasGroup, false);
    }

    public void ShowStatusMessage(string message, Color color)
    {
        if (statusMessageText == null || statusMessageCanvasGroup == null || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (statusMessageCoroutine != null)
        {
            StopCoroutine(statusMessageCoroutine);
        }

        statusMessageCoroutine = StartCoroutine(StatusMessageRoutine(message, color));
    }

    private IEnumerator StatusMessageRoutine(string message, Color color)
    {
        statusMessageText.text = message;
        statusMessageText.color = color;

        float fadeInDuration = Mathf.Max(0.01f, statusMessageFadeInDuration);
        float holdDuration = Mathf.Max(0f, statusMessageHoldDuration);
        float fadeOutDuration = Mathf.Max(0.01f, statusMessageFadeOutDuration);

        statusMessageCanvasGroup.alpha = 0f;

        float elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            statusMessageCanvasGroup.alpha = Mathf.Lerp(0f, 1f, elapsed / fadeInDuration);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        statusMessageCanvasGroup.alpha = 1f;
        if (holdDuration > 0f)
        {
            yield return new WaitForSecondsRealtime(holdDuration);
        }

        elapsed = 0f;
        while (elapsed < fadeOutDuration)
        {
            statusMessageCanvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsed / fadeOutDuration);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        statusMessageCanvasGroup.alpha = 0f;
        statusMessageCoroutine = null;
    }

    public void ShowGameOver(PlayerManager deadPlayer)
    {
        if (isGameOverShowing)
        {
            return;
        }

        ResolveGameOverBindings();
        if (!HasUsableGameOverBindings())
        {
            Debug.LogWarning("InGameUIManager cannot show game over because the UI Manager Game Over Canvas is missing required bindings.");
            return;
        }

        RunRewardSummary summary = BuildFallbackSummary();
        if (RewardManager.Instance != null &&
            RewardManager.Instance.TryGetRunRewardSummary(out RunRewardSummary runSummary))
        {
            summary = runSummary;
        }

        if (HasAnyGameOverSummaryField())
        {
            PopulateStructuredSummary(summary);
        }
        else if (gameOverDetailsText != null)
        {
            gameOverDetailsText.text = BuildSummaryDetailsText(summary);
        }

        if (gameOverTitleText != null)
        {
            gameOverTitleText.text = "GAME OVER";
        }

        SetCanvasGroupVisible(inGameUICanvasGroup, false);
        SetCanvasGroupVisible(PauseUICanvasGroup, false);
        SetGameOverCanvasVisible(true);
        if (returnToLobbyButton != null)
        {
            returnToLobbyButton.interactable = true;
        }

        isGameOverShowing = true;
        Time.timeScale = 0f;
    }

    private void ResolveGameOverBindings()
    {
        if (TryBindGameOverCanvas(gameOverCanvasRoot))
        {
            return;
        }

        RectTransform discoveredRoot = FindGameOverCanvasRoot();
        TryBindGameOverCanvas(discoveredRoot);
    }

    private RectTransform FindGameOverCanvasRoot()
    {
        Transform directChild = transform.Find(GameOverCanvasName);
        if (directChild != null)
        {
            return directChild as RectTransform;
        }

        Canvas[] canvases = GetComponentsInChildren<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas != null && canvas.gameObject.name == GameOverCanvasName)
            {
                return canvas.transform as RectTransform;
            }
        }

        return null;
    }

    private bool TryBindGameOverCanvas(RectTransform canvasRoot)
    {
        if (canvasRoot == null)
        {
            ClearGameOverSummaryBindings();
            return false;
        }

        gameOverCanvasRoot = canvasRoot;
        gameOverCanvasGroup = canvasRoot.GetComponent<CanvasGroup>();
        if (gameOverCanvasGroup == null)
        {
            gameOverCanvasGroup = canvasRoot.gameObject.AddComponent<CanvasGroup>();
        }

        gameOverTitleText = gameOverTitleText != null ? gameOverTitleText : FindTextByPath(canvasRoot, AuthoredTitlePath);
        gameOverDetailsText = gameOverDetailsText != null ? gameOverDetailsText : FindTextByPath(canvasRoot, "Main Box/Details");

        Transform returnButtonTransform = FindDescendantByName(canvasRoot, AuthoredReturnToLobbyButtonPath);
        Button discoveredButton = returnButtonTransform != null ? returnButtonTransform.GetComponent<Button>() : null;
        BindReturnToLobbyButton(returnToLobbyButton != null ? returnToLobbyButton : discoveredButton != null ? discoveredButton : canvasRoot.GetComponentInChildren<Button>(true));

        CacheGameOverCanvasVisibility(canvasRoot, true);
        TryBindStructuredSummaryFields(canvasRoot);
        return HasUsableGameOverBindings();
    }

    private bool HasUsableGameOverBindings()
    {
        return gameOverCanvasRoot != null &&
               gameOverCanvasGroup != null &&
               returnToLobbyButton != null;
    }

    private void TryBindStructuredSummaryFields(Transform root)
    {
        ClearGameOverSummaryBindings();
        if (root == null)
        {
            return;
        }

        Transform mainBox = FindDescendantByName(root, "Main Box");
        Transform summaryRoot = mainBox != null ? mainBox : root;

        runtimeValueText = FindValueTextUnderLabel(summaryRoot, "Runtime");
        floorsClearedValueText = FindValueTextUnderLabel(summaryRoot, "Floors Cleared");
        stagesClearedValueText = FindValueTextUnderLabel(summaryRoot, "Stage Cleared");
        enemiesKilledValueText = FindValueTextUnderLabel(summaryRoot, "Enemies Killed");
        killEssenceValueText = FindValueTextUnderLabel(summaryRoot, "Kill Essence");
        baseClearValueText = FindValueTextUnderLabel(summaryRoot, "Base Clear");
        floorDepthValueText = FindValueTextUnderLabel(summaryRoot, "Floor Depth");
        rawSubtotalValueText = FindValueTextUnderLabel(summaryRoot, "Floors Cleared", 1);
        runMultiplierValueText = FindValueTextUnderLabel(summaryRoot, "Multiplier");
        rewardTotalValueText = FindValueTextUnderLabel(summaryRoot, "Reward");
        composureBonusValueText = FindValueTextUnderLabel(summaryRoot, "Composure Bonus");
        otherBonusValueText = FindValueTextUnderLabel(summaryRoot, "Others");
        soulEssenceEarnedValueText = FindValueTextUnderLabel(summaryRoot, "Soul Essence");
    }

    private void ClearGameOverSummaryBindings()
    {
        runtimeValueText = null;
        floorsClearedValueText = null;
        stagesClearedValueText = null;
        enemiesKilledValueText = null;
        killEssenceValueText = null;
        baseClearValueText = null;
        floorDepthValueText = null;
        rawSubtotalValueText = null;
        runMultiplierValueText = null;
        rewardTotalValueText = null;
        composureBonusValueText = null;
        otherBonusValueText = null;
        soulEssenceEarnedValueText = null;
    }

    private bool HasAnyGameOverSummaryField()
    {
        return runtimeValueText != null ||
               floorsClearedValueText != null ||
               stagesClearedValueText != null ||
               enemiesKilledValueText != null ||
               killEssenceValueText != null ||
               baseClearValueText != null ||
               floorDepthValueText != null ||
               rawSubtotalValueText != null ||
               runMultiplierValueText != null ||
               rewardTotalValueText != null ||
               composureBonusValueText != null ||
               otherBonusValueText != null ||
               soulEssenceEarnedValueText != null;
    }

    private void PopulateStructuredSummary(RunRewardSummary summary)
    {
        int otherEssence = Mathf.Max(0, summary.totalEssenceEarned - summary.stageRewardEssence - summary.composureBonusEssence);
        int stagesCleared = Mathf.Max(summary.stagesCleared, summary.stageReached);
        int floorsCleared = Mathf.Max(0, summary.floorReached);

        SetText(runtimeValueText, FormatRuntime(summary.runtimeSeconds));
        SetText(floorsClearedValueText, floorsCleared.ToString());
        SetText(stagesClearedValueText, stagesCleared.ToString());
        SetText(enemiesKilledValueText, summary.enemiesDefeated.ToString());
        SetText(killEssenceValueText, summary.rawKillEssence.ToString());
        SetText(baseClearValueText, summary.rawBaseEssence.ToString());
        SetText(floorDepthValueText, summary.rawFloorEssence.ToString());
        SetText(rawSubtotalValueText, summary.rawEssenceBeforeMultipliers.ToString());
        SetText(runMultiplierValueText, $"x{summary.effectiveCombinedMultiplier:0.00}");
        SetText(rewardTotalValueText, summary.stageRewardEssence.ToString());
        SetText(composureBonusValueText, summary.composureBonusEssence.ToString());
        SetText(otherBonusValueText, otherEssence.ToString());
        SetText(soulEssenceEarnedValueText, summary.totalEssenceEarned.ToString());
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

    private string BuildSummaryDetailsText(RunRewardSummary summary)
    {
        int otherEssence = Mathf.Max(0, summary.totalEssenceEarned - summary.stageRewardEssence - summary.composureBonusEssence);
        int stagesCleared = Mathf.Max(summary.stagesCleared, summary.stageReached);
        int floorsCleared = Mathf.Max(0, summary.floorReached);

        return
            $"{FormatTwoColumn($"Run time :{FormatRuntime(summary.runtimeSeconds)}", $"Stages Cleared :{stagesCleared}")}\n" +
            $"{FormatTwoColumn($"Floors Cleared :{floorsCleared}", $"Enemies Killed: {summary.enemiesDefeated}")}\n\n" +
            "<align=center>Soul Essence Calculation</align>\n\n" +
            $"{FormatTwoColumn($"Kill Essence :{summary.rawKillEssence}", $"Run Multiplier :x{summary.effectiveCombinedMultiplier:0.00}")}\n" +
            $"{FormatTwoColumn($"Base Clear :{summary.rawBaseEssence}", $"Reward Total:{summary.stageRewardEssence}")}\n" +
            $"{FormatTwoColumn($"Floor Depth:{summary.rawFloorEssence}", $"Composure Bonus :{summary.composureBonusEssence}")}\n" +
            $"{FormatTwoColumn($"Raw Subtotal:{summary.rawEssenceBeforeMultipliers}", $"Other Bonus :{otherEssence}")}\n\n" +
            $"<align=center>Soul Essence Earned :{summary.totalEssenceEarned}</align>";
    }

    private string FormatRuntime(float runtimeSeconds)
    {
        TimeSpan runTime = TimeSpan.FromSeconds(Mathf.Max(0f, runtimeSeconds));
        return runTime.TotalHours >= 1
            ? runTime.ToString(@"hh\:mm\:ss")
            : runTime.ToString(@"mm\:ss");
    }

    private string FormatTwoColumn(string left, string right)
    {
        return $"{left,-34}{right}";
    }

    private void BindReturnToLobbyButton(Button button)
    {
        if (returnToLobbyButton != null)
        {
            returnToLobbyButton.onClick.RemoveListener(HandleReturnToLobbyPressed);
        }

        returnToLobbyButton = button;
        if (returnToLobbyButton == null)
        {
            return;
        }

        returnToLobbyButton.onClick.RemoveListener(HandleReturnToLobbyPressed);
        returnToLobbyButton.onClick.AddListener(HandleReturnToLobbyPressed);
    }

    private void HandleReturnToLobbyPressed()
    {
        Time.timeScale = 1f;
        isGameOverShowing = false;
        SetGameOverCanvasVisible(false);
        SetCanvasGroupVisible(inGameUICanvasGroup, true);
        SetCanvasGroupVisible(PauseUICanvasGroup, false);

        PlayerManager player = observedPlayer != null ? observedPlayer : FindFirstObjectByType<PlayerManager>();
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

    private void CacheGameOverCanvasVisibility(RectTransform root, bool useScaleForVisibility)
    {
        gameOverCanvasRoot = root;
        useScaleVisibilityForGameOverCanvas = useScaleForVisibility && root != null;
        gameOverCanvasVisibleScale = Vector3.one;

        if (gameOverCanvasRoot == null)
        {
            return;
        }

        Vector3 currentScale = gameOverCanvasRoot.localScale;
        gameOverCanvasVisibleScale = currentScale.sqrMagnitude > 0.0001f ? currentScale : Vector3.one;
    }

    private void SetGameOverCanvasVisible(bool visible)
    {
        if (gameOverCanvasGroup != null)
        {
            gameOverCanvasGroup.alpha = visible ? 1f : 0f;
            gameOverCanvasGroup.interactable = visible;
            gameOverCanvasGroup.blocksRaycasts = visible;
        }

        if (useScaleVisibilityForGameOverCanvas && gameOverCanvasRoot != null)
        {
            gameOverCanvasRoot.localScale = visible ? gameOverCanvasVisibleScale : Vector3.zero;
        }
    }

    private static TextMeshProUGUI FindTextByPath(Transform root, string path)
    {
        if (root == null || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        Transform found = root.Find(path);
        return found != null ? found.GetComponent<TextMeshProUGUI>() : null;
    }

    private static Transform FindDescendantByName(Transform root, string objectName)
    {
        return FindDescendantByName(root, objectName, 0);
    }

    private static Transform FindDescendantByName(Transform root, string objectName, int occurrenceIndex)
    {
        if (root == null || string.IsNullOrWhiteSpace(objectName))
        {
            return null;
        }

        int matchesSkipped = 0;
        return FindDescendantByName(root, NormalizeName(objectName), Mathf.Max(0, occurrenceIndex), ref matchesSkipped);
    }

    private static Transform FindDescendantByName(Transform root, string normalizedName, int occurrenceIndex, ref int matchesSkipped)
    {
        if (root == null)
        {
            return null;
        }

        if (NormalizeName(root.name) == normalizedName)
        {
            if (matchesSkipped >= occurrenceIndex)
            {
                return root;
            }

            matchesSkipped++;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDescendantByName(root.GetChild(i), normalizedName, occurrenceIndex, ref matchesSkipped);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static TextMeshProUGUI FindValueTextUnderLabel(Transform root, string labelObjectName, int occurrenceIndex = 0)
    {
        Transform labelRoot = FindDescendantByName(root, labelObjectName, occurrenceIndex);
        if (labelRoot == null)
        {
            return null;
        }

        TextMeshProUGUI[] texts = labelRoot.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            TextMeshProUGUI candidate = texts[i];
            if (candidate != null && NormalizeName(candidate.gameObject.name) == "Text Out")
            {
                return candidate;
            }
        }

        for (int i = 0; i < texts.Length; i++)
        {
            TextMeshProUGUI candidate = texts[i];
            if (candidate != null && candidate.transform != labelRoot && NormalizeName(candidate.gameObject.name) != "Text Bold")
            {
                return candidate;
            }
        }

        return null;
    }

    private static string NormalizeName(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static void SetText(TextMeshProUGUI text, string value)
    {
        if (text != null)
        {
            text.text = value;
        }
    }

    private static void SetCanvasGroupVisible(CanvasGroup group, bool visible)
    {
        if (group == null)
        {
            return;
        }

        group.alpha = visible ? 1f : 0f;
        group.interactable = visible;
        group.blocksRaycasts = visible;
    }
}
