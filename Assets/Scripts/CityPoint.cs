using UnityEngine;

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

    public enum State { Idle, Selected, Reachable, Dimmed }
    public State CurrentState { get; private set; } = State.Idle;

    private Material _mat;
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int BaseColorId     = Shader.PropertyToID("_BaseColor");

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

    private void Start() => ApplyState(State.Idle);

    public void SetState(State newState)
    {
        CurrentState = newState;
        ApplyState(newState);
    }

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

    private void ApplyState(State state)
    {
        Color c = state switch
        {
            State.Selected  => selectedColor,
            State.Reachable => reachableColor,
            State.Dimmed    => dimmedColor,
            _               => idleColor,
        };

        if (_mat != null)
        {
            _mat.SetColor(BaseColorId, c);
            if (_mat.IsKeywordEnabled("_EMISSION") || _mat.HasProperty(EmissionColorId))
            {
                _mat.EnableKeyword("_EMISSION");
                _mat.SetColor(EmissionColorId, c * (state == State.Dimmed ? 0.2f : 2.0f));
            }
        }

        if (pulseEffect != null)
            pulseEffect.SetActive(state == State.Idle || state == State.Reachable);
    }
}
