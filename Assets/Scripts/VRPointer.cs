using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class VRPointer : MonoBehaviour
{
    [Header("References")]
    public OVRCameraRig cameraRig;

    [Header("Settings")]
    public float maxRayDistance = 12f;
    public LayerMask rayMask = ~0;

    private LineRenderer _line;

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
        if (cameraRig == null) return;

        Transform anchor  = cameraRig.rightHandAnchor;
        Vector3   origin  = anchor.position;
        Vector3   forward = anchor.forward;
        Vector3   endPt   = origin + forward * maxRayDistance;

        if (Physics.Raycast(origin, forward, out RaycastHit hit, maxRayDistance, rayMask))
        {
            endPt = hit.point;

            if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch))
            {
                CityPoint city = hit.collider.GetComponentInParent<CityPoint>();
                if (city != null && RouteManager.Instance != null)
                    RouteManager.Instance.OnCityClicked(city);
            }
        }

        _line.SetPosition(0, origin);
        _line.SetPosition(1, endPt);
    }
}
