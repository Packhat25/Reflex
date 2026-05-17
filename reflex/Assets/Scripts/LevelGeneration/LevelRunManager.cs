using System;
using System.Collections.Generic;
using System.Text;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;

[Serializable]
public class GeneratedLevelConnection
{
    public int doorIndex;
    public int destinationNodeId;
}

[Serializable]
public class GeneratedLevelNode
{
    public int id;
    public int depth;
    public string sceneName;
    public readonly List<GeneratedLevelConnection> connections = new List<GeneratedLevelConnection>();
}

public struct LevelDoorRoute
{
    public int DoorIndex;
    public int DestinationNodeId;
    public int DestinationDepth;
    public string DestinationSceneName;
    public string DestinationLabel;
}

public enum LevelClearReason
{
    Unknown = 0,
    AlwaysUnlocked = 1,
    NoActiveSpawners = 2,
    RoomEvaluated = 3,
    SceneRequested = 4
}

[Serializable]
public struct LevelClearContext
{
    public int nodeId;
    public int floorDepth;
    public string sceneName;
    public LevelClearReason reason;
    public bool hasRoomReport;
    public EmotionRoomReport roomReport;
}

[DefaultExecutionOrder(-1000)]
public class LevelRunManager : MonoBehaviour
{
    public static event Action<int, int, string> LevelEntered;
    public static event Action<int, int, string> LevelCleared;
    public static event Action<LevelClearContext> LevelClearedDetailed;

    private static LevelRunManager _instance;

    [Header("Profile")]
    [SerializeField] private LevelGenerationProfile generationProfile;

    [Header("Scene Pool")]
    [SerializeField] private string lobbySceneName = "Lobby";
    [SerializeField]
    private string[] roomSceneNames =
    {
        "Level_1_Scene",
        "Level_2_Scene",
        "Level_3_Scene",
        "Level_4_Scene",
        "Final Boss Level",
    };
    [SerializeField] private bool useSequentialFallbackRoomOrder = true;
    [SerializeField] private bool randomizeStageOrderEachFloor = true;
    [SerializeField] private bool keepBossStageAtEnd = true;

    [Header("Generated Run")]
    [SerializeField, Min(1)] private int generatedRoomCount = 5;
    [SerializeField, Min(1)] private int minDoorChoices = 1;
    [SerializeField, Min(1)] private int maxDoorChoices = 3;
    [SerializeField, Min(1)] private int maxForwardRoomSkip = 3;
    [SerializeField] private int fixedSeed;

    [Header("Floor Loop")]
    [SerializeField, Min(1)] private int startingFloor = 1;

    [Header("Boss Floor Rules")]
    [SerializeField, Min(1)] private int firstBossFloor = 3;
    [SerializeField, Min(1)] private int bossFloorInterval = 3;

    [Header("Floor Difficulty")]
    [SerializeField, Min(0f)] private float enemyHealthPerFloorStep = 0.50f;
    [SerializeField, Min(0f)] private float enemyDamagePerFloorStep = 0.20f;
    [SerializeField, Min(0f)] private float spawnCountPerFloorStep = 0.10f;
    [SerializeField, Min(0f)] private float respawnDelayReductionPerFloorStep = 0.05f;
    [SerializeField, Min(0.1f)] private float minimumRespawnDelayFloorMultiplier = 0.45f;

    [Header("Door Rules")]
    [SerializeField] private bool lockDoorsWhileRoomActive = true;
    [SerializeField] private bool autoBindSceneDoors = true;
    [SerializeField] private bool autoAdvanceWhenNoDoors = true;
    [SerializeField] private bool useSingleRandomOpenDoor = true;
    [SerializeField, Min(0f)] private float entryDoorGroupRadius = 2.25f;

    [Header("Progression")]
    [SerializeField] private bool unlockCurrentLevelAfterClear = true;
    [SerializeField] private bool unlockLevelsWithoutSpawners = true;
    [SerializeField] private bool disableSpawnersAfterLevelClear = true;

    [Header("Debug")]
    [SerializeField] private bool logGeneratedGraph = true;
    [SerializeField] private bool logDoorBinding = true;
    [SerializeField] private bool logProgression = true;

    private readonly Dictionary<int, GeneratedLevelNode> _nodesById = new Dictionary<int, GeneratedLevelNode>();
    private readonly HashSet<int> _clearedNodeIds = new HashSet<int>();
    private LevelGenerationRuntimeOverrides _runtimeOverrides;
    private int _currentNodeId;
    private int _pendingNodeId = -1;
    private int _deepestDepthReached;
    private int _activeSeed;
    private int _currentFloor = 1;
    private PlayerManager _persistentPlayer;
    private EmotionRoomReport _pendingRoomClearReport;
    private bool _hasPendingRoomClearReport;

    public static bool HasInstance
    {
        get { return _instance != null; }
    }

    public static LevelRunManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<LevelRunManager>();

                if (_instance == null)
                {
                    GameObject managerObject = new GameObject("Level Run Manager");
                    _instance = managerObject.AddComponent<LevelRunManager>();
                }
            }

            return _instance;
        }
    }

    public bool AreDoorsUnlocked
    {
        get
        {
            bool baseDoorUnlock = !LockDoorsWhileRoomActive ||
                                  IsCurrentNodeCleared ||
                                  !EmotionEngine.HasInstance ||
                                  !EmotionEngine.Instance.IsRoomActive;

            if (!baseDoorUnlock)
            {
                return false;
            }

            if (_currentNodeId == 0)
            {
                return true;
            }

            return !RewardManager.HasInstance ||
                   !RewardManager.Instance.IsAwaitingBuffChoiceForDoorUnlock;
        }
    }

    public int ActiveSeed
    {
        get { return _activeSeed; }
    }

    public int CurrentFloor
    {
        get { return Mathf.Max(1, _currentFloor); }
    }

    public int StagesPerFloor
    {
        get { return GeneratedRoomCount; }
    }

    public int CurrentStage
    {
        get
        {
            if (!_nodesById.TryGetValue(_currentNodeId, out GeneratedLevelNode node) || node.id == 0)
            {
                return 0;
            }

            return GetStageFromDepth(node.depth);
        }
    }

    public int CurrentLevelDepth
    {
        get
        {
            return _nodesById.TryGetValue(_currentNodeId, out GeneratedLevelNode node) ? node.depth : 0;
        }
    }

    public int DeepestDepthReached
    {
        get { return _deepestDepthReached; }
    }

    public bool IsCurrentNodeCleared
    {
        get { return _clearedNodeIds.Contains(_currentNodeId); }
    }

    public bool TryGetPersistentPlayerTransform(out Transform playerTransform)
    {
        playerTransform = _persistentPlayer != null ? _persistentPlayer.transform : null;
        return playerTransform != null;
    }

    public LevelClearContext LastClearContext { get; private set; }

    public float CurrentFloorEnemyHealthMultiplier => 1f + Mathf.Max(0, CurrentFloor - 1) * enemyHealthPerFloorStep;
    public float CurrentFloorEnemyDamageMultiplier => 1f + Mathf.Max(0, CurrentFloor - 1) * enemyDamagePerFloorStep;
    public float CurrentFloorSpawnMultiplier => 1f + Mathf.Max(0, CurrentFloor - 1) * spawnCountPerFloorStep;
    public float CurrentFloorRespawnDelayMultiplier
    {
        get
        {
            float reduction = Mathf.Max(0, CurrentFloor - 1) * respawnDelayReductionPerFloorStep;
            return Mathf.Max(minimumRespawnDelayFloorMultiplier, 1f - reduction);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        _ = Instance;
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
        _currentFloor = Mathf.Max(1, startingFloor);
        LoadDefaultProfileIfNeeded();
        GenerateNewRun();
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnEnable()
    {
        EmotionEngine.RoomEvaluated += HandleRoomEvaluated;
    }

    private void OnDisable()
    {
        EmotionEngine.RoomEvaluated -= HandleRoomEvaluated;
    }

    private void Start()
    {
        HandleSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            _instance = null;
        }
    }

    public void GenerateNewRun()
    {
        _nodesById.Clear();
        _clearedNodeIds.Clear();
        _currentNodeId = 0;
        _pendingNodeId = -1;
        _deepestDepthReached = 0;
        _activeSeed = CreateRunSeed();

        System.Random random = new System.Random(_activeSeed);
        Dictionary<string, int> roomUseCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        GeneratedLevelNode lobbyNode = CreateNode(0, 0, LobbySceneName);
        _nodesById.Add(lobbyNode.id, lobbyNode);
        _clearedNodeIds.Add(lobbyNode.id);

        string previousScene = LobbySceneName;
        List<string> stageSceneOrder = randomizeStageOrderEachFloor
            ? BuildStageSceneOrder(random, GeneratedRoomCount, previousScene)
            : null;

        for (int stage = 1; stage <= GeneratedRoomCount; stage++)
        {
            string sceneName;
            if (stageSceneOrder != null && stageSceneOrder.Count >= stage)
            {
                sceneName = stageSceneOrder[stage - 1];
            }
            else
            {
                sceneName = PickRoomScene(random, previousScene, stage, roomUseCounts);
            }

            int floorDepth = ComposeFloorDepth(CurrentFloor, stage);
            GeneratedLevelNode node = CreateNode(stage, floorDepth, sceneName);
            _nodesById.Add(node.id, node);
            IncrementRoomUseCount(roomUseCounts, sceneName);
            previousScene = sceneName;
        }

        BuildForwardConnections(random);
        ConnectLastStageToFloorTransition();

        if (LogGeneratedGraph)
        {
            Debug.Log(BuildGraphLog());
        }

        ResetPersistentPlayerRunState();
    }

    public void TravelTo(LevelDoorRoute route)
    {
        if (!AreDoorsUnlocked)
        {
            Debug.Log("Door is locked until the active room is clear.");
            return;
        }

        if (!_nodesById.TryGetValue(route.DestinationNodeId, out GeneratedLevelNode destination))
        {
            Debug.LogError("Generated door route points to a missing level node: " + route.DestinationNodeId);
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(destination.sceneName))
        {
            Debug.LogError("Cannot load scene '" + destination.sceneName + "'. Add it to Build Settings.");
            return;
        }

        if (destination.id == 0 && _currentNodeId != 0)
        {
            if (_currentNodeId == GeneratedRoomCount)
            {
                AdvanceToNextFloor();
            }
            else
            {
                Debug.LogWarning("Ignoring unexpected transition to node 0 from node " + _currentNodeId + ".");
            }

            return;
        }

        _pendingNodeId = destination.id;
        SceneManager.LoadScene(destination.sceneName, LoadSceneMode.Single);
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!scene.IsValid())
        {
            return;
        }

        if (_pendingNodeId >= 0)
        {
            _currentNodeId = _pendingNodeId;
            _pendingNodeId = -1;
        }
        else
        {
            SyncCurrentNodeToLoadedScene(scene.name);
        }

        EnsurePersistentPlayer(scene);
        RefreshCurrentLevelProgression(scene);
        AnnounceLevelEntered();

        if (AutoBindSceneDoors)
        {
            ConfigureDoorsForCurrentScene(scene.name);
        }
    }

    private void EnsurePersistentPlayer(Scene loadedScene)
    {
        PlayerManager[] players = FindObjectsByType<PlayerManager>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        PlayerManager scenePlayer = null;

        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null && players[i].gameObject.scene == loadedScene)
            {
                scenePlayer = players[i];
                break;
            }
        }

        if (_persistentPlayer == null)
        {
            _persistentPlayer = scenePlayer != null ? scenePlayer : GetFirstPlayer(players);

            if (_persistentPlayer != null)
            {
                DontDestroyOnLoad(_persistentPlayer.gameObject);
                RefreshPlayerSceneReferences(_persistentPlayer);
            }

            return;
        }

        if (scenePlayer != null && scenePlayer != _persistentPlayer)
        {
            MovePersistentPlayerTo(scenePlayer.transform);

            Destroy(scenePlayer.gameObject);
        }
        else if (scenePlayer == null && TryGetSpawnPoint(loadedScene, out LevelPlayerSpawnPoint spawnPoint))
        {
            MovePersistentPlayerTo(spawnPoint.transform);
        }

        RefreshPlayerSceneReferences(_persistentPlayer);
    }

    private void MovePersistentPlayerTo(Transform target)
    {
        if (_persistentPlayer == null || target == null)
        {
            return;
        }

        CharacterController controller = _persistentPlayer.GetComponent<CharacterController>();
        bool controllerWasEnabled = controller != null && controller.enabled;
        if (controllerWasEnabled)
        {
            controller.enabled = false;
        }

        _persistentPlayer.transform.SetPositionAndRotation(target.position, target.rotation);

        if (controllerWasEnabled)
        {
            controller.enabled = true;
        }
    }

    private bool TryGetSpawnPoint(Scene scene, out LevelPlayerSpawnPoint spawnPoint)
    {
        LevelPlayerSpawnPoint[] spawnPoints = FindObjectsByType<LevelPlayerSpawnPoint>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        spawnPoint = null;

        for (int i = 0; i < spawnPoints.Length; i++)
        {
            LevelPlayerSpawnPoint candidate = spawnPoints[i];
            if (candidate == null || candidate.gameObject.scene != scene)
            {
                continue;
            }

            if (spawnPoint == null ||
                candidate.Priority > spawnPoint.Priority ||
                (candidate.Priority == spawnPoint.Priority && candidate.DefaultSpawn && !spawnPoint.DefaultSpawn))
            {
                spawnPoint = candidate;
            }
        }

        return spawnPoint != null;
    }

    private PlayerManager GetFirstPlayer(PlayerManager[] players)
    {
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null)
            {
                return players[i];
            }
        }

        return null;
    }

    private void RefreshPlayerSceneReferences(PlayerManager player)
    {
        Camera sceneCamera = BindSceneCamera(player.transform);
        RefreshPerspectiveCameras(sceneCamera);

        PlayerMovementManagement movement = player.GetComponent<PlayerMovementManagement>();
        if (movement != null)
        {
            movement.RefreshSceneReferences();
        }

        PlayerOcclusionFader occlusionFader = player.GetComponent<PlayerOcclusionFader>();
        if (occlusionFader == null)
        {
            occlusionFader = player.gameObject.AddComponent<PlayerOcclusionFader>();
        }

        occlusionFader.RefreshForScene(sceneCamera);
    }

    private Camera BindSceneCamera(Transform playerTarget)
    {
        if (playerTarget == null)
        {
            return Camera.main;
        }

        Camera sceneCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        CinemachineCamera[] cinemachineCameras = FindObjectsByType<CinemachineCamera>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        if (cinemachineCameras.Length > 0)
        {
            if (sceneCamera != null && sceneCamera.GetComponent<CinemachineBrain>() == null)
            {
                sceneCamera.gameObject.AddComponent<CinemachineBrain>();
            }

            for (int i = 0; i < cinemachineCameras.Length; i++)
            {
                CinemachineCamera cinemachineCamera = cinemachineCameras[i];
                cinemachineCamera.Follow = playerTarget;
                cinemachineCamera.LookAt = null;
                cinemachineCamera.OnTargetObjectWarped(playerTarget, Vector3.zero);
            }

            if (sceneCamera != null && sceneCamera.TryGetComponent(out SceneCameraFollow fallbackFollow))
            {
                fallbackFollow.enabled = false;
            }
        }
        else if (sceneCamera != null)
        {
            SceneCameraFollow fallbackFollow = sceneCamera.GetComponent<SceneCameraFollow>();
            if (fallbackFollow == null)
            {
                fallbackFollow = sceneCamera.gameObject.AddComponent<SceneCameraFollow>();
            }

            fallbackFollow.enabled = true;
            fallbackFollow.Configure(playerTarget, true);
        }

        return sceneCamera;
    }

    private void RefreshPerspectiveCameras(Camera sceneCamera)
    {
        if (sceneCamera == null)
        {
            sceneCamera = Camera.main;
        }

        PerspectiveChanger[] perspectiveChangers = FindObjectsByType<PerspectiveChanger>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < perspectiveChangers.Length; i++)
        {
            perspectiveChangers[i].SetCamera(sceneCamera);
        }

        Billboarding[] billboards = FindObjectsByType<Billboarding>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < billboards.Length; i++)
        {
            billboards[i].SetCamera(sceneCamera);
        }
    }

    private void RefreshCurrentLevelProgression(Scene scene)
    {
        if (!_nodesById.TryGetValue(_currentNodeId, out GeneratedLevelNode node))
        {
            return;
        }

        _deepestDepthReached = Mathf.Max(_deepestDepthReached, node.depth);

        LevelRoomDefinition roomDefinition = GetRoomDefinition(scene);
        LevelRoomClearRule clearRule = roomDefinition != null
            ? roomDefinition.ClearRule
            : LevelRoomClearRule.UseGlobalDefaults;

        if (node.id == 0 || clearRule == LevelRoomClearRule.AlwaysUnlocked)
        {
            MarkCurrentNodeCleared("room configured as always unlocked", LevelClearReason.AlwaysUnlocked);
            return;
        }

        if (clearRule == LevelRoomClearRule.Manual)
        {
            return;
        }

        bool shouldUnlockWithoutSpawners =
            clearRule == LevelRoomClearRule.EnemySpawners ||
            (clearRule == LevelRoomClearRule.UseGlobalDefaults && UnlockLevelsWithoutSpawners);

        bool isBossScene = IsSceneConfiguredAsBoss(node.sceneName);
        if (shouldUnlockWithoutSpawners && !isBossScene && !SceneHasActiveSpawner(scene))
        {
            MarkCurrentNodeCleared("no active spawners", LevelClearReason.NoActiveSpawners);
        }
    }

    private bool SceneHasActiveSpawner(Scene scene)
    {
        EnemySpawner[] spawners = FindObjectsByType<EnemySpawner>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < spawners.Length; i++)
        {
            if (spawners[i] != null && spawners[i].gameObject.scene == scene)
            {
                return true;
            }
        }

        return false;
    }

    private bool SceneHasUpcomingWaves(Scene scene)
    {
        EnemySpawner[] spawners = FindObjectsByType<EnemySpawner>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < spawners.Length; i++)
        {
            EnemySpawner spawner = spawners[i];
            if (spawner != null &&
                spawner.gameObject.scene == scene &&
                spawner.HasUpcomingWave)
            {
                return true;
            }
        }

        return false;
    }

    private void HandleRoomEvaluated(EmotionRoomReport report)
    {
        if (!UnlockCurrentLevelAfterClear)
        {
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (SceneHasUpcomingWaves(activeScene))
        {
            if (LogProgression)
            {
                Debug.Log("Deferring level clear because a spawner has an upcoming wave queued.");
            }

            return;
        }

        _pendingRoomClearReport = report;
        _hasPendingRoomClearReport = true;
        MarkCurrentNodeCleared("room cleared", LevelClearReason.RoomEvaluated);
        _hasPendingRoomClearReport = false;
    }

    public void MarkCurrentLevelClearedFromScene(string source)
    {
        string reason = string.IsNullOrWhiteSpace(source) ? "scene script requested clear" : source + " requested clear";
        MarkCurrentNodeCleared(reason, LevelClearReason.SceneRequested);
    }

    private void MarkCurrentNodeCleared(string reason, LevelClearReason clearReason)
    {
        if (!_nodesById.TryGetValue(_currentNodeId, out GeneratedLevelNode node) ||
            !_clearedNodeIds.Add(_currentNodeId))
        {
            return;
        }

        LevelClearContext clearContext = new LevelClearContext
        {
            nodeId = node.id,
            floorDepth = node.depth,
            sceneName = node.sceneName,
            reason = clearReason,
            hasRoomReport = clearReason == LevelClearReason.RoomEvaluated && _hasPendingRoomClearReport,
            roomReport = _pendingRoomClearReport
        };

        LastClearContext = clearContext;
        LevelCleared?.Invoke(node.id, node.depth, node.sceneName);
        LevelClearedDetailed?.Invoke(clearContext);

        if (ShouldDisableSpawnersAfterClear() && node.id != 0)
        {
            DisableCurrentSceneSpawners();
        }

        if (LogProgression)
        {
            if (node.id == 0)
            {
                Debug.Log("Lobby node marked clear because " + reason + ".");
            }
            else
            {
                Debug.Log("Level cleared: floor " + GetFloorFromDepth(node.depth) +
                          " stage " + GetStageFromDepth(node.depth) +
                          " (" + node.sceneName + ") because " + reason + ".");
            }
        }

        List<LevelDoor> doors = LevelDoorAutoBinder.FindOrCreateDoors();
        TryAutoAdvanceWithoutDoors(node, doors.Count, "level cleared");
    }

    private bool ShouldDisableSpawnersAfterClear()
    {
        LevelRoomDefinition roomDefinition = GetRoomDefinition(SceneManager.GetActiveScene());
        if (roomDefinition != null && roomDefinition.HasSpawnerDisableOverride)
        {
            return roomDefinition.DisableSpawnersAfterClear;
        }

        return DisableSpawnersAfterLevelClear;
    }

    private void DisableCurrentSceneSpawners()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        EnemySpawner[] spawners = FindObjectsByType<EnemySpawner>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < spawners.Length; i++)
        {
            EnemySpawner spawner = spawners[i];
            if (spawner != null && spawner.gameObject.scene == activeScene)
            {
                spawner.enabled = false;
            }
        }
    }

    private void AnnounceLevelEntered()
    {
        if (!_nodesById.TryGetValue(_currentNodeId, out GeneratedLevelNode node))
        {
            return;
        }

        LevelEntered?.Invoke(node.id, node.depth, node.sceneName);

        if (LogProgression)
        {
            if (node.id == 0)
            {
                Debug.Log("Entered Lobby. Current floor: " + CurrentFloor + ".");
            }
            else
            {
                Debug.Log("Level entered: floor " + GetFloorFromDepth(node.depth) +
                          " stage " + GetStageFromDepth(node.depth) +
                          " (" + node.sceneName + "). Cleared: " + IsCurrentNodeCleared);
            }
        }
    }

    private void ConfigureDoorsForCurrentScene(string sceneName)
    {
        List<LevelDoor> doors = LevelDoorAutoBinder.FindOrCreateDoors();

        if (!_nodesById.TryGetValue(_currentNodeId, out GeneratedLevelNode currentNode))
        {
            ClearDoors(doors);
            return;
        }

        if (!SceneNameEquals(currentNode.sceneName, sceneName))
        {
            ClearDoors(doors);
            return;
        }

        HashSet<LevelDoor> blockedEntryDoors = ApplyEntryDoorLock(doors, currentNode);

        int routedDoorCount = 0;
        if (useSingleRandomOpenDoor)
        {
            ConfigureSingleRandomDoor(doors, currentNode, blockedEntryDoors, out routedDoorCount);
        }
        else
        {
            for (int i = 0; i < doors.Count; i++)
            {
                if (currentNode.connections.Count == 1)
                {
                    doors[i].Configure(BuildDoorRoute(currentNode.connections[0]));
                    routedDoorCount = 1;
                }
                else if (i < currentNode.connections.Count)
                {
                    doors[i].Configure(BuildDoorRoute(currentNode.connections[i]));
                    routedDoorCount++;
                }
                else
                {
                    doors[i].ClearRoute(i);
                }
            }
        }

        if (LogDoorBinding && doors.Count > 0)
        {
            Debug.Log("Bound " + routedDoorCount +
                      " generated door route(s) in " + sceneName + " from node " + currentNode.id + ".");
        }

        TryAutoAdvanceWithoutDoors(currentNode, routedDoorCount, "scene has no generated door candidates");
    }

    private void ClearDoors(List<LevelDoor> doors)
    {
        for (int i = 0; i < doors.Count; i++)
        {
            doors[i].SetEntryBlocked(false);
            doors[i].ClearRoute(i);
        }
    }

    private HashSet<LevelDoor> ApplyEntryDoorLock(List<LevelDoor> doors, GeneratedLevelNode currentNode)
    {
        HashSet<LevelDoor> blockedDoors = new HashSet<LevelDoor>();

        if (doors == null || doors.Count == 0)
        {
            return blockedDoors;
        }

        for (int i = 0; i < doors.Count; i++)
        {
            if (doors[i] != null)
            {
                doors[i].SetEntryBlocked(false);
            }
        }

        // Entry lock should only apply to non-lobby nodes and only when there's another way forward.
        if (currentNode == null || currentNode.id == 0 || _persistentPlayer == null)
        {
            return blockedDoors;
        }

        List<LevelDoor> usableDoors = new List<LevelDoor>();
        for (int i = 0; i < doors.Count; i++)
        {
            if (doors[i] != null)
            {
                usableDoors.Add(doors[i]);
            }
        }

        if (usableDoors.Count <= 1)
        {
            return blockedDoors;
        }

        Vector3 playerPosition = _persistentPlayer.transform.position;
        LevelDoor nearestDoor = null;
        float nearestDoorSqrDistance = float.MaxValue;

        for (int i = 0; i < usableDoors.Count; i++)
        {
            float sqrDistance = (usableDoors[i].transform.position - playerPosition).sqrMagnitude;
            if (sqrDistance < nearestDoorSqrDistance)
            {
                nearestDoorSqrDistance = sqrDistance;
                nearestDoor = usableDoors[i];
            }
        }

        if (nearestDoor == null)
        {
            return blockedDoors;
        }

        float lockGroupRadius = Mathf.Max(0f, entryDoorGroupRadius);
        float lockGroupRadiusSqr = lockGroupRadius * lockGroupRadius;
        for (int i = 0; i < usableDoors.Count; i++)
        {
            LevelDoor door = usableDoors[i];
            bool shouldBlock = door == nearestDoor;

            if (!shouldBlock && lockGroupRadius > 0f)
            {
                float sqrDistanceToNearest = (door.transform.position - nearestDoor.transform.position).sqrMagnitude;
                shouldBlock = sqrDistanceToNearest <= lockGroupRadiusSqr;
            }

            if (!shouldBlock)
            {
                continue;
            }

            blockedDoors.Add(door);
        }

        // Never block every routed door in a room.
        if (blockedDoors.Count >= usableDoors.Count)
        {
            blockedDoors.Clear();
            blockedDoors.Add(nearestDoor);
        }

        foreach (LevelDoor blockedDoor in blockedDoors)
        {
            if (blockedDoor != null)
            {
                blockedDoor.SetEntryBlocked(true);
            }
        }

        if (LogDoorBinding)
        {
            Debug.Log("Blocked " + blockedDoors.Count + " entry door(s) in node " + currentNode.id + ".");
        }

        return blockedDoors;
    }

    private void ConfigureSingleRandomDoor(
        List<LevelDoor> doors,
        GeneratedLevelNode currentNode,
        HashSet<LevelDoor> blockedEntryDoors,
        out int routedDoorCount)
    {
        routedDoorCount = 0;

        if (doors == null)
        {
            return;
        }

        if (doors.Count == 0 || currentNode == null || currentNode.connections.Count == 0)
        {
            for (int i = 0; i < doors.Count; i++)
            {
                if (doors[i] != null)
                {
                    doors[i].ClearRoute(i);
                }
            }

            return;
        }

        Dictionary<string, List<LevelDoor>> doorGroups = new Dictionary<string, List<LevelDoor>>();
        for (int i = 0; i < doors.Count; i++)
        {
            LevelDoor door = doors[i];
            if (door == null)
            {
                continue;
            }

            string groupKey = GetRandomDoorSelectionGroupKey(door, i);
            if (!doorGroups.TryGetValue(groupKey, out List<LevelDoor> groupDoors))
            {
                groupDoors = new List<LevelDoor>();
                doorGroups[groupKey] = groupDoors;
            }

            groupDoors.Add(door);
        }

        if (doorGroups.Count == 0)
        {
            return;
        }

        List<string> selectableGroupKeys = new List<string>();
        foreach (KeyValuePair<string, List<LevelDoor>> group in doorGroups)
        {
            bool groupContainsBlockedEntryDoor = false;
            if (blockedEntryDoors != null)
            {
                for (int doorIndex = 0; doorIndex < group.Value.Count; doorIndex++)
                {
                    if (blockedEntryDoors.Contains(group.Value[doorIndex]))
                    {
                        groupContainsBlockedEntryDoor = true;
                        break;
                    }
                }
            }

            if (!groupContainsBlockedEntryDoor)
            {
                selectableGroupKeys.Add(group.Key);
            }
        }

        if (selectableGroupKeys.Count == 0)
        {
            foreach (KeyValuePair<string, List<LevelDoor>> group in doorGroups)
            {
                selectableGroupKeys.Add(group.Key);
            }
        }

        if (selectableGroupKeys.Count == 0)
        {
            return;
        }

        int selectedGroupIndex = UnityEngine.Random.Range(0, selectableGroupKeys.Count);
        string selectedGroupKey = selectableGroupKeys[selectedGroupIndex];
        List<LevelDoor> selectedGroupDoors = doorGroups[selectedGroupKey];
        HashSet<LevelDoor> selectedDoorSet = new HashSet<LevelDoor>(selectedGroupDoors);

        int selectedConnectionIndex = UnityEngine.Random.Range(0, currentNode.connections.Count);
        LevelDoorRoute selectedRoute = BuildDoorRoute(currentNode.connections[selectedConnectionIndex]);

        if (blockedEntryDoors != null)
        {
            foreach (LevelDoor selectedGroupDoor in selectedGroupDoors)
            {
                if (selectedGroupDoor != null && blockedEntryDoors.Contains(selectedGroupDoor))
                {
                    // Fallback when all groups were entry-side candidates: keep progression possible.
                    selectedGroupDoor.SetEntryBlocked(false);
                }
            }
        }

        for (int i = 0; i < doors.Count; i++)
        {
            LevelDoor door = doors[i];
            if (door == null)
            {
                continue;
            }

            if (selectedDoorSet.Contains(door))
            {
                door.Configure(selectedRoute);
                routedDoorCount++;
            }
            else
            {
                door.ClearRoute(i);
            }
        }
    }

    private string GetRandomDoorSelectionGroupKey(LevelDoor door, int fallbackIndex)
    {
        if (door == null)
        {
            return "door-null-" + fallbackIndex;
        }

        string linkedPairGroupKey = TryGetLinkedPairDoorGroupKey(door.transform);
        if (!string.IsNullOrEmpty(linkedPairGroupKey))
        {
            return linkedPairGroupKey;
        }

        return "door-single-" + door.GetInstanceID();
    }

    private string TryGetLinkedPairDoorGroupKey(Transform doorTransform)
    {
        if (doorTransform == null)
        {
            return null;
        }

        string selfName = NormalizeName(doorTransform.name);
        if (IsLinkedPairDoorObjectName(selfName))
        {
            Transform selfParent = doorTransform.parent;
            if (selfParent != null)
            {
                // Sibling pair objects like "Door/Doors S" + "Door/Doors W" should count as one logical door.
                return "door-pair-sibling-group-" + selfParent.GetInstanceID();
            }
        }

        Transform parent = doorTransform.parent;
        if (parent != null && IsLinkedPairDoorObjectName(NormalizeName(parent.name)))
        {
            return "door-pair-parent-group-" + parent.GetInstanceID();
        }

        return null;
    }

    private bool IsLinkedPairDoorObjectName(string objectName)
    {
        string normalizedName = NormalizeName(objectName);
        return IsLinkedPairDoorNameAlias(normalizedName, "door s") ||
               IsLinkedPairDoorNameAlias(normalizedName, "door w") ||
               IsLinkedPairDoorNameAlias(normalizedName, "doors s") ||
               IsLinkedPairDoorNameAlias(normalizedName, "doors w");
    }

    private bool IsLinkedPairDoorNameAlias(string normalizedName, string alias)
    {
        return normalizedName == alias ||
               normalizedName.StartsWith(alias + " ", StringComparison.Ordinal) ||
               normalizedName.StartsWith(alias + "(", StringComparison.Ordinal) ||
               normalizedName.StartsWith(alias + "_", StringComparison.Ordinal);
    }

    private LevelDoorRoute BuildDoorRoute(GeneratedLevelConnection connection)
    {
        GeneratedLevelNode destination = _nodesById[connection.destinationNodeId];
        return new LevelDoorRoute
        {
            DoorIndex = connection.doorIndex,
            DestinationNodeId = destination.id,
            DestinationDepth = destination.depth,
            DestinationSceneName = destination.sceneName,
            DestinationLabel = GetDestinationLabel(destination)
        };
    }

    private string GetDestinationLabel(GeneratedLevelNode destination)
    {
        if (destination.id == 0)
        {
            return "Next Floor";
        }

        return "Floor " + GetFloorFromDepth(destination.depth) +
               " - Stage " + GetStageFromDepth(destination.depth);
    }

    private void SyncCurrentNodeToLoadedScene(string sceneName)
    {
        if (_nodesById.TryGetValue(_currentNodeId, out GeneratedLevelNode currentNode) &&
            SceneNameEquals(currentNode.sceneName, sceneName))
        {
            return;
        }

        if (SceneNameEquals(sceneName, LobbySceneName))
        {
            _currentNodeId = 0;
            return;
        }

        if (TryFindGeneratedNodeForScene(sceneName, out GeneratedLevelNode sceneNode))
        {
            _currentNodeId = sceneNode.id;
            return;
        }

        if (IsSceneConfiguredAsBoss(sceneName))
        {
            int targetBossFloor = GetFirstBossFloorAtOrAfter(CurrentFloor);
            if (targetBossFloor != CurrentFloor)
            {
                _currentFloor = targetBossFloor;
                GenerateNewRun();

                if (TryFindGeneratedNodeForScene(sceneName, out GeneratedLevelNode bossNode))
                {
                    _currentNodeId = bossNode.id;
                }
            }
        }
    }

    private GeneratedLevelNode CreateNode(int id, int depth, string sceneName)
    {
        return new GeneratedLevelNode
        {
            id = id,
            depth = depth,
            sceneName = sceneName
        };
    }

    private void BuildForwardConnections(System.Random random)
    {
        for (int nodeId = 0; nodeId < GeneratedRoomCount; nodeId++)
        {
            GeneratedLevelNode node = _nodesById[nodeId];
            List<int> possibleDestinations = GetPossibleForwardDestinations(nodeId);

            if (possibleDestinations.Count == 0)
            {
                continue;
            }

            int cappedMin = Mathf.Clamp(MinDoorChoices, 1, possibleDestinations.Count);
            int cappedMax = Mathf.Clamp(MaxDoorChoices, cappedMin, possibleDestinations.Count);
            int exitCount = random.Next(cappedMin, cappedMax + 1);

            AddConnection(node, nodeId + 1);

            while (node.connections.Count < exitCount)
            {
                int destinationId = possibleDestinations[random.Next(possibleDestinations.Count)];

                if (!HasConnection(node, destinationId))
                {
                    AddConnection(node, destinationId);
                }
            }
        }
    }

    private List<int> GetPossibleForwardDestinations(int nodeId)
    {
        List<int> destinations = new List<int>();
        int maxDestination = Mathf.Min(GeneratedRoomCount, nodeId + MaxForwardRoomSkip);

        for (int destinationId = nodeId + 1; destinationId <= maxDestination; destinationId++)
        {
            destinations.Add(destinationId);
        }

        return destinations;
    }

    private void ConnectLastStageToFloorTransition()
    {
        GeneratedLevelNode finalRoom = _nodesById[GeneratedRoomCount];
        AddConnection(finalRoom, 0);
    }

    private void AdvanceToNextFloor()
    {
        _currentFloor = Mathf.Max(1, _currentFloor + 1);
        GenerateNewRun();

        if (!_nodesById.TryGetValue(1, out GeneratedLevelNode nextFloorStart))
        {
            Debug.LogError("Unable to start next floor because stage 1 node is missing.");
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(nextFloorStart.sceneName))
        {
            Debug.LogError("Cannot load next-floor stage scene '" + nextFloorStart.sceneName + "'. Add it to Build Settings.");
            return;
        }

        _pendingNodeId = nextFloorStart.id;

        if (LogProgression)
        {
            Debug.Log("Advancing to Floor " + CurrentFloor + " starting at stage 1 (" + nextFloorStart.sceneName + ").");
        }

        SceneManager.LoadScene(nextFloorStart.sceneName, LoadSceneMode.Single);
    }

    private void AddConnection(GeneratedLevelNode source, int destinationNodeId)
    {
        source.connections.Add(new GeneratedLevelConnection
        {
            doorIndex = source.connections.Count,
            destinationNodeId = destinationNodeId
        });
    }

    private bool HasConnection(GeneratedLevelNode source, int destinationNodeId)
    {
        for (int i = 0; i < source.connections.Count; i++)
        {
            if (source.connections[i].destinationNodeId == destinationNodeId)
            {
                return true;
            }
        }

        return false;
    }

    private int CreateRunSeed()
    {
        int configuredSeed = FixedSeed;
        if (configuredSeed != 0)
        {
            return configuredSeed;
        }

        unchecked
        {
            int seed = Guid.NewGuid().GetHashCode();
            seed = (seed * 397) ^ Environment.TickCount;
            seed = (seed * 397) ^ DateTime.UtcNow.Ticks.GetHashCode();
            return seed != 0 ? seed : 1;
        }
    }

    private List<string> BuildStageSceneOrder(System.Random random, int stageCount, string previousScene)
    {
        List<string> orderedScenes = new List<string>();
        if (stageCount <= 0)
        {
            return orderedScenes;
        }

        List<string> nonBossScenes = new List<string>();
        List<string> bossScenes = new List<string>();
        PopulateStageScenePools(nonBossScenes, bossScenes);

        if (nonBossScenes.Count == 0 && bossScenes.Count == 0)
        {
            return orderedScenes;
        }

        bool isBossFloor = IsBossFloor(CurrentFloor);
        bool shouldPlaceBossAtEnd = keepBossStageAtEnd && isBossFloor && bossScenes.Count > 0;
        int nonBossStageCount = shouldPlaceBossAtEnd
            ? Mathf.Max(0, stageCount - 1)
            : stageCount;

        string lastScene = previousScene;
        List<string> primaryPool = new List<string>();

        if (nonBossScenes.Count > 0)
        {
            primaryPool.AddRange(nonBossScenes);
        }

        if (isBossFloor && !keepBossStageAtEnd && bossScenes.Count > 0)
        {
            for (int i = 0; i < bossScenes.Count; i++)
            {
                AddUniqueScene(primaryPool, bossScenes[i]);
            }
        }

        if (primaryPool.Count == 0 && bossScenes.Count > 0)
        {
            primaryPool.AddRange(bossScenes);
        }

        for (int stageIndex = 0; stageIndex < nonBossStageCount; stageIndex++)
        {
            string sceneName = PickRandomSceneFromPool(random, primaryPool, lastScene);
            orderedScenes.Add(sceneName);
            lastScene = sceneName;
        }

        if (shouldPlaceBossAtEnd && orderedScenes.Count < stageCount)
        {
            string bossScene = bossScenes[random.Next(bossScenes.Count)];
            orderedScenes.Add(bossScene);
            lastScene = bossScene;
        }

        while (orderedScenes.Count < stageCount)
        {
            string sceneName = PickRandomSceneFromPool(random, primaryPool, lastScene);
            orderedScenes.Add(sceneName);
            lastScene = sceneName;
        }

        return orderedScenes;
    }

    private void PopulateStageScenePools(List<string> nonBossScenes, List<string> bossScenes)
    {
        if (nonBossScenes == null || bossScenes == null)
        {
            return;
        }

        if (generationProfile != null && generationProfile.RoomScenes != null)
        {
            for (int i = 0; i < generationProfile.RoomScenes.Length; i++)
            {
                LevelSceneCandidate candidate = generationProfile.RoomScenes[i];
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.SceneName))
                {
                    continue;
                }

                if (candidate.RoomKind == LevelRoomKind.Boss)
                {
                    AddUniqueScene(bossScenes, candidate.SceneName);
                }
                else
                {
                    AddUniqueScene(nonBossScenes, candidate.SceneName);
                }
            }
        }

        if (nonBossScenes.Count > 0 || bossScenes.Count > 0)
        {
            return;
        }

        for (int i = 0; i < roomSceneNames.Length; i++)
        {
            string sceneName = roomSceneNames[i];
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                continue;
            }

            if (SceneNameEquals(sceneName, "Final Boss Level"))
            {
                AddUniqueScene(bossScenes, sceneName);
            }
            else
            {
                AddUniqueScene(nonBossScenes, sceneName);
            }
        }
    }

    private string PickRandomSceneFromPool(System.Random random, List<string> pool, string previousScene)
    {
        if (pool == null || pool.Count == 0)
        {
            return LobbySceneName;
        }

        if (pool.Count == 1)
        {
            return pool[0];
        }

        List<int> candidateIndices = new List<int>();
        for (int i = 0; i < pool.Count; i++)
        {
            if (!SceneNameEquals(pool[i], previousScene))
            {
                candidateIndices.Add(i);
            }
        }

        if (candidateIndices.Count == 0)
        {
            return pool[random.Next(pool.Count)];
        }

        int randomIndex = candidateIndices[random.Next(candidateIndices.Count)];
        return pool[randomIndex];
    }

    private string PickRoomScene(System.Random random, string previousScene, int depth, Dictionary<string, int> roomUseCounts)
    {
        if (TryPickProfileRoomScene(random, previousScene, depth, roomUseCounts, out string profileSceneName))
        {
            return profileSceneName;
        }

        if (useSequentialFallbackRoomOrder && roomSceneNames.Length > 0)
        {
            int fallbackIndex = Mathf.Clamp(depth - 1, 0, roomSceneNames.Length - 1);
            string fallbackScene = roomSceneNames[fallbackIndex];
            if (!string.IsNullOrWhiteSpace(fallbackScene))
            {
                return fallbackScene;
            }
        }

        List<string> validRooms = new List<string>();

        for (int i = 0; i < roomSceneNames.Length; i++)
        {
            if (!string.IsNullOrEmpty(roomSceneNames[i]))
            {
                validRooms.Add(roomSceneNames[i]);
            }
        }

        if (validRooms.Count == 0)
        {
            return LobbySceneName;
        }

        if (validRooms.Count == 1)
        {
            return validRooms[0];
        }

        List<string> repeatSafeRooms = new List<string>();
        for (int i = 0; i < validRooms.Count; i++)
        {
            if (!SceneNameEquals(validRooms[i], previousScene))
            {
                repeatSafeRooms.Add(validRooms[i]);
            }
        }

        List<string> pickPool = repeatSafeRooms.Count > 0 ? repeatSafeRooms : validRooms;
        pickPool = KeepLeastUsedRoomNames(pickPool, roomUseCounts);

        for (int attempt = 0; attempt < 8; attempt++)
        {
            string candidate = pickPool[random.Next(pickPool.Count)];
            if (!SceneNameEquals(candidate, previousScene))
            {
                return candidate;
            }
        }

        return pickPool[random.Next(pickPool.Count)];
    }

    private bool TryPickProfileRoomScene(System.Random random, string previousScene, int depth, Dictionary<string, int> roomUseCounts, out string sceneName)
    {
        sceneName = null;

        if (generationProfile == null || generationProfile.RoomScenes == null)
        {
            return false;
        }

        List<LevelSceneCandidate> candidates = new List<LevelSceneCandidate>();
        List<LevelSceneCandidate> repeatSafeCandidates = new List<LevelSceneCandidate>();

        for (int i = 0; i < generationProfile.RoomScenes.Length; i++)
        {
            LevelSceneCandidate candidate = generationProfile.RoomScenes[i];
            if (candidate == null || !candidate.IsValidForDepth(depth))
            {
                continue;
            }

            candidates.Add(candidate);

            if (candidate.CanRepeatConsecutively || !SceneNameEquals(candidate.SceneName, previousScene))
            {
                repeatSafeCandidates.Add(candidate);
            }
        }

        List<LevelSceneCandidate> pickPool = repeatSafeCandidates.Count > 0 ? repeatSafeCandidates : candidates;
        pickPool = KeepLeastUsedCandidates(pickPool, roomUseCounts);
        if (pickPool.Count == 0)
        {
            return false;
        }

        int totalWeight = 0;
        for (int i = 0; i < pickPool.Count; i++)
        {
            totalWeight += pickPool[i].Weight;
        }

        if (totalWeight <= 0)
        {
            return false;
        }

        int roll = random.Next(0, totalWeight);
        for (int i = 0; i < pickPool.Count; i++)
        {
            roll -= pickPool[i].Weight;
            if (roll < 0)
            {
                sceneName = pickPool[i].SceneName;
                return true;
            }
        }

        sceneName = pickPool[pickPool.Count - 1].SceneName;
        return true;
    }

    private List<LevelSceneCandidate> KeepLeastUsedCandidates(List<LevelSceneCandidate> candidates, Dictionary<string, int> roomUseCounts)
    {
        if (candidates.Count <= 1)
        {
            return candidates;
        }

        int lowestUseCount = int.MaxValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            lowestUseCount = Mathf.Min(lowestUseCount, GetRoomUseCount(roomUseCounts, candidates[i].SceneName));
        }

        List<LevelSceneCandidate> leastUsedCandidates = new List<LevelSceneCandidate>();
        for (int i = 0; i < candidates.Count; i++)
        {
            if (GetRoomUseCount(roomUseCounts, candidates[i].SceneName) == lowestUseCount)
            {
                leastUsedCandidates.Add(candidates[i]);
            }
        }

        return leastUsedCandidates;
    }

    private List<string> KeepLeastUsedRoomNames(List<string> roomNames, Dictionary<string, int> roomUseCounts)
    {
        if (roomNames.Count <= 1)
        {
            return roomNames;
        }

        int lowestUseCount = int.MaxValue;
        for (int i = 0; i < roomNames.Count; i++)
        {
            lowestUseCount = Mathf.Min(lowestUseCount, GetRoomUseCount(roomUseCounts, roomNames[i]));
        }

        List<string> leastUsedRooms = new List<string>();
        for (int i = 0; i < roomNames.Count; i++)
        {
            if (GetRoomUseCount(roomUseCounts, roomNames[i]) == lowestUseCount)
            {
                leastUsedRooms.Add(roomNames[i]);
            }
        }

        return leastUsedRooms;
    }

    private int GetRoomUseCount(Dictionary<string, int> roomUseCounts, string sceneName)
    {
        if (roomUseCounts == null || string.IsNullOrWhiteSpace(sceneName))
        {
            return 0;
        }

        return roomUseCounts.TryGetValue(sceneName, out int useCount) ? useCount : 0;
    }

    private void IncrementRoomUseCount(Dictionary<string, int> roomUseCounts, string sceneName)
    {
        if (roomUseCounts == null || string.IsNullOrWhiteSpace(sceneName))
        {
            return;
        }

        roomUseCounts[sceneName] = GetRoomUseCount(roomUseCounts, sceneName) + 1;
    }

    private string BuildGraphLog()
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("Generated level run floor: " + CurrentFloor + " seed: " + _activeSeed);

        for (int i = 0; i <= GeneratedRoomCount; i++)
        {
            GeneratedLevelNode node = _nodesById[i];
            builder.Append("Node ");
            builder.Append(node.id);
            builder.Append(" [");
            builder.Append(node.sceneName);
            builder.Append("] -> ");

            if (node.connections.Count == 0)
            {
                builder.Append("none");
            }

            for (int connectionIndex = 0; connectionIndex < node.connections.Count; connectionIndex++)
            {
                GeneratedLevelConnection connection = node.connections[connectionIndex];
                GeneratedLevelNode destination = _nodesById[connection.destinationNodeId];

                if (connectionIndex > 0)
                {
                    builder.Append(", ");
                }

                builder.Append("Door ");
                builder.Append(connection.doorIndex);
                builder.Append(": ");
                builder.Append(destination.sceneName);
                builder.Append(" (node ");
                builder.Append(destination.id);
                builder.Append(")");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private bool SceneNameEquals(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private string NormalizeName(string value)
    {
        return string.IsNullOrEmpty(value) ? string.Empty : value.Trim().ToLowerInvariant();
    }

    private void AddUniqueScene(List<string> sceneNames, string sceneName)
    {
        if (sceneNames == null || string.IsNullOrWhiteSpace(sceneName))
        {
            return;
        }

        for (int i = 0; i < sceneNames.Count; i++)
        {
            if (SceneNameEquals(sceneNames[i], sceneName))
            {
                return;
            }
        }

        sceneNames.Add(sceneName);
    }

    private bool IsBossFloor(int floor)
    {
        int firstFloor = Mathf.Max(1, firstBossFloor);
        int interval = Mathf.Max(1, bossFloorInterval);
        int clampedFloor = Mathf.Max(1, floor);

        if (clampedFloor < firstFloor)
        {
            return false;
        }

        return (clampedFloor - firstFloor) % interval == 0;
    }

    private int GetFirstBossFloorAtOrAfter(int floor)
    {
        int firstFloor = Mathf.Max(1, firstBossFloor);
        int interval = Mathf.Max(1, bossFloorInterval);
        int clampedFloor = Mathf.Max(1, floor);

        if (clampedFloor <= firstFloor)
        {
            return firstFloor;
        }

        int distance = clampedFloor - firstFloor;
        int steps = (distance + interval - 1) / interval;
        return firstFloor + steps * interval;
    }

    private bool TryFindGeneratedNodeForScene(string sceneName, out GeneratedLevelNode nodeMatch)
    {
        nodeMatch = null;
        bool preferHighestNodeId = IsSceneConfiguredAsBoss(sceneName);

        for (int nodeId = 1; nodeId <= GeneratedRoomCount; nodeId++)
        {
            if (!_nodesById.TryGetValue(nodeId, out GeneratedLevelNode candidate) ||
                !SceneNameEquals(candidate.sceneName, sceneName))
            {
                continue;
            }

            if (nodeMatch == null)
            {
                nodeMatch = candidate;
                continue;
            }

            if (preferHighestNodeId && candidate.id > nodeMatch.id)
            {
                nodeMatch = candidate;
            }
            else if (!preferHighestNodeId && candidate.id < nodeMatch.id)
            {
                nodeMatch = candidate;
            }
        }

        return nodeMatch != null;
    }

    private bool IsSceneConfiguredAsBoss(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            return false;
        }

        if (generationProfile != null && generationProfile.RoomScenes != null)
        {
            for (int i = 0; i < generationProfile.RoomScenes.Length; i++)
            {
                LevelSceneCandidate candidate = generationProfile.RoomScenes[i];
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.SceneName))
                {
                    continue;
                }

                if (SceneNameEquals(candidate.SceneName, sceneName))
                {
                    return candidate.RoomKind == LevelRoomKind.Boss;
                }
            }
        }

        return SceneNameEquals(sceneName, "Final Boss Level");
    }

    private int ComposeFloorDepth(int floor, int stage)
    {
        int clampedFloor = Mathf.Max(1, floor);
        int clampedStage = Mathf.Clamp(stage, 0, GeneratedRoomCount);
        return ((clampedFloor - 1) * GeneratedRoomCount) + clampedStage;
    }

    private int GetFloorFromDepth(int depth)
    {
        if (depth <= 0)
        {
            return CurrentFloor;
        }

        return ((depth - 1) / GeneratedRoomCount) + 1;
    }

    private int GetStageFromDepth(int depth)
    {
        if (depth <= 0)
        {
            return 0;
        }

        return ((depth - 1) % GeneratedRoomCount) + 1;
    }

    private void TryAutoAdvanceWithoutDoors(GeneratedLevelNode node, int availableDoorCount, string reason)
    {
        if (!AutoAdvanceWhenNoDoors || node == null || availableDoorCount > 0)
        {
            return;
        }

        if (!SceneNameEquals(SceneManager.GetActiveScene().name, node.sceneName))
        {
            return;
        }

        if (node.connections.Count != 1)
        {
            if (LogProgression && node.connections.Count > 1)
            {
                Debug.LogWarning("Auto-advance is disabled in scene '" + node.sceneName +
                                 "' because it has no generated doors and multiple routes.");
            }

            return;
        }

        // Non-lobby nodes should only auto-advance after the room is cleared.
        if (node.id != 0 && !IsCurrentNodeCleared)
        {
            return;
        }

        if (LogProgression)
        {
            Debug.Log("Auto-advancing from node " + node.id + " (" + node.sceneName +
                      ") because " + reason + ".");
        }

        TravelTo(BuildDoorRoute(node.connections[0]));
    }

    private void ResetPersistentPlayerRunState()
    {
        if (_persistentPlayer != null)
        {
            _persistentPlayer.ResetTemporaryRunState();
        }
    }

    private void LoadDefaultProfileIfNeeded()
    {
        if (generationProfile == null)
        {
            generationProfile = Resources.Load<LevelGenerationProfile>("LevelGeneration/Default Level Generation Profile");
        }
    }

    public void SetGenerationProfile(LevelGenerationProfile profile, bool regenerateRun)
    {
        generationProfile = profile;
        if (regenerateRun)
        {
            GenerateNewRun();
            ConfigureDoorsForCurrentScene(SceneManager.GetActiveScene().name);
        }
    }

    public void ApplyRuntimeOverrides(LevelGenerationRuntimeOverrides overrides, bool regenerateRun)
    {
        _runtimeOverrides = overrides;
        if (regenerateRun)
        {
            GenerateNewRun();
            ConfigureDoorsForCurrentScene(SceneManager.GetActiveScene().name);
        }
    }

    public void ClearRuntimeOverrides(bool regenerateRun)
    {
        _runtimeOverrides = default;
        if (regenerateRun)
        {
            GenerateNewRun();
            ConfigureDoorsForCurrentScene(SceneManager.GetActiveScene().name);
        }
    }

    private LevelRoomDefinition GetRoomDefinition(Scene scene)
    {
        LevelRoomDefinition[] definitions = FindObjectsByType<LevelRoomDefinition>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < definitions.Length; i++)
        {
            if (definitions[i] != null && definitions[i].gameObject.scene == scene)
            {
                return definitions[i];
            }
        }

        return null;
    }

    private string LobbySceneName => generationProfile != null ? generationProfile.LobbySceneName : lobbySceneName;

    private int GeneratedRoomCount => _runtimeOverrides.overrideGeneratedRoomCount
        ? Mathf.Max(1, _runtimeOverrides.generatedRoomCount)
        : generationProfile != null ? generationProfile.GeneratedRoomCount : Mathf.Max(1, generatedRoomCount);

    private int MinDoorChoices => _runtimeOverrides.overrideDoorChoices
        ? Mathf.Max(1, _runtimeOverrides.minDoorChoices)
        : generationProfile != null ? generationProfile.MinDoorChoices : Mathf.Max(1, minDoorChoices);

    private int MaxDoorChoices => _runtimeOverrides.overrideDoorChoices
        ? Mathf.Max(MinDoorChoices, _runtimeOverrides.maxDoorChoices)
        : generationProfile != null ? generationProfile.MaxDoorChoices : Mathf.Max(MinDoorChoices, maxDoorChoices);

    private int MaxForwardRoomSkip => _runtimeOverrides.overrideMaxForwardRoomSkip
        ? Mathf.Max(1, _runtimeOverrides.maxForwardRoomSkip)
        : generationProfile != null ? generationProfile.MaxForwardRoomSkip : Mathf.Max(1, maxForwardRoomSkip);

    private int FixedSeed => _runtimeOverrides.overrideFixedSeed
        ? _runtimeOverrides.fixedSeed
        : generationProfile != null ? generationProfile.FixedSeed : fixedSeed;

    private bool LockDoorsWhileRoomActive => generationProfile != null ? generationProfile.LockDoorsWhileRoomActive : lockDoorsWhileRoomActive;
    private bool AutoBindSceneDoors => generationProfile != null ? generationProfile.AutoBindSceneDoors : autoBindSceneDoors;
    private bool AutoAdvanceWhenNoDoors => generationProfile != null ? generationProfile.AutoAdvanceWhenNoDoors : autoAdvanceWhenNoDoors;
    private bool UnlockCurrentLevelAfterClear => generationProfile != null ? generationProfile.UnlockCurrentLevelAfterClear : unlockCurrentLevelAfterClear;
    private bool UnlockLevelsWithoutSpawners => generationProfile != null ? generationProfile.UnlockLevelsWithoutSpawners : unlockLevelsWithoutSpawners;
    private bool DisableSpawnersAfterLevelClear => generationProfile != null ? generationProfile.DisableSpawnersAfterLevelClear : disableSpawnersAfterLevelClear;
    private bool LogGeneratedGraph => generationProfile != null ? generationProfile.LogGeneratedGraph : logGeneratedGraph;
    private bool LogDoorBinding => generationProfile != null ? generationProfile.LogDoorBinding : logDoorBinding;
    private bool LogProgression => generationProfile != null ? generationProfile.LogProgression : logProgression;
}

public class SceneCameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = new Vector3(-14.5f, 14.3f, -14.5f);
    [SerializeField] private Vector3 eulerAngles = new Vector3(35f, 45f, 0f);
    [SerializeField, Min(0f)] private float followSharpness = 18f;

    public void Configure(Transform followTarget, bool snap)
    {
        target = followTarget;

        if (snap)
        {
            SnapToTarget();
        }
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        Vector3 targetPosition = target.position + offset;
        float t = followSharpness <= 0f ? 1f : 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, targetPosition, t);
        transform.rotation = Quaternion.Euler(eulerAngles);
    }

    private void SnapToTarget()
    {
        if (target == null)
        {
            return;
        }

        transform.position = target.position + offset;
        transform.rotation = Quaternion.Euler(eulerAngles);
    }
}
