using UnityEngine;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

public class GlobeRotator : MonoBehaviour
{
    [Header("Thumbstick (VR)")]
    public float thumbstickSpeed = 80f;

    [Header("Grab Rotation (VR)")]
    public float grabSensitivity = 400f;

    [Header("Editor Mouse Rotation")]
    public float mouseSensitivity = 0.4f;

    private Transform _rightAnchor;
    private bool    _vrgrabbing;
    private Vector3 _lastGrabPos;
    private bool    _editorDragging;
    private Vector2 _lastEditorMousePos;

    private void Start()
    {
        var rig = FindFirstObjectByType<OVRCameraRig>();
        if (rig != null) _rightAnchor = rig.rightControllerAnchor;
    }

    private void Update()
    {
        if (!UnityEngine.XR.XRSettings.isDeviceActive)
        {
#if UNITY_EDITOR
            EditorMouseSpin();
#endif
        }
        else
        {
            ThumbstickSpin();
            GrabSpin();
        }
    }

    private void ThumbstickSpin()
    {
        Vector2 stick = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);
        if (Mathf.Abs(stick.x) > 0.05f)
            transform.Rotate(Vector3.up, -stick.x * thumbstickSpeed * Time.deltaTime, Space.World);
    }

    private void GrabSpin()
    {
        if (_rightAnchor == null) return;

        bool gripDown = OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch);
        bool grip     = OVRInput.Get    (OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch);
        bool gripUp   = OVRInput.GetUp  (OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch);

        if (gripDown && !_vrgrabbing)
        {
            Ray ray = new Ray(_rightAnchor.position, _rightAnchor.forward);
            if (Physics.Raycast(ray, out _, 20f, ~0, QueryTriggerInteraction.Ignore))
            {
                _vrgrabbing  = true;
                _lastGrabPos = _rightAnchor.position;
            }
        }

        if (_vrgrabbing && grip)
        {
            Vector3 delta = _rightAnchor.position - _lastGrabPos;
            transform.Rotate(Vector3.up,                  -delta.x * grabSensitivity, Space.World);
            transform.Rotate(Camera.main.transform.right,  delta.y * grabSensitivity, Space.World);
            _lastGrabPos = _rightAnchor.position;
        }

        if (gripUp) _vrgrabbing = false;
    }

#if UNITY_EDITOR
    private void EditorMouseSpin()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        bool startDrag = mouse.middleButton.wasPressedThisFrame;
        if (!startDrag && mouse.leftButton.wasPressedThisFrame)
        {
            var cam = Camera.main;
            if (cam != null)
            {
                Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
                if (Physics.Raycast(ray, out RaycastHit hit, 30f))
                {
                    if (hit.collider != null &&
                        hit.collider.gameObject == gameObject &&
                        hit.collider.GetComponentInParent<CityPoint>() == null)
                        startDrag = true;
                }
            }
        }

        if (startDrag)
        {
            _editorDragging     = true;
            _lastEditorMousePos = mouse.position.ReadValue();
        }

        if (mouse.middleButton.wasReleasedThisFrame || mouse.leftButton.wasReleasedThisFrame)
            _editorDragging = false;

        if (_editorDragging)
        {
            Vector2 current = mouse.position.ReadValue();
            Vector2 delta   = current - _lastEditorMousePos;

            transform.Rotate(Vector3.up, -delta.x * mouseSensitivity, Space.World);

            var cam = Camera.main;
            if (cam != null)
                transform.Rotate(cam.transform.right, delta.y * mouseSensitivity, Space.World);

            _lastEditorMousePos = current;
        }
    }
#endif
}
