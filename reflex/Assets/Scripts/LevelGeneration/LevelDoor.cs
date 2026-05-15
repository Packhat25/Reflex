using UnityEngine;

public class LevelDoor : MonoBehaviour, IInteractable
{
    [SerializeField] private string unlockedPromptPrefix = "Enter";
    [SerializeField] private string noRoutePrompt = "Door inactive";
    [SerializeField] private string roomLockedPrompt = "Clear room first";

    private LevelDoorRoute _route;
    private bool _hasRoute;

    public int DoorIndex { get; private set; }

    public void Configure(LevelDoorRoute route)
    {
        _route = route;
        _hasRoute = true;
        DoorIndex = route.DoorIndex;
    }

    public void ClearRoute(int doorIndex)
    {
        _hasRoute = false;
        DoorIndex = doorIndex;
    }

    public string GetInteractionText()
    {
        if (!_hasRoute)
        {
            return noRoutePrompt;
        }

        if (LevelRunManager.HasInstance && !LevelRunManager.Instance.AreDoorsUnlocked)
        {
            return roomLockedPrompt;
        }

        string destination = string.IsNullOrEmpty(_route.DestinationLabel)
            ? _route.DestinationSceneName
            : _route.DestinationLabel;

        return unlockedPromptPrefix + " " + destination;
    }

    public void Interact(PlayerManager player)
    {
        if (!_hasRoute)
        {
            Debug.Log("Door has no generated route assigned yet.", this);
            return;
        }

        LevelRunManager.Instance.TravelTo(_route);
    }
}
