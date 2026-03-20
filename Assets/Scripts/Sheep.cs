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

    public float heightSpeed = 10f;

    const float AvoidanceInterval = 0.1f;

    enum State { Walking = 0, Grazing = 1, Held = 2 }
    State _state = State.Walking;

    void EnterState(State s) {
        _state = s;
        _animator.SetInteger(AnimState, (int)s);
    }

    Animator _animator;
    static readonly int AnimState = Animator.StringToHash("state");
    static readonly int EmissiveColor = Shader.PropertyToID("_EmissiveColor");

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
    
    float _avoidanceTimer;

    bool _changingHeight;
    float _meshOffset;
    float _meshOffsetTarget;
    float _descentTimer;
    Vector3 _safeDropTarget;
    State _pendingState;
    bool _hasPendingState;
    public Vector3 ReservedLandingSpot => _changingHeight && _meshOffsetTarget == 0f && _safeDropTarget != Vector3.zero ? _safeDropTarget : Vector3.zero;

    static PlanetCameraController _cam;
    static Vector2 _screenCenter;

    bool _cameraAligned;
    bool _facingFrozen;
    Collider _hoveredCollider;

    Coroutine _highlightCoroutine;
    Coroutine _shadowCoroutine;
    Coroutine _dropSearchCoroutine;

    static readonly Color EmissiveBase = new Color(0.8803151f, 0.5257697f, 0f);

    void Start() {
        _animator = GetComponent<Animator>();
        _mesh = transform.GetChild(0);
        _surfaceCollider = transform.GetChild(1).GetComponent<BoxCollider>();
        _bodyMat = _mesh.GetChild(4).GetComponent<MeshRenderer>().material;
        _dropShadow = transform.GetChild(2);
        _dropShadowMat = transform.GetChild(2).GetComponent<MeshRenderer>().material;
        _botLayer = LayerMask.GetMask("BotRaycast");

        if (_cam == null) _cam = FindFirstObjectByType<PlanetCameraController>();

        _speedScale = 1f;
        _wanderTimer = Random.Range(1f, 3f);
        _avoidanceTimer = Random.Range(0f, AvoidanceInterval);
        SnapToSurface();
        PickWanderDir();
        _facingDir = _moveDir;
        EnterState(State.Walking);
    }

    void Update() {
        if (_state == State.Held)
            _screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

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

            if (!_changingHeight) {
                _avoidanceTimer -= Time.deltaTime;
                if (_avoidanceTimer <= 0f) {
                    _avoidanceTimer = AvoidanceInterval;
                    ApplyNeighbourAvoidance();
                    if (Physics.Raycast(transform.position, _facingDir, 2f, _botLayer))
                        PickWanderDir();
                }
            }

            SnapToSurface();
            SmoothFacing(3f);
        }

        _wanderTimer -= Time.deltaTime;
        if (_wanderTimer > 0f) return;

        _wanderTimer = Random.Range(1f, 3f);
        if (Random.value > 0.4f) { EnterState(State.Walking); PickWanderDir(); }
        else EnterState(State.Grazing);
    }

    void ApplyNeighbourAvoidance() {
        const float avoidRadius = 1.5f;
        const float avoidRadiusSq = avoidRadius * avoidRadius;
        const float avoidCore = 1f;
        const float steerRate = 100f;

        Vector3 sn = transform.position.normalized;
        Vector3 steerAway = Vector3.zero;
        float maxWeight = 0f;

        foreach (var s in manager.sheep) {
            if (s == this || s.ReservedLandingSpot == Vector3.zero) continue;
            Vector3 away = Vector3.ProjectOnPlane(transform.position - s.ReservedLandingSpot, sn);
            float distSq = away.sqrMagnitude;
            if (distSq >= avoidRadiusSq) continue;
            float dist = Mathf.Sqrt(distSq);
            float weight = 1f - Mathf.Clamp01((dist - avoidCore) / (avoidRadius - avoidCore));
            steerAway += away.normalized * weight;
            if (weight > maxWeight) maxWeight = weight;
        }

        foreach (var c in Physics.OverlapSphere(_surfaceCollider.bounds.center, avoidRadius, _botLayer)) {
            var other = SheepFrom(c);
            if (other == null || other == this || other._changingHeight || other._hasPendingState) continue;
            Vector3 toOther = Vector3.ProjectOnPlane(other.transform.position - transform.position, sn);
            if (toOther.sqrMagnitude < 0.0001f) toOther = Vector3.ProjectOnPlane(Random.onUnitSphere, sn);
            float distSq = toOther.sqrMagnitude;
            if (distSq >= avoidRadiusSq) continue;
            float dist = Mathf.Sqrt(distSq);
            float weight = 1f - Mathf.Clamp01((dist - avoidCore) / (avoidRadius - avoidCore));
            float dot = Vector3.Dot(_facingDir, toOther.normalized);
            if (dot > 0f) _speedScale = Mathf.Min(_speedScale, 1f - weight * dot);
            steerAway -= toOther.normalized * weight;
            if (weight > maxWeight) maxWeight = weight;
        }

        if (steerAway.sqrMagnitude > 0.0001f)
            _moveDir = Vector3.Slerp(_moveDir, steerAway.normalized, steerRate * maxWeight * Time.deltaTime).normalized;
    }

    void PickWanderDir() => _moveDir = Vector3.ProjectOnPlane(Random.onUnitSphere, transform.position.normalized).normalized;

    public void Pickup() {
        if (_dropSearchCoroutine != null) {
            StopCoroutine(_dropSearchCoroutine);
            _dropSearchCoroutine = null;
        }

        EnterState(State.Held);
        _cameraAligned = false;
        _facingFrozen = true;
        _hoveredCollider = null;
        _safeDropTarget = Vector3.zero;
        _hasPendingState = false;
        FadeDropShadow(0.4f);
        _meshOffsetTarget = selectedHeight;
        _changingHeight = true;
        _descentTimer = 0f;
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
        _wanderTimer = Random.Range(1f, 3f);
        _speedScale = 0f;
        _moveDir = _facingDir;
        EnterState(State.Walking);
        _pendingState = State.Walking;
        _hasPendingState = true;

        _dropSearchCoroutine = StartCoroutine(FindSafeDropAsync());
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

        float t = 1f - Mathf.Exp(-heightSpeed * Time.deltaTime);
        _meshOffset = Mathf.Lerp(_meshOffset, _meshOffsetTarget, t);

        bool heightDone = Mathf.Abs(_meshOffset - _meshOffsetTarget) < 0.05f || _descentTimer > 10f;
        if (heightDone) _meshOffset = _meshOffsetTarget;
        _mesh.localPosition = Vector3.up * _meshOffset;

        bool posDone = _safeDropTarget == Vector3.zero;
        if (!posDone && _meshOffsetTarget <= 0f) {
            float descentFraction = 1f - Mathf.Clamp01(_meshOffset / selectedHeight);
            if (descentFraction >= 0.1f) {
                float step = moveSpeed * 2.5f * Time.deltaTime;
                transform.position = Vector3.MoveTowards(transform.position, _safeDropTarget, step);
                if ((transform.position - _safeDropTarget).sqrMagnitude < 0.0001f) {
                    transform.position = _safeDropTarget;
                    posDone = true;
                    _safeDropTarget = Vector3.zero;
                }
            }
        }

        if (heightDone && posDone) {
            _changingHeight = false;
            if (_hasPendingState) ApplyPendingState();
        }
    }

    void ApplyPendingState() {
        _hasPendingState = false;
        _safeDropTarget = Vector3.zero;
        _wanderTimer = Random.Range(1f, 3f);
        SnapToSurface();
        _speedScale = 0f;
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
        Quaternion current = Quaternion.LookRotation(_facingDir, sn);
        Quaternion desired = Quaternion.LookRotation(target, sn);
        Quaternion result = Quaternion.RotateTowards(current, desired, speed * 45f * Time.deltaTime);
        _facingDir = result * Vector3.forward;
        transform.rotation = result;
    }

    IEnumerator FindSafeDropAsync() {
        Vector3 sn = transform.position.normalized;
        Vector3 base_ = sn * (planetRadius + surfaceOffset);

        if (IsClear(base_, excludeSelf: true, exclude: null)) {
            _safeDropTarget = base_;
            _dropSearchCoroutine = null;
            yield break;
        }

        Vector3 right = Vector3.ProjectOnPlane(Vector3.right, sn).normalized;
        if (right.sqrMagnitude < 0.001f)
            right = Vector3.ProjectOnPlane(Vector3.forward, sn).normalized;
        Vector3 fwd = Vector3.Cross(sn, right).normalized;

        for (int i = 1; i <= 16; i++) {
            float a = i * 37f * Mathf.Deg2Rad;
            Vector3 candidate = (base_ + (right * Mathf.Cos(a) + fwd * Mathf.Sin(a)) * (i * 0.5f))
                                .normalized * (planetRadius + surfaceOffset);

            if (IsClear(candidate, excludeSelf: true, exclude: null)) {
                _safeDropTarget = candidate;
                _dropSearchCoroutine = null;
                yield break;
            }

            if (i % 4 == 0) yield return null;
        }

        _safeDropTarget = base_;
        _dropSearchCoroutine = null;
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
        Vector3 posNorm = pos.normalized;
        foreach (var c in Physics.OverlapCapsule(pos - posNorm * 0.1f, pos + posNorm * 0.1f, 0.675f, _botLayer)) {
            var s = SheepFrom(c);
            if (s == null) continue;
            if (excludeSelf && s == this) continue;
            if (s == exclude) continue;
            return false;
        }

        const float reservationRadiusSq = 1.35f * 1.35f;
        foreach (var s in manager.sheep) {
            if (s == this || s.ReservedLandingSpot == Vector3.zero) continue;
            if ((pos - s.ReservedLandingSpot).sqrMagnitude < reservationRadiusSq) return false;
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
        transform.position = new Vector3(Mathf.Cos(lat) * Mathf.Sin(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Cos(lon)) * (planetRadius + surfaceOffset);
        PickWanderDir();
    }

    public void FadeHighlight(float target) {
        if (_highlightCoroutine != null) StopCoroutine(_highlightCoroutine);
        _highlightCoroutine = StartCoroutine(FadeEmissive(target));
    }

    IEnumerator FadeEmissive(float to) {
        float from = _bodyMat.GetColor(EmissiveColor).r / EmissiveBase.r;
        for (float t = 0f; t < 1f; t += Time.deltaTime * 5f) {
            _bodyMat.SetColor(EmissiveColor, EmissiveBase * Mathf.Lerp(from, to, t));
            yield return null;
        }
        _bodyMat.SetColor(EmissiveColor, EmissiveBase * to);
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