using System;
using System.Collections.Generic;
using UnityEngine;

public class LevelDoor : MonoBehaviour, IInteractable
{
    [Header("Generation")]
    [SerializeField] private bool participateInGeneration = true;
    [SerializeField] private int routeOrder = -1;
    [SerializeField] private string doorId;

    [Header("Prompts")]
    [SerializeField] private string unlockedPromptPrefix = "Enter";
    [SerializeField] private string noRoutePrompt = "Door inactive";
    [SerializeField] private string roomLockedPrompt = "Clear room first";
    [SerializeField] private string buffChoicePrompt = "Choose a buff first";
    [SerializeField] private string entryBlockedPrompt = "Cannot go back through this door";

    [Header("Door Animation")]
    [SerializeField, Min(0f)] private float openOffset = 1.21f;
    [SerializeField, Min(0.01f)] private float openCloseDuration = 0.32f;
    [SerializeField, Min(0f)] private float lobbyOpenDistance = 4f;
    [SerializeField] private bool useLobbyProximityOpen = true;

    private sealed class DoorLeaf
    {
        public Transform Transform;
        public Vector3 ClosedLocalPosition;
        public Vector3 OpenLocalPosition;
    }

    private readonly List<DoorLeaf> _doorLeaves = new List<DoorLeaf>();
    private LevelDoorSlideSettings _slideSettings;
    private LevelDoorRoute _route;
    private bool _hasRoute;
    private bool _isEntryBlocked;
    private bool _targetOpen;
    private float _openBlend;
    private Transform _cachedPlayerTransform;
    private float _nextPlayerLookupTime;

    public int DoorIndex { get; private set; }
    public bool ParticipateInGeneration => participateInGeneration;
    public int RouteOrder => routeOrder;
    public string DoorId => doorId;
    public bool HasRoute => _hasRoute;

    private void Awake()
    {
        ResolveSlideSettings();
        CacheDoorLeaves();
        ApplyDoorLeafPose(0f);
    }

    private void Update()
    {
        bool shouldOpen = ShouldBeOpen();
        if (shouldOpen != _targetOpen)
        {
            _targetOpen = shouldOpen;
        }

        float direction = _targetOpen ? 1f : -1f;
        if ((direction > 0f && _openBlend < 1f) || (direction < 0f && _openBlend > 0f))
        {
            float duration = Mathf.Max(0.01f, openCloseDuration);
            _openBlend = Mathf.Clamp01(_openBlend + (direction * Time.deltaTime / duration));
            ApplyDoorLeafPose(_openBlend);
        }
    }

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

    public void SetEntryBlocked(bool isBlocked)
    {
        _isEntryBlocked = isBlocked;
    }

    public string GetInteractionText()
    {
        if (!_hasRoute)
        {
            return noRoutePrompt;
        }

        if (_isEntryBlocked)
        {
            return entryBlockedPrompt;
        }

        if (RewardManager.HasInstance && RewardManager.Instance.IsAwaitingBuffChoiceForDoorUnlock)
        {
            return buffChoicePrompt;
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

        if (_isEntryBlocked)
        {
            Debug.Log("Blocked entry door cannot be used for re-entry.", this);
            return;
        }

        LevelRunManager.Instance.TravelTo(_route);
    }

    private bool ShouldBeOpen()
    {
        if (!_hasRoute || _isEntryBlocked)
        {
            return false;
        }

        if (!LevelRunManager.HasInstance)
        {
            return true;
        }

        LevelRunManager runManager = LevelRunManager.Instance;
        bool isLobbyNode = runManager.CurrentStage == 0;

        if (isLobbyNode && useLobbyProximityOpen)
        {
            return IsPlayerNearDoor();
        }

        return runManager.AreDoorsUnlocked;
    }

    private bool IsPlayerNearDoor()
    {
        if (lobbyOpenDistance <= 0f)
        {
            return false;
        }

        if ((_cachedPlayerTransform == null || !_cachedPlayerTransform.gameObject.activeInHierarchy) &&
            Time.time >= _nextPlayerLookupTime)
        {
            _cachedPlayerTransform = ResolvePlayerTransform();
            _nextPlayerLookupTime = Time.time + 0.5f;
        }

        if (_cachedPlayerTransform == null)
        {
            return false;
        }

        float sqrDistance = (transform.position - _cachedPlayerTransform.position).sqrMagnitude;
        return sqrDistance <= lobbyOpenDistance * lobbyOpenDistance;
    }

    private Transform ResolvePlayerTransform()
    {
        if (LevelRunManager.HasInstance &&
            LevelRunManager.Instance.TryGetPersistentPlayerTransform(out Transform persistentPlayerTransform))
        {
            return persistentPlayerTransform;
        }

        PlayerManager player = FindFirstObjectByType<PlayerManager>();
        return player != null ? player.transform : null;
    }

    private void CacheDoorLeaves()
    {
        _doorLeaves.Clear();
        ResolveSlideSettings();

        Transform[] transforms = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform leafTransform = transforms[i];
            if (leafTransform == null || leafTransform == transform)
            {
                continue;
            }

            string normalizedName = Normalize(leafTransform.name);
            bool isLeftLeaf = IsLeafName(normalizedName, "door_left");
            bool isRightLeaf = IsLeafName(normalizedName, "door_right");

            if (!isLeftLeaf && !isRightLeaf)
            {
                continue;
            }

            Vector3 closedLocalPosition = leafTransform.localPosition;
            DoorLeaf leaf = new DoorLeaf
            {
                Transform = leafTransform,
                ClosedLocalPosition = closedLocalPosition,
                OpenLocalPosition = BuildNamedSlideOpenLocalPosition(closedLocalPosition, isLeftLeaf)
            };

            _doorLeaves.Add(leaf);
        }
    }

    private Vector3 BuildNamedSlideOpenLocalPosition(Vector3 closedLocalPosition, bool isLeftLeaf)
    {
        Vector3 slideDirection = _slideSettings != null
            ? _slideSettings.GetSlideDirection(isLeftLeaf)
            : isLeftLeaf ? Vector3.left : Vector3.right;

        return closedLocalPosition + (slideDirection * openOffset);
    }

    private void ResolveSlideSettings()
    {
        if (_slideSettings == null)
        {
            _slideSettings = GetComponent<LevelDoorSlideSettings>();
            if (_slideSettings == null)
            {
                _slideSettings = gameObject.AddComponent<LevelDoorSlideSettings>();
            }
        }
    }

    private void ApplyDoorLeafPose(float openBlend)
    {
        if (_doorLeaves.Count == 0)
        {
            return;
        }

        float easedBlend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(openBlend));
        for (int i = 0; i < _doorLeaves.Count; i++)
        {
            DoorLeaf leaf = _doorLeaves[i];
            if (leaf?.Transform == null)
            {
                continue;
            }

            leaf.Transform.localPosition = Vector3.Lerp(
                leaf.ClosedLocalPosition,
                leaf.OpenLocalPosition,
                easedBlend);
        }
    }

    private static bool IsLeafName(string normalizedName, string expected)
    {
        return normalizedName == expected ||
               normalizedName.StartsWith(expected + " ", StringComparison.Ordinal) ||
               normalizedName.StartsWith(expected + "(", StringComparison.Ordinal) ||
               normalizedName.StartsWith(expected + "_", StringComparison.Ordinal);
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrEmpty(value) ? string.Empty : value.Trim().ToLowerInvariant();
    }
}
