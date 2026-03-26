using UnityEngine;

/// <summary>
/// Secondary grab path using OVRGrabbable.
/// FIX B-001: Guards against PlaneController's own index-trigger drag so only one
///            system fires OnPlaneReleased at a time.
/// FIX B-002: Auto-initialises via RouteManager.Start — no manual call required.
/// </summary>
[RequireComponent(typeof(OVRGrabbable))]
[RequireComponent(typeof(Rigidbody))]
public class PlaneGrabHandler : MonoBehaviour
{
    private OVRGrabbable  _grabbable;
    private RouteManager  _routeManager;
    private PlaneController _planeController;   // FIX B-001: needed to check IsDragging
    private bool          _wasGrabbed;

    private void Awake()
    {
        _grabbable       = GetComponent<OVRGrabbable>();
        _planeController = GetComponent<PlaneController>();

        Rigidbody rb      = GetComponent<Rigidbody>();
        rb.useGravity     = false;
        rb.constraints    = RigidbodyConstraints.FreezeRotation;
    }

    /// <summary>Called by RouteManager.Start to wire the reference automatically.</summary>
    public void Initialize(RouteManager manager)
    {
        _routeManager = manager;
    }

    private void Update()
    {
        // On PC (no XR device) OVRGrabbable.isGrabbed is always false; nothing to do.
        if (!UnityEngine.XR.XRSettings.isDeviceActive) return;
        if (_grabbable == null) return;

        bool grabbed = _grabbable.isGrabbed;

        // FIX B-001: If PlaneController's own trigger-drag is active, this handler
        // must not also fire — that would cause a double OnPlaneReleased call.
        if (_planeController != null && _planeController.IsDragging)
        {
            _wasGrabbed = grabbed; // keep state in sync without firing
            return;
        }

        if (_wasGrabbed && !grabbed)
            _routeManager?.OnPlaneReleased(transform.position);

        _wasGrabbed = grabbed;
    }
}
