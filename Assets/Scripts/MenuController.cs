using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuController : MonoBehaviour
{
    [Header("Scene Names (must match Build Settings)")]
    public string gameSceneName = "GameScene";
    public string menuSceneName = "MenuScene";

    [Header("In-Game Menu Panel (GameScene only)")]
    public GameObject inGameMenuPanel;

    [Header("Settings Panel (dummy)")]
    public GameObject settingsPanel;

    private bool _menuVisible;

    private void Start()
    {
        if (inGameMenuPanel != null) inGameMenuPanel.SetActive(false);
        if (settingsPanel   != null) settingsPanel  .SetActive(false);
        _menuVisible = false;
    }

    private void Update()
    {
        if (OVRInput.GetDown(OVRInput.Button.Start))
            ToggleInGameMenu();
        }

    public void OnStartPressed()
    {
        SceneManager.LoadScene(gameSceneName);
    }

    public void OnSettingsPressed()
    {
        if (settingsPanel == null) return;
            settingsPanel.SetActive(!settingsPanel.activeSelf);
    }

    public void OnExitPressed()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void OnBackToMenuPressed()
    {
        SceneManager.LoadScene(menuSceneName);
    }

    public void ToggleInGameMenu()
    {
        if (inGameMenuPanel == null) return;
        _menuVisible = !_menuVisible;
        inGameMenuPanel.SetActive(_menuVisible);
        if (!_menuVisible && settingsPanel != null)
            settingsPanel.SetActive(false);
    }
}
