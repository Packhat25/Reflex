using System;
using System.Collections.Generic;
using UnityEngine;

public enum PlayerEmotionState
{
    Calm,
    Aggressive
}

[Serializable]
public struct EmotionProfileSnapshot
{
    public PlayerEmotionState state;
    public float aggressionScore;
    public float recentAggressionScore;
    public float confidence;
    public float damagePressureScore;
    public float combatIntentScore;
    public float movementPressureScore;
    public float timePressureScore;
    public float damageTaken;
    public int deathCount;
    public int enemiesEncountered;
    public int enemiesEngaged;
    public int attacksPerformed;
    public int enemyHits;
    public float effectiveEnemyHits;
    public float timeRunning;
    public float timeIdle;
    public float averageMovementSpeed;
    public int activeSpawnerCount;
    public float currentRoomTime;
    public float lastRoomClearTime;
}

[Serializable]
public struct EmotionRoomReport
{
    public int roomNumber;
    public PlayerEmotionState emotionBefore;
    public PlayerEmotionState emotionAfter;
    public float scoreBefore;
    public float scoreAfter;
    public float duration;
    public int spawnerCount;
    public int baseSpawnCount;
    public int adjustedSpawnCount;
    public float damageTaken;
    public int deathCount;
    public int enemiesEncountered;
    public int enemiesEngaged;
    public int attacksPerformed;
    public int enemyHits;
    public float effectiveEnemyHits;
    public float timeRunning;
    public float timeIdle;
    public float averageMovementSpeed;
}

[Serializable]
public struct EmotionRoomStartReport
{
    public int roomNumber;
    public int activeSpawnerCount;
    public PlayerEmotionState emotionState;
    public float aggressionScore;
    public float confidence;
}

public class EmotionEngine : MonoBehaviour
{
    public static event Action<PlayerEmotionState, EmotionProfileSnapshot> EmotionChanged;
    public static event Action<EmotionProfileSnapshot> EmotionProfileUpdated;
    public static event Action<EmotionRoomStartReport> RoomStarted;
    public static event Action<EmotionRoomReport> RoomEvaluated;

    private sealed class ActiveRoomContributor
    {
        public string name;
        public int baseSpawnCount;
        public int adjustedSpawnCount;
    }

    private static EmotionEngine _instance;

    public static bool HasInstance => _instance != null;

    public static EmotionEngine Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<EmotionEngine>();

                if (_instance == null)
                {
                    GameObject emotionEngineObject = new GameObject("Emotion Engine");
                    _instance = emotionEngineObject.AddComponent<EmotionEngine>();
                }
            }

            return _instance;
        }
    }

    [Header("Emotion State")]
    [SerializeField] private PlayerEmotionState startingEmotion = PlayerEmotionState.Calm;
    [SerializeField, Range(0f, 1f)] private float aggressiveThreshold = 0.67f;
    [SerializeField, Range(0f, 1f)] private float calmThreshold = 0.40f;
    [SerializeField, Range(0f, 1f)] private float scoreSmoothing = 0.35f;
    [SerializeField, Range(0f, 1f)] private float aggressionRiseSmoothing = 0.18f;
    [SerializeField, Range(0f, 1f)] private float aggressionFallSmoothing = 0.42f;
    [SerializeField] private float evaluationInterval = 1f;
    [SerializeField] private bool logEmotionChanges = true;

    [Header("Aggression Tempo")]
    [SerializeField, Min(0f)] private float calmDecayDelay = 1.2f;
    [SerializeField, Range(0f, 0.25f)] private float calmDecayPerSecond = 0.04f;
    [SerializeField, Range(0.1f, 1f)] private float attackIntentScale = 0.68f;
    [SerializeField, Range(0.1f, 1f)] private float hitIntentScale = 0.62f;

    [Header("Forgiveness Tuning")]
    [SerializeField, Range(0f, 1f)] private float passiveRecoveryBoost = 0.22f;
    [SerializeField, Range(0f, 0.25f)] private float passiveForgivenessBias = 0.015f;

    [Header("Expected Values")]
    [SerializeField] private float expectedDamageTaken = 50f;
    [SerializeField] private float expectedEnemyEncounters = 8f;
    [SerializeField] private float expectedAttacks = 20f;
    [SerializeField] private float expectedAverageMovementSpeed = 5f;
    [SerializeField] private float expectedRoomClearTime = 120f;
    [SerializeField] private float expectedDeaths = 2f;
    [SerializeField, Min(0.1f)] private float expectedAttacksPerEncounter = 2.25f;
    [SerializeField, Min(5f)] private float expectedDecisionWindowSeconds = 45f;

    [Header("Recent Behavior Tuning")]
    [SerializeField, Range(0f, 1f)] private float recentBehaviorWeight = 0.62f;
    [SerializeField] private float expectedRoomDamageTaken = 20f;
    [SerializeField] private float expectedRoomEnemyEncounters = 4f;
    [SerializeField] private float expectedRoomAttacks = 10f;
    [SerializeField] private float expectedRoomMovementSpeed = 4f;
    [SerializeField] private float expectedRoomDeaths = 1f;
    [SerializeField, Min(1f)] private float minimumRateSampleWindow = 12f;
    [SerializeField, Range(0f, 1f)] private float neutralRateScore = 0.5f;
    [SerializeField] private float minimumEvidenceForChange = 0.42f;

    [Header("Aggression Qualification")]
    [SerializeField, Min(1)] private int minimumAggressiveAttacks = 7;
    [SerializeField, Min(1)] private int minimumAggressiveEnemiesEncountered = 3;
    [SerializeField, Min(1)] private int minimumAggressiveEnemiesEngaged = 3;
    [SerializeField, Min(2f)] private float minimumAggressiveWindowSeconds = 16f;
    [SerializeField, Range(0f, 1f)] private float earlyBurstAggressionCap = 0.48f;
    [SerializeField, Range(0f, 1f)] private float lowQualificationCalmBonus = 0.28f;

    [Header("Adaptive Spawning")]
    [SerializeField] private float aggressiveSpawnMultiplier = 1.35f;
    [SerializeField] private float calmSpawnMultiplier = 0.85f;

    [Header("Aggression Anti-Spike")]
    [SerializeField] private bool useMultiHitDiminishingReturns = true;
    [SerializeField, Min(0.05f)] private float multiHitBurstWindow = 0.45f;
    [SerializeField, Min(0f)] private float additionalHitFalloff = 0.85f;
    [SerializeField, Min(0.2f)] private float maxEffectiveHitsPerAttack = 1.6f;

    [Header("Progression Stability")]
    [SerializeField] private bool rebaseTelemetryOnLevelEntered = true;
    [SerializeField, Range(0f, 1f)] private float levelCarryoverFactor = 0.35f;
    [SerializeField] private bool clearRoomStateOnLevelEntered = true;

    [Header("Debug")]
    [SerializeField] private bool createDebugHud = true;

    public PlayerEmotionState CurrentEmotion { get; private set; }
    public float AggressionScore { get; private set; }
    public float RecentAggressionScore { get; private set; }
    public float Confidence { get; private set; }
    public EmotionProfileSnapshot CurrentSnapshot => BuildSnapshot();
    public EmotionRoomReport LastRoomReport { get; private set; }
    public bool IsRoomActive => _activeRoomContributors.Count > 0;
    public int ActiveSpawnerCount => _activeRoomContributors.Count;

    private readonly HashSet<int> _encounteredEnemyIds = new HashSet<int>();
    private readonly HashSet<int> _engagedEnemyIds = new HashSet<int>();
    private readonly Dictionary<int, ActiveRoomContributor> _activeRoomContributors = new Dictionary<int, ActiveRoomContributor>();
    private float _damageTaken;
    private int _deathCount;
    private int _attacksPerformed;
    private int _enemyHits;
    private float _effectiveEnemyHits;
    private float _timeRunning;
    private float _timeIdle;
    private float _movementSpeedTotal;
    private int _movementSamples;
    private float _currentRoomTime;
    private float _lastRoomClearTime;
    private bool _roomTimerRunning;
    private float _evaluationTimer;
    private int _roomsCleared;
    private int _currentRoomBaseSpawnCount;
    private int _currentRoomAdjustedSpawnCount;
    private int _currentRoomSpawnerCount;
    private int _nextAnonymousRoomId = -1;
    private EmotionProfileSnapshot _roomStartSnapshot;
    private PlayerEmotionState _roomStartEmotion;
    private float _roomStartScore;
    private float _roomStartMovementSpeedTotal;
    private int _roomStartMovementSamples;
    private float _damagePressureScore;
    private float _combatIntentScore;
    private float _movementPressureScore;
    private float _timePressureScore;
    private float _lastCombatIntentTime;
    private float _lastEmotionEvaluationTime;
    private int _hitsInCurrentAttack;
    private float _effectiveHitsInCurrentAttack;
    private float _lastAttackStartedTime;
    private float _lastEnemyHitTime;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        CurrentEmotion = startingEmotion;
        AggressionScore = startingEmotion == PlayerEmotionState.Aggressive ? aggressiveThreshold : calmThreshold;
        RecentAggressionScore = AggressionScore;
        _evaluationTimer = evaluationInterval;
        _lastCombatIntentTime = Time.time;
        _lastEmotionEvaluationTime = Time.time;

        if (createDebugHud)
        {
            EmotionDebugHUD.EnsureExists();
        }

        _ = EmotionDirector.Instance;
    }

    private void Update()
    {
        if (_roomTimerRunning)
        {
            _currentRoomTime += Time.deltaTime;
        }

        if (!IsRoomActive)
        {
            _evaluationTimer = evaluationInterval;
            return;
        }

        _evaluationTimer -= Time.deltaTime;
        if (_evaluationTimer <= 0f)
        {
            _evaluationTimer = evaluationInterval;
            EvaluateEmotion(false);
        }
    }

    private void OnEnable()
    {
        LevelRunManager.LevelEntered += HandleLevelEntered;
    }

    private void OnDisable()
    {
        LevelRunManager.LevelEntered -= HandleLevelEntered;
    }

    private void OnDestroy()
    {
        LevelRunManager.LevelEntered -= HandleLevelEntered;

        if (_instance == this)
        {
            _instance = null;
        }
    }

    public void BeginRoom()
    {
        BeginRoom(0, 0);
    }

    public int BeginRoom(int baseSpawnCount, int adjustedSpawnCount)
    {
        return BeginRoom(CreateAnonymousRoomId(), "Room", baseSpawnCount, adjustedSpawnCount);
    }

    public int BeginRoom(UnityEngine.Object source, int baseSpawnCount, int adjustedSpawnCount)
    {
        if (source == null)
        {
            return BeginRoom(baseSpawnCount, adjustedSpawnCount);
        }

        return BeginRoom(source.GetInstanceID(), source.name, baseSpawnCount, adjustedSpawnCount);
    }

    public void RecordRoomCleared()
    {
        if (_activeRoomContributors.Count == 0)
        {
            return;
        }

        if (_activeRoomContributors.Count > 1)
        {
            Debug.LogWarning("EmotionEngine.RecordRoomCleared() was called without a source while multiple spawners are active. Use RecordRoomCleared(source) so the correct wave can be cleared.");
            return;
        }

        int onlySourceId = 0;
        foreach (int sourceId in _activeRoomContributors.Keys)
        {
            onlySourceId = sourceId;
            break;
        }

        RecordRoomCleared(onlySourceId);
    }

    public void RecordRoomCleared(UnityEngine.Object source)
    {
        if (source == null)
        {
            RecordRoomCleared();
            return;
        }

        RecordRoomCleared(source.GetInstanceID());
    }

    public void RecordDamageTaken(float amount)
    {
        if (!IsRoomActive)
        {
            return;
        }

        if (amount <= 0f)
        {
            return;
        }

        _damageTaken += amount;
        _lastCombatIntentTime = Time.time;
        EvaluateEmotion(false);
    }

    public void RecordDeath()
    {
        if (!IsRoomActive)
        {
            return;
        }

        _deathCount++;
        _lastCombatIntentTime = Time.time;
        EvaluateEmotion(true);
    }

    public void RecordEnemyEncounter(EnemyController enemy)
    {
        if (!IsRoomActive)
        {
            return;
        }

        if (enemy == null)
        {
            return;
        }

        if (_encounteredEnemyIds.Add(enemy.GetInstanceID()))
        {
            EvaluateEmotion(false);
        }
    }

    public void RecordAttackStarted()
    {
        if (!IsRoomActive)
        {
            return;
        }

        _attacksPerformed++;
        _lastCombatIntentTime = Time.time;
        _lastAttackStartedTime = Time.time;
        _hitsInCurrentAttack = 0;
        _effectiveHitsInCurrentAttack = 0f;
        EvaluateEmotion(false);
    }

    public void RecordEnemyHit(float damage)
    {
        RecordEnemyHit(damage, null);
    }

    public void RecordEnemyHit(float damage, EnemyController enemy)
    {
        if (!IsRoomActive)
        {
            return;
        }

        if (damage <= 0f)
        {
            return;
        }

        _enemyHits++;
        if (enemy != null)
        {
            _engagedEnemyIds.Add(enemy.GetInstanceID());
        }
        _lastCombatIntentTime = Time.time;
        _effectiveEnemyHits += CalculateEffectiveHitContribution();
        _lastEnemyHitTime = Time.time;
        EvaluateEmotion(false);
    }

    public void RecordMovement(float speed, bool isMoving, bool isIdle)
    {
        if (!IsRoomActive)
        {
            return;
        }

        float deltaTime = Time.deltaTime;

        if (isMoving)
        {
            _timeRunning += deltaTime;
        }

        if (isIdle)
        {
            _timeIdle += deltaTime;
        }

        _movementSpeedTotal += Mathf.Max(0f, speed);
        _movementSamples++;
    }

    public int GetRecommendedSpawnCount(int baseSpawnCount)
    {
        if (baseSpawnCount <= 0)
        {
            return 0;
        }

        if (CurrentEmotion == PlayerEmotionState.Aggressive)
        {
            return Mathf.Max(1, Mathf.CeilToInt(baseSpawnCount * aggressiveSpawnMultiplier));
        }

        return Mathf.Max(1, Mathf.RoundToInt(baseSpawnCount * calmSpawnMultiplier));
    }

    public void ResetProfile()
    {
        _encounteredEnemyIds.Clear();
        _engagedEnemyIds.Clear();
        _damageTaken = 0f;
        _deathCount = 0;
        _attacksPerformed = 0;
        _enemyHits = 0;
        _effectiveEnemyHits = 0f;
        _timeRunning = 0f;
        _timeIdle = 0f;
        _movementSpeedTotal = 0f;
        _movementSamples = 0;
        _currentRoomTime = 0f;
        _lastRoomClearTime = 0f;
        _roomTimerRunning = false;
        _activeRoomContributors.Clear();
        _roomsCleared = 0;
        _currentRoomBaseSpawnCount = 0;
        _currentRoomAdjustedSpawnCount = 0;
        _currentRoomSpawnerCount = 0;
        LastRoomReport = default;
        CurrentEmotion = startingEmotion;
        AggressionScore = startingEmotion == PlayerEmotionState.Aggressive ? aggressiveThreshold : calmThreshold;
        RecentAggressionScore = AggressionScore;
        Confidence = 0f;
        _damagePressureScore = 0f;
        _combatIntentScore = 0f;
        _movementPressureScore = 0f;
        _timePressureScore = 0f;
        _lastCombatIntentTime = Time.time;
        _lastEmotionEvaluationTime = Time.time;
        _hitsInCurrentAttack = 0;
        _effectiveHitsInCurrentAttack = 0f;
        _lastAttackStartedTime = 0f;
        _lastEnemyHitTime = 0f;
        EmotionChanged?.Invoke(CurrentEmotion, BuildSnapshot());
    }

    private int BeginRoom(int sourceId, string sourceName, int baseSpawnCount, int adjustedSpawnCount)
    {
        if (_activeRoomContributors.Count == 0)
        {
            StartRoomWindow();
        }

        int sanitizedBaseCount = Mathf.Max(0, baseSpawnCount);
        int sanitizedAdjustedCount = Mathf.Max(0, adjustedSpawnCount);

        if (_activeRoomContributors.TryGetValue(sourceId, out ActiveRoomContributor existingContributor))
        {
            _currentRoomBaseSpawnCount -= existingContributor.baseSpawnCount;
            _currentRoomAdjustedSpawnCount -= existingContributor.adjustedSpawnCount;
        }
        else
        {
            _currentRoomSpawnerCount++;
        }

        _activeRoomContributors[sourceId] = new ActiveRoomContributor
        {
            name = string.IsNullOrWhiteSpace(sourceName) ? "Spawner" : sourceName,
            baseSpawnCount = sanitizedBaseCount,
            adjustedSpawnCount = sanitizedAdjustedCount
        };

        _currentRoomBaseSpawnCount += sanitizedBaseCount;
        _currentRoomAdjustedSpawnCount += sanitizedAdjustedCount;

        return sourceId;
    }

    private void StartRoomWindow()
    {
        _currentRoomTime = 0f;
        _roomTimerRunning = true;
        _currentRoomBaseSpawnCount = 0;
        _currentRoomAdjustedSpawnCount = 0;
        _currentRoomSpawnerCount = 0;
        _roomStartSnapshot = BuildSnapshot();
        _roomStartEmotion = CurrentEmotion;
        _roomStartScore = AggressionScore;
        _roomStartMovementSpeedTotal = _movementSpeedTotal;
        _roomStartMovementSamples = _movementSamples;

        RoomStarted?.Invoke(new EmotionRoomStartReport
        {
            roomNumber = _roomsCleared + 1,
            activeSpawnerCount = _activeRoomContributors.Count,
            emotionState = CurrentEmotion,
            aggressionScore = AggressionScore,
            confidence = Confidence
        });
    }

    private void RecordRoomCleared(int sourceId)
    {
        if (!_activeRoomContributors.Remove(sourceId))
        {
            return;
        }

        if (_activeRoomContributors.Count > 0)
        {
            return;
        }

        CompleteRoomWindow();
    }

    private void CompleteRoomWindow()
    {
        if (!_roomTimerRunning)
        {
            return;
        }

        float clearDuration = _currentRoomTime;
        _lastRoomClearTime = clearDuration;
        _roomTimerRunning = false;
        EvaluateEmotion(true);

        _roomsCleared++;
        LastRoomReport = BuildRoomReport(clearDuration);
        RoomEvaluated?.Invoke(LastRoomReport);

        if (logEmotionChanges)
        {
            Debug.Log($"Room {_roomsCleared} evaluated across {_currentRoomSpawnerCount} spawner(s): {LastRoomReport.emotionBefore} -> {LastRoomReport.emotionAfter} ({LastRoomReport.scoreBefore:0.00} -> {LastRoomReport.scoreAfter:0.00})");
        }

        _currentRoomTime = 0f;
        _currentRoomBaseSpawnCount = 0;
        _currentRoomAdjustedSpawnCount = 0;
        _currentRoomSpawnerCount = 0;
    }

    private int CreateAnonymousRoomId()
    {
        return _nextAnonymousRoomId--;
    }

    private EmotionRoomReport BuildRoomReport(float clearDuration)
    {
        float roomMovementSpeedTotal = _movementSpeedTotal - _roomStartMovementSpeedTotal;
        int roomMovementSamples = _movementSamples - _roomStartMovementSamples;
        float roomAverageSpeed = roomMovementSamples > 0 ? roomMovementSpeedTotal / roomMovementSamples : 0f;

        return new EmotionRoomReport
        {
            roomNumber = _roomsCleared,
            emotionBefore = _roomStartEmotion,
            emotionAfter = CurrentEmotion,
            scoreBefore = _roomStartScore,
            scoreAfter = AggressionScore,
            duration = clearDuration,
            spawnerCount = _currentRoomSpawnerCount,
            baseSpawnCount = _currentRoomBaseSpawnCount,
            adjustedSpawnCount = _currentRoomAdjustedSpawnCount,
            damageTaken = _damageTaken - _roomStartSnapshot.damageTaken,
            deathCount = _deathCount - _roomStartSnapshot.deathCount,
            enemiesEncountered = _encounteredEnemyIds.Count - _roomStartSnapshot.enemiesEncountered,
            enemiesEngaged = _engagedEnemyIds.Count - _roomStartSnapshot.enemiesEngaged,
            attacksPerformed = _attacksPerformed - _roomStartSnapshot.attacksPerformed,
            enemyHits = _enemyHits - _roomStartSnapshot.enemyHits,
            effectiveEnemyHits = _effectiveEnemyHits - _roomStartSnapshot.effectiveEnemyHits,
            timeRunning = _timeRunning - _roomStartSnapshot.timeRunning,
            timeIdle = _timeIdle - _roomStartSnapshot.timeIdle,
            averageMovementSpeed = roomAverageSpeed
        };
    }

    private void EvaluateEmotion(bool forceImmediate)
    {
        float now = Time.time;
        float elapsedSinceLastEvaluation = Mathf.Max(0f, now - _lastEmotionEvaluationTime);
        _lastEmotionEvaluationTime = now;

        float targetScore = CalculateAggressionScore();
        targetScore = ApplyPassiveCalmDecay(targetScore, elapsedSinceLastEvaluation, now);

        if (forceImmediate)
        {
            AggressionScore = targetScore;
        }
        else
        {
            float directionalSmoothing = targetScore >= AggressionScore ? aggressionRiseSmoothing : aggressionFallSmoothing;
            float smoothing = directionalSmoothing > 0f ? directionalSmoothing : scoreSmoothing;
            AggressionScore = Mathf.Lerp(AggressionScore, targetScore, smoothing);
        }

        EmotionProfileSnapshot snapshot = BuildSnapshot();
        EmotionProfileUpdated?.Invoke(snapshot);

        if (Confidence < minimumEvidenceForChange)
        {
            return;
        }

        PlayerEmotionState nextEmotion = CurrentEmotion;
        if (AggressionScore >= aggressiveThreshold)
        {
            nextEmotion = PlayerEmotionState.Aggressive;
        }
        else if (AggressionScore <= calmThreshold)
        {
            nextEmotion = PlayerEmotionState.Calm;
        }

        if (nextEmotion == CurrentEmotion)
        {
            return;
        }

        CurrentEmotion = nextEmotion;
        EmotionChanged?.Invoke(CurrentEmotion, snapshot);

        if (logEmotionChanges)
        {
            Debug.Log($"Emotion profile changed to {CurrentEmotion} ({AggressionScore:0.00})");
        }
    }

    private float CalculateAggressionScore()
    {
        float lifetimeScore = CalculateWeightedScore(
            _damageTaken,
            _deathCount,
            _encounteredEnemyIds.Count,
            _engagedEnemyIds.Count,
            _attacksPerformed,
            _effectiveEnemyHits,
            GetAverageMovementSpeed(),
            _timeRunning,
            _timeIdle,
            GetLifetimeTrackedTimeForScoring(),
            expectedDamageTaken,
            expectedDeaths,
            expectedEnemyEncounters,
            expectedAttacks,
            expectedAverageMovementSpeed,
            expectedRoomClearTime);

        EmotionProfileSnapshot recentSnapshot = GetRecentBehaviorSnapshot();
        RecentAggressionScore = CalculateWeightedScore(
            recentSnapshot.damageTaken,
            recentSnapshot.deathCount,
            recentSnapshot.enemiesEncountered,
            recentSnapshot.enemiesEngaged,
            recentSnapshot.attacksPerformed,
            recentSnapshot.effectiveEnemyHits,
            recentSnapshot.averageMovementSpeed,
            recentSnapshot.timeRunning,
            recentSnapshot.timeIdle,
            GetRecentRoomTimeForScoring(recentSnapshot),
            expectedRoomDamageTaken,
            expectedRoomDeaths,
            expectedRoomEnemyEncounters,
            expectedRoomAttacks,
            expectedRoomMovementSpeed,
            expectedRoomClearTime);

        Confidence = CalculateConfidence(recentSnapshot);
        float effectiveRecentWeight = recentBehaviorWeight * Confidence;

        if (RecentAggressionScore < lifetimeScore)
        {
            float recoveryDelta = lifetimeScore - RecentAggressionScore;
            effectiveRecentWeight = Mathf.Clamp01(effectiveRecentWeight + (recoveryDelta * passiveRecoveryBoost));
        }

        float blendedScore = Mathf.Lerp(lifetimeScore, RecentAggressionScore, effectiveRecentWeight);
        blendedScore -= CalculatePassiveForgivenessBias(recentSnapshot);
        return Mathf.Clamp01(blendedScore);
    }

    private float CalculateWeightedScore(
        float damageTaken,
        int deathCount,
        int enemiesEncountered,
        int enemiesEngaged,
        int attacksPerformed,
        float effectiveEnemyHits,
        float averageMovementSpeed,
        float timeRunning,
        float timeIdle,
        float roomTime,
        float expectedDamage,
        float expectedDeathCount,
        float expectedEncounters,
        float expectedAttackCount,
        float expectedMovementSpeed,
        float expectedClearTime)
    {
        float damagePressureScore = CalculateRateScore(damageTaken, roomTime, expectedDamage, expectedClearTime);
        float deathPressureScore = CalculateRateScore(deathCount, roomTime, expectedDeathCount, expectedClearTime);
        float encounterPressureScore = CalculateRateScore(enemiesEncountered, roomTime, expectedEncounters, expectedClearTime);
        float attackVolumeScore = CalculateRateScore(attacksPerformed, roomTime, expectedAttackCount, expectedClearTime);
        float rawHitScore = attacksPerformed <= 0 ? 0f : Mathf.Clamp01(effectiveEnemyHits / attacksPerformed);
        float hitConversionScore = Mathf.Clamp01(rawHitScore * hitIntentScale);
        float initiativeScore = CalculateInitiativeScore(attacksPerformed, enemiesEncountered);
        float engagementScore = CalculateEngagementScore(enemiesEncountered, enemiesEngaged, attacksPerformed);
        float combatCommitmentScore = CalculateCombatCommitmentScore(attackVolumeScore, initiativeScore, engagementScore, hitConversionScore);
        float movementAggressionScore = CalculateMovementScore(averageMovementSpeed, timeRunning, timeIdle, expectedMovementSpeed);
        float tempoAggressionScore = CalculateTempoAggressionScore(roomTime, expectedClearTime);
        float calmAvoidanceScore = CalculateCalmAvoidanceScore(engagementScore, initiativeScore, timeRunning, timeIdle, damagePressureScore, tempoAggressionScore);

        _damagePressureScore = Mathf.Clamp01((damagePressureScore * 0.72f) + (deathPressureScore * 0.28f));
        _combatIntentScore = combatCommitmentScore;
        _movementPressureScore = movementAggressionScore;
        _timePressureScore = tempoAggressionScore;

        float aggressiveEvidence =
            _damagePressureScore * 0.20f +
            encounterPressureScore * 0.12f +
            combatCommitmentScore * 0.33f +
            movementAggressionScore * 0.20f +
            tempoAggressionScore * 0.15f;

        float calmEvidence =
            calmAvoidanceScore * 0.55f +
            (1f - combatCommitmentScore) * 0.20f +
            (1f - encounterPressureScore) * 0.10f +
            (1f - _damagePressureScore) * 0.08f +
            (1f - tempoAggressionScore) * 0.07f;

        float aggressionQualification = CalculateAggressionQualification(attacksPerformed, enemiesEncountered, enemiesEngaged, roomTime);
        aggressiveEvidence *= aggressionQualification;
        calmEvidence += (1f - aggressionQualification) * lowQualificationCalmBonus;

        float score = aggressiveEvidence / Mathf.Max(0.0001f, aggressiveEvidence + calmEvidence);

        float engagementRatio = enemiesEncountered > 0
            ? (float)enemiesEngaged / enemiesEncountered
            : 1f;

        bool isLowCommitmentBurst =
            attacksPerformed <= 2 &&
            effectiveEnemyHits <= 2.2f &&
            roomTime <= minimumAggressiveWindowSeconds &&
            (enemiesEngaged <= 1 || engagementRatio <= 0.45f);

        if (isLowCommitmentBurst)
        {
            score = Mathf.Min(score, earlyBurstAggressionCap);
        }

        return Mathf.Clamp01(score);
    }

    private float CalculateMovementScore(float averageSpeed, float timeRunning, float timeIdle, float expectedMovementSpeed)
    {
        float speedScore = SafeRatio(averageSpeed, expectedMovementSpeed);
        float trackedTime = GetTrackedTime(timeRunning, timeIdle);
        float runningRatio = Mathf.Clamp01(timeRunning / trackedTime);
        float idleRatio = Mathf.Clamp01(timeIdle / trackedTime);

        return Mathf.Clamp01((speedScore * 0.5f) + (runningRatio * 0.35f) + ((1f - idleRatio) * 0.15f));
    }

    private float CalculateRateScore(float value, float observedTime, float expectedValue, float expectedClearTime)
    {
        if (expectedValue <= 0f || expectedClearTime <= 0f)
        {
            return 0f;
        }

        float minWindow = Mathf.Max(1f, minimumRateSampleWindow);
        float safeObservedTime = Mathf.Max(0.05f, observedTime);
        float actualRate = Mathf.Max(0f, value) / safeObservedTime;
        float expectedRate = expectedValue / expectedClearTime;
        float rawRateScore = SafeRatio(actualRate, expectedRate);

        if (observedTime <= 0f)
        {
            return Mathf.Clamp01(neutralRateScore);
        }

        float confidenceStart = Mathf.Max(0.25f, minWindow * 0.35f);
        float sampleConfidence = Mathf.InverseLerp(confidenceStart, minWindow, observedTime);
        return Mathf.Lerp(Mathf.Clamp01(neutralRateScore), rawRateScore, sampleConfidence);
    }

    private float CalculateInitiativeScore(int attacksPerformed, int enemiesEncountered)
    {
        if (enemiesEncountered <= 0)
        {
            return Mathf.Clamp01(neutralRateScore);
        }

        float attacksPerEncounter = (float)attacksPerformed / enemiesEncountered;
        float initiativeScore = SafeRatio(attacksPerEncounter, Mathf.Max(0.1f, expectedAttacksPerEncounter));
        return Mathf.Clamp01(initiativeScore * attackIntentScale);
    }

    private float CalculateEngagementScore(int enemiesEncountered, int enemiesEngaged, int attacksPerformed)
    {
        if (enemiesEncountered <= 0)
        {
            return Mathf.Clamp01(neutralRateScore);
        }

        return Mathf.Clamp01((float)Mathf.Max(0, enemiesEngaged) / enemiesEncountered);
    }

    private float CalculateAggressionQualification(int attacksPerformed, int enemiesEncountered, int enemiesEngaged, float roomTime)
    {
        float minimumTime = Mathf.Max(2f, minimumAggressiveWindowSeconds);
        float attackProgress = Mathf.InverseLerp(Mathf.Max(1f, minimumAggressiveAttacks * 0.5f), minimumAggressiveAttacks, attacksPerformed);
        float encounterProgress = Mathf.InverseLerp(1f, Mathf.Max(1, minimumAggressiveEnemiesEncountered), enemiesEncountered);
        float engagedProgress = Mathf.InverseLerp(1f, Mathf.Max(1, minimumAggressiveEnemiesEngaged), enemiesEngaged);
        float timeProgress = Mathf.InverseLerp(minimumTime * 0.35f, minimumTime, roomTime);

        float weightedQualification = Mathf.Clamp01(
            attackProgress * 0.36f +
            encounterProgress * 0.24f +
            engagedProgress * 0.24f +
            timeProgress * 0.16f);

        float strictGate =
            attackProgress *
            Mathf.Lerp(0.25f, 1f, encounterProgress) *
            Mathf.Lerp(0.25f, 1f, engagedProgress) *
            Mathf.Lerp(0.30f, 1f, timeProgress);

        return Mathf.Clamp01(weightedQualification * strictGate);
    }

    private float CalculateCombatCommitmentScore(float attackVolumeScore, float initiativeScore, float engagementScore, float hitConversionScore)
    {
        return Mathf.Clamp01(
            attackVolumeScore * 0.28f +
            initiativeScore * 0.32f +
            engagementScore * 0.25f +
            hitConversionScore * 0.15f);
    }

    private float CalculateTempoAggressionScore(float roomTime, float expectedClearTime)
    {
        if (roomTime <= 0f || expectedClearTime <= 0f)
        {
            return 0.5f;
        }

        float clearRatio = Mathf.Clamp01(roomTime / expectedClearTime);
        return 1f - clearRatio;
    }

    private float CalculateCalmAvoidanceScore(
        float engagementScore,
        float initiativeScore,
        float timeRunning,
        float timeIdle,
        float damagePressureScore,
        float tempoAggressionScore)
    {
        float trackedTime = GetTrackedTime(timeRunning, timeIdle);
        float idleRatio = Mathf.Clamp01(timeIdle / trackedTime);
        float lowEngagementScore = 1f - Mathf.Clamp01(engagementScore);
        float lowInitiativeScore = 1f - Mathf.Clamp01(initiativeScore);
        float lowDamageScore = 1f - damagePressureScore;
        float slowTempoScore = 1f - tempoAggressionScore;

        return Mathf.Clamp01(
            lowEngagementScore * 0.50f +
            lowInitiativeScore * 0.20f +
            idleRatio * 0.20f +
            slowTempoScore * 0.07f +
            lowDamageScore * 0.03f);
    }

    private EmotionProfileSnapshot GetRecentBehaviorSnapshot()
    {
        if (_roomTimerRunning)
        {
            return BuildRoomDeltaSnapshot(_currentRoomTime);
        }

        if (LastRoomReport.roomNumber > 0)
        {
            return BuildSnapshotFromRoomReport(LastRoomReport);
        }

        return BuildSnapshot();
    }

    private EmotionProfileSnapshot BuildRoomDeltaSnapshot(float roomTime)
    {
        float roomMovementSpeedTotal = _movementSpeedTotal - _roomStartMovementSpeedTotal;
        int roomMovementSamples = _movementSamples - _roomStartMovementSamples;

        return new EmotionProfileSnapshot
        {
            state = CurrentEmotion,
            aggressionScore = AggressionScore,
            damageTaken = _damageTaken - _roomStartSnapshot.damageTaken,
            deathCount = _deathCount - _roomStartSnapshot.deathCount,
            enemiesEncountered = _encounteredEnemyIds.Count - _roomStartSnapshot.enemiesEncountered,
            enemiesEngaged = _engagedEnemyIds.Count - _roomStartSnapshot.enemiesEngaged,
            attacksPerformed = _attacksPerformed - _roomStartSnapshot.attacksPerformed,
            enemyHits = _enemyHits - _roomStartSnapshot.enemyHits,
            effectiveEnemyHits = _effectiveEnemyHits - _roomStartSnapshot.effectiveEnemyHits,
            timeRunning = _timeRunning - _roomStartSnapshot.timeRunning,
            timeIdle = _timeIdle - _roomStartSnapshot.timeIdle,
            averageMovementSpeed = roomMovementSamples > 0 ? roomMovementSpeedTotal / roomMovementSamples : 0f,
            activeSpawnerCount = ActiveSpawnerCount,
            currentRoomTime = roomTime,
            lastRoomClearTime = _lastRoomClearTime
        };
    }

    private EmotionProfileSnapshot BuildSnapshotFromRoomReport(EmotionRoomReport report)
    {
        return new EmotionProfileSnapshot
        {
            state = report.emotionAfter,
            aggressionScore = report.scoreAfter,
            damageTaken = report.damageTaken,
            deathCount = report.deathCount,
            enemiesEncountered = report.enemiesEncountered,
            enemiesEngaged = report.enemiesEngaged,
            attacksPerformed = report.attacksPerformed,
            enemyHits = report.enemyHits,
            effectiveEnemyHits = report.effectiveEnemyHits,
            timeRunning = report.timeRunning,
            timeIdle = report.timeIdle,
            averageMovementSpeed = report.averageMovementSpeed,
            activeSpawnerCount = ActiveSpawnerCount,
            currentRoomTime = 0f,
            lastRoomClearTime = report.duration
        };
    }

    private float GetRecentRoomTimeForScoring(EmotionProfileSnapshot recentSnapshot)
    {
        return recentSnapshot.currentRoomTime > 0f ? recentSnapshot.currentRoomTime : recentSnapshot.lastRoomClearTime;
    }

    private float CalculateConfidence(EmotionProfileSnapshot recentSnapshot)
    {
        float roomTime = GetRecentRoomTimeForScoring(recentSnapshot);
        float activityTime = recentSnapshot.timeRunning + recentSnapshot.timeIdle;
        float durationEvidence = SafeRatio(roomTime > 0f ? roomTime : activityTime, Mathf.Max(5f, expectedDecisionWindowSeconds));
        float encounterEvidence = SafeRatio(recentSnapshot.enemiesEncountered, expectedRoomEnemyEncounters);
        float attackEvidence = SafeRatio(recentSnapshot.attacksPerformed, expectedRoomAttacks);
        float combatEvidence = SafeRatio(
            recentSnapshot.attacksPerformed + recentSnapshot.effectiveEnemyHits,
            Mathf.Max(1f, expectedRoomAttacks + expectedRoomEnemyEncounters));
        float engagementEvidence = recentSnapshot.enemiesEncountered <= 0
            ? (recentSnapshot.attacksPerformed > 0 ? 1f : 0f)
            : SafeRatio(recentSnapshot.enemiesEngaged, recentSnapshot.enemiesEncountered);
        float damageEvidence = SafeRatio(recentSnapshot.damageTaken, expectedRoomDamageTaken);
        float movementEvidence = SafeRatio(activityTime, Mathf.Max(10f, expectedDecisionWindowSeconds * 0.75f));

        return Mathf.Clamp01(
            durationEvidence * 0.26f +
            encounterEvidence * 0.16f +
            attackEvidence * 0.14f +
            combatEvidence * 0.16f +
            engagementEvidence * 0.12f +
            movementEvidence * 0.12f +
            damageEvidence * 0.04f);
    }

    private float GetLifetimeTrackedTimeForScoring()
    {
        float trackedTime = _timeRunning + _timeIdle;
        if (trackedTime > 0f)
        {
            return trackedTime;
        }

        if (_lastRoomClearTime > 0f)
        {
            return _lastRoomClearTime;
        }

        return _currentRoomTime;
    }

    private float GetAverageMovementSpeed()
    {
        if (_movementSamples <= 0)
        {
            return 0f;
        }

        return _movementSpeedTotal / _movementSamples;
    }

    private float GetTrackedTime(float timeRunning, float timeIdle)
    {
        return Mathf.Max(0.01f, timeRunning + timeIdle);
    }

    private float SafeRatio(float value, float expectedValue)
    {
        if (expectedValue <= 0f)
        {
            return 0f;
        }

        return Mathf.Clamp01(value / expectedValue);
    }

    private float ApplyPassiveCalmDecay(float targetScore, float elapsedSinceLastEvaluation, float now)
    {
        if (!IsRoomActive)
        {
            return targetScore;
        }

        if (calmDecayPerSecond <= 0f)
        {
            return targetScore;
        }

        if (now - _lastCombatIntentTime <= calmDecayDelay)
        {
            return targetScore;
        }

        float decayAmount = calmDecayPerSecond * elapsedSinceLastEvaluation;
        return Mathf.Clamp01(targetScore - decayAmount);
    }

    private float CalculatePassiveForgivenessBias(EmotionProfileSnapshot recentSnapshot)
    {
        if (!IsRoomActive)
        {
            return 0f;
        }

        if (passiveForgivenessBias <= 0f)
        {
            return 0f;
        }

        float disengageTime = Mathf.Max(0f, Time.time - _lastCombatIntentTime);
        float disengageFactor = Mathf.InverseLerp(calmDecayDelay, calmDecayDelay + 4f, disengageTime);

        float pressureFactor = Mathf.Clamp01(
            (SafeRatio(recentSnapshot.damageTaken, expectedRoomDamageTaken) * 0.65f) +
            (SafeRatio(recentSnapshot.deathCount, Mathf.Max(1f, expectedRoomDeaths)) * 0.35f));

        float lowPressureFactor = 1f - pressureFactor;
        return passiveForgivenessBias * disengageFactor * lowPressureFactor;
    }

    private float CalculateEffectiveHitContribution()
    {
        if (!useMultiHitDiminishingReturns)
        {
            return 1f;
        }

        float burstWindow = Mathf.Max(0.05f, multiHitBurstWindow);
        bool hasRecentAttackContext = _lastAttackStartedTime > 0f && (Time.time - _lastAttackStartedTime) <= burstWindow;
        bool inBurstWindow = _lastEnemyHitTime > 0f && (Time.time - _lastEnemyHitTime) <= burstWindow;

        if (!hasRecentAttackContext && !inBurstWindow)
        {
            _hitsInCurrentAttack = 0;
            _effectiveHitsInCurrentAttack = 0f;
        }

        _hitsInCurrentAttack++;
        float hitWeight = GetHitWeight(_hitsInCurrentAttack);
        float remainingAttackBudget = Mathf.Max(0f, maxEffectiveHitsPerAttack - _effectiveHitsInCurrentAttack);
        float contribution = Mathf.Min(hitWeight, remainingAttackBudget);
        _effectiveHitsInCurrentAttack += contribution;
        return contribution;
    }

    private float GetHitWeight(int hitIndex)
    {
        if (hitIndex <= 1)
        {
            return 1f;
        }

        float falloff = Mathf.Max(0f, additionalHitFalloff);
        return 1f / (1f + ((hitIndex - 1) * falloff));
    }

    private void HandleLevelEntered(int nodeId, int floorDepth, string sceneName)
    {
        if (floorDepth <= 0)
        {
            return;
        }

        if (clearRoomStateOnLevelEntered)
        {
            _activeRoomContributors.Clear();
            _roomTimerRunning = false;
            _currentRoomTime = 0f;
            _currentRoomBaseSpawnCount = 0;
            _currentRoomAdjustedSpawnCount = 0;
            _currentRoomSpawnerCount = 0;
            _lastRoomClearTime = 0f;
        }

        if (rebaseTelemetryOnLevelEntered)
        {
            RebaseTelemetry(levelCarryoverFactor);
        }

        EmotionProfileUpdated?.Invoke(BuildSnapshot());

        if (logEmotionChanges)
        {
            Debug.Log($"Emotion telemetry rebased for floor {floorDepth} ({sceneName}). Aggression: {AggressionScore:0.00}");
        }
    }

    private void RebaseTelemetry(float carryover)
    {
        float factor = Mathf.Clamp01(carryover);

        _damageTaken *= factor;
        _deathCount = Mathf.RoundToInt(_deathCount * factor);
        _attacksPerformed = Mathf.RoundToInt(_attacksPerformed * factor);
        _enemyHits = Mathf.RoundToInt(_enemyHits * factor);
        _effectiveEnemyHits *= factor;
        _timeRunning *= factor;
        _timeIdle *= factor;
        _movementSpeedTotal *= factor;
        _movementSamples = Mathf.RoundToInt(_movementSamples * factor);
        _hitsInCurrentAttack = 0;
        _effectiveHitsInCurrentAttack = 0f;
        _lastAttackStartedTime = 0f;
        _lastEnemyHitTime = 0f;
        _lastCombatIntentTime = Time.time;
        _lastEmotionEvaluationTime = Time.time;

        if (factor < 1f)
        {
            _encounteredEnemyIds.Clear();
            _engagedEnemyIds.Clear();
        }
    }

    private EmotionProfileSnapshot BuildSnapshot()
    {
        return new EmotionProfileSnapshot
        {
            state = CurrentEmotion,
            aggressionScore = AggressionScore,
            recentAggressionScore = RecentAggressionScore,
            confidence = Confidence,
            damagePressureScore = _damagePressureScore,
            combatIntentScore = _combatIntentScore,
            movementPressureScore = _movementPressureScore,
            timePressureScore = _timePressureScore,
            damageTaken = _damageTaken,
            deathCount = _deathCount,
            enemiesEncountered = _encounteredEnemyIds.Count,
            enemiesEngaged = _engagedEnemyIds.Count,
            attacksPerformed = _attacksPerformed,
            enemyHits = _enemyHits,
            effectiveEnemyHits = _effectiveEnemyHits,
            timeRunning = _timeRunning,
            timeIdle = _timeIdle,
            averageMovementSpeed = GetAverageMovementSpeed(),
            activeSpawnerCount = ActiveSpawnerCount,
            currentRoomTime = _currentRoomTime,
            lastRoomClearTime = _lastRoomClearTime
        };
    }
}
