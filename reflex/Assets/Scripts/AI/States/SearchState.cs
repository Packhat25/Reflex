using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class SearchState : IEnemyState
{
    private EnemyController _enemy;
    private float _searchTimer;
    private const float SearchDuration = 12f;

    private List<Vector3> _searchPoints = new List<Vector3>();
    private int _currentPointIndex;
    private bool _waitingAtPoint;
    private float _waitTimer;
    private const float WaitAtPointDuration = 1.2f;

    private Quaternion _initialScanRotation;
    private const float ScanAngle = 120f;
    private const float ScanSpeed = 2.2f;
    private const float SearchRadius = 5f;
    private const int SearchPointCount = 5;
    private const float GoldenAngle = 137.50777f;
    private int _swarmIndex;

    public SearchState(EnemyController enemy)
    {
        _enemy = enemy;
    }

    public void OnEnter()
    {
        _searchTimer = SearchDuration;
        _waitingAtPoint = false;
        _waitTimer = 0f;

        if (_enemy.agent != null)
        {
            _enemy.agent.speed = Mathf.Max(0.5f, _enemy.speed * 0.82f);
            _enemy.agent.stoppingDistance = 0.35f;
            _enemy.agent.autoBraking = true;
            _enemy.agent.autoRepath = true;
            _enemy.agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            _enemy.agent.avoidancePriority = Mathf.Clamp(35 + Mathf.Abs(_enemy.GetInstanceID()) % 45, 1, 99);
        }

        _swarmIndex = GetStableSwarmIndex();
        _searchPoints = BuildSearchPoints();

        _currentPointIndex = 0;

        Debug.Log("ENTERING SEARCH STATE");
        if (_enemy.spriteRenderer != null)
        {
            _enemy.spriteRenderer.color = Color.yellow;
        }
        _enemy.HideLaser();

        MoveToNextSearchPoint();
    }

    public void Tick()
    {
        if (_enemy.CanSeePlayer())
        {
            _enemy.ChangeState(new ChaseState(_enemy));
            return;
        }

        _searchTimer -= Time.deltaTime;
        if (_searchTimer <= 0f)
        {
            _enemy.ChangeState(new PatrolState(_enemy));
            return;
        }

        if (_waitingAtPoint)
        {
            _waitTimer -= Time.deltaTime;
            PerformScan();
            if (_waitTimer <= 0f)
            {
                _waitingAtPoint = false;
                MoveToNextSearchPoint();
            }
            return;
        }

        if (!HasNavigableAgent())
        {
            return;
        }

        if (HasReachedDestination(0.2f))
        {
            BeginPointWait();
        }
    }

    private List<Vector3> BuildSearchPoints()
    {
        var points = new List<Vector3>();
        Vector3 center = _enemy.lastKnownPlayerPosition;

        if (center == Vector3.zero)
        {
            center = _enemy.transform.position;
        }

        TryAddSearchPoint(points, center + GetSearchOffset(1.6f));

        for (int i = 0; i < SearchPointCount; i++)
        {
            float angle = (i * (360f / SearchPointCount)) + (_swarmIndex * GoldenAngle * 0.35f);
            float radius = Mathf.Lerp(2f, SearchRadius, (i + 1f) / SearchPointCount);
            Vector3 direction = Quaternion.Euler(0, angle, 0) * Vector3.forward;
            Vector3 candidate = center + direction * radius + GetSearchOffset(0.75f);
            TryAddSearchPoint(points, candidate);
        }

        if (points.Count == 0)
        {
            points.Add(_enemy.transform.position);
        }

        return points;
    }

    private void MoveToNextSearchPoint()
    {
        if (_searchPoints.Count == 0)
        {
            _enemy.ChangeState(new PatrolState(_enemy));
            return;
        }

        Vector3 nextPoint = _searchPoints[_currentPointIndex];
        if (TrySetDestination(nextPoint, 0.25f, 3f))
        {
            _enemy.DrawLaser(nextPoint, false);
        }

        _currentPointIndex = (_currentPointIndex + 1) % _searchPoints.Count;
    }

    private void BeginPointWait()
    {
        _waitingAtPoint = true;
        _waitTimer = WaitAtPointDuration;
        _initialScanRotation = _enemy.transform.rotation;
        if (HasNavigableAgent())
        {
            _enemy.agent.ResetPath();
        }
    }

    private void PerformScan()
    {
        float scanAngleOffset = Mathf.Sin(Time.time * ScanSpeed) * (ScanAngle / 2f);
        Quaternion scanRotation = Quaternion.AngleAxis(scanAngleOffset, Vector3.up);
        _enemy.transform.rotation = _initialScanRotation * scanRotation;
    }

    public void OnExit()
    {
        if (_enemy.agent != null)
        {
            _enemy.agent.speed = _enemy.speed;
        }

        if (_enemy.spriteRenderer != null)
        {
            _enemy.spriteRenderer.color = Color.white;
        }
        _enemy.HideLaser();
    }

    private void TryAddSearchPoint(List<Vector3> points, Vector3 candidate)
    {
        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
        {
            points.Add(hit.position);
        }
    }

    private bool HasNavigableAgent()
    {
        return _enemy.agent != null && _enemy.agent.isActiveAndEnabled && _enemy.agent.isOnNavMesh;
    }

    private bool HasReachedDestination(float extraTolerance)
    {
        if (!HasNavigableAgent() || _enemy.agent.pathPending)
        {
            return false;
        }

        float allowedDistance = Mathf.Max(_enemy.agent.stoppingDistance + extraTolerance, extraTolerance);
        return _enemy.agent.remainingDistance <= allowedDistance && (!_enemy.agent.hasPath || _enemy.agent.velocity.sqrMagnitude < 0.09f);
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

    private Vector3 GetSearchOffset(float radius)
    {
        float angle = (_swarmIndex * GoldenAngle) * Mathf.Deg2Rad;
        Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        float distance = radius * (0.45f + (_swarmIndex % 4) * 0.18f);
        return direction * Mathf.Min(radius, distance);
    }
}
