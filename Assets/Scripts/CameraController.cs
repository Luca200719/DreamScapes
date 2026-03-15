using UnityEngine;
using UnityEngine.InputSystem;

public class PlanetCameraOrbit : MonoBehaviour {

    int userLayer;

    public Collider planetCollider;
    bool pressingPlanet;
    bool pressingSheep;
    bool isRotating;

    Mouse mouse;
    Camera cam;

    Vector2 moveDelta;
    RaycastHit hit;

    Quaternion yawRot;
    Quaternion pitchRot;
    Quaternion orbit;

    public float orbitSpeed = 50f;
    public float smoothing = 8f;
    public float sheepOrbitSensitivity = 0.6f;

    public float scrollSpeed = 5f;
    public float minDistance = 2f;
    public float maxDistance = 150f;

    float _currentYaw;
    float _currentPitch;
    float _currentDistance;
    float _targetYaw;
    float _targetPitch;
    float _targetDistance;

    Sheep currSheep;

    public Camera Cam => cam;

    void Start() {
        userLayer = LayerMask.GetMask("GlobalRaycast");
        mouse = Mouse.current;
        cam = GetComponent<Camera>();

        Vector3 offset = transform.position;
        _currentDistance = _targetDistance = offset.magnitude;
        _currentYaw = _targetYaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
        _currentPitch = _targetPitch = -Mathf.Asin(offset.normalized.y) * Mathf.Rad2Deg;
    }

    void DirToAngles(Vector3 dir, out float yaw, out float pitch) {
        dir = dir.normalized;
        yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        pitch = -Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
    }

    void Update() {
        if (mouse.leftButton.wasPressedThisFrame) {
            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            if (Physics.Raycast(ray, out hit, maxDistance * 2f, userLayer)) {
                if (hit.collider == planetCollider) {
                    pressingPlanet = true;
                    isRotating = true;
                }
                else if (hit.transform.parent.TryGetComponent(out currSheep)) {
                    currSheep.Select(this);
                    pressingSheep = true;
                    isRotating = true;

                    DirToAngles(currSheep.transform.position, out _targetYaw, out _targetPitch);

                    _targetYaw = _currentYaw + Mathf.DeltaAngle(_currentYaw, _targetYaw);
                    _targetPitch = _currentPitch + Mathf.DeltaAngle(_currentPitch, _targetPitch);
                }
            }
        }

        if (mouse.leftButton.wasReleasedThisFrame) {
            pressingPlanet = false;
            if (pressingSheep) {
                currSheep.Deselect();
                pressingSheep = false;
            }
        }

        if (pressingPlanet) {
            moveDelta = mouse.delta.ReadValue();
            _targetYaw += moveDelta.x * orbitSpeed * Time.deltaTime;
            _targetPitch -= moveDelta.y * orbitSpeed * Time.deltaTime;
            isRotating = true;
        }
        else if (pressingSheep && currSheep.CameraReady) {
            moveDelta = mouse.delta.ReadValue();
            _targetYaw += moveDelta.x * sheepOrbitSensitivity * orbitSpeed * Time.deltaTime;
            _targetPitch -= moveDelta.y * sheepOrbitSensitivity * orbitSpeed * Time.deltaTime;
            isRotating = true;
        }

        if (isRotating) {
            bool yawClose = Mathf.Abs(Mathf.DeltaAngle(_currentYaw, _targetYaw)) < 0.5f;
            bool pitchClose = Mathf.Abs(Mathf.DeltaAngle(_currentPitch, _targetPitch)) < 0.5f;
            bool distanceClose = Mathf.Abs(_currentDistance - _targetDistance) < 0.05f;

            _currentYaw = Mathf.LerpAngle(_currentYaw, _targetYaw, smoothing * Time.deltaTime);
            _currentPitch = Mathf.LerpAngle(_currentPitch, _targetPitch, smoothing * Time.deltaTime);
            _currentDistance = Mathf.Lerp(_currentDistance, _targetDistance, smoothing * Time.deltaTime);
            ApplyOrbit();

            if (yawClose && pitchClose && distanceClose && pressingSheep && currSheep != null) {
                currSheep.NotifyCameraReady();
            }

            if (yawClose && pitchClose && distanceClose && !pressingPlanet && !pressingSheep) {
                _currentYaw = _targetYaw;
                _currentPitch = _targetPitch;
                _currentDistance = _targetDistance;
                isRotating = false;
                ApplyOrbit();
            }
        }
    }

    void ApplyOrbit() {
        yawRot = Quaternion.AngleAxis(_currentYaw, Vector3.up);
        pitchRot = Quaternion.AngleAxis(_currentPitch, Vector3.right);
        orbit = yawRot * pitchRot;
        transform.position = orbit * Vector3.forward * _currentDistance;
        transform.LookAt(Vector3.zero, orbit * Vector3.up);
    }
}