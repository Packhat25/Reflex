using UnityEngine;
using UnityEngine.InputSystem;

public class FloorDebugHUD : MonoBehaviour
{
    private const float BaseWidth = 260f;
    private const float BaseHeight = 120f;
    private const float Padding = 12f;
    private const float BaseScreenReference = 1080f;
    private const float MinimumScale = 0.7f;
    private const float MaximumScale = 1.15f;

    private static FloorDebugHUD _instance;

    [SerializeField] private bool visible = true;
    [SerializeField] private Key toggleKey = Key.F5;
    [SerializeField] private bool showDifficultyDetails = true;

    private GUIStyle _panelStyle;
    private GUIStyle _titleStyle;
    private GUIStyle _labelStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        EnsureExists();
    }

    public static void EnsureExists()
    {
        if (_instance != null)
        {
            return;
        }

        _instance = FindFirstObjectByType<FloorDebugHUD>();
        if (_instance != null)
        {
            return;
        }

        GameObject hudObject = new GameObject("Floor Debug HUD");
        _instance = hudObject.AddComponent<FloorDebugHUD>();
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
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
        {
            visible = !visible;
        }
    }

    private void OnGUI()
    {
        if (!visible)
        {
            return;
        }

        EnsureStyles();

        float scale = Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / BaseScreenReference, MinimumScale, MaximumScale);
        float width = BaseWidth * scale;
        float height = BaseHeight * scale;
        Rect area = ResolveArea(width, height);

        GUILayout.BeginArea(area, GUIContent.none, _panelStyle);
        GUILayout.Label("FLOOR DEBUG", _titleStyle);

        if (!LevelRunManager.HasInstance)
        {
            DrawLine("Run manager not initialized.");
            GUILayout.EndArea();
            return;
        }

        LevelRunManager runManager = LevelRunManager.Instance;
        int currentFloor = runManager.CurrentFloor;
        int currentStage = runManager.CurrentStage;
        int stagesPerFloor = runManager.StagesPerFloor;

        DrawLine($"Floor: {currentFloor}");
        DrawLine(currentStage <= 0
            ? $"Stage: Lobby (next 1/{stagesPerFloor})"
            : $"Stage: {currentStage}/{stagesPerFloor}");

        if (showDifficultyDetails)
        {
            DrawLine($"HP x{runManager.CurrentFloorEnemyHealthMultiplier:0.00}");
            DrawLine($"DMG x{runManager.CurrentFloorEnemyDamageMultiplier:0.00}");
            DrawLine($"Spawn x{runManager.CurrentFloorSpawnMultiplier:0.00}");
            DrawLine($"Respawn x{runManager.CurrentFloorRespawnDelayMultiplier:0.00}");
        }

        DrawLine($"Toggle: {toggleKey}");
        GUILayout.EndArea();
    }

    private Rect ResolveArea(float width, float height)
    {
        float x = Padding;
        float y = Padding;

        if (EmotionDebugHUD.TryGetVisibleArea(out Rect otherHudArea))
        {
            x = otherHudArea.xMax + Padding;
            y = otherHudArea.yMin;

            if (x + width > Screen.width - Padding)
            {
                x = otherHudArea.xMin;
                y = otherHudArea.yMax + Padding;
            }
        }

        x = Mathf.Clamp(x, Padding, Mathf.Max(Padding, Screen.width - width - Padding));
        y = Mathf.Clamp(y, Padding, Mathf.Max(Padding, Screen.height - height - Padding));
        return new Rect(x, y, width, height);
    }

    private void DrawLine(string text)
    {
        GUILayout.Label(text, _labelStyle);
    }

    private void EnsureStyles()
    {
        if (_panelStyle != null)
        {
            return;
        }

        _panelStyle = new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(12, 12, 10, 10),
            alignment = TextAnchor.UpperLeft
        };

        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            wordWrap = true,
            normal = { textColor = Color.white }
        };
    }
}
