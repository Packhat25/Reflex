using UnityEngine;

public class EnemyHurtbox : MonoBehaviour
{
    [Header("References")]
    public EnemyController enemyController;

    private void Start()
    {
        if (enemyController == null)
        {
            enemyController = GetComponentInParent<EnemyController>();
        }
    }

    /// <summary>
    /// Call this method from your player's weapon/attack script when it intersects with this collider.
    /// </summary>
    public void ReceiveDamage(float damageAmount, float attackStunDuration, Vector3 knockbackForce)
    {
        if (enemyController != null)
        {
            // Successfully passing all 3 arguments over to the Controller
            enemyController.TakeDamage(damageAmount, attackStunDuration, knockbackForce);
        }
        else
        {
            Debug.LogWarning("EnemyHurtbox doesn't have an EnemyController assigned!");
        }
    }
}