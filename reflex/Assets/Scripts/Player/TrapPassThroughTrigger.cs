using UnityEngine;

public class TrapPassThroughTrigger : MonoBehaviour
{
    public TrapStateController controller;

    public TrapStateController Controller => controller;

    public void Configure(TrapStateController targetController)
    {
        controller = targetController;
    }

    private void Reset()
    {
        controller = GetComponentInParent<TrapStateController>();

        if (TryGetComponent(out Collider triggerCollider))
        {
            triggerCollider.isTrigger = true;
        }
    }

    private void Awake()
    {
        if (controller == null)
        {
            controller = GetComponentInParent<TrapStateController>();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (controller == null || !controller.TryGetPlayer(other, out PlayerManager player))
        {
            return;
        }

        controller.NotifyPlayerEnteredPassThroughTrigger(player);
    }

    private void OnTriggerExit(Collider other)
    {
        if (controller == null || !controller.TryGetPlayer(other, out PlayerManager player))
        {
            return;
        }

        controller.NotifyPlayerExitedPassThroughTrigger(player);
    }
}
