using System.Collections;
using UnityEngine;

public class Sheep : MonoBehaviour {

    [HideInInspector] public Transform planet;
    [HideInInspector] public float planetRadius;
    [HideInInspector] public SheepManager manager;

    public float moveSpeed = 2f;
    public float selectedHeight = 5f;
    public float surfaceOffset = 0.5f;
    public float screenCenterPull = 2.5f;
    public float directionSmoothing = 8f;
    public float screenCenterDeadZone = 0.01f;
    public float maxScreenDistance = 0.05f;

    enum State { Walking = 0, Grazing = 1, Held = 2 }
    State _state = State.Walking;

    void EnterState(State s) {
        _state = s;
        _animator.SetInteger(AnimState, (int)s);
    }

    Animator _animator;
    static readonly int AnimState = Animator.StringToHash("state");

    Transform _mesh;
    BoxCollider _surfaceCollider;
    Material _bodyMat;
    Transform _dropShadow;
    Material _dropShadowMat;
    int _botLayer;

    Vector3 _moveDir;
    Vector3 _facingDir;
    float _wanderTimer;
    float _speedScale;

    bool _changingHeight;
    float _meshOffset;
    float _meshOffsetTarget;
    float _descentTimer;
    Vector3 _safeDropTarget;
    State _pendingState;
    bool _hasPendingState;

    static PlanetCameraController _cam;
    static Vector2 _screenCenter;

    bool _cameraAligned;
    bool _facingFrozen;
    Collider _hoveredCollider;

    Coroutine _highlightCoroutine;
    Coroutine _shadowCoroutine;

    void Start() {
        _animator = GetComponent<Animator>();
        _mesh = transform.GetChild(0);
        _surfaceCollider = transform.GetChild(1).GetComponent<BoxCollider>();
        _bodyMat = _mesh.GetChild(4).GetComponent<MeshRenderer>().material;
        _dropShadow = transform.GetChild(2);
        _dropShadowMat = transform.GetChild(2).GetComponent<MeshRenderer>().material;
        _botLayer = LayerMask.GetMask("BotRaycast");

        _speedScale = 1f;
        _wanderTimer = Random.Range(1f, 3f);
        SnapToSurface();
        PickWanderDir();
        _facingDir = _moveDir;
        EnterState(State.Walking);
    }

    void Update() {
        UpdateDescent();

        switch (_state) {
            case State.Walking:
            case State.Grazing:
                UpdateWander();
                break;
            case State.Held:
                if (_cameraAligned) {
                    NudgeToScreenCenter();
                    CheckForSuitors();
                }
                if (!_facingFrozen) SmoothFacing(directionSmoothing);
                break;
        }
    }

    void UpdateWander() {
        _speedScale = Mathf.MoveTowards(_speedScale, 1f, 1.5f * Time.deltaTime);

        if (_state == State.Walking) {
            transform.position += _facingDir * (moveSpeed * _speedScale * Time.deltaTime);
            if (!_changingHeight) SeparateFromNearbySheep();

            SnapToSurface();
            SmoothFacing(3f);
            if (Physics.Raycast(transform.position, _facingDir, 2f, _botLayer))
                PickWanderDir();
        }

        _wanderTimer -= Time.deltaTime;
        if (_wanderTimer > 0f) return;

        _wanderTimer = Random.Range(1f, 3f);
        if (Random.value > 0.4f) { EnterState(State.Walking); PickWanderDir(); }
        else EnterState(State.Grazing);
    }

    void SeparateFromNearbySheep() {
        const float r = 1.5f;
        foreach (var c in Physics.OverlapSphere(_surfaceCollider.bounds.center, r, _botLayer)) {
            var other = SheepFrom(c);
            if (other == null || other == this || other._changingHeight || other._hasPendingState) continue; Vector3 sn = transform.position.normalized;
            Vector3 away = Vector3.ProjectOnPlane(transform.position - other.transform.position, sn);
            float overlap = r - away.magnitude;
            if (overlap <= 0f) continue;
            if (away.sqrMagnitude < 0.0001f) away = Vector3.ProjectOnPlane(Random.onUnitSphere, sn);
            transform.position += away.normalized * overlap;
            SnapToSurface();
            _moveDir = Vector3.Slerp(_moveDir, away.normalized, 0.5f);
        }
    }

    void PickWanderDir() =>
        _moveDir = Vector3.ProjectOnPlane(Random.onUnitSphere, transform.position.normalized).normalized;

    public void Pickup() {
        EnterState(State.Held);
        _cameraAligned = false;
        _facingFrozen = true;
        _hoveredCollider = null;
        _safeDropTarget = Vector3.zero;
        _hasPendingState = false;
        FadeDropShadow(0.4f);
        _meshOffsetTarget = selectedHeight;
        _changingHeight = true;
        if (_cam == null) _cam = FindFirstObjectByType<PlanetCameraController>();
        _screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
    }

    public void NotifyCameraAligned() {
        if (_state != State.Held) return;
        _moveDir = _facingDir;
        _cameraAligned = true;
        _facingFrozen = false;
    }

    public void Release() {
        if (_hoveredCollider != null) {
            SheepFrom(_hoveredCollider)?.FadeHighlight(0f);
            _hoveredCollider = null;
        }

        FadeDropShadow(0f);
        _cameraAligned = false;
        _meshOffsetTarget = 0f;
        _changingHeight = true;
        _descentTimer = 0f;
        _safeDropTarget = FindSafeDropPosition(excludeSelf: true);
        _wanderTimer = Random.Range(1f, 3f);
        _speedScale = 0f;
        _moveDir = _facingDir;
        EnterState(State.Walking);
    }

    void NudgeToScreenCenter() {
        Vector3 screen = _cam.Cam.WorldToScreenPoint(transform.position);

        Vector3 pushDir;
        float strength;

        if (screen.z < 0f) {
            pushDir = OnSurface(_cam.Cam.transform.forward);
            strength = moveSpeed * screenCenterPull * 10f;
        }
        else {
            Vector2 delta = _screenCenter - new Vector2(screen.x, screen.y);
            float dist = delta.magnitude;
            float deadPx = screenCenterDeadZone * Screen.height;
            float maxPx = maxScreenDistance * Screen.height;
            if (dist < deadPx) return;

            float t = (dist - deadPx) / (maxPx - deadPx);
            float k = t <= 1f ? Mathf.Pow(t, 3f) : 1f + Mathf.Pow(t - 1f, 2f);
            pushDir = OnSurface(_cam.Cam.transform.right * delta.x + _cam.Cam.transform.up * delta.y);
            strength = moveSpeed * screenCenterPull * k;
        }

        if (pushDir.sqrMagnitude < 0.0001f) return;
        transform.position = (transform.position + pushDir * (strength * Time.deltaTime)).normalized
                             * (planetRadius + surfaceOffset);
        _moveDir = OnSurface(pushDir);
    }

    void CheckForSuitors() {
        if (_changingHeight) return;
        const float r = 1.5f;

        Vector3 surfacePos = transform.position.normalized * (planetRadius + surfaceOffset);
        Collider best = null;
        foreach (var c in Physics.OverlapSphere(surfacePos, r, _botLayer)) {
            var s = SheepFrom(c);
            if (s != null && s != this) { best = c; break; }
        }

        if (best == _hoveredCollider) return;
        SheepFrom(_hoveredCollider)?.FadeHighlight(0f);
        _hoveredCollider = best;
        SheepFrom(best)?.FadeHighlight(10f);
    }

    void UpdateDescent() {
        if (!_changingHeight) return;

        _descentTimer += Time.deltaTime;
        float t = 1f - Mathf.Exp(-5f * Time.deltaTime);
        _meshOffset = Mathf.Lerp(_meshOffset, _meshOffsetTarget, t);
        if (Mathf.Abs(_meshOffset - _meshOffsetTarget) < 0.05f || _descentTimer > 1f) {
            _meshOffset = _meshOffsetTarget;
            _changingHeight = false;
            if (_hasPendingState && _safeDropTarget == Vector3.zero)
                ApplyPendingState();
        }
        _mesh.localPosition = Vector3.up * _meshOffset;

        if (_safeDropTarget == Vector3.zero || _meshOffsetTarget > 0f) return;

        float descentFraction = 1f - Mathf.Clamp01(_meshOffset / selectedHeight);
        if (descentFraction < 0.1f) return;

        Vector3 slideDir = OnSurface(_safeDropTarget - transform.position);
        if (slideDir.sqrMagnitude > 0.0001f) _facingDir = Vector3.Slerp(_facingDir, slideDir, 4f * Time.deltaTime);
        transform.position = Vector3.Slerp(transform.position, _safeDropTarget, 2.5f * Time.deltaTime);

        transform.position = Vector3.Slerp(transform.position, _safeDropTarget, 2.5f * Time.deltaTime); if (Vector3.Distance(transform.position, _safeDropTarget) < 0.01f) {
            transform.position = _safeDropTarget;
            _safeDropTarget = Vector3.zero;
            if (!_changingHeight && _hasPendingState)
                ApplyPendingState();
        }
    }

    void ApplyPendingState() {
        Debug.Log(_moveDir + " - " + _facingDir);

        _hasPendingState = false;
        _moveDir = _facingDir;
        EnterState(_pendingState);
    }

    void SnapToSurface() {
        Vector3 d = transform.position;
        transform.position = (d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.up)
                             * (planetRadius + surfaceOffset);
    }

    Vector3 OnSurface(Vector3 v) =>
        Vector3.ProjectOnPlane(v, transform.position.normalized).normalized;

    void SmoothFacing(float speed) {
        Vector3 target = OnSurface(_moveDir);
        if (target.sqrMagnitude < 0.0001f) return;
        Vector3 sn = transform.position.normalized;
        _facingDir = Vector3.ProjectOnPlane(
            Vector3.Slerp(_facingDir, target, speed * Time.deltaTime), sn).normalized;
        transform.rotation = Quaternion.LookRotation(_facingDir, sn);
    }

    Vector3 FindSafeDropPosition(bool excludeSelf = false, Sheep exclude = null) {
        Vector3 sn = transform.position.normalized;
        Vector3 base_ = sn * (planetRadius + surfaceOffset);
        if (IsClear(base_, excludeSelf, exclude)) return base_;

        Vector3 right = Vector3.ProjectOnPlane(Vector3.right, sn).normalized;
        if (right.sqrMagnitude < 0.001f)
            right = Vector3.ProjectOnPlane(Vector3.forward, sn).normalized;
        Vector3 fwd = Vector3.Cross(sn, right).normalized;

        for (int i = 1; i <= 16; i++) {
            float a = i * 37f * Mathf.Deg2Rad;
            Vector3 candidate = (base_ + (right * Mathf.Cos(a) + fwd * Mathf.Sin(a)) * (i * 0.5f))
                                .normalized * (planetRadius + surfaceOffset);
            if (IsClear(candidate, excludeSelf, exclude)) return candidate;
        }
        return base_;
    }

    bool IsClear(Vector3 pos, bool excludeSelf, Sheep exclude) {
        foreach (var c in Physics.OverlapSphere(pos, 1f, _botLayer)) {
            var s = SheepFrom(c);
            if (s == null) continue;
            if (excludeSelf && s == this) continue;
            if (s == exclude) continue;
            return false;
        }
        return true;
    }

    public void SnapAndSeparate() => transform.position = FindSafeDropPosition();

    public void PlaceOnSphere(Vector3 worldPos) {
        if (worldPos.sqrMagnitude < 0.0001f) worldPos = Vector3.up;
        transform.position = planet.position + worldPos.normalized * (planetRadius + surfaceOffset);
        PickWanderDir();
    }

    public void PlaceOnSphere(float latDeg, float lonDeg) {
        float lat = latDeg * Mathf.Deg2Rad;
        float lon = lonDeg * Mathf.Deg2Rad;
        transform.position = new Vector3(
            Mathf.Cos(lat) * Mathf.Sin(lon),
            Mathf.Sin(lat),
            Mathf.Cos(lat) * Mathf.Cos(lon)) * (planetRadius + surfaceOffset);
        PickWanderDir();
    }

    public void FadeHighlight(float target) {
        if (_highlightCoroutine != null) StopCoroutine(_highlightCoroutine);
        _highlightCoroutine = StartCoroutine(FadeEmissive(target));
    }

    IEnumerator FadeEmissive(float to) {
        Color base_ = new Color(0.8803151f, 0.5257697f, 0f);
        float from = _bodyMat.GetColor("_EmissiveColor").r / base_.r;
        for (float t = 0f; t < 1f; t += Time.deltaTime * 5f) {
            _bodyMat.SetColor("_EmissiveColor", base_ * Mathf.Lerp(from, to, t));
            yield return null;
        }
        _bodyMat.SetColor("_EmissiveColor", base_ * to);
    }

    public void FadeDropShadow(float target) {
        if (_shadowCoroutine != null) StopCoroutine(_shadowCoroutine);
        _shadowCoroutine = StartCoroutine(FadeShadow(target));
    }

    IEnumerator FadeShadow(float to) {
        float from = _dropShadowMat.color.a;
        for (float t = 0f; t < 1f; t += Time.deltaTime * 5f) {
            float a = Mathf.Lerp(from, to, t);
            _dropShadowMat.color = new Color(0f, 0f, 0f, a);
            _dropShadow.localScale = new Vector3(a * 5f, 0.01f, a * 5f);
            yield return null;
        }
        _dropShadowMat.color = new Color(0f, 0f, 0f, to);
        _dropShadow.localScale = new Vector3(to * 5f, 0.01f, to * 5f);
    }

    static Sheep SheepFrom(Collider c) =>
        c?.transform.parent?.parent?.GetComponent<Sheep>();

    void OnDrawGizmosSelected() {
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position, Vector3.zero);
        Gizmos.DrawWireSphere(transform.position, 0.15f);
    }
}