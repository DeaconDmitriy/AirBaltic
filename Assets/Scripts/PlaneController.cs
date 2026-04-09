using System.Collections;
using UnityEngine;

/// <summary>
/// Controls the AirBaltic plane model: spawning, dragging, arc flight, and return.
/// Audio is fully delegated to AudioManager (no internal AudioSource needed).
/// </summary>
[RequireComponent(typeof(Collider))]
public class PlaneController : MonoBehaviour
{
    [Header("Globe reference")]
    public Transform globeTransform;

    [Header("Model orientation correction")]
    [Tooltip("Rotate model around its UP axis so the nose faces forward. Try 0 / 90 / -90 / 180.")]
    public float modelYawOffset = 0f;

    [Header("Scale")]
    [Tooltip("Visible size of the plane.")]
    public float planeDisplayScale = 0.3f;

    [Header("Flight")]
    public float flyDuration = 2.5f;
    public float arcLift     = 0.15f;
    public float snapRadius  = 0.25f;

    [Header("Drag (VR)")]
    public float dragDepth = 0.6f;

    /// <summary>True while the player is dragging the plane with the index trigger.</summary>
    public bool IsDragging { get; private set; }

    /// <summary>True while the plane is executing a flight arc (either leg).</summary>
    public bool IsFlying => _flying;

    private Transform _rightAnchor;
    private bool      _active;
    private bool      _flying;
    private Vector3   _normalScale;
    private CityPoint _departureCopy;
    private Vector3   _departureWorldPos;

    // ── Lifecycle ─────────────────────────────────────────────────────

    private void Start()
    {
        var rig = FindFirstObjectByType<OVRCameraRig>();
        if (rig != null) _rightAnchor = rig.rightControllerAnchor;
    }

    private void Update()
    {
        if (!_active || _flying) return;

        if (_departureCopy != null && !IsDragging)
        {
            transform.position = AboveSurface(_departureCopy.transform.position);
            OrientOnSurface(_departureCopy.transform.position);
        }

        HandleDrag();
    }

    // ── Public API ────────────────────────────────────────────────────

    public void SpawnAt(CityPoint city)
    {
        _normalScale = Vector3.one * planeDisplayScale;

        StopAllCoroutines();
        _departureCopy     = city;
        _departureWorldPos = AboveSurface(city.transform.position);
        transform.position = _departureWorldPos;
        OrientOnSurface(city.transform.position);
        transform.localScale = Vector3.zero;

        _active    = true;
        _flying    = false;
        IsDragging = false;
        gameObject.SetActive(true);

        StartCoroutine(PopIn());
    }

    public void FlyTo(CityPoint destination)
    {
        if (!gameObject.activeInHierarchy) gameObject.SetActive(true);
        StopAllCoroutines();
        _flying = true;  // pre-set so callers polling IsFlying see it immediately
        Vector3 from   = _departureCopy != null
            ? AboveSurface(_departureCopy.transform.position)
            : transform.position;
        Vector3 target = AboveSurface(destination.transform.position);
        StartCoroutine(FlyArc(from, target, arcFlight: true));
    }

    /// <summary>
    /// Updates the logical departure for the next flight leg.
    /// Call this between two legs of a transfer route so FlyTo() starts from
    /// the correct city (the transfer point, not the original departure).
    /// </summary>
    public void UpdateDeparture(CityPoint city)
    {
        _departureCopy     = city;
        _departureWorldPos = AboveSurface(city.transform.position);
    }

    public void ReturnToDeparture()
    {
        if (!gameObject.activeInHierarchy) return;
        StopAllCoroutines();
        Vector3 dest = _departureCopy != null
            ? AboveSurface(_departureCopy.transform.position)
            : _departureWorldPos;
        StartCoroutine(FlyArc(transform.position, dest, arcFlight: false));
    }

    public void Hide()
    {
        StopAllCoroutines();
        _active    = false;
        IsDragging = false;
        _flying    = false;
        gameObject.SetActive(false);
    }

    // ── Drag (VR index trigger) ───────────────────────────────────────

    private void HandleDrag()
    {
        if (_rightAnchor == null) return;

        bool trigDown = OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch);
        bool trig     = OVRInput.Get    (OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch);
        bool trigUp   = OVRInput.GetUp  (OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch);

        if (!IsDragging && trigDown)
        {
            Ray ray = new Ray(_rightAnchor.position, _rightAnchor.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, 10f) &&
                (hit.collider.gameObject == gameObject || hit.collider.transform.IsChildOf(transform)))
                IsDragging = true;
        }

        if (IsDragging && trig)
        {
            transform.position = _rightAnchor.position + _rightAnchor.forward * dragDepth;
            transform.rotation = _rightAnchor.rotation * Quaternion.Euler(0, modelYawOffset, 0);
        }

        if (IsDragging && trigUp)
        {
            IsDragging = false;
            OnReleased();
        }
    }

    private void OnReleased()
    {
        CityPoint nearest     = null;
        float     nearestDist = snapRadius;

        var manager = RouteManager.Instance;
        if (manager != null)
        {
            foreach (var city in manager.AllCities)
            {
                float d = Vector3.Distance(transform.position, city.transform.position);
                if (d < nearestDist) { nearestDist = d; nearest = city; }
            }
        }

        if (nearest != null) manager.OnPlaneDroppedOnCity(nearest);
        else                 ReturnToDeparture();
    }

    // ── Coroutines ────────────────────────────────────────────────────

    private IEnumerator PopIn()
    {
        float dur = 0.4f, elapsed = 0f;
        while (elapsed < dur)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / dur));
            transform.localScale = _normalScale * t;
            yield return null;
        }
        transform.localScale = _normalScale;
    }

    private IEnumerator FlyArc(Vector3 from, Vector3 to, bool arcFlight)
    {
        _flying = true;

        // Takeoff sound via AudioManager
        if (arcFlight) AudioManager.Instance?.PlayTakeOff();

        Transform  globe   = globeTransform;
        Vector3    center0 = GetGlobeCenter();

        Quaternion initRot      = globe != null ? globe.rotation : Quaternion.identity;
        Vector3    fromRelLocal = Quaternion.Inverse(initRot) * (from - center0);
        Vector3    toRelLocal   = Quaternion.Inverse(initRot) * (to   - center0);
        float      r            = fromRelLocal.magnitude;

        float elapsed = 0f;
        while (elapsed < flyDuration)
        {
            elapsed += Time.deltaTime;
            float t  = Mathf.Clamp01(elapsed / flyDuration);
            float ts = Mathf.SmoothStep(0f, 1f, t);

            Quaternion curRot    = globe != null ? globe.rotation : Quaternion.identity;
            Vector3    curCenter = GetGlobeCenter();

            Vector3 pos;
            if (arcFlight && r > 0.001f)
            {
                Vector3 onSphLocal = Vector3.Slerp(fromRelLocal.normalized, toRelLocal.normalized, ts) * r;
                Vector3 onSphWorld = curRot * onSphLocal;
                float   lift       = arcLift * Mathf.Sin(ts * Mathf.PI);
                pos = curCenter + onSphWorld + onSphWorld.normalized * lift;
            }
            else
            {
                Vector3 fromW = curCenter + curRot * fromRelLocal;
                Vector3 toW   = curCenter + curRot * toRelLocal;
                pos = Vector3.Lerp(fromW, toW, ts);
            }
            transform.position = pos;

            // Orient nose toward travel direction
            if (arcFlight && r > 0.001f)
            {
                float   tLook        = Mathf.Clamp01(ts + 0.03f);
                Vector3 nextSphLocal = Vector3.Slerp(fromRelLocal.normalized, toRelLocal.normalized, tLook) * r;
                Vector3 nextPos      = curCenter + curRot * nextSphLocal;
                Vector3 fwd          = (nextPos - pos).normalized;
                Vector3 up           = (pos - curCenter).normalized;
                if (fwd.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(fwd, up)
                                       * Quaternion.Euler(0, modelYawOffset, 0);
            }

            // Scale shrinks to 0 in the last 15% of the flight
            float scaleT = (t > 0.85f)
                ? Mathf.SmoothStep(1f, 0f, (t - 0.85f) / 0.15f)
                : 1f;
            transform.localScale = _normalScale * scaleT;

            yield return null;
        }

        // Land: snap to final position, play landing sound, hide plane
        Quaternion finalRot  = globe != null ? globe.rotation : Quaternion.identity;
        transform.position   = GetGlobeCenter() + finalRot * toRelLocal;
        transform.localScale = Vector3.zero;
        _flying              = false;

        // Landing sound via AudioManager
        if (arcFlight) AudioManager.Instance?.PlayLanding();

        gameObject.SetActive(false);
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private Transform _globeCache;
    private Vector3 GetGlobeCenter()
    {
        if (globeTransform != null) return globeTransform.position;
        if (_globeCache != null)    return _globeCache.position;
        var g = GameObject.Find("Globe");
        if (g != null) { _globeCache = g.transform; return g.transform.position; }
        return Vector3.zero;
    }

    private Vector3 AboveSurface(Vector3 cityWorldPos)
    {
        Vector3 center  = GetGlobeCenter();
        Vector3 outward = (cityWorldPos - center).normalized;
        return cityWorldPos + outward * 0.01f;
    }

    private void OrientOnSurface(Vector3 cityWorldPos)
    {
        Vector3 center  = GetGlobeCenter();
        Vector3 outward = (cityWorldPos - center).normalized;

        Vector3 worldY = Vector3.up;
        Vector3 north  = (worldY - Vector3.Dot(worldY, outward) * outward).normalized;
        if (north.sqrMagnitude < 0.001f) north = Vector3.forward;

        transform.rotation = Quaternion.LookRotation(north, outward)
                           * Quaternion.Euler(0, modelYawOffset, 0);
    }
}
