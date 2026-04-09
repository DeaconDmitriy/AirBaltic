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

    [Header("City Proximity Audio")]
    [Tooltip("How close the camera must get to a city dot (world units) before city ambience plays.")]
    public float cityAudioRadius = 0.5f;

    public CityPoint[] AllCities => allCities;

    /// <summary>True when no city has been selected yet.</summary>
    public bool IsIdle => _state == AppState.Idle;

    private enum AppState { Idle, DepartureSelected, RouteComplete }
    private AppState     _state      = AppState.Idle;
    private CityPoint    _departure;
    private CityPoint    _destination;
    private CityPoint    _transfer;       // null = direct route; non-null = via this city
    private LineRenderer _line;
    private Transform    _rightAnchor;

    // FIX B-004: cache VRPointer on Start instead of FindFirstObjectByType every frame
    private VRPointer _vrPointer;

    // Proximity audio state — tracks which city we are currently "near"
    private CityPoint _proxCity;
    private Transform _headTransform;   // camera / headset centre eye

    // ── Lifecycle ─────────────────────────────────────────────────────

    private void Awake()
    {
        // FIX B-003: proper singleton with destroy-guard
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance      = this;
        _line         = GetComponent<LineRenderer>();
        _line.enabled = false;
    }

    private void Start()
    {
        var rig = FindFirstObjectByType<OVRCameraRig>();
        if (rig != null)
        {
            _rightAnchor   = rig.rightControllerAnchor;
            _headTransform = rig.centerEyeAnchor;        // VR: centre-eye = head position
        }

        // Editor fallback: use Camera.main when no XR device is active
        if (_headTransform == null)
            _headTransform = Camera.main?.transform;

        if (allCities == null || allCities.Length == 0)
            allCities = FindObjectsByType<CityPoint>(FindObjectsSortMode.None);

        // FIX B-004: cache once, not every Update frame
        _vrPointer = FindFirstObjectByType<VRPointer>();

        // FIX B-002: auto-initialise PlaneGrabHandler so _routeManager is never null
        if (planeController != null)
        {
            var grabHandler = planeController.GetComponent<PlaneGrabHandler>();
            grabHandler?.Initialize(this);
        }

        ResetAllCities();
    }

    // FIX B-003: clear singleton reference so scene reloads start clean
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Route sequence coroutine handle — stored so ResetState() can cancel it mid-flight
    private Coroutine _routeCoroutine;

    // ── Update ────────────────────────────────────────────────────────

    private void Update()
    {
        if (OVRInput.GetDown(OVRInput.Button.Two,  OVRInput.Controller.RTouch) ||
            OVRInput.GetDown(OVRInput.Button.Four, OVRInput.Controller.LTouch))
        {
            ResetState();
            return;
        }

        if (_state == AppState.RouteComplete) return;

        // FIX B-004: use cached reference — no FindFirstObjectByType per frame
        if (_vrPointer == null)
            HandleCityRaycast();

        // Proximity-based city audio: play city sound when camera is close to any city dot
        UpdateProximityAudio();
    }

    private void LateUpdate()
    {
        UpdateRouteLine();
    }

    // ── Public API ────────────────────────────────────────────────────

    public void OnCityClicked(CityPoint city)
    {
        if (_state == AppState.RouteComplete) return;

        AudioManager.Instance?.PlayButtonSound(0);

        // Haptic: sharp click on city selection
        OVRInput.SetControllerVibration(0.1f, 0.4f, OVRInput.Controller.RTouch);
        StartCoroutine(StopVibrationAfter(0.08f, OVRInput.Controller.RTouch));

        OnCityTapped(city);
    }

    public void OnPlaneReleased(Vector3 worldPosition)
    {
        if (_state != AppState.DepartureSelected) return;

        const float snapRadius = 0.30f;
        CityPoint nearest    = null;
        float     nearestDist = snapRadius;

        foreach (var city in allCities)
        {
            if (city == _departure) continue;
            float d = Vector3.Distance(worldPosition, city.transform.position);
            if (d < nearestDist) { nearestDist = d; nearest = city; }
        }

        if (nearest != null) CompleteRoute(nearest);
        else                 planeController?.ReturnToDeparture();
    }

    public void OnPlaneDroppedOnCity(CityPoint city)
    {
        if (_state != AppState.DepartureSelected) return;

        if (city != _departure) CompleteRoute(city);
        else                    planeController?.ReturnToDeparture();
    }

    public void ResetRoute() => ResetState();

    // ── Proximity Audio ───────────────────────────────────────────────

    /// <summary>
    /// Checks which city (if any) the camera is within cityAudioRadius of.
    /// Plays city ambience on enter, stops it on exit.
    /// Only active in the Idle state — once a departure is selected the
    /// user has already committed; ambience is then managed by route logic.
    /// </summary>
    private void UpdateProximityAudio()
    {
        // Only run in Idle state and when we have a camera reference
        if (_state != AppState.Idle || allCities == null) return;

        // Refresh camera ref in case Camera.main changed (e.g. scene load)
        if (_headTransform == null)
            _headTransform = Camera.main?.transform;
        if (_headTransform == null) return;

        // Find the nearest city within the proximity radius
        CityPoint nearest     = null;
        float     nearestDist = cityAudioRadius;

        foreach (var city in allCities)
        {
            float d = Vector3.Distance(_headTransform.position, city.transform.position);
            if (d < nearestDist)
            {
                nearestDist = d;
                nearest     = city;
            }
        }

        // Only act on changes (enter / exit) to avoid hammering AudioManager every frame
        if (nearest == _proxCity) return;

        _proxCity = nearest;

        if (nearest != null)
            AudioManager.Instance?.PlayCityAmbience();
        else
            AudioManager.Instance?.StopAmbience();
    }

    // ── Private Logic ─────────────────────────────────────────────────

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
                else
                    CompleteRoute(city);   // all cities are now reachable
                break;
        }
    }

    private void SelectDeparture(CityPoint city)
    {
        _state     = AppState.DepartureSelected;
        _departure = city;
        _proxCity  = null;   // hand off ambience control to route logic

        city.SetState(CityPoint.State.Selected);

        // All other cities are reachable — routes exist (direct or via transfer)
        foreach (var c in allCities)
        {
            if (c == city) continue;
            c.SetState(CityPoint.State.Reachable);
        }

        uiManager?.ShowInfoCard(city);
        planeController?.SpawnAt(city);

        // Ensure ambience keeps playing after click (proximity may have already started it)
        AudioManager.Instance?.PlayCityAmbience();
    }

    private void CompleteRoute(CityPoint destination)
    {
        _state       = AppState.RouteComplete;
        _destination = destination;
        _transfer    = FindTransferCity(_departure, destination);

        // Mark destination visually (blue = arrived)
        destination.SetState(CityPoint.State.Arrived);
        // Transfer point (if any) stays Selected (orange) — departure is already Selected

        // Draw the full route arc immediately so the user can see the path
        if (_transfer != null)
            DrawTransferArc(_departure.transform.position,
                            _transfer.transform.position,
                            destination.transform.position);
        else
            DrawArc(_departure.transform.position, destination.transform.position);

        // Ambience stops when the plane takes off
        AudioManager.Instance?.StopAmbience();

        // Start sequenced flight — cards appear only after each landing
        if (_routeCoroutine != null) StopCoroutine(_routeCoroutine);
        _routeCoroutine = StartCoroutine(ExecuteRouteSequence(destination));
    }

    /// <summary>
    /// Executes the full flight sequence with landing-triggered UI cards.
    ///
    /// Direct route:   departure ──fly──► destination ──land──► show route card
    /// Transfer route: departure ──fly──► transfer ──land──► show transfer card
    ///                 ► wait ► re-spawn at transfer ──fly──► destination ──land──► show route card
    /// </summary>
    private System.Collections.IEnumerator ExecuteRouteSequence(CityPoint destination)
    {
        if (planeController == null) yield break;

        if (_transfer != null)
        {
            // ── Leg 1: departure → transfer ──────────────────────────────────
            planeController.FlyTo(_transfer);

            // Wait one frame so FlyArc has been entered and _flying is definitely true
            yield return null;
            yield return new WaitUntil(() => !planeController.IsFlying);

            // Plane has landed at transfer — show the transfer city info card
            uiManager?.ShowInfoCard(_transfer);

            // Brief pause so the user can read the card
            yield return new WaitForSecondsRealtime(2.0f);

            // ── Leg 2: transfer → destination ────────────────────────────────
            // Update departure so FlyTo() uses the transfer city as the start position
            planeController.UpdateDeparture(_transfer);
            planeController.SpawnAt(_transfer);

            // Wait for the pop-in animation to finish (0.4 s in PlaneController)
            yield return new WaitForSeconds(0.5f);

            planeController.FlyTo(destination);

            yield return null;
            yield return new WaitUntil(() => !planeController.IsFlying);
        }
        else
        {
            // ── Direct route: departure → destination ─────────────────────────
            planeController.FlyTo(destination);

            yield return null;
            yield return new WaitUntil(() => !planeController.IsFlying);
        }

        // Plane has landed at the final destination — show the full route card
        uiManager?.ShowMissionCard(_departure, _transfer, destination);

        // Strong haptic: route completed
        OVRInput.SetControllerVibration(0.2f, 0.8f, OVRInput.Controller.RTouch);
        StartCoroutine(StopVibrationAfter(0.15f, OVRInput.Controller.RTouch));

        _routeCoroutine = null;
    }

    /// <summary>Helper: stops controller vibration after <paramref name="seconds"/> seconds.</summary>
    private System.Collections.IEnumerator StopVibrationAfter(float seconds, OVRInput.Controller ctrl)
    {
        yield return new WaitForSecondsRealtime(seconds);
        OVRInput.SetControllerVibration(0f, 0f, ctrl);
    }

    private void ResetState()
    {
        // Cancel any in-progress flight sequence
        if (_routeCoroutine != null)
        {
            StopCoroutine(_routeCoroutine);
            _routeCoroutine = null;
        }

        // Stop any lingering vibration
        OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);

        _state        = AppState.Idle;
        _departure    = null;
        _destination  = null;
        _transfer     = null;
        _proxCity     = null;   // reset proximity so it re-evaluates on next frame
        _line.enabled = false;
        ResetAllCities();
        uiManager?.HideAll();
        planeController?.Hide();
        AudioManager.Instance?.StopAmbience();
    }

    private void ResetAllCities()
    {
        foreach (var c in allCities)
            c.SetState(CityPoint.State.Idle);
    }

    // ── Transfer Routing ──────────────────────────────────────────────

    /// <summary>
    /// Returns an intermediate city if no direct route exists between
    /// <paramref name="from"/> and <paramref name="to"/>, or null for a direct route.
    /// A route is "direct" when either city has the other in its connectedCities array.
    /// Otherwise Latvia (the AirBaltic hub) is preferred as the transfer point.
    /// </summary>
    private CityPoint FindTransferCity(CityPoint from, CityPoint to)
    {
        // Direct if either city lists the other as a connection
        if (HasDirectRoute(from, to)) return null;

        // Prefer Latvia as hub
        foreach (var city in allCities)
        {
            if (city == from || city == to) continue;
            if (city.cityName == "Latvia" &&
                HasDirectRoute(from, city) &&
                HasDirectRoute(city, to))
                return city;
        }

        // Generic 1-hop fallback (any city reachable from both)
        foreach (var city in allCities)
        {
            if (city == from || city == to) continue;
            if (HasDirectRoute(from, city) && HasDirectRoute(city, to))
                return city;
        }

        // No intermediate found — display as direct anyway
        return null;
    }

    /// <summary>Returns true if a→b or b→a exists in either city's connectedCities.</summary>
    private static bool HasDirectRoute(CityPoint a, CityPoint b) =>
        a.IsConnectedTo(b) || b.IsConnectedTo(a);

    // ── Arc Line ──────────────────────────────────────────────────────

    private Transform _globeCache;
    private Vector3 GlobeCenter()
    {
        if (globeTransform != null) return globeTransform.position;
        if (_globeCache != null)    return _globeCache.position;
        var g = GameObject.Find("Globe");
        if (g != null) { _globeCache = g.transform; return g.transform.position; }
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

    /// <summary>
    /// Draws a two-segment arc: from → via → to.
    /// Each leg gets arcSegments points; the shared midpoint is not duplicated.
    /// </summary>
    private void DrawTransferArc(Vector3 from, Vector3 via, Vector3 to)
    {
        int total = arcSegments * 2 + 1;
        _line.enabled       = true;
        _line.positionCount = total;

        Vector3 center = GlobeCenter();

        // Leg 1: from → via
        Vector3 fromRel = from - center;
        Vector3 viaRel  = via  - center;
        float   r1      = fromRel.magnitude;

        for (int i = 0; i <= arcSegments; i++)
        {
            float   t        = i / (float)arcSegments;
            Vector3 onSphere = Vector3.Slerp(fromRel.normalized, viaRel.normalized, t) * r1;
            float   lift     = arcLift * Mathf.Sin(t * Mathf.PI);
            _line.SetPosition(i, center + onSphere + onSphere.normalized * lift);
        }

        // Leg 2: via → to  (skip index 0 to avoid duplicating the midpoint)
        Vector3 toRel = to - center;
        float   r2    = viaRel.magnitude;

        for (int i = 1; i <= arcSegments; i++)
        {
            float   t        = i / (float)arcSegments;
            Vector3 onSphere = Vector3.Slerp(viaRel.normalized, toRel.normalized, t) * r2;
            float   lift     = arcLift * Mathf.Sin(t * Mathf.PI);
            _line.SetPosition(arcSegments + i, center + onSphere + onSphere.normalized * lift);
        }
    }

    private void UpdateRouteLine()
    {
        if (_state != AppState.RouteComplete) return;
        if (_departure == null || _destination == null) return;

        if (_transfer != null)
            DrawTransferArc(_departure.transform.position,
                            _transfer.transform.position,
                            _destination.transform.position);
        else
            DrawArc(_departure.transform.position, _destination.transform.position);
    }
}
