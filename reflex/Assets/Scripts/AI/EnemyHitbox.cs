using UnityEngine;

public class EnemyHitbox : MonoBehaviour
{
    [SerializeField] private EnemyController enemyController;
    
    private void OnTriggerEnter(Collider other)
    {
        TryDamagePlayer(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryDamagePlayer(other);
    }

    private void TryDamagePlayer(Collider other)
    {
        PlayerManager playerManager = other.GetComponentInParent<PlayerManager>();
        if (playerManager != null && (other.CompareTag("Player") || playerManager.CompareTag("Player")))
        {
            playerManager.TakeDamage(enemyController.attackDamage);
            enemyController.HitboxOff();
        }
    }
}
