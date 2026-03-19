using UnityEngine;

[RequireComponent(typeof(OVRGrabbable))]
[RequireComponent(typeof(Rigidbody))]
public class PlaneGrabHandler : MonoBehaviour
{
    private OVRGrabbable _grabbable;
    private RouteManager _routeManager;
    private bool         _wasGrabbed;

    void Awake()
    {
        _grabbable = GetComponent<OVRGrabbable>();

        Rigidbody rb = GetComponent<Rigidbody>();
        rb.useGravity  = false;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
    }

    public void Initialize(RouteManager manager)
    {
        _routeManager = manager;
    }

    void Update()
    {
        bool grabbed = _grabbable.isGrabbed;

        if (_wasGrabbed && !grabbed)
            _routeManager?.OnPlaneReleased(transform.position);

        _wasGrabbed = grabbed;
    }
}
