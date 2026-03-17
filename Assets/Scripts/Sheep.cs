using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

public class Sheep : MonoBehaviour {

    [HideInInspector] public Transform planet;
    [HideInInspector] public float planetRadius;

    Animator _animator;
    static readonly int AnimWalking = Animator.StringToHash("isWalking");
    static readonly int AnimGrazing = Animator.StringToHash("isGrazing");
    static readonly int AnimInAir = Animator.StringToHash("isInAir");

    Transform _mesh;
    BoxCollider _surfaceCollider;
    Material _bodyMat;
    Transform _dropShadow;
    Material _dropShadowMat;

    Collider currentSheepCollider;

    int botLayer;

    float stateSpeed = 2f;
    public float moveSpeed = 2f;
    public float selectedHeight = 5f;
    public float surfaceOffset = 0.5f;

    public float screenCenterPull = 2.5f;
    public float directionSmoothing = 100f;

    public float screenCenterDeadZone = 0.01f;
    public float maxScreenDistance = 0.05f;

    bool changingHeight;
    float _currentMeshOffset = 0f;
    float _targetMeshOffset = 0f;

    Vector3 _moveDirection;
    Vector3 _facingDirection;

    float _wanderTimer;
    RaycastHit hit;
    float pickState;
    bool isGrazing = false;
    bool selected = false;

    bool _cameraReady;
    public bool CameraReady => _cameraReady;
    PlanetCameraOrbit _camController;
    Vector2 _screenCenter;

    Coroutine currentlyFadingHighlight = null;
    Coroutine currentlyFadingDropShadow = null;

    void Start() {
        _animator = GetComponent<Animator>();

        _mesh = transform.GetChild(0);
        _surfaceCollider = transform.GetChild(1).GetComponent<BoxCollider>();

        _bodyMat = _mesh.GetChild(4).GetComponent<MeshRenderer>().material;
        _dropShadow = transform.GetChild(2);
        _dropShadowMat = transform.GetChild(2).GetComponent<MeshRenderer>().material;

        botLayer = LayerMask.GetMask("BotRaycast");
        _wanderTimer = 2 + Random.Range(-1f, 1f);
        SnapToSurface();
        PickNewWanderDirection();
        _facingDirection = _moveDirection;
    }

    void Update() {
        if (changingHeight) {
            _currentMeshOffset = Mathf.Lerp(_currentMeshOffset, _targetMeshOffset, 10f * Time.deltaTime);
            if (Mathf.Abs(_currentMeshOffset - _targetMeshOffset) < 0.001f) {
                _currentMeshOffset = _targetMeshOffset;
                changingHeight = false;
            }

            _mesh.localPosition = Vector3.up * _currentMeshOffset;
        }

        if (selected) {
            if (_cameraReady) {
                NudgeTowardScreenCenter();
                CheckSheepCollisions();
            }
            SmoothFacing();
        }
        else {
            if (!isGrazing) {
                MoveOnSurface();
                SnapToSurface();
                SmoothFacing();
                CheckForSheep();
            }

            _wanderTimer -= Time.deltaTime * stateSpeed;
            if (_wanderTimer <= 0) {
                _wanderTimer = 2 + Random.Range(-1f, 1f);
                pickState = Random.value;
                if (pickState > 0.4f) {
                    isGrazing = false;
                    stateSpeed = 1f;
                    _animator.SetBool(AnimGrazing, false);
                    _animator.SetBool(AnimWalking, true);
                    PickNewWanderDirection();
                }
                else {
                    isGrazing = true;
                    stateSpeed = 2f;
                    _animator.SetBool(AnimGrazing, true);
                    _animator.SetBool(AnimWalking, false);
                }
            }
        }
    }

    void NudgeTowardScreenCenter() {
        if (_camController == null) return;

        float deadZonePx = screenCenterDeadZone * Screen.height;
        float maxDistPx = maxScreenDistance * Screen.height;

        Vector3 sheepScreen = _camController.Cam.WorldToScreenPoint(transform.position);

        if (sheepScreen.z < 0f) {
            Vector3 surfaceNormalBehind = transform.position.normalized;
            Vector3 camForward = _camController.Cam.transform.forward;
            Vector3 pullDir = Vector3.ProjectOnPlane(camForward, surfaceNormalBehind).normalized;
            if (pullDir.sqrMagnitude < 0.0001f) return;

            float hardSpeed = moveSpeed * screenCenterPull * 10f;
            Vector3 newPosBehind = transform.position + pullDir * (hardSpeed * Time.deltaTime);
            transform.position = newPosBehind.normalized * (planetRadius + surfaceOffset);

            Vector3 tangentBehind = Vector3.ProjectOnPlane(pullDir, transform.position.normalized).normalized;
            if (tangentBehind.sqrMagnitude > 0.0001f) _moveDirection = tangentBehind;
            return;
        }

        Vector2 screenDelta = _screenCenter - new Vector2(sheepScreen.x, sheepScreen.y);
        float distance = screenDelta.magnitude;

        if (distance < deadZonePx) return;

        float t = (distance - deadZonePx) / (maxDistPx - deadZonePx);
        float pullStrength = t <= 1f ? Mathf.Pow(t, 3f) : 1f + Mathf.Pow(t - 1f, 2f);

        Vector3 surfaceNormal = transform.position.normalized;
        Vector3 camRight = _camController.Cam.transform.right;
        Vector3 camUp = _camController.Cam.transform.up;

        Vector3 worldNudge = (camRight * screenDelta.x + camUp * screenDelta.y).normalized;
        Vector3 targetDir = Vector3.ProjectOnPlane(worldNudge, surfaceNormal).normalized;

        if (targetDir.sqrMagnitude < 0.0001f) return;

        float speed = moveSpeed * screenCenterPull * pullStrength;
        Vector3 newPos = transform.position + targetDir * (speed * Time.deltaTime);
        transform.position = newPos.normalized * (planetRadius + surfaceOffset);

        Vector3 tangent = Vector3.ProjectOnPlane(targetDir, transform.position.normalized).normalized;
        if (tangent.sqrMagnitude > 0.0001f) _moveDirection = tangent;
    }

    void CheckSheepCollisions() {
        if (changingHeight) return;

        Collider[] hits = Physics.OverlapSphere(_surfaceCollider.bounds.center, 1f, botLayer);

        if (hits.Length > 0) {
            if (currentSheepCollider != hits[0]) {
                if (currentSheepCollider != null) {
                    currentSheepCollider.transform.parent.parent.GetComponent<Sheep>().FadeHighlight(0f);
                }
                currentSheepCollider = hits[0];
                currentSheepCollider.transform.parent.parent.GetComponent<Sheep>().FadeHighlight(10f);
            }
        }

        else if (currentSheepCollider != null) {
            currentSheepCollider.transform.parent.parent.GetComponent<Sheep>().FadeHighlight(0f);
            currentSheepCollider = null;
        }
    }

    void SmoothFacing() {
        Vector3 surfaceNormal = transform.position.normalized;
        Vector3 target = Vector3.ProjectOnPlane(_moveDirection, surfaceNormal).normalized;

        if (target.sqrMagnitude < 0.0001f) return;

        _facingDirection = Vector3.Slerp(_facingDirection, target, directionSmoothing * Time.deltaTime);
        _facingDirection = Vector3.ProjectOnPlane(_facingDirection, surfaceNormal).normalized;

        Quaternion targetRot = Quaternion.LookRotation(_facingDirection, surfaceNormal);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, directionSmoothing * Time.deltaTime);
    }

    void MoveOnSurface() {
        transform.position += _moveDirection * (moveSpeed * Time.deltaTime);
    }

    void SnapToSurface() {
        Vector3 toSheep = transform.position;
        if (toSheep.sqrMagnitude < 0.0001f) toSheep = Vector3.up;
        transform.position = toSheep.normalized * (planetRadius + surfaceOffset);
    }

    void CheckForSheep() {
        bool blocked = true;
        while (blocked) {
            if (Physics.Raycast(transform.position, _moveDirection, out hit, 2f, botLayer))
                PickNewWanderDirection();
            else
                blocked = false;
        }
    }

    public void Select(PlanetCameraOrbit cam) {
        _animator.SetBool(AnimWalking, false);
        _animator.SetBool(AnimGrazing, false);
        _animator.SetBool(AnimInAir, true);

        FadeDropShadow(0.4f);

        selected = true;
        _cameraReady = false;
        _camController = cam;
        _screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);
        _targetMeshOffset = selectedHeight;
        changingHeight = true;
        SnapToSurface();
    }

    public void NotifyCameraReady() {
        _cameraReady = true;
    }

    public void Deselect() {
        if (!isGrazing) {
            _animator.SetBool(AnimWalking, true);
            _animator.SetBool(AnimGrazing, false);
        }
        else {
            _animator.SetBool(AnimWalking, false);
            _animator.SetBool(AnimGrazing, true);
        }
        _animator.SetBool(AnimInAir, false);

        if (currentSheepCollider != null) {
            currentSheepCollider.transform.parent.parent.GetComponent<Sheep>().FadeHighlight(0f);
            currentSheepCollider = null;
        }

        FadeDropShadow(0f);

        selected = false;
        _camController = null;
        _targetMeshOffset = 0;
        changingHeight = true;
        SnapToSurface();
    }

    void PickNewWanderDirection() {
        Vector3 surfaceNormal = transform.position.normalized;
        _moveDirection = Vector3.ProjectOnPlane(Random.onUnitSphere, surfaceNormal).normalized;
    }

    public void PlaceOnSphere(Vector3 worldPosition) {
        if (worldPosition.sqrMagnitude < 0.0001f) worldPosition = Vector3.up;
        transform.position = planet.position + worldPosition.normalized * (planetRadius + surfaceOffset);
        PickNewWanderDirection();
    }

    public void PlaceOnSphere(float latitudeDeg, float longitudeDeg) {
        float lat = latitudeDeg * Mathf.Deg2Rad;
        float lon = longitudeDeg * Mathf.Deg2Rad;
        Vector3 dir = new Vector3(Mathf.Cos(lat) * Mathf.Sin(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Cos(lon));
        transform.position = dir * (planetRadius + surfaceOffset);
        PickNewWanderDirection();
    }

    public void FadeHighlight(float target) {
        if (currentlyFadingHighlight != null) {
            StopCoroutine(currentlyFadingHighlight);
        }

        currentlyFadingHighlight = StartCoroutine(BodyGlow(target));
    }

    IEnumerator BodyGlow(float targetIntensity) {
        Color baseColor = new Color(0.8803151f, 0.5257697f, 0f);

        float t = 0f;

        Color currentEmissive = _bodyMat.GetColor("_EmissiveColor");

        float currentIntensity = currentEmissive.r / baseColor.r;

        while (t < 1f) {
            t += Time.deltaTime * 5f;

            float intensity = Mathf.Lerp(currentIntensity, targetIntensity, t);

            _bodyMat.SetColor("_EmissiveColor", baseColor * intensity);

            yield return null;
        }
    }

    public void FadeDropShadow(float target) {
        if (currentlyFadingDropShadow != null) {
            StopCoroutine(currentlyFadingDropShadow);
        }

        currentlyFadingDropShadow = StartCoroutine(DropShadow(target));
    }

    IEnumerator DropShadow(float targetIntensity) {
        float currentIntensity = _dropShadowMat.color.a;

        float t = 0f;

        Color c = _dropShadowMat.color;

        while (t < 1f) {
            t += Time.deltaTime * 5f;

            float intensity = Mathf.Lerp(currentIntensity, targetIntensity, t);

            c.a = intensity;
            _dropShadowMat.color = c;

            _dropShadow.localScale = new Vector3(intensity * 5f, 0.01f, intensity * 5f);

            yield return null;
        }
    }

    void OnDrawGizmosSelected() {
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position, Vector3.zero);
        Gizmos.DrawWireSphere(transform.position, 0.15f);
    }
}