using UnityEngine;
using UnityEngine.AI;

public class ChaseState : IEnemyState
{
    private EnemyController _enemy;
    private float _lostSightTimer;
    private float _repathTimer;
    private float _repathInterval;
    private int _swarmIndex;
    private Vector3 _personalOffset;
    private const float SightGracePeriod = 1.25f;
    private const float MinRepathInterval = 0.16f;
    private const float MaxRepathInterval = 0.34f;
    private const float GoldenAngle = 137.50777f;

    public ChaseState(EnemyController enemy)
    {
        _enemy = enemy;
    }

    public void OnEnter()
    {
        Debug.Log("ENTERED CHASE STATE!");
        if (_enemy.agent != null)
        {
            _enemy.agent.speed = _enemy.speed;
            _enemy.agent.stoppingDistance = GetChaseStoppingDistance();
            _enemy.agent.autoBraking = true;
            _enemy.agent.autoRepath = true;
            _enemy.agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            _enemy.agent.avoidancePriority = Mathf.Clamp(35 + Mathf.Abs(_enemy.GetInstanceID()) % 45, 1, 99);
        }

        if (_enemy.spriteRenderer != null)
        {
            _enemy.spriteRenderer.color = new Color(0.45f, 0.05f, 0.05f);
        }

        _lostSightTimer = 0f;
        _repathInterval = Random.Range(MinRepathInterval, MaxRepathInterval);
        _repathTimer = Random.Range(0f, _repathInterval);
        _swarmIndex = GetStableSwarmIndex();
        _personalOffset = GetRingDirection(_swarmIndex);
    }

    public void Tick()
    {
        if (_enemy.player == null) return;

        bool hasLineOfSight = HasLineOfSightToPlayer();
        float distanceToPlayer = Vector3.Distance(_enemy.transform.position, _enemy.player.position);

        if (hasLineOfSight)
        {
            _lostSightTimer = 0f;
            _enemy.lastKnownPlayerPosition = _enemy.player.position;
            _enemy.DrawLaser(_enemy.player.position, true);

            if (distanceToPlayer <= GetAttackEnterDistance())
            {
                _enemy.ChangeState(new AttackState(_enemy));
                return;
            }
        }
        else
        {
            _lostSightTimer += Time.deltaTime;
            _enemy.DrawLaser(_enemy.lastKnownPlayerPosition == Vector3.zero ? _enemy.player.position : _enemy.lastKnownPlayerPosition, false);
        }

        if (_lostSightTimer > SightGracePeriod)
        {
            _enemy.ChangeState(new SearchState(_enemy));
            return;
        }

        _repathTimer -= Time.deltaTime;
        if (_repathTimer <= 0f)
        {
            _repathTimer = _repathInterval;
            UpdateChaseDestination();
        }
    }

    public void OnExit()
    {
        _enemy.HideLaser();
    }

    private bool HasLineOfSightToPlayer()
    {
        Vector3 eyePosition = _enemy.transform.position + Vector3.up * 1f;
        Vector3 playerTargetPosition = _enemy.player.position + Vector3.up * 1f;
        Vector3 dirToPlayer = (playerTargetPosition - eyePosition).normalized;

        if (!Physics.Raycast(eyePosition, dirToPlayer, out RaycastHit hit, _enemy.chaseLeashRange, _enemy.detectionLayers))
        {
            return false;
        }

        return hit.collider.CompareTag("Player");
    }

    private void UpdateChaseDestination()
    {
        if (!HasNavigableAgent())
        {
            return;
        }

        Vector3 playerPosition = _enemy.player.position;
        Vector3 approachDirection = _personalOffset;
        Vector3 directionFromPlayer = _enemy.transform.position - playerPosition;
        directionFromPlayer.y = 0f;

        if (directionFromPlayer.sqrMagnitude > 0.25f)
        {
            approachDirection = Vector3.Slerp(approachDirection, directionFromPlayer.normalized, 0.35f).normalized;
        }

        Vector3 destination = playerPosition + approachDirection * GetRingDistance();
        destination += GetLocalSeparationOffset();
        TrySetDestination(destination, 0.45f, 3f);
    }

    private bool HasNavigableAgent()
    {
        return _enemy.agent != null && _enemy.agent.isActiveAndEnabled && _enemy.agent.isOnNavMesh;
    }

    private bool TrySetDestination(Vector3 destination, float repathThreshold, float sampleRadius)
    {
        if (!HasNavigableAgent())
        {
            return false;
        }

        Vector3 sampledDestination = destination;
        if (NavMesh.SamplePosition(destination, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
        {
            sampledDestination = hit.position;
        }

        if (_enemy.agent.hasPath && Vector3.SqrMagnitude(_enemy.agent.destination - sampledDestination) <= repathThreshold * repathThreshold)
        {
            return true;
        }

        _enemy.agent.isStopped = false;
        return _enemy.agent.SetDestination(sampledDestination);
    }

    private int GetStableSwarmIndex()
    {
        var enemies = SwarmManager.GetAllEnemies(_enemy.enemyType);
        int index = enemies.IndexOf(_enemy);
        return Mathf.Max(0, index);
    }

    private Vector3 GetRingDirection(int swarmIndex)
    {
        float angle = (swarmIndex * GoldenAngle) * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
    }

    private float GetRingDistance()
    {
        float desiredDistance = GetChaseStoppingDistance();
        int ringCapacity = Mathf.Max(6, Mathf.CeilToInt((Mathf.PI * 2f * Mathf.Max(desiredDistance, 1f)) / 0.9f));
        int ring = _swarmIndex / ringCapacity;
        return desiredDistance + ring * 0.75f;
    }

    private Vector3 GetLocalSeparationOffset()
    {
        float desiredSpacing = Mathf.Max(0.85f, _enemy.agent.radius * 2.75f);
        float desiredSpacingSqr = desiredSpacing * desiredSpacing;
        Vector3 separation = Vector3.zero;
        int neighbors = 0;

        foreach (EnemyController other in SwarmManager.GetAllEnemies(_enemy.enemyType))
        {
            if (other == null || other == _enemy)
            {
                continue;
            }

            Vector3 away = _enemy.transform.position - other.transform.position;
            away.y = 0f;
            float distanceSqr = away.sqrMagnitude;
            if (distanceSqr <= 0.001f || distanceSqr > desiredSpacingSqr)
            {
                continue;
            }

            float distance = Mathf.Sqrt(distanceSqr);
            separation += away.normalized * ((desiredSpacing - distance) / desiredSpacing);
            neighbors++;
        }

        if (neighbors == 0)
        {
            return Vector3.zero;
        }

        separation /= neighbors;
        return Vector3.ClampMagnitude(separation, 1.15f);
    }

    private float GetAttackRange()
    {
        if (_enemy.attackRange > 0f)
        {
            return _enemy.attackRange;
        }

        return _enemy.EnemyStatData != null ? Mathf.Max(0.25f, _enemy.EnemyStatData.attackRange) : 2f;
    }

    private float GetChaseStoppingDistance()
    {
        return Mathf.Max(0.35f, GetAttackRange() * 0.82f);
    }

    private float GetAttackEnterDistance()
    {
        return Mathf.Max(0.35f, GetAttackRange() * 0.96f);
    }
}
