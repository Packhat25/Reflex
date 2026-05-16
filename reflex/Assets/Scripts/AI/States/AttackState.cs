using UnityEngine;

public class AttackState : IEnemyState
{
    private EnemyController _enemy;
    private float _attackTimer;
    private bool _hasFinishedAttacking;

    public AttackState(EnemyController enemy)
    {
        _enemy = enemy;
    }

    public void OnEnter()
    {
        if (_enemy.agent != null && _enemy.agent.isActiveAndEnabled && _enemy.agent.isOnNavMesh)
        {
            _enemy.agent.isStopped = true;
            _enemy.agent.ResetPath();
        }

        _attackTimer = _enemy.GetDirectorAttackOpeningDelay();
    }

    public void Tick()
    {
        if (_enemy.player == null)
        {
            _enemy.ChangeState(new SearchState(_enemy));
            return;
        }

        // 1. Keep looking at player while standing still
        Vector3 dirToPlayer = (_enemy.player.position - _enemy.transform.position).normalized;
        dirToPlayer.y = 0;
        if (dirToPlayer.sqrMagnitude > 0.01f)
        {
            _enemy.transform.rotation = Quaternion.LookRotation(dirToPlayer);
        }

        // 2. Handle the swing timer
        _attackTimer -= Time.deltaTime;
        if (_attackTimer <= 0)
        {
            // Trigger the actual Hitbox code we wrote in EnemyController!
            _enemy.AttackPlayer();
            _attackTimer = _enemy.attackCooldown; // Reset timer for the next bite
        }

        // 3. Only go back to Chase if the player runs away
        float distance = Vector3.Distance(_enemy.transform.position, _enemy.player.position);
        if (distance > GetAttackReleaseDistance())
        {
            _enemy.ChangeState(new ChaseState(_enemy));
        }
    }

    public void OnExit()
    {
        if (_enemy.agent != null && _enemy.agent.isActiveAndEnabled && _enemy.agent.isOnNavMesh)
        {
            _enemy.agent.isStopped = false;
        }
    }

    private float GetAttackRange()
    {
        if (_enemy.attackRange > 0f)
        {
            return _enemy.attackRange;
        }

        return _enemy.EnemyStatData != null ? Mathf.Max(0.25f, _enemy.EnemyStatData.attackRange) : 2f;
    }

    private float GetAttackReleaseDistance()
    {
        float attackRange = GetAttackRange();
        return Mathf.Max(attackRange + 0.35f, attackRange * 1.22f);
    }
}
