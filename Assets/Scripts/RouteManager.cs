using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class RouteManager : MonoBehaviour
{
    public static RouteManager Instance { get; private set; }

    [Header("Cities (assign all CityPoint objects)")]
    public CityPoint[] allCities;

    [Header("References")]
    public PlaneController planeController;
    public UIManager       uiManager;
    public Transform       globeTransform;

    [Header("Route Line")]
    public int   arcSegments = 40;
    public float arcLift     = 0.08f;

    public CityPoint[] AllCities => allCities;

    private enum AppState { Idle, DepartureSelected, RouteComplete }
    private AppState   _state      = AppState.Idle;
    private CityPoint  _departure;
    private CityPoint  _destination;
    private LineRenderer _line;
    private Transform  _rightAnchor;

    private void Awake()
    {
        Instance = this;
        _line    = GetComponent<LineRenderer>();
        _line.enabled = false;
    }

    private void Start()
    {
        var rig = FindFirstObjectByType<OVRCameraRig>();
        if (rig != null) _rightAnchor = rig.rightControllerAnchor;

        if (allCities == null || allCities.Length == 0)
            allCities = FindObjectsByType<CityPoint>(FindObjectsSortMode.None);

        ResetAllCities();
    }

    private void Update()
    {
        if (OVRInput.GetDown(OVRInput.Button.Two,  OVRInput.Controller.RTouch) ||
            OVRInput.GetDown(OVRInput.Button.Four, OVRInput.Controller.LTouch))
        {
            ResetState();
            return;
        }

        if (_state == AppState.RouteComplete) return;

        if (FindFirstObjectByType<VRPointer>() == null)
            HandleCityRaycast();
    }

    private void LateUpdate()
                {
        UpdateRouteLine();
    }

    public void OnCityClicked(CityPoint city)
    {
        if (_state == AppState.RouteComplete) return;
        OnCityTapped(city);
                }

    public void OnPlaneReleased(Vector3 worldPosition)
    {
        if (_state != AppState.DepartureSelected) return;

        const float snapRadius = 0.30f;
        CityPoint nearest = null;
        float nearestDist = snapRadius;

        foreach (var city in allCities)
        {
            if (city.CurrentState != CityPoint.State.Reachable) continue;
            float d = Vector3.Distance(worldPosition, city.transform.position);
            if (d < nearestDist) { nearestDist = d; nearest = city; }
        }

        if (nearest != null) CompleteRoute(nearest);
        else                 planeController?.ReturnToDeparture();
    }

    public void OnPlaneDroppedOnCity(CityPoint city)
    {
        if (_state != AppState.DepartureSelected) return;

        if (city.CurrentState == CityPoint.State.Reachable) CompleteRoute(city);
        else                                                  planeController?.ReturnToDeparture();
    }

    private void HandleCityRaycast()
    {
        if (_rightAnchor == null) return;
        if (!OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch)) return;

        Ray ray = new Ray(_rightAnchor.position, _rightAnchor.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, 20f)) return;

        CityPoint city = hit.collider.GetComponentInParent<CityPoint>();
        if (city == null) return;

        OnCityClicked(city);
    }

    private void OnCityTapped(CityPoint city)
    {
        switch (_state)
        {
            case AppState.Idle:
                SelectDeparture(city);
                break;

            case AppState.DepartureSelected:
                if (city == _departure)
                    ResetState();
                else if (city.CurrentState == CityPoint.State.Reachable)
                    CompleteRoute(city);
                else
                    uiManager?.ShowInfoCard(city);
                break;
        }
    }

    private void SelectDeparture(CityPoint city)
    {
        _state     = AppState.DepartureSelected;
        _departure = city;

        city.SetState(CityPoint.State.Selected);

        foreach (var c in allCities)
        {
            if (c == city) continue;
            c.SetState(city.IsConnectedTo(c) ? CityPoint.State.Reachable : CityPoint.State.Dimmed);
        }

        uiManager?.ShowInfoCard(city);
        planeController?.SpawnAt(city);
    }

    private void CompleteRoute(CityPoint destination)
    {
        _state       = AppState.RouteComplete;
        _destination = destination;

        DrawArc(_departure.transform.position, destination.transform.position);

        string missionText = _departure.GetMissionText(destination);
        uiManager?.ShowMissionCard(_departure, destination, missionText);

        planeController?.FlyTo(destination);
    }

    public void ResetRoute() => ResetState();

    private void ResetState()
    {
        _state       = AppState.Idle;
        _departure   = null;
        _destination = null;
        _line.enabled = false;
        ResetAllCities();
        uiManager?.HideAll();
        planeController?.Hide();
    }

    private void ResetAllCities()
    {
        foreach (var c in allCities)
            c.SetState(CityPoint.State.Idle);
    }

    private Vector3 GlobeCenter()
    {
        if (globeTransform != null) return globeTransform.position;
        var g = GameObject.Find("Globe");
        if (g != null) { globeTransform = g.transform; return g.transform.position; }
        return Vector3.zero;
    }

    private void DrawArc(Vector3 from, Vector3 to)
    {
        _line.enabled       = true;
        _line.positionCount = arcSegments + 1;

        Vector3 center  = GlobeCenter();
        Vector3 fromRel = from - center;
        Vector3 toRel   = to   - center;
        float   r       = fromRel.magnitude;

        for (int i = 0; i <= arcSegments; i++)
        {
            float   t        = i / (float)arcSegments;
            Vector3 onSphere = Vector3.Slerp(fromRel.normalized, toRel.normalized, t) * r;
            float   lift     = arcLift * Mathf.Sin(t * Mathf.PI);
            _line.SetPosition(i, center + onSphere + onSphere.normalized * lift);
        }
    }

    private void UpdateRouteLine()
    {
        if (_state != AppState.RouteComplete) return;
        if (_departure == null || _destination == null) return;
        DrawArc(_departure.transform.position, _destination.transform.position);
    }
}
