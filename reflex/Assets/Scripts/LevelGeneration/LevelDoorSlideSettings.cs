using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Reflex/Level Generation/Level Door Slide Settings")]
public class LevelDoorSlideSettings : MonoBehaviour
{
    [Header("Door Leaf Slide Direction (Local Space)")]
    [Tooltip("Direction used for children named door_left.")]
    [SerializeField] private Vector3 doorLeftSlideDirection = Vector3.left;
    [Tooltip("Direction used for children named door_right.")]
    [SerializeField] private Vector3 doorRightSlideDirection = Vector3.right;
    [SerializeField] private bool normalizeDirections = true;

    public Vector3 GetSlideDirection(bool isLeftLeaf)
    {
        Vector3 direction = isLeftLeaf ? doorLeftSlideDirection : doorRightSlideDirection;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            direction = isLeftLeaf ? Vector3.left : Vector3.right;
        }

        return normalizeDirections ? direction.normalized : direction;
    }
}
