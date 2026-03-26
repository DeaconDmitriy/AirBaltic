using UnityEngine;

/// <summary>
/// VR laser pointer for the right controller.
/// Responsibilities:
///   • Draw the laser line each frame
///   • On index trigger press → forward the hit city to RouteManager
///
/// City ambience is handled by RouteManager.UpdateProximityAudio() (distance-based),
/// NOT by this pointer — so no hover logic lives here.
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

    void Update()
    {
        // On PC (no XR device) hide the laser completely
        if (!UnityEngine.XR.XRSettings.isDeviceActive)
        {
            if (_line.enabled) _line.enabled = false;
            return;
        }

        if (cameraRig == null) return;

        Transform anchor  = cameraRig.rightHandAnchor;
        Vector3   origin  = anchor.position;
        Vector3   forward = anchor.forward;
        Vector3   endPt   = origin + forward * maxRayDistance;

        if (Physics.Raycast(origin, forward, out RaycastHit hit, maxRayDistance, rayMask))
        {
            endPt = hit.point;

            // When the pause menu is open MenuController handles its own trigger input;
            // skip city-selection so the trigger press is not double-handled.
            if (!MenuController.IsOpen &&
                OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch))
            {
                CityPoint city = hit.collider.GetComponentInParent<CityPoint>();
                if (city != null && RouteManager.Instance != null)
                    RouteManager.Instance.OnCityClicked(city);
            }
        }

        // Always draw the laser — when the menu is open the beam points at it,
        // giving the player a clear aiming cue for the menu buttons.
        _line.enabled = true;
        _line.SetPosition(0, origin);
        _line.SetPosition(1, endPt);
    }
}
