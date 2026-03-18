using UnityEngine;
using UnityEngine.InputSystem;

public class PlanetCameraController : MonoBehaviour {

    public Collider planetCollider;

    public float orbitSpeed = 50f;
    public float smoothing = 8f;
    public float sheepOrbitSensitivity = 0.6f;
    public float minDistance = 2f;
    public float maxDistance = 150f;

    bool _pressingPlanet;
    bool _pressingSheep;
    bool _isRotating;
    bool _cameraAlignedToSheep;

    Sheep _heldSheep;

    Mouse _mouse;
    Camera _cam;
    public Camera Cam => _cam;

    int _userLayer;

    float _currentYaw;
    float _currentPitch;
    float _currentDistance;
    float _targetYaw;
    float _targetPitch;
    float _targetDistance;

    void Start() {
        _userLayer = LayerMask.GetMask("GlobalRaycast");
        _mouse = Mouse.current;
        _cam = GetComponent<Camera>();

        Vector3 offset = transform.position;
        _currentDistance = _targetDistance = offset.magnitude;
        _currentYaw = _targetYaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
        _currentPitch = _targetPitch = -Mathf.Asin(offset.normalized.y) * Mathf.Rad2Deg;
    }

    void Update() {
        HandlePress();
        HandleRelease();
        HandleDrag();
        HandleRotation();
    }

    void HandlePress() {
        if (!_mouse.leftButton.wasPressedThisFrame) return;

        Ray ray = _cam.ScreenPointToRay(_mouse.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit, maxDistance * 2f, _userLayer)) return;

        if (hit.collider == planetCollider) {
            _pressingPlanet = true;
            _isRotating = true;
            return;
        }

        Sheep sheep = hit.transform.GetComponentInParent<Sheep>();
        if (sheep == null) return;

        _heldSheep = sheep;
        _heldSheep.Pickup();
        _pressingSheep = true;
        _cameraAlignedToSheep = false;
        _isRotating = true;

        DirToAngles(_heldSheep.transform.position, out float ty, out float tp);
        _targetYaw = _currentYaw + Mathf.DeltaAngle(_currentYaw, ty);
        _targetPitch = _currentPitch + Mathf.DeltaAngle(_currentPitch, tp);
    }

    void HandleRelease() {
        if (!_mouse.leftButton.wasReleasedThisFrame) return;

        _pressingPlanet = false;

        if (_pressingSheep) {
            _heldSheep.Release();
            _heldSheep = null;
            _pressingSheep = false;
            _cameraAlignedToSheep = false;
        }
    }

    void HandleDrag() {
        if (_pressingPlanet) {
            Vector2 delta = _mouse.delta.ReadValue();
            _targetYaw += delta.x * orbitSpeed * Time.deltaTime;
            _targetPitch -= delta.y * orbitSpeed * Time.deltaTime;
            _isRotating = true;
        }
        else if (_pressingSheep && _cameraAlignedToSheep) {
            Vector2 delta = _mouse.delta.ReadValue();
            _targetYaw += delta.x * sheepOrbitSensitivity * orbitSpeed * Time.deltaTime;
            _targetPitch -= delta.y * sheepOrbitSensitivity * orbitSpeed * Time.deltaTime;
            _isRotating = true;
        }
    }

    void HandleRotation() {
        if (!_isRotating) return;

        bool yawClose = Mathf.Abs(Mathf.DeltaAngle(_currentYaw, _targetYaw)) < 0.05f;
        bool pitchClose = Mathf.Abs(Mathf.DeltaAngle(_currentPitch, _targetPitch)) < 0.05f;

        _currentYaw = Mathf.LerpAngle(_currentYaw, _targetYaw, smoothing * Time.deltaTime);
        _currentPitch = Mathf.LerpAngle(_currentPitch, _targetPitch, smoothing * Time.deltaTime);
        _currentDistance = Mathf.Lerp(_currentDistance, _targetDistance, smoothing * Time.deltaTime);
        ApplyOrbit();

        if (yawClose && pitchClose && _pressingSheep && !_cameraAlignedToSheep) {
            _cameraAlignedToSheep = true;
            _heldSheep.NotifyCameraAligned();
        }

        if (yawClose && pitchClose && !_pressingPlanet && !_pressingSheep) {
            _currentYaw = _targetYaw;
            _currentPitch = _targetPitch;
            _currentDistance = _targetDistance;
            _isRotating = false;
            ApplyOrbit();
        }
    }

    void ApplyOrbit() {
        Quaternion yawRot = Quaternion.AngleAxis(_currentYaw, Vector3.up);
        Quaternion pitchRot = Quaternion.AngleAxis(_currentPitch, Vector3.right);
        Quaternion orbit = yawRot * pitchRot;
        transform.position = orbit * Vector3.forward * _currentDistance;
        transform.LookAt(Vector3.zero, orbit * Vector3.up);
    }

    static void DirToAngles(Vector3 dir, out float yaw, out float pitch) {
        dir = dir.normalized;
        yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        pitch = -Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
    }
}