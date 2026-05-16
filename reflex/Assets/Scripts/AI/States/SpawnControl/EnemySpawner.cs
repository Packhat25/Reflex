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

    private readonly List<GameObject> _currentEnemies = new List<GameObject>();
    private float _timer;
    private bool _waveClearReported;
    private bool _waitingForRoomToClear;
    private bool _hasSpawnedWave;

    public bool HasSpawnedWave => _hasSpawnedWave;
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
            if (!_waveClearReported)
            {
                EmotionEngine.Instance.RecordRoomCleared(this);
                _waveClearReported = true;
                _waitingForRoomToClear = true;
            }

            if (_waitingForRoomToClear)
            {
                if (EmotionEngine.Instance.IsRoomActive)
                {
                    return;
                }

                _waitingForRoomToClear = false;
                _timer = GetEmotionAdjustedRespawnDelay();

                if (logEmotionSpawnRate)
                {
                    Debug.Log($"<color=cyan>{name}: next wave in {_timer:0.00}s ({EmotionEngine.Instance.CurrentEmotion})</color>");
                }
            }

            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                SpawnWave();
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
        _hasSpawnedWave = true;
        _currentEnemies.Clear();
        _waveClearReported = false;
        _waitingForRoomToClear = false;

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

    private int GetEmotionAdjustedSpawnCount()
    {
        if (!useEmotionSpawnCount)
        {
            return spawnCount;
        }

        return EmotionDirector.Instance.GetRecommendedSpawnCount(spawnCount);
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

        return Mathf.Max(minimumRespawnDelay, respawnDelay * multiplier);
    }

    private void OnDisable()
    {
        if (_hasSpawnedWave && !_waveClearReported && EmotionEngine.HasInstance)
        {
            EmotionEngine.Instance.RecordRoomCleared(this);
            _waveClearReported = true;
        }
    }
}
