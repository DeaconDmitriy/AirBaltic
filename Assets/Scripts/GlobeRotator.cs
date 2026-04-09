using UnityEngine;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

public class GlobeRotator : MonoBehaviour
{
    [Header("Thumbstick (VR)")]
    public float thumbstickSpeed = 80f;

    [Header("Grab Rotation (VR) — right grip")]
    public float grabSensitivity = 400f;

    [Header("Zoom")]
    public float zoomSpeed = 1.5f;
    public float minScale  = 0.3f;
    public float maxScale  = 3.0f;

    [Header("Move (VR) — left grip")]
    public bool moveEnabled = true;

    [Header("Editor Mouse")]
    public float mouseSensitivity = 0.4f;
    public float scrollZoomSpeed  = 0.5f;

    // ── Runtime state ─────────────────────────────────────────────────────────

    private Transform _rightAnchor;
    private Transform _leftAnchor;

    // Rotation (right grip)
    private bool    _vrgrabbing;
    private Vector3 _lastGrabPos;

    // Zoom
    private float _targetScale;
    private bool  _pinching;
    private float _pinchStartDist;
    private float _scaleAtPinchStart;

    // Move (left grip)
    private bool    _leftGrabbing;
    private Vector3 _lastLeftPos;

    // Editor
    private bool    _editorDragging;
    private Vector2 _lastEditorMousePos;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Start()
    {
        var rig = FindFirstObjectByType<OVRCameraRig>();
        if (rig != null)
        {
            _rightAnchor = rig.rightControllerAnchor;
            _leftAnchor  = rig.leftControllerAnchor;
        }
        _targetScale = transform.localScale.x;
    }

    private void Update()
    {
        // Block globe interaction while the pause menu is open
        if (MenuController.IsOpen) return;

        if (!UnityEngine.XR.XRSettings.isDeviceActive)
        {
#if UNITY_EDITOR
            EditorMouseSpin();
            EditorScrollZoom();
#endif
        }
        else
        {
            ThumbstickSpin();
            ThumbstickZoom();
            PinchZoom();
            if (!_pinching) GrabSpin();
            if (moveEnabled) MoveGlobe();
        }

        // Smooth scale lerp — runs in both VR and Editor
        float current = transform.localScale.x;
        if (!Mathf.Approximately(current, _targetScale))
        {
            float next = Mathf.Lerp(current, _targetScale, Time.deltaTime * 8f);
            transform.localScale = Vector3.one * next;
        }
    }

    // ── Thumbstick spin (left stick X → Y-axis rotation) ─────────────────────

    private void ThumbstickSpin()
    {
        Vector2 stick = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);
        if (Mathf.Abs(stick.x) > 0.05f)
            transform.Rotate(Vector3.up, -stick.x * thumbstickSpeed * Time.deltaTime, Space.World);
    }

    // ── Thumbstick zoom (left stick Y → scale) ────────────────────────────────

    private void ThumbstickZoom()
    {
        float y = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick).y;
        if (Mathf.Abs(y) > 0.05f)
            _targetScale = Mathf.Clamp(_targetScale + y * zoomSpeed * Time.deltaTime, minScale, maxScale);
    }

    // ── Two-hand pinch zoom (both grip buttons held) ──────────────────────────

    private void PinchZoom()
    {
        if (_leftAnchor == null || _rightAnchor == null) return;

        bool leftGrip  = OVRInput.Get    (OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch);
        bool rightGrip = OVRInput.Get    (OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch);
        bool leftDown  = OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch);
        bool rightDown = OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch);

        bool bothHeld = leftGrip && rightGrip;

        // Start pinch when the second grip is pressed while the first is already held
        if (bothHeld && !_pinching && (leftDown || rightDown))
        {
            _pinching          = true;
            _pinchStartDist    = Vector3.Distance(_leftAnchor.position, _rightAnchor.position);
            _scaleAtPinchStart = _targetScale;
            _leftGrabbing      = false;   // cancel any active move grab
            _vrgrabbing        = false;   // cancel any active rotate grab
        }

        if (_pinching && bothHeld)
        {
            float dist = Vector3.Distance(_leftAnchor.position, _rightAnchor.position);
            if (_pinchStartDist > 0.001f)
                _targetScale = Mathf.Clamp(_scaleAtPinchStart * (dist / _pinchStartDist), minScale, maxScale);
        }

        if (_pinching && !bothHeld)
            _pinching = false;
    }

    // ── Right-grip grab rotation ───────────────────────────────────────────────

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

    // ── Left-grip globe translate ─────────────────────────────────────────────

    private void MoveGlobe()
    {
        if (_leftAnchor == null) return;

        bool gripDown = OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch);
        bool grip     = OVRInput.Get    (OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch);
        bool gripUp   = OVRInput.GetUp  (OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch);

        // Only start a move grab if we are NOT entering a two-hand pinch this frame
        if (gripDown && !_leftGrabbing && !_pinching)
        {
            Ray ray = new Ray(_leftAnchor.position, _leftAnchor.forward);
            if (Physics.Raycast(ray, out _, 20f, ~0, QueryTriggerInteraction.Ignore))
            {
                _leftGrabbing = true;
                _lastLeftPos  = _leftAnchor.position;
            }
        }

        if (_leftGrabbing && grip && !_pinching)
        {
            Vector3 delta = _leftAnchor.position - _lastLeftPos;
            transform.position += delta;
            _lastLeftPos = _leftAnchor.position;
        }

        if (gripUp) _leftGrabbing = false;
    }

    // ── Editor ────────────────────────────────────────────────────────────────

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

    private void EditorScrollZoom()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
            _targetScale = Mathf.Clamp(
                _targetScale + scroll * scrollZoomSpeed * Time.deltaTime,
                minScale, maxScale);
    }
#endif
}
