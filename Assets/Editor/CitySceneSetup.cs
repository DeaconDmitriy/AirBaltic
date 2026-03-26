using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// AirBaltic Globe — one-click scene setup tool.
///
/// Menu: AirBaltic ▶ Setup Globe Destinations
///
/// What this script does:
///   1. Loads destination card sprites from Assets/Models/AirBalticUI/.
///   2. Assigns the correct sprite to each CityPoint's destinationCard field.
///   3. Ensures the UIManager's new null fields are cleared so it builds its own
///      UI panels at runtime (no manual Inspector wiring needed).
///   4. Re-scans and refreshes the RouteManager's allCities array.
///   5. Marks the scene dirty so the changes are saved.
///
/// Run this ONCE after opening the GameScene in the Unity Editor.
/// It is safe to run multiple times (idempotent).
/// </summary>
public static class CitySceneSetup
{
    // Maps cityName → sprite filename (no extension, in Assets/Models/AirBalticUI/)
    private static readonly Dictionary<string, string> CityCardMap = new()
    {
        { "Canada",   "Canada"       },
        { "Dubai",    "Dubai"        },
        { "England",  "England"      },
        { "Japan",    "Japan"        },
        { "Latvia",   "Latvia"       },
        { "Russia",   "Russia"       },
        { "New York", "UnitedStates" },
    };

    private const string CARD_PATH = "Assets/Models/AirBalticUI/";

    // ── Main entry point ──────────────────────────────────────────────────────

    [MenuItem("AirBaltic/Setup Globe Destinations")]
    static void SetupGlobeDestinations()
    {
        int assigned = 0;
        int missing  = 0;

        // ── 1. Fix sprite importer settings for all destination PNG files ──────
        EnsureSpritesImported();

        // ── 2. Assign destinationCard sprite to every CityPoint in the scene ──
        var cities = Object.FindObjectsByType<CityPoint>(FindObjectsSortMode.None);
        if (cities.Length == 0)
        {
            Debug.LogError("[AirBaltic Setup] No CityPoint objects found. Is GameScene open?");
            return;
        }

        foreach (var city in cities)
        {
            if (!CityCardMap.TryGetValue(city.cityName, out string spriteName))
            {
                Debug.LogWarning($"[AirBaltic Setup] No card mapping for city: \"{city.cityName}\" — skipping.");
                missing++;
                continue;
            }

            string path    = $"{CARD_PATH}{spriteName}.png";
            var    sprite  = AssetDatabase.LoadAssetAtPath<Sprite>(path);

            if (sprite == null)
            {
                Debug.LogError($"[AirBaltic Setup] Sprite not found at: {path}");
                missing++;
                continue;
            }

            if (city.destinationCard != sprite)
            {
                Undo.RecordObject(city, "Assign Destination Card Sprite");
                city.destinationCard = sprite;
                EditorUtility.SetDirty(city);
                assigned++;
            }
            else
            {
                assigned++; // already correct
            }
        }

        // ── 3. Refresh RouteManager.allCities (catches new Canada + Russia) ──
        var routeManager = Object.FindFirstObjectByType<RouteManager>();
        if (routeManager != null)
        {
            var allCityList = Object.FindObjectsByType<CityPoint>(FindObjectsSortMode.None);
            Undo.RecordObject(routeManager, "Refresh RouteManager Cities");
            routeManager.allCities = allCityList;
            EditorUtility.SetDirty(routeManager);
            Debug.Log($"[AirBaltic Setup] RouteManager.allCities refreshed → {allCityList.Length} cities.");
        }
        else
        {
            Debug.LogWarning("[AirBaltic Setup] RouteManager not found in scene.");
        }

        // ── 4. Clear UIManager new-panel refs so they build themselves at runtime ──
        var uiManager = Object.FindFirstObjectByType<UIManager>();
        if (uiManager != null)
        {
            Undo.RecordObject(uiManager, "Clear UIManager auto-build refs");
            uiManager.infoImagePanel  = null;
            uiManager.infoCardImage   = null;
            uiManager.infoCardText    = null;
            uiManager.routeDualPanel  = null;
            uiManager.leftCardImage   = null;
            uiManager.leftCardText    = null;
            uiManager.rightCardImage  = null;
            uiManager.rightCardText   = null;
            EditorUtility.SetDirty(uiManager);
            Debug.Log("[AirBaltic Setup] UIManager new-panel fields cleared — panels will be created at runtime.");
        }

        // ── 5. Save scene ─────────────────────────────────────────────────────
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log($"[AirBaltic Setup] Done. Sprites assigned: {assigned}, missing: {missing}.");
        EditorUtility.DisplayDialog("AirBaltic Setup Complete",
            $"Destination cards assigned: {assigned} / {cities.Length}\n\n" +
            (missing > 0 ? $"⚠ {missing} sprite(s) missing — check Console.\n\n" : "") +
            "Remember to save the scene (Ctrl+S).",
            "OK");
    }

    // ── Utility: ensure all destination PNGs are imported as Sprites ──────────

    private static void EnsureSpritesImported()
    {
        foreach (var entry in CityCardMap)
        {
            string path = $"{CARD_PATH}{entry.Value}.png";
            if (!File.Exists(Path.Combine(Application.dataPath, "..", path))) continue;

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;

            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType    = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                Debug.Log($"[AirBaltic Setup] Re-imported {path} as Sprite.");
            }
        }
    }

    // ── Audit helper: lists all city assignments in the Console ───────────────

    [MenuItem("AirBaltic/Audit City Destination Cards")]
    static void AuditCityCards()
    {
        var cities = Object.FindObjectsByType<CityPoint>(FindObjectsSortMode.None);
        if (cities.Length == 0) { Debug.LogError("No CityPoint objects found."); return; }

        Debug.Log($"[AirBaltic Audit] Found {cities.Length} CityPoint objects:");
        foreach (var city in cities)
        {
            string card = city.destinationCard != null ? city.destinationCard.name : "NULL ⚠";
            int    connections = city.connectedCities?.Length ?? 0;
            Debug.Log($"  • {city.cityName} | card={card} | connections={connections} | pos={city.transform.localPosition:F4}",
                      city);
        }
    }
}
