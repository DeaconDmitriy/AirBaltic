using UnityEngine;
using UnityEditor;

/// <summary>
/// One-click tool to find the rogue AudioSource that plays ambient sound at startup.
/// Menu: AirBaltic → Audit AudioSources
///
/// Run this while the scene is open (NOT in Play Mode).
/// Click any warning line in the Console to select the offending GameObject.
/// </summary>
public static class AudioSourceAuditor
{
    [MenuItem("AirBaltic/Audit AudioSources in Scene")]
    static void Audit()
    {
        var sources = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
        int rogueCount = 0;

        Debug.Log($"[AudioAudit] Scanning {sources.Length} AudioSource(s) in scene...");

        foreach (var src in sources)
        {
            bool playOnAwake = src.playOnAwake;
            bool hasClip     = src.clip != null;
            bool isLoop      = src.loop;
            string go        = src.gameObject.name;

            // Flag anything that could auto-play ambient at startup
            if (playOnAwake && hasClip)
            {
                Debug.LogWarning(
                    $"[AudioAudit] ⚠ ROGUE: '{go}' — clip: '{src.clip.name}'" +
                    $" | loop={isLoop} | volume={src.volume:F2}" +
                    $"\n  → Uncheck 'Play On Awake' or remove this AudioSource " +
                    $"if AudioManager owns it now.",
                    src.gameObject);   // ← clicking this line selects the GameObject
                rogueCount++;
            }

            // Also flag AudioSources with no clip but Play On Awake ticked (harmless but messy)
            if (playOnAwake && !hasClip)
            {
                Debug.Log(
                    $"[AudioAudit] ℹ '{go}' has Play On Awake=true but no clip assigned (harmless).",
                    src.gameObject);
            }
        }

        // Check AudioManager clips are assigned
        var am = Object.FindFirstObjectByType<AudioManager>();
        if (am == null)
        {
            Debug.LogError("[AudioAudit] ✖ No AudioManager found in scene! Create the GameObject and attach AudioManager.cs");
        }
        else
        {
            if (am.cityAmbienceSound == null)
                Debug.LogWarning("[AudioAudit] ⚠ AudioManager.cityAmbienceSound is NOT assigned — city hover sound will be silent!", am.gameObject);
            else
                Debug.Log($"[AudioAudit] ✔ cityAmbienceSound = '{am.cityAmbienceSound.name}'", am.gameObject);

            if (am.backgroundMusic == null)
                Debug.LogWarning("[AudioAudit] ⚠ AudioManager.backgroundMusic is NOT assigned — no background music.", am.gameObject);
            else
                Debug.Log($"[AudioAudit] ✔ backgroundMusic = '{am.backgroundMusic.name}'", am.gameObject);

            if (am.takeOffSound == null)
                Debug.LogWarning("[AudioAudit] ⚠ AudioManager.takeOffSound is NOT assigned.", am.gameObject);

            if (am.landingSound == null)
                Debug.LogWarning("[AudioAudit] ⚠ AudioManager.landingSound is NOT assigned.", am.gameObject);

            if (am.buttonSounds == null || am.buttonSounds.Length == 0)
                Debug.LogWarning("[AudioAudit] ⚠ AudioManager.buttonSounds array is empty — UI click sounds will be silent.", am.gameObject);
            else
                Debug.Log($"[AudioAudit] ✔ buttonSounds: {am.buttonSounds.Length} clip(s) assigned.", am.gameObject);
        }

        if (rogueCount == 0)
            Debug.Log("[AudioAudit] ✔ No rogue Play-On-Awake AudioSources found.");
        else
            Debug.LogWarning($"[AudioAudit] Found {rogueCount} rogue AudioSource(s). See warnings above — click each line to select the GameObject.");
    }
}
