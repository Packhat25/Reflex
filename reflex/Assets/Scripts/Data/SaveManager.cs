using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.IO;
using System.Collections.Generic;

[System.Serializable]
public class SaveData
{
    public string equippedWeaponName;
    public int soulEssence;
    
    public int healthUpgradeLevel;
    public int damageUpgradeLevel;
    public int critUpgradeLevel;
}

public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }

    public SaveData currentSave;

    [Header("Optional Weapon References")]
    [SerializeField] private List<WeaponData> availableWeapons = new List<WeaponData>();

    private string saveFilePath;
    private readonly Dictionary<string, WeaponData> weaponLookup = new Dictionary<string, WeaponData>(StringComparer.OrdinalIgnoreCase);
    private bool hasPendingEquippedWeaponApply;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (FindFirstObjectByType<SaveManager>() != null)
        {
            return;
        }

        GameObject saveManagerObject = new GameObject("SaveManager");
        saveManagerObject.AddComponent<SaveManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        
        saveFilePath = Path.Combine(Application.persistentDataPath, "save_data.json");
        LoadGame();
        RebuildWeaponLookup(null);
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    public void SaveGame()
    {
        string json = JsonUtility.ToJson(currentSave, true);
        File.WriteAllText(saveFilePath, json);
        Debug.Log("Game Saved to " + saveFilePath);
    }

    public void LoadGame()
    {
        if (File.Exists(saveFilePath))
        {
            string json = File.ReadAllText(saveFilePath);
            currentSave = JsonUtility.FromJson<SaveData>(json);
            if (currentSave == null)
            {
                Debug.LogWarning("Save file exists but was unreadable. Creating a fresh save.");
                CreateDefaultSave();
                SaveGame();
                return;
            }

            Debug.Log("Game Loaded from " + saveFilePath);
        }
        else
        {
            CreateDefaultSave();
            SaveGame();
        }

        if (currentSave == null)
        {
            CreateDefaultSave();
            SaveGame();
        }

        hasPendingEquippedWeaponApply = !string.IsNullOrWhiteSpace(currentSave.equippedWeaponName);
    }

    private void CreateDefaultSave()
    {
        currentSave = new SaveData
        {
            equippedWeaponName = "", // Empty implies default
            soulEssence = 0,
            healthUpgradeLevel = 0,
            damageUpgradeLevel = 0,
            critUpgradeLevel = 0
        };
    }

    public void RefreshPlayerStats()
    {
        PlayerManager playerManager = FindFirstObjectByType<PlayerManager>();
        if (playerManager != null)
        {
            ApplyToPlayer(playerManager);
        }
    }

    public void ApplyToPlayer(PlayerManager playerManager)
    {
        if (playerManager == null || currentSave == null)
        {
            return;
        }

        playerManager.soulEssence = currentSave.soulEssence;
        TryApplySavedWeapon(playerManager);

        // Let the UpgradeManager handle applying the permanent upgrade stats
        if (UpgradeManager.Instance != null)
        {
            UpgradeManager.Instance.ApplyUpgradesToPlayer();
        }
    }
    
    public void SetEquippedWeapon(string weaponName)
    {
        if (currentSave == null)
        {
            CreateDefaultSave();
        }

        currentSave.equippedWeaponName = weaponName;
        hasPendingEquippedWeaponApply = false;
        SaveGame();
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!hasPendingEquippedWeaponApply || currentSave == null || string.IsNullOrWhiteSpace(currentSave.equippedWeaponName))
        {
            return;
        }

        PlayerManager playerManager = FindFirstObjectByType<PlayerManager>();
        if (playerManager != null)
        {
            TryApplySavedWeapon(playerManager);
        }
    }

    private void TryApplySavedWeapon(PlayerManager playerManager)
    {
        if (playerManager == null || currentSave == null || string.IsNullOrWhiteSpace(currentSave.equippedWeaponName))
        {
            hasPendingEquippedWeaponApply = false;
            return;
        }

        if (!TryResolveWeaponData(currentSave.equippedWeaponName, playerManager, out WeaponData weaponToEquip))
        {
            hasPendingEquippedWeaponApply = true;
            Debug.LogWarning($"Saved equipped weapon '{currentSave.equippedWeaponName}' was not found in runtime weapon references yet.");
            return;
        }

        if (playerManager.TryGetComponent(out WeaponManager weaponManager))
        {
            weaponManager.EquipWeaponFromSave(weaponToEquip);
        }
        else
        {
            playerManager.weaponData = weaponToEquip;
        }

        hasPendingEquippedWeaponApply = false;
    }

    private bool TryResolveWeaponData(string weaponName, PlayerManager contextPlayer, out WeaponData weaponData)
    {
        weaponData = null;
        if (string.IsNullOrWhiteSpace(weaponName))
        {
            return false;
        }

        if (weaponLookup.TryGetValue(weaponName, out weaponData))
        {
            return true;
        }

        RebuildWeaponLookup(contextPlayer);
        return weaponLookup.TryGetValue(weaponName, out weaponData);
    }

    private void RebuildWeaponLookup(PlayerManager contextPlayer)
    {
        weaponLookup.Clear();

        if (availableWeapons != null)
        {
            for (int i = 0; i < availableWeapons.Count; i++)
            {
                AddWeaponReference(availableWeapons[i]);
            }
        }

        if (contextPlayer != null)
        {
            AddWeaponReference(contextPlayer.weaponData);
        }

        WeaponPickup[] pickups = FindObjectsByType<WeaponPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < pickups.Length; i++)
        {
            if (pickups[i] != null)
            {
                AddWeaponReference(pickups[i].weaponData);
            }
        }

        WeaponData[] loadedWeaponAssets = Resources.FindObjectsOfTypeAll<WeaponData>();
        for (int i = 0; i < loadedWeaponAssets.Length; i++)
        {
            AddWeaponReference(loadedWeaponAssets[i]);
        }
    }

    private void AddWeaponReference(WeaponData weaponData)
    {
        if (weaponData == null || string.IsNullOrWhiteSpace(weaponData.weaponName))
        {
            return;
        }

        string weaponName = weaponData.weaponName.Trim();
        if (!weaponLookup.ContainsKey(weaponName))
        {
            weaponLookup.Add(weaponName, weaponData);
        }
    }
}
