using UnityEngine;
using TMPro;

public class CityPoint : MonoBehaviour
{
    [Header("City Data")]
    public string cityName = "City Name";

    [TextArea(3, 8)]
    public string cityDescription = "City information.\n(Text to be filled by the client)";

    [Header("Geography")]
    public float latitude;
    public float longitude;

    [Header("Connections (one-way, from this city)")]
    public CityPoint[] connectedCities;

    [TextArea(2, 5)]
    public string[] missionTexts;

    [Header("Destination Card")]
    [Tooltip("Assign the matching PNG from Assets/Models/AirBalticUI/ (imported as Sprite).")]
    public Sprite destinationCard;

    [Header("Visuals")]
    public Renderer markerRenderer;
    public GameObject pulseEffect;

    [Header("Colors")]
    public Color idleColor      = new Color(1f, 0.15f, 0.15f);
    public Color selectedColor  = new Color(1f, 0.65f, 0f);
    public Color reachableColor = new Color(0.1f, 1f, 0.2f);
    public Color dimmedColor    = new Color(0.25f, 0.25f, 0.25f);
    public Color hoverColor     = new Color(1f, 0.95f, 0.30f);   // bright yellow — hover pre-click
    public Color arrivedColor   = new Color(0.25f, 0.70f, 1.00f); // sky-blue — final destination

    [Header("City Label")]
    [Tooltip("Size of the floating city name text in world units.")]
    public float labelFontSize  = 1.8f;
    [Tooltip("How far the label floats above the city dot (outward from globe centre).")]
    public float labelOffset    = 0.045f;

    public enum State { Idle, Selected, Reachable, Dimmed, Hovered, Arrived }
    public State CurrentState { get; private set; } = State.Idle;

    private Material  _mat;
    private State     _preHoverState = State.Idle;   // state to restore after hover ends

    // City label
    private Transform _labelTransform;
    private Transform _globeTransform;  // cached for outward-direction offset

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int BaseColorId     = Shader.PropertyToID("_BaseColor");

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (markerRenderer != null && markerRenderer.sharedMaterial != null)
        {
            _mat = new Material(markerRenderer.sharedMaterial);
            markerRenderer.material = _mat;
        }
        else if (markerRenderer != null)
        {
            Debug.LogWarning($"[CityPoint] {gameObject.name}: markerRenderer has no material assigned.", this);
        }
    }

    private void Start()
    {
        // Cache globe transform for label offset direction
        var globeGO = GameObject.Find("Globe");
        _globeTransform = globeGO != null ? globeGO.transform : null;

        ApplyState(State.Idle);
        CreateLabel();
    }

    private void LateUpdate()
    {
        if (_labelTransform == null || Camera.main == null) return;

        // Position: outward from globe centre, above city dot
        Vector3 globeCenter = _globeTransform != null ? _globeTransform.position : Vector3.zero;
        Vector3 outward     = (transform.position - globeCenter).normalized;
        _labelTransform.position = transform.position + outward * labelOffset;

        // Billboard: always face camera
        _labelTransform.rotation = Camera.main.transform.rotation;
    }

    // ── Public State API ──────────────────────────────────────────────────────

    /// <summary>
    /// Sets a permanent state (Idle/Selected/Reachable/Dimmed/Arrived).
    /// Also syncs the pre-hover baseline so hovering after a state change works correctly.
    /// </summary>
    public void SetState(State newState)
    {
        _preHoverState = newState;  // keep baseline in sync
        CurrentState   = newState;
        ApplyState(newState);
    }

    /// <summary>
    /// Applies a transient hover highlight. Restores previous state when hovered=false.
    /// No-op if the city is already Selected or Arrived (already clearly highlighted).
    /// </summary>
    public void SetHovered(bool hovered)
    {
        if (hovered)
        {
            // Don't override important states
            if (CurrentState == State.Selected || CurrentState == State.Arrived
                || CurrentState == State.Hovered) return;

            _preHoverState = CurrentState;
            CurrentState   = State.Hovered;
            ApplyState(State.Hovered);
        }
        else if (CurrentState == State.Hovered)
        {
            CurrentState = _preHoverState;
            ApplyState(_preHoverState);
        }
    }

    // ── Connection helpers ────────────────────────────────────────────────────

    public bool IsConnectedTo(CityPoint other)
    {
        if (connectedCities == null) return false;
        foreach (var c in connectedCities)
            if (c == other) return true;
        return false;
    }

    public string GetMissionText(CityPoint destination)
    {
        if (connectedCities != null)
            for (int i = 0; i < connectedCities.Length; i++)
                if (connectedCities[i] == destination)
                    return (missionTexts != null && i < missionTexts.Length && !string.IsNullOrWhiteSpace(missionTexts[i]))
                        ? missionTexts[i]
                        : DefaultMission(destination);
        return DefaultMission(destination);
    }

    private string DefaultMission(CityPoint destination) =>
        $"Route: {cityName} → {destination.cityName}\n\n" +
        "Airline Mission:\n(Text to be filled by the client)";

    // ── Visual state ──────────────────────────────────────────────────────────

    private void ApplyState(State state)
    {
        float emissionMult;
        Color c;

        switch (state)
        {
            case State.Selected:
                c = selectedColor;  emissionMult = 2.0f;  break;
            case State.Reachable:
                c = reachableColor; emissionMult = 2.0f;  break;
            case State.Dimmed:
                c = dimmedColor;    emissionMult = 0.2f;  break;
            case State.Hovered:
                c = hoverColor;     emissionMult = 3.0f;  break;  // extra-bright to pop
            case State.Arrived:
                c = arrivedColor;   emissionMult = 2.0f;  break;
            default: // Idle
                c = idleColor;      emissionMult = 2.0f;  break;
        }

        if (_mat != null)
        {
            _mat.SetColor(BaseColorId, c);
            if (_mat.IsKeywordEnabled("_EMISSION") || _mat.HasProperty(EmissionColorId))
            {
                _mat.EnableKeyword("_EMISSION");
                _mat.SetColor(EmissionColorId, c * emissionMult);
            }
        }

        if (pulseEffect != null)
            pulseEffect.SetActive(state == State.Idle || state == State.Reachable
                                                       || state == State.Hovered);
    }

    // ── Label creation ────────────────────────────────────────────────────────

    private void CreateLabel()
    {
        if (string.IsNullOrWhiteSpace(cityName)) return;

        var go          = new GameObject("CityLabel");
        go.transform.SetParent(transform, false);  // child so it moves with city
        _labelTransform = go.transform;

        var tmp               = go.AddComponent<TextMeshPro>();
        tmp.text              = cityName;
        tmp.fontSize          = labelFontSize;
        tmp.color             = Color.white;
        tmp.alignment         = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.sortingOrder      = 10;   // render on top of globe surface

        // Scale: TMP world-space text uses Unity units — keep it small
        go.transform.localScale = Vector3.one * 0.012f;
    }
}
