// ── New Input System is installed (com.unity.inputsystem ≥ 1.7).
// Used on all non-Android platforms (Editor + standalone PC).
// VR builds run on Android and use OVRInput, which is independent of InputSystem.
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Pause Menu — works in both desktop (editor / PC) and VR (Meta Quest).
///
/// ── Toggle ─────────────────────────────────────────────────────────
///   VR      : OVR Start button
///   Desktop : Escape key (new InputSystem)
///
/// ── Interaction ────────────────────────────────────────────────────
///   Desktop : Unity UI EventSystem + GraphicRaycaster.
///             Mouse clicks fire Button.onClick, drag fires Slider events,
///             Toggle clicks invert the checkbox.
///             Key requirement: canvas.worldCamera must be set so the
///             WorldSpace GraphicRaycaster can map screen→world positions.
///
///   VR      : Physics.Raycast from the right controller anchor hits the
///             canvas BoxCollider; UV coordinates identify which zone was
///             hit; action methods are called directly.  The VR path also
///             uses the thumbstick for fine-grained slider control.
///
/// ── Inspector ──────────────────────────────────────────────────────
///   Drag in UIResume.png, UISettings.png, UIExit.png (imported as Sprite)
///   from Assets/Models/AirBalticUI/.  If left null a labelled rectangle
///   is shown instead — fully functional but unstyled.
///
/// ── Pause ──────────────────────────────────────────────────────────
///   Time.timeScale → 0 when menu opens, 1 when it closes.
///   Audio continues — only gameplay simulation freezes.
/// </summary>
public class MenuController : MonoBehaviour
{
    // ── Singleton + open-state ────────────────────────────────────────────

    public static MenuController Instance { get; private set; }

    /// <summary>True while the pause menu is visible. Checked by VRPointer and GlobeRotator.</summary>
    public static bool IsOpen => Instance != null && Instance._isOpen;

    // ── Inspector ─────────────────────────────────────────────────────────

    [Header("Button Sprites  (UIResume / UISettings / UIExit)")]
    public Sprite resumeSprite;
    public Sprite settingsSprite;
    public Sprite exitSprite;

    [Header("Positioning")]
    public float menuDistance       = 0.70f;
    public float menuVerticalOffset = -0.06f;

    // ── Layout constants ──────────────────────────────────────────────────

    private const float SCALE   = 0.001f;
    private const float PANEL_W = 420f;
    private const float PANEL_H = 500f;

    // UV zone: { minU, minV, maxU, maxV }
    private static readonly float[] Z_RESUME   = { 0.07f, 0.63f, 0.93f, 0.85f };
    private static readonly float[] Z_SETTINGS = { 0.07f, 0.38f, 0.93f, 0.60f };
    private static readonly float[] Z_QUIT     = { 0.07f, 0.12f, 0.93f, 0.34f };
    private static readonly float[] Z_SLIDER   = { 0.07f, 0.57f, 0.93f, 0.73f };
    private static readonly float[] Z_MUTE     = { 0.62f, 0.40f, 0.90f, 0.54f };
    private static readonly float[] Z_BACK     = { 0.22f, 0.06f, 0.78f, 0.23f };

    // ── Button colours ────────────────────────────────────────────────────

    private static readonly Color C_BTN_NORMAL   = Color.white;
    private static readonly Color C_BTN_HOVER    = new Color(0.70f, 0.88f, 1.00f);
    private static readonly Color C_FALLBACK     = new Color(0.13f, 0.25f, 0.55f, 0.93f);
    private static readonly Color C_FALLBACK_HOV = new Color(0.22f, 0.42f, 0.78f, 0.95f);
    private static readonly Color C_MUTE_OFF     = new Color(0.12f, 0.18f, 0.42f, 0.88f);
    private static readonly Color C_MUTE_ON      = new Color(0.35f, 0.62f, 1.00f, 0.95f);

    // ── Runtime state ─────────────────────────────────────────────────────

    private bool  _isOpen;
    private float _masterVolume = 1f;
    private bool  _isMuted;
    private int   _hoveredBtn = -1;


    // ── Scene references ──────────────────────────────────────────────────

    private Transform _headTransform;
    private Transform _rightAnchor;       // right controller anchor (VR)
    private Canvas    _canvas;            // cached for worldCamera updates

    // ── Built UI references ───────────────────────────────────────────────

    private GameObject      _menuRoot;
    private GameObject      _settingsPanel;
    private Image[]         _btnImages    = new Image[3];  // 0=resume 1=settings 2=quit
    private Slider          _volSlider;
    private TextMeshProUGUI _volValueText;
    private Image           _muteCheckBg;  // changes colour for checked/unchecked

    // ── Lifecycle ─────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        var rig = FindFirstObjectByType<OVRCameraRig>();
        if (rig != null)
        {
            _rightAnchor   = rig.rightHandAnchor;
            _headTransform = rig.centerEyeAnchor;
        }
        if (_headTransform == null)
            _headTransform = Camera.main?.transform;

        if (AudioManager.Instance != null)
        {
            _masterVolume = AudioManager.Instance.MasterVolume;
            _isMuted      = AudioManager.Instance.IsMuted;
        }

        BuildMenu();
        SetMenuVisible(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        // ── Toggle the menu ──────────────────────────────────────────
        bool togglePressed = OVRInput.GetDown(OVRInput.Button.Start);

#if UNITY_EDITOR
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            togglePressed = true;
#endif

        if (togglePressed)
        {
            ToggleMenu();
            return;
        }

        if (!_isOpen) return;

        // ── Per-frame interaction (VR only; desktop handled by EventSystem) ──
        if (UnityEngine.XR.XRSettings.isDeviceActive)
            HandleVRInteraction();
    }

    private void LateUpdate()
    {
        if (!_isOpen || _menuRoot == null || _headTransform == null) return;

        // Keep canvas in front of player
        Vector3 fwd = _headTransform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
        fwd.Normalize();

        _menuRoot.transform.position = _headTransform.position
                                     + fwd * menuDistance
                                     + Vector3.up * menuVerticalOffset;
        _menuRoot.transform.rotation = Quaternion.LookRotation(fwd);

        // Keep worldCamera current so GraphicRaycaster can map mouse correctly
        if (_canvas != null && Camera.main != null)
            _canvas.worldCamera = Camera.main;
    }

    // ── Public API ────────────────────────────────────────────────────────

    public void ToggleMenu()  => SetMenuVisible(!_isOpen);
    public void CloseMenu()   => SetMenuVisible(false);

    // ── Button / action handlers (called by Button.onClick AND VR path) ───

    public void OnResumePressed()
    {
        AudioManager.Instance?.PlayButtonSound(0);
        SetMenuVisible(false);
    }

    public void OnSettingsPressed()
    {
        AudioManager.Instance?.PlayButtonSound(1);
        if (_settingsPanel != null)
            _settingsPanel.SetActive(!_settingsPanel.activeSelf);
    }

    public void OnQuitPressed()
    {
        AudioManager.Instance?.PlayButtonSound(2);
        Time.timeScale = 1f;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void OnBackPressed()
    {
        AudioManager.Instance?.PlayButtonSound(0);
        _settingsPanel?.SetActive(false);
    }

    /// <summary>Called by Unity UI Slider.onValueChanged AND the VR path.</summary>
    public void OnVolumeChanged(float value)
    {
        _masterVolume = Mathf.Clamp01(value);
        AudioManager.Instance?.SetMasterVolume(_masterVolume);
        UpdateVolText();
        // Sync slider visual without re-firing the callback
        if (_volSlider != null) _volSlider.SetValueWithoutNotify(_masterVolume);
    }

    /// <summary>Called by Unity UI Toggle.onValueChanged AND the VR path.</summary>
    public void OnMuteToggled(bool muted)
    {
        _isMuted = muted;
        AudioManager.Instance?.SetMasterMute(_isMuted);
        UpdateMuteVisual();
    }

    // ── Visibility & pause ────────────────────────────────────────────────

    private void SetMenuVisible(bool visible)
    {
        _isOpen = visible;
        if (_menuRoot != null) _menuRoot.SetActive(visible);

        Time.timeScale = visible ? 0f : 1f;

        if (!visible && _settingsPanel != null)
            _settingsPanel.SetActive(false);

        if (visible)
        {
            if (AudioManager.Instance != null)
            {
                _masterVolume = AudioManager.Instance.MasterVolume;
                _isMuted      = AudioManager.Instance.IsMuted;
            }
            // Sync Unity UI state
            if (_volSlider != null) _volSlider.SetValueWithoutNotify(_masterVolume);
            UpdateVolText();
            UpdateMuteVisual();
        }
    }

    // ── VR interaction (Physics.Raycast from right controller) ────────────

    private void HandleVRInteraction()
    {
        if (_rightAnchor == null) return;

        bool  trigger = OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger,
                                         OVRInput.Controller.RTouch);
        float thumbH  = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick,
                                     OVRInput.Controller.RTouch).x;

        var ray = new Ray(_rightAnchor.position, _rightAnchor.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, 10f)) { SetHover(-1); return; }
        if (hit.collider.gameObject != _menuRoot)            { SetHover(-1); return; }

        Vector3 local = _menuRoot.transform.InverseTransformPoint(hit.point);
        float u = local.x / (PANEL_W * SCALE) + 0.5f;
        float v = local.y / (PANEL_H * SCALE) + 0.5f;

        bool inSettings = _settingsPanel != null && _settingsPanel.activeSelf;

        if (!inSettings)
        {
            if      (InZone(u, v, Z_RESUME))   { SetHover(0); if (trigger) OnResumePressed(); }
            else if (InZone(u, v, Z_SETTINGS)) { SetHover(1); if (trigger) OnSettingsPressed(); }
            else if (InZone(u, v, Z_QUIT))     { SetHover(2); if (trigger) OnQuitPressed(); }
            else                                SetHover(-1);
        }
        else
        {
            SetHover(-1);

            if (InZone(u, v, Z_SLIDER))
            {
                if (trigger)
                {
                    float t = Mathf.Clamp01((u - Z_SLIDER[0]) / (Z_SLIDER[2] - Z_SLIDER[0]));
                    OnVolumeChanged(t);
                }
                else if (Mathf.Abs(thumbH) > 0.05f)
                {
                    OnVolumeChanged(_masterVolume + thumbH * Time.unscaledDeltaTime * 0.45f);
                }
            }
            else if (InZone(u, v, Z_MUTE))
            {
                if (trigger)
                {
                    _isMuted = !_isMuted;
                    OnMuteToggled(_isMuted);
                }
            }
            else if (InZone(u, v, Z_BACK))
            {
                if (trigger) OnBackPressed();
            }
        }

        // Thumbstick fine-tune even off the slider zone
        if (!InZone(u, v, Z_SLIDER) && Mathf.Abs(thumbH) > 0.05f
            && _settingsPanel != null && _settingsPanel.activeSelf)
        {
            OnVolumeChanged(_masterVolume + thumbH * Time.unscaledDeltaTime * 0.45f);
        }
    }

    // ── Hover highlight ───────────────────────────────────────────────────

    private void SetHover(int index)
    {
        if (_hoveredBtn == index) return;

        if (_hoveredBtn >= 0 && _hoveredBtn < _btnImages.Length
            && _btnImages[_hoveredBtn] != null)
        {
            var img = _btnImages[_hoveredBtn];
            img.color = img.sprite != null ? C_BTN_NORMAL : C_FALLBACK;
        }

        _hoveredBtn = index;

        if (_hoveredBtn >= 0 && _hoveredBtn < _btnImages.Length
            && _btnImages[_hoveredBtn] != null)
        {
            var img = _btnImages[_hoveredBtn];
            img.color = img.sprite != null ? C_BTN_HOVER : C_FALLBACK_HOV;
        }
    }

    // ── Visual updates ────────────────────────────────────────────────────

    private void UpdateVolText()
    {
        if (_volValueText != null)
            _volValueText.text = $"{Mathf.RoundToInt(_masterVolume * 100f)}%";
    }

    private void UpdateMuteVisual()
    {
        if (_muteCheckBg != null)
            _muteCheckBg.color = _isMuted ? C_MUTE_ON : C_MUTE_OFF;
    }

    // ── UI Construction ───────────────────────────────────────────────────

    private void BuildMenu()
    {
        // ── Root canvas ──────────────────────────────────────────────
        _menuRoot = new GameObject("PauseMenuCanvas");

        _canvas             = _menuRoot.AddComponent<Canvas>();
        _canvas.renderMode  = RenderMode.WorldSpace;
        _canvas.sortingOrder = 100;
        // worldCamera is required for the GraphicRaycaster to map screen→world
        // in WorldSpace mode. Set here and refreshed every LateUpdate.
        _canvas.worldCamera = Camera.main;

        _menuRoot.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;

        // GraphicRaycaster: handles desktop mouse events via the EventSystem
        _menuRoot.AddComponent<GraphicRaycaster>();

        var rt        = _menuRoot.GetComponent<RectTransform>();
        rt.sizeDelta  = new Vector2(PANEL_W, PANEL_H);
        rt.localScale = Vector3.one * SCALE;

        // BoxCollider: VR Physics.Raycast hits this
        var col  = _menuRoot.AddComponent<BoxCollider>();
        col.size = new Vector3(PANEL_W * SCALE, PANEL_H * SCALE, 0.008f);

        // ── Panel background ──────────────────────────────────────────
        var bgRootRT = MakeRect("BG", _menuRoot.transform, V(0, 0), V(1, 1));
        bgRootRT.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.08f, 0.18f, 0.96f);

        // ── Title ─────────────────────────────────────────────────────
        MakeTMP("Title", _menuRoot.transform, V(0.05f, 0.88f), V(0.95f, 0.98f),
                "MENU", 44, FontStyles.Bold, new Color(0.62f, 0.82f, 1f),
                TextAlignmentOptions.Center);

        var titleLineRT = MakeRect("TitleLine", _menuRoot.transform, V(0.05f, 0.872f), V(0.95f, 0.876f));
        titleLineRT.gameObject.AddComponent<Image>().color = new Color(0.45f, 0.62f, 1f, 0.42f);

        // ── Three main buttons ────────────────────────────────────────
        _btnImages[0] = BuildButton("ResumeBtn",   _menuRoot.transform, Z_RESUME,
                                    resumeSprite,   "RESUME",   OnResumePressed);
        _btnImages[1] = BuildButton("SettingsBtn",  _menuRoot.transform, Z_SETTINGS,
                                    settingsSprite, "SETTINGS", OnSettingsPressed);
        _btnImages[2] = BuildButton("QuitBtn",      _menuRoot.transform, Z_QUIT,
                                    exitSprite,     "QUIT",     OnQuitPressed);

        // ── Settings sub-panel ────────────────────────────────────────
        BuildSettingsPanel();
    }

    /// <summary>
    /// Creates a Unity UI Button (Image + Button component + onClick).
    /// raycastTarget is forced true so the GraphicRaycaster detects mouse hover/click.
    /// </summary>
    private Image BuildButton(string name, Transform parent, float[] zone,
                               Sprite sprite, string fallbackLabel,
                               UnityEngine.Events.UnityAction onClick)
    {
        var rt  = MakeRect(name, parent, V(zone[0], zone[1]), V(zone[2], zone[3]));
        var img = rt.gameObject.AddComponent<Image>();
        img.raycastTarget = true;   // ← required for EventSystem to see this element

        if (sprite != null)
        {
            img.sprite         = sprite;
            img.color          = C_BTN_NORMAL;
            img.preserveAspect = true;
        }
        else
        {
            img.color = C_FALLBACK;
            var lbl = MakeTMP(name + "Lbl", rt, V(0, 0), V(1, 1),
                              fallbackLabel, 34, FontStyles.Bold, Color.white,
                              TextAlignmentOptions.Center);
            lbl.raycastTarget = false;
        }

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;

        var cols = btn.colors;
        cols.normalColor      = img.sprite != null ? C_BTN_NORMAL   : C_FALLBACK;
        cols.highlightedColor = img.sprite != null ? C_BTN_HOVER     : C_FALLBACK_HOV;
        cols.pressedColor     = new Color(0.50f, 0.75f, 1.00f);
        cols.selectedColor    = cols.normalColor;
        cols.fadeDuration     = 0.08f;
        btn.colors = cols;

        btn.onClick.AddListener(onClick);
        return img;
    }

    private void BuildSettingsPanel()
    {
        _settingsPanel = new GameObject("SettingsPanel");
        _settingsPanel.transform.SetParent(_menuRoot.transform, false);

        var rt     = _settingsPanel.AddComponent<RectTransform>();
        rt.anchorMin = V(0, 0);
        rt.anchorMax = V(1, 1);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var bgSettRT = MakeRect("BG", _settingsPanel.transform, V(0, 0), V(1, 1));
        bgSettRT.gameObject.AddComponent<Image>().color = new Color(0.04f, 0.06f, 0.14f, 0.97f);

        MakeTMP("Title", _settingsPanel.transform, V(0.05f, 0.88f), V(0.95f, 0.98f),
                "SETTINGS", 44, FontStyles.Bold, new Color(0.62f, 0.82f, 1f),
                TextAlignmentOptions.Center);

        var settTitleLineRT = MakeRect("TitleLine", _settingsPanel.transform, V(0.05f, 0.872f), V(0.95f, 0.876f));
        settTitleLineRT.gameObject.AddComponent<Image>().color = new Color(0.45f, 0.62f, 1f, 0.42f);

        // ── Volume label ──────────────────────────────────────────────
        MakeTMP("VolLabel", _settingsPanel.transform, V(0.07f, 0.77f), V(0.93f, 0.87f),
                "AUDIO VOLUME", 26, FontStyles.Normal, new Color(0.78f, 0.90f, 1f),
                TextAlignmentOptions.Center);

        // ── Volume Slider ─────────────────────────────────────────────
        _volSlider = BuildVolumeSlider(_settingsPanel.transform);

        // Percentage text
        _volValueText = MakeTMP("VolValue", _settingsPanel.transform,
                                 V(0.07f, 0.53f), V(0.93f, 0.60f),
                                 $"{Mathf.RoundToInt(_masterVolume * 100f)}%",
                                 22, FontStyles.Normal, Color.white,
                                 TextAlignmentOptions.Center);

        MakeTMP("SliderHint", _settingsPanel.transform,
                V(0.07f, 0.47f), V(0.93f, 0.53f),
                "click or drag  |  thumbstick to fine-tune",
                15, FontStyles.Normal, new Color(0.55f, 0.68f, 0.80f, 0.90f),
                TextAlignmentOptions.Center);

        // ── Mute toggle ───────────────────────────────────────────────
        MakeTMP("MuteLabel", _settingsPanel.transform,
                V(0.07f, Z_MUTE[1]), V(Z_MUTE[0] - 0.02f, Z_MUTE[3]),
                "MUTE AUDIO", 24, FontStyles.Normal, new Color(0.78f, 0.90f, 1f),
                TextAlignmentOptions.Left);

        BuildMuteToggle(_settingsPanel.transform);

        // ── Back button ───────────────────────────────────────────────
        var backRT = MakeRect("BackBtn", _settingsPanel.transform,
                               V(Z_BACK[0], Z_BACK[1]), V(Z_BACK[2], Z_BACK[3]));
        var backImg = backRT.gameObject.AddComponent<Image>();
        backImg.color         = C_FALLBACK;
        backImg.raycastTarget = true;
        var backBtn = backRT.gameObject.AddComponent<Button>();
        backBtn.targetGraphic = backImg;
        var backCols = backBtn.colors;
        backCols.normalColor      = C_FALLBACK;
        backCols.highlightedColor = C_FALLBACK_HOV;
        backCols.pressedColor     = new Color(0.22f, 0.50f, 0.90f);
        backBtn.colors = backCols;
        backBtn.onClick.AddListener(OnBackPressed);
        MakeTMP("BackLbl", backRT, V(0, 0), V(1, 1),
                "BACK", 30, FontStyles.Bold, Color.white,
                TextAlignmentOptions.Center);

        _settingsPanel.SetActive(false);
    }

    /// <summary>
    /// Builds a proper Unity UI Slider for the volume control.
    /// Structure matches what Unity's own Slider expects for fill and handle.
    /// </summary>
    private Slider BuildVolumeSlider(Transform parent)
    {
        // Root
        var sliderRT = MakeRect("VolumeSlider", parent,
                                 V(Z_SLIDER[0], Z_SLIDER[1]),
                                 V(Z_SLIDER[2], Z_SLIDER[3]));
        var slider = sliderRT.gameObject.AddComponent<Slider>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue  = 0f;
        slider.maxValue  = 1f;

        // Background track
        var bgRT  = MakeRect("Background", sliderRT, V(0, 0), V(1, 1));
        var bgImg = bgRT.gameObject.AddComponent<Image>();
        bgImg.color         = new Color(0.12f, 0.18f, 0.42f, 0.88f);
        bgImg.raycastTarget = true;   // must be true so slider detects mouse on track

        // Fill Area (stretch, slight inset so handle can travel past endpoints)
        var fillAreaRT      = MakeRect("Fill Area", sliderRT, V(0, 0.25f), V(1, 0.75f));
        fillAreaRT.offsetMin = new Vector2(5f,  0f);
        fillAreaRT.offsetMax = new Vector2(-5f, 0f);

        // Fill child — anchors managed by Slider component
        var fillRT  = MakeRect("Fill", fillAreaRT, V(0, 0), V(0, 1));
        fillRT.sizeDelta = new Vector2(10f, 0f);
        var fillImg = fillRT.gameObject.AddComponent<Image>();
        fillImg.color         = new Color(0.35f, 0.62f, 1f, 0.92f);
        fillImg.raycastTarget = false;

        // Handle Slide Area
        var handleAreaRT      = MakeRect("Handle Slide Area", sliderRT, V(0, 0), V(1, 1));
        handleAreaRT.offsetMin = new Vector2(10f,  0f);
        handleAreaRT.offsetMax = new Vector2(-10f, 0f);

        // Handle
        var handleRT  = MakeRect("Handle", handleAreaRT, V(0, 0), V(0, 1));
        handleRT.sizeDelta = new Vector2(24f, 0f);
        var handleImg = handleRT.gameObject.AddComponent<Image>();
        handleImg.color         = new Color(0.90f, 0.95f, 1.00f);
        handleImg.raycastTarget = true;

        // Wire up slider
        slider.fillRect      = fillRT;
        slider.handleRect    = handleRT;
        slider.targetGraphic = handleImg;
        slider.SetValueWithoutNotify(_masterVolume);
        slider.onValueChanged.AddListener(OnVolumeChanged);   // desktop drag fires this

        return slider;
    }

    /// <summary>
    /// Builds a Unity UI Toggle for the mute checkbox.
    /// The checkbox background changes colour to indicate checked/unchecked state.
    /// </summary>
    private void BuildMuteToggle(Transform parent)
    {
        // Checkbox outline border
        var muteOutlineRT = MakeRect("MuteOutline", parent,
                  V(Z_MUTE[0] - 0.010f, Z_MUTE[1] - 0.012f),
                  V(Z_MUTE[2] + 0.010f, Z_MUTE[3] + 0.012f));
        muteOutlineRT.gameObject.AddComponent<Image>().color = new Color(0.45f, 0.62f, 1f, 0.45f);

        // Toggle root — same size as checkbox
        var toggleRT = MakeRect("MuteToggle", parent,
                                 V(Z_MUTE[0], Z_MUTE[1]), V(Z_MUTE[2], Z_MUTE[3]));
        var toggle = toggleRT.gameObject.AddComponent<Toggle>();

        // Background (the checkbox box — colour changes on state)
        var bgRT  = MakeRect("Background", toggleRT, V(0, 0), V(1, 1));
        _muteCheckBg              = bgRT.gameObject.AddComponent<Image>();
        _muteCheckBg.color        = _isMuted ? C_MUTE_ON : C_MUTE_OFF;
        _muteCheckBg.raycastTarget = true;

        // Checkmark fill (hidden child; Toggle.graphic drives visibility)
        var checkRT  = MakeRect("Checkmark", bgRT, V(0.10f, 0.10f), V(0.90f, 0.90f));
        var checkImg = checkRT.gameObject.AddComponent<Image>();
        checkImg.color         = new Color(0.95f, 0.98f, 1.00f, 0.90f);
        checkImg.raycastTarget = false;

        toggle.targetGraphic = _muteCheckBg;
        toggle.graphic       = checkImg;
        toggle.isOn          = _isMuted;

        var cols = toggle.colors;
        cols.normalColor      = Color.white;   // tint applied to targetGraphic
        cols.highlightedColor = new Color(1f, 1f, 1f, 0.85f);
        cols.pressedColor     = new Color(0.8f, 0.9f, 1f);
        toggle.colors = cols;

        toggle.onValueChanged.AddListener(OnMuteToggled);    // desktop click fires this
    }

    // ── Static UI helpers ─────────────────────────────────────────────────

    private static RectTransform MakeRect(string name, Transform parent,
                                           Vector2 anchorMin, Vector2 anchorMax)
    {
        var go       = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt       = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    private static TextMeshProUGUI MakeTMP(string name, Transform parent,
                                            Vector2 anchorMin, Vector2 anchorMax,
                                            string text, float size, FontStyles style,
                                            Color color, TextAlignmentOptions align)
    {
        var rt  = MakeRect(name, parent, anchorMin, anchorMax);
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.text              = text;
        tmp.fontSize          = size;
        tmp.fontStyle         = style;
        tmp.color             = color;
        tmp.alignment         = align;
        tmp.enableWordWrapping = false;
        tmp.overflowMode      = TextOverflowModes.Overflow;
        tmp.raycastTarget     = false;
        return tmp;
    }

    private static bool InZone(float u, float v, float[] z)
        => u >= z[0] && u <= z[2] && v >= z[1] && v <= z[3];

    private static Vector2 V(float x, float y) => new Vector2(x, y);
}
