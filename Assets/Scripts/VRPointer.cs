using UnityEngine;

/// <summary>
/// VR laser pointer for the right controller.
/// Responsibilities:
///   • Draw the laser line each frame
///   • Manage city hover state (highlight + haptic when ray enters/exits a city)
///   • On index trigger press → forward the hit city to RouteManager + haptic click
///
/// City ambience is handled by RouteManager.UpdateProximityAudio() (distance-based),
/// NOT by this pointer.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class VRPointer : MonoBehaviour
{
    [Header("References")]
    public OVRCameraRig cameraRig;

    [Header("Settings")]
    public float     maxRayDistance = 12f;
    public LayerMask rayMask        = ~0;

    private LineRenderer _line;
    private CityPoint    _lastHoveredCity;   // city the ray was on last frame

    // ── Lifecycle ─────────────────────────────────────────────────────

    void Awake()
    {
        _line = GetComponent<LineRenderer>();
        _line.positionCount = 2;
        _line.startWidth    = 0.004f;
        _line.endWidth      = 0.001f;
        _line.useWorldSpace = true;

        var mat = new Material(Shader.Find("Sprites/Default"));
        _line.material   = mat;
        _line.startColor = new Color(1f, 1f, 1f, 0.85f);
        _line.endColor   = new Color(1f, 0.6f, 0.2f, 0.10f);
    }

    void Start()
    {
        if (cameraRig == null)
            cameraRig = FindFirstObjectByType<OVRCameraRig>();
    }

    void Update()
    {
        // On PC (no XR device) hide the laser completely
        if (!UnityEngine.XR.XRSettings.isDeviceActive)
        {
            if (_line.enabled) _line.enabled = false;
            ClearHover();   // clear hover when switching off VR
            return;
        }

        if (cameraRig == null) return;

        Transform anchor  = cameraRig.rightHandAnchor;
        Vector3   origin  = anchor.position;
        Vector3   forward = anchor.forward;
        Vector3   endPt   = origin + forward * maxRayDistance;

        // ── Raycast ────────────────────────────────────────────────────
        CityPoint hitCity = null;
        if (Physics.Raycast(origin, forward, out RaycastHit hit, maxRayDistance, rayMask))
        {
            endPt = hit.point;

            if (!MenuController.IsOpen)
                hitCity = hit.collider.GetComponentInParent<CityPoint>();
        }

        // ── Hover management ───────────────────────────────────────────
        if (hitCity != _lastHoveredCity)
        {
            // Left the previous city
            _lastHoveredCity?.SetHovered(false);
            _lastHoveredCity = hitCity;

            // Entered a new city
            if (hitCity != null)
            {
                hitCity.SetHovered(true);
                // Light haptic pulse on hover enter
                OVRInput.SetControllerVibration(0.03f, 0.12f, OVRInput.Controller.RTouch);
                StartCoroutine(StopVibrationAfter(0.05f));
            }
        }
        else if (hitCity != null
                 && hitCity.CurrentState != CityPoint.State.Hovered
                 && hitCity.CurrentState != CityPoint.State.Selected
                 && hitCity.CurrentState != CityPoint.State.Arrived)
        {
            // Same city but its state was externally reset (e.g. route cancelled) —
            // re-apply hover so it stays highlighted while the ray is on it
            hitCity.SetHovered(true);
        }

        // ── Click / select ─────────────────────────────────────────────
        if (!MenuController.IsOpen &&
            OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch))
        {
            if (hitCity != null && RouteManager.Instance != null)
                RouteManager.Instance.OnCityClicked(hitCity);
                // Note: RouteManager.OnCityClicked already fires its own (stronger) haptic
        }

        // Always draw the laser — when the menu is open the beam points at it
        _line.enabled = true;
        _line.SetPosition(0, origin);
        _line.SetPosition(1, endPt);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    /// <summary>Removes hover from the currently tracked city (called on device inactive).</summary>
    private void ClearHover()
    {
        if (_lastHoveredCity == null) return;
        _lastHoveredCity.SetHovered(false);
        _lastHoveredCity = null;
    }

    private System.Collections.IEnumerator StopVibrationAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
    }
}
