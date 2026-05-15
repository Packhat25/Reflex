using UnityEngine;

[CreateAssetMenu(fileName = "NewEnemyData", menuName = "Stats/Enemy Data")]
public class EnemyData : ScriptableObject
{
    public float maxHealth = 100f;
    public float speed = 3f;
    public float attackRange = 2f;
    public float attackCooldown = 1.5f;
    public float attackDamage = 10f;
    public bool directionInfluencedDuringAtk = true;
    public bool isStationaryUnit = false; // For enemies that don't move (e.g., turrets)
    
}