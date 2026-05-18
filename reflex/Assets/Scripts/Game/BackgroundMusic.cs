using UnityEngine;

public class BackgroundMusic : MonoBehaviour
{
    private static BackgroundMusic instance;

    void Awake()
    {
        // Check if an instance of this music player already exists
        if (instance != null && instance != this)
        {
            Destroy(this.gameObject); // Destroy duplicate music players
            return;
        }

        instance = this;
        DontDestroyOnLoad(this.gameObject); // Keep this object alive across scenes
    }
}