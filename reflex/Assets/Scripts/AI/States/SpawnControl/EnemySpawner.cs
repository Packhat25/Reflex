using System.Collections.Generic;
using UnityEngine;

public class EnemySpawner : MonoBehaviour
{
    [Header("Spawner Settings")]
    public GameObject enemyPrefab; // The blueprint to spawn
    public int spawnCount = 3; // How many enemies to spawn per wave
    public float spawnRadius = 5f; // How far from the spawner to place them
    public float spawnHeight = 0f; // Height offset for spawning enemies
    public float respawnDelay = 3f; // How long to wait after the entire wave is gone

    [Header("Emotion Spawn Scaling")]
    public bool useEmotionSpawnCount = true;
    public bool useEmotionSpawnRate = true;
    public bool useContinuousEmotionRespawnRate = true;
    [Min(0.01f)] public float calmRespawnDelayMultiplier = 1.25f;
    [Min(0.01f)] public float aggressiveRespawnDelayMultiplier = 0.65f;
    [Range(0f, 1f)] public float respawnRateConfidenceFloor = 0.3f;
    [Min(0f)] public float minimumRespawnDelay = 0.25f;
    public bool logEmotionSpawnRate = true;

    [Header("Wave Sequencing")]
    [SerializeField] private bool enableAdditionalWaves = true;
    [SerializeField, Min(1)] private int maxWavesPerRoom = 3;
    [SerializeField, Range(0f, 1f)] private float additionalWaveChanceFloorOne = 0.12f;
    [SerializeField, Range(0f, 1f)] private float additionalWaveChancePerFloor = 0.06f;
    [SerializeField, Range(0f, 1f)] private float maxAdditionalWaveChance = 0.75f;
    [SerializeField] private bool logWaveRolls = true;

    private readonly List<GameObject> _currentEnemies = new List<GameObject>();
    private float _timer;
    private bool _waveClearHandled;
    private bool _waitingForRoomToClear;
    private bool _hasSpawnedWave;
    private bool _hasUpcomingWave;
    private bool _roomClearReported;
    private int _wavesSpawned;

    public bool HasSpawnedWave => _hasSpawnedWave;
    public bool HasUpcomingWave => _hasUpcomingWave;
    public int WavesSpawned => _wavesSpawned;
    public int AliveEnemyCount
    {
        get
        {
            RemoveDestroyedEnemies();
            return _currentEnemies.Count;
        }
    }

    void Start()
    {
        SpawnWave();
    }

    void Update()
    {
        RemoveDestroyedEnemies();

        if (_currentEnemies.Count == 0)
        {
            if (!_waveClearHandled)
            {
                ResolveCurrentWaveEnd();
            }

            if (_waitingForRoomToClear)
            {
                if (EmotionEngine.Instance.IsRoomActive)
                {
                    return;
                }

                _waitingForRoomToClear = false;
            }

            if (_hasUpcomingWave)
            {
                _timer -= Time.deltaTime;
                if (_timer <= 0f)
                {
                    SpawnWave();
                }
            }
        }
    }

    private void RemoveDestroyedEnemies()
    {
        for (int i = _currentEnemies.Count - 1; i >= 0; i--)
        {
            if (_currentEnemies[i] == null)
            {
                _currentEnemies.RemoveAt(i);
            }
        }
    }

    private void SpawnWave()
    {
        _wavesSpawned++;
        _hasSpawnedWave = true;
        _currentEnemies.Clear();
        _waveClearHandled = false;
        _waitingForRoomToClear = false;
        _hasUpcomingWave = false;
        _roomClearReported = false;

        int adjustedSpawnCount = GetEmotionAdjustedSpawnCount();
        EmotionEngine.Instance.BeginRoom(this, spawnCount, adjustedSpawnCount);

        for (int i = 0; i < adjustedSpawnCount; i++)
        {
            Vector3 offset = Random.insideUnitSphere * spawnRadius;
            offset.y = spawnHeight;  // Use the configurable spawn height
            Vector3 spawnPosition = transform.position + offset;
            GameObject enemy = Instantiate(enemyPrefab, spawnPosition, transform.rotation);
            Transform hitbox = enemy.transform.Find("Hurt Box");
            if (hitbox != null)
            {
                hitbox.tag = "Enemy";
            }
            _currentEnemies.Add(enemy);
        }

        _timer = GetEmotionAdjustedRespawnDelay();
        Debug.Log($"<color=green>SPAWNED WAVE OF {adjustedSpawnCount} ENEMIES ({EmotionDirector.Instance.CurrentDirective.strategy}); active spawners: {EmotionEngine.Instance.ActiveSpawnerCount}</color>");
    }

    private void ResolveCurrentWaveEnd()
    {
        _waveClearHandled = true;

        if (TryScheduleAdditionalWave())
        {
            return;
        }

        EmotionEngine.Instance.RecordRoomCleared(this);
        _roomClearReported = true;
        _waitingForRoomToClear = true;
    }

    private bool TryScheduleAdditionalWave()
    {
        if (!enableAdditionalWaves)
        {
            return false;
        }

        int maxAllowedWaves = Mathf.Max(1, maxWavesPerRoom);
        if (_wavesSpawned >= maxAllowedWaves)
        {
            return false;
        }

        float chance = GetAdditionalWaveChanceForCurrentFloor();
        float roll = Random.value;
        bool queueAnotherWave = roll < chance;

        if (logWaveRolls)
        {
            int floor = LevelRunManager.HasInstance ? Mathf.Max(1, LevelRunManager.Instance.CurrentFloor) : 1;
            string outcome = queueAnotherWave ? "queue next wave" : "end room";
            Debug.Log($"<color=orange>{name}: wave {_wavesSpawned}/{maxAllowedWaves}, floor {floor}, roll {roll:0.00} vs chance {chance:0.00} -> {outcome}</color>");
        }

        if (!queueAnotherWave)
        {
            return false;
        }

        _hasUpcomingWave = true;
        _timer = GetEmotionAdjustedRespawnDelay();

        if (logEmotionSpawnRate)
        {
            Debug.Log($"<color=cyan>{name}: next wave in {_timer:0.00}s ({EmotionEngine.Instance.CurrentEmotion})</color>");
        }

        return true;
    }

    private float GetAdditionalWaveChanceForCurrentFloor()
    {
        int floor = LevelRunManager.HasInstance ? Mathf.Max(1, LevelRunManager.Instance.CurrentFloor) : 1;
        float chance = additionalWaveChanceFloorOne + ((floor - 1) * additionalWaveChancePerFloor);
        return Mathf.Clamp(chance, 0f, Mathf.Clamp01(maxAdditionalWaveChance));
    }

    private int GetEmotionAdjustedSpawnCount()
    {
        int baseCount;
        if (!useEmotionSpawnCount)
        {
            baseCount = spawnCount;
        }
        else
        {
            baseCount = EmotionDirector.Instance.GetRecommendedSpawnCount(spawnCount);
        }

        float floorMultiplier = LevelRunManager.HasInstance ? LevelRunManager.Instance.CurrentFloorSpawnMultiplier : 1f;
        return Mathf.Max(1, Mathf.CeilToInt(baseCount * floorMultiplier));
    }

    private float GetEmotionAdjustedRespawnDelay()
    {
        if (!useEmotionSpawnRate)
        {
            return Mathf.Max(minimumRespawnDelay, respawnDelay);
        }

        float multiplier;
        if (useContinuousEmotionRespawnRate)
        {
            EmotionEngine engine = EmotionEngine.Instance;
            float confidenceInfluence = Mathf.Lerp(respawnRateConfidenceFloor, 1f, engine.Confidence);
            float blend = Mathf.Lerp(0.5f, engine.AggressionScore, confidenceInfluence);
            multiplier = Mathf.Lerp(calmRespawnDelayMultiplier, aggressiveRespawnDelayMultiplier, blend);
        }
        else
        {
            multiplier = EmotionEngine.Instance.CurrentEmotion == PlayerEmotionState.Aggressive
                ? aggressiveRespawnDelayMultiplier
                : calmRespawnDelayMultiplier;
        }

        float floorMultiplier = LevelRunManager.HasInstance ? LevelRunManager.Instance.CurrentFloorRespawnDelayMultiplier : 1f;
        return Mathf.Max(minimumRespawnDelay, respawnDelay * multiplier * floorMultiplier);
    }

    private void OnDisable()
    {
        if (_hasSpawnedWave && !_roomClearReported && EmotionEngine.HasInstance)
        {
            EmotionEngine.Instance.RecordRoomCleared(this);
            _roomClearReported = true;
        }
    }
}
