using UnityEngine;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

[DefaultExecutionOrder(-50)]
public class EditorTestBridge : MonoBehaviour
{
#if UNITY_EDITOR
    [Header("Camera")]
    public float moveSpeed       = 1.5f;
    public float lookSensitivity = 0.15f;

    private float  _pitch, _yaw;
    private bool   _mouseCapture;
    private Camera _cam;

    private void Start()
    {
        if (UnityEngine.XR.XRSettings.isDeviceActive)
        {
            enabled = false;
            return;
        }

        _cam = GetComponent<Camera>();
        if (_cam == null) _cam = Camera.main;

        var e = transform.eulerAngles;
        _pitch = e.x > 180f ? e.x - 360f : e.x;
        _yaw   = e.y;
    }

    private void Update()
    {
        var mouse    = Mouse.current;
        var keyboard = Keyboard.current;
        if (mouse == null || keyboard == null) return;

        HandleMouseCapture(mouse);
        if (_mouseCapture) HandleLook(mouse);
        HandleMovement(keyboard);
        HandleClick(mouse);
        HandleHotkeys(keyboard);
    }

    private void HandleMouseCapture(Mouse mouse)
    {
        if (mouse.rightButton.wasPressedThisFrame)
        {
            _mouseCapture    = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }
        if (mouse.rightButton.wasReleasedThisFrame)
        {
            _mouseCapture    = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }
    }

    private void HandleLook(Mouse mouse)
    {
        Vector2 delta = mouse.delta.ReadValue();
        _yaw   += delta.x * lookSensitivity;
        _pitch -= delta.y * lookSensitivity;
        _pitch  = Mathf.Clamp(_pitch, -80f, 80f);
        transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }

    private void HandleMovement(Keyboard kb)
    {
        Vector3 dir = Vector3.zero;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed)    dir += Vector3.forward;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed)  dir += Vector3.back;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  dir += Vector3.left;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) dir += Vector3.right;
        if (kb.eKey.isPressed) dir += Vector3.up;
        if (kb.qKey.isPressed) dir += Vector3.down;

        transform.position += transform.rotation * dir * moveSpeed * Time.deltaTime;
    }

    private void HandleClick(Mouse mouse)
    {
        if (!mouse.leftButton.wasPressedThisFrame) return;
        if (_cam == null) return;

        Ray ray = _cam.ScreenPointToRay(mouse.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit, 30f)) return;

        var city = hit.collider.GetComponentInParent<CityPoint>();
        if (city != null && RouteManager.Instance != null)
            RouteManager.Instance.OnCityClicked(city);
    }

    private void HandleHotkeys(Keyboard kb)
    {
        if (kb.rKey.wasPressedThisFrame && RouteManager.Instance != null)
            RouteManager.Instance.ResetRoute();

        if (kb.fKey.wasPressedThisFrame)
        {
            var menus = Object.FindObjectsByType<MenuController>(FindObjectsSortMode.None);
            foreach (var m in menus)
                m.ToggleInGameMenu();
        }
    }
#endif
}
