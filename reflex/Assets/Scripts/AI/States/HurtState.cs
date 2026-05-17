using UnityEngine;

public class HurtState : IEnemyState
{
    private EnemyController _enemy;
    private float _stunDuration;
    private float _timer;
    private Vector3 _knockbackVel;
    private float _decayRate = 5f; // How fast the knockback slows down

    public HurtState(EnemyController enemy, float stunDuration)
    {
        _enemy = enemy;
        _stunDuration = stunDuration;
    }

    public void OnEnter()
    {
        _timer = 0f;

        // Pull the knockback vector assigned during TakeDamage
        _knockbackVel = _enemy.knockbackVelocity;

        if (_enemy.agent != null && _enemy.agent.isActiveAndEnabled && _enemy.agent.isOnNavMesh)
        {
            // Stop the agent completely so it doesn't try to navigate back to its target mid-air
            _enemy.agent.isStopped = true;
            _enemy.agent.ResetPath();
        }
    }

    public void Tick()
    {
        _timer += Time.deltaTime;

        // 1. Seamlessly apply and decay the knockback force over time
        if (_knockbackVel.sqrMagnitude > 0.01f)
        {
            if (_enemy.agent != null && _enemy.agent.isOnNavMesh)
            {
                // NavMeshAgent.Move handles collision mapping so enemies don't clip through walls
                _enemy.agent.Move(_knockbackVel * Time.deltaTime);
            }
            else
            {
                // Fallback if the agent gets detached from NavMesh
                _enemy.transform.position += _knockbackVel * Time.deltaTime;
            }

            // Smoothly decay the speed of the knockback toward 0
            _knockbackVel = Vector3.Lerp(_knockbackVel, Vector3.zero, Time.deltaTime * _decayRate);
        }

        // 2. Check if the stun timer has finished
        if (_timer >= _stunDuration)
        {
            // Fall back to chase/search depending on player status
            if (_enemy.CanSeePlayer())
            {
                _enemy.ChangeState(new ChaseState(_enemy));
            }
            else
            {
                _enemy.ChangeState(new SearchState(_enemy));
            }
        }
    }

    public void OnExit()
    {
        // Clear variables
        _enemy.knockbackVelocity = Vector3.zero;

        // Re-enable NavMesh calculations when exiting the hurt state
        if (_enemy.agent != null && _enemy.agent.isActiveAndEnabled && _enemy.agent.isOnNavMesh)
        {
            _enemy.agent.isStopped = false;
        }
    }
}