using System.Collections;
using UnityEngine;

public class Sheep : MonoBehaviour {

    [HideInInspector] public Transform planet;
    [HideInInspector] public float planetRadius;
    [HideInInspector] public SheepManager manager;

    Animator _animator;
    static readonly int AnimWalking = Animator.StringToHash("isWalking");
    static readonly int AnimGrazing = Animator.StringToHash("isGrazing");
    static readonly int AnimInAir = Animator.StringToHash("isInAir");
    static readonly int AnimSleeping = Animator.StringToHash("isSleeping"); // add this bool to your Animator

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

    public float approachSpeed = 1.5f;
    public float nuzzleDistance = -0.5f; // negative = overlap before triggering
    public float interactionSleepDuration = 3f;

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
    [HideInInspector] public bool isSleeping = false;

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

        if (isSleeping) return;

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

    // --- INTERACTION SEQUENCE ---

    // Called on the dropped sheep when it lands on another.
    // Both sheep run InteractionSequence independently in parallel.
    public void StartInteraction(Sheep other) {
        StartCoroutine(InteractionSequence(other));
        other.StartCoroutine(other.InteractionSequence(this));
    }

    IEnumerator InteractionSequence(Sheep other) {
        isSleeping = true; // lock out normal Update behaviour immediately

        // Wait for this sheep to finish landing
        while (changingHeight) yield return null;

        // Wait for the other sheep to land too
        while (other.changingHeight) yield return null;

        // Approach: walk toward each other until heads are within nuzzleDistance
        _animator.SetBool(AnimWalking, true);
        _animator.SetBool(AnimGrazing, false);
        _animator.SetBool(AnimInAir, false);

        while (true) {
            Vector3 surfaceNormal = transform.position.normalized;
            Vector3 toOther = Vector3.ProjectOnPlane(
                (other.transform.position - transform.position).normalized,
                surfaceNormal
            ).normalized;

            float dist = Vector3.Distance(transform.position, other.transform.position);
            if (dist <= nuzzleDistance) break;

            // Always steer facing toward the other sheep
            if (toOther.sqrMagnitude > 0.0001f) {
                _facingDirection = Vector3.Slerp(_facingDirection, toOther, directionSmoothing * Time.deltaTime);
                _facingDirection = Vector3.ProjectOnPlane(_facingDirection, surfaceNormal).normalized;
                transform.rotation = Quaternion.LookRotation(_facingDirection, surfaceNormal);
            }

            // Dead zone: only move once facing is roughly aligned with toOther
            // This prevents curving past each other — they rotate in place first
            float alignment = Vector3.Dot(_facingDirection, toOther);
            if (alignment > 0.85f) {
                transform.position += _facingDirection * (approachSpeed * Time.deltaTime);
                SnapToSurface();
            }

            yield return null;
        }

        _animator.SetBool(AnimWalking, false);

        // One sheep triggers the spawn — position tie-break ensures only one fires
        if (transform.position.sqrMagnitude >= other.transform.position.sqrMagnitude) {
            Vector3 midpoint = (transform.position + other.transform.position) * 0.5f;

            // TODO: replace with your animated instantiation sequence, e.g:
            //   yield return StartCoroutine(manager.PlaySpawnSequence(midpoint));
            // The spawn and its animation play here in full before sleep begins.
            yield return new WaitForSeconds(manager.spawnAnimationDuration);
            manager.QueueSpawn(midpoint);
        }
        else {
            // Non-initiator waits the same duration so both enter sleep together
            yield return new WaitForSeconds(manager.spawnAnimationDuration);
        }

        // Both sheep go to sleep
        _animator.SetBool(AnimSleeping, true);

        yield return new WaitForSeconds(interactionSleepDuration);

        // Wake up and resume
        _animator.SetBool(AnimSleeping, false);
        _animator.SetBool(AnimWalking, true);

        isGrazing = false;
        isSleeping = false;
        _wanderTimer = 2f + Random.Range(-1f, 1f);
        PickNewWanderDirection();
    }

    // --- SELECT / DESELECT ---

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
        _animator.SetBool(AnimInAir, false);
        FadeDropShadow(0f);

        selected = false;
        _camController = null;
        _targetMeshOffset = 0;
        changingHeight = true;

        // Smoothly nudge to a clear position — never inside another sheep
        Vector3 safePos = FindSafeDropPosition();
        transform.position = Vector3.Lerp(transform.position, safePos, 0.85f);
        SnapToSurface();

        if (currentSheepCollider != null) {
            Sheep other = currentSheepCollider.transform.parent.parent.GetComponent<Sheep>();
            other.FadeHighlight(0f);
            currentSheepCollider = null;
            // Interaction still queues — they'll walk to each other from their safe positions
            StartInteraction(other);
            return;
        }

        if (!isGrazing) {
            _animator.SetBool(AnimWalking, true);
            _animator.SetBool(AnimGrazing, false);
        }
        else {
            _animator.SetBool(AnimWalking, false);
            _animator.SetBool(AnimGrazing, true);
        }
    }

    // --- MOVEMENT / SURFACE ---

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
            Sheep candidate = hits[0].transform.parent.parent.GetComponent<Sheep>();
            // Don't highlight or allow dropping onto a sheep that's already in a sequence
            if (!candidate.isSleeping && currentSheepCollider != hits[0]) {
                if (currentSheepCollider != null)
                    currentSheepCollider.transform.parent.parent.GetComponent<Sheep>().FadeHighlight(0f);
                currentSheepCollider = hits[0];
                candidate.FadeHighlight(10f);
            }
            else if (candidate.isSleeping && currentSheepCollider != null) {
                currentSheepCollider.transform.parent.parent.GetComponent<Sheep>().FadeHighlight(0f);
                currentSheepCollider = null;
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

        float smoothSpeed = selected ? directionSmoothing : 3f;

        // Steer facing toward the desired direction
        _facingDirection = Vector3.Slerp(_facingDirection, target, smoothSpeed * Time.deltaTime);
        _facingDirection = Vector3.ProjectOnPlane(_facingDirection, surfaceNormal).normalized;

        transform.rotation = Quaternion.LookRotation(_facingDirection, surfaceNormal);
    }

    void MoveOnSurface() {
        // Always move in the direction the sheep is facing — turning feels car-like
        transform.position += _facingDirection * (moveSpeed * Time.deltaTime);
    }

    Vector3 FindSafeDropPosition() {
        Vector3 surfaceNormal = transform.position.normalized;
        Vector3 basePos = surfaceNormal * (planetRadius + surfaceOffset);

        if (!Physics.CheckSphere(basePos, 0.8f, botLayer))
            return basePos;

        // Spiral outward until a clear spot is found
        Vector3 right = Vector3.ProjectOnPlane(Vector3.right, surfaceNormal).normalized;
        if (right.sqrMagnitude < 0.001f)
            right = Vector3.ProjectOnPlane(Vector3.forward, surfaceNormal).normalized;
        Vector3 fwd = Vector3.Cross(surfaceNormal, right).normalized;

        for (int i = 1; i <= 16; i++) {
            float angle = i * 37f * Mathf.Deg2Rad; // golden angle spiral
            float radius = i * 0.5f;
            Vector3 offset = (right * Mathf.Cos(angle) + fwd * Mathf.Sin(angle)) * radius;
            Vector3 candidate = (basePos + offset).normalized * (planetRadius + surfaceOffset);

            if (!Physics.CheckSphere(candidate, 0.8f, botLayer))
                return candidate;
        }

        return basePos;
    }

    void SnapToSurface() {
        Vector3 toSheep = transform.position;
        if (toSheep.sqrMagnitude < 0.0001f) toSheep = Vector3.up;
        transform.position = toSheep.normalized * (planetRadius + surfaceOffset);
    }

    void CheckForSheep() {
        // Raycast ahead to avoid walking into other sheep
        bool blocked = true;
        while (blocked) {
            if (Physics.Raycast(transform.position, _facingDirection, out hit, 2f, botLayer))
                PickNewWanderDirection();
            else
                blocked = false;
        }

        // Also push away if already overlapping — prevents stumbling into a drop zone
        Collider[] overlapping = Physics.OverlapSphere(transform.position, 0.8f, botLayer);
        foreach (Collider col in overlapping) {
            if (col.transform.parent.parent == transform) continue; // skip self
            Vector3 surfaceNormal = transform.position.normalized;
            Vector3 away = Vector3.ProjectOnPlane(
                (transform.position - col.transform.parent.parent.position).normalized,
                surfaceNormal
            ).normalized;
            if (away.sqrMagnitude > 0.0001f) {
                _facingDirection = away;
                _moveDirection = away;
            }
        }
    }

    void PickNewWanderDirection() {
        Vector3 surfaceNormal = transform.position.normalized;
        _moveDirection = Vector3.ProjectOnPlane(Random.onUnitSphere, surfaceNormal).normalized;
    }

    // --- PLACEMENT ---

    public void PlaceOnSphere(Vector3 worldPosition) {
        if (worldPosition.sqrMagnitude < 0.0001f) worldPosition = Vector3.up;
        transform.position = planet.position + worldPosition.normalized * (planetRadius + surfaceOffset);
        PickNewWanderDirection();
    }

    public void PlaceOnSphere(float latitudeDeg, float longitudeDeg) {
        float lat = latitudeDeg * Mathf.Deg2Rad;
        float lon = longitudeDeg * Mathf.Deg2Rad;
        Vector3 dir = new Vector3(
            Mathf.Cos(lat) * Mathf.Sin(lon),
            Mathf.Sin(lat),
            Mathf.Cos(lat) * Mathf.Cos(lon)
        );
        transform.position = dir * (planetRadius + surfaceOffset);
        PickNewWanderDirection();
    }

    // --- VISUALS ---

    public void FadeHighlight(float target) {
        if (currentlyFadingHighlight != null) StopCoroutine(currentlyFadingHighlight);
        currentlyFadingHighlight = StartCoroutine(BodyGlow(target));
    }

    IEnumerator BodyGlow(float targetIntensity) {
        Color baseColor = new Color(0.8803151f, 0.5257697f, 0f);
        Color currentEmissive = _bodyMat.GetColor("_EmissiveColor");
        float currentIntensity = currentEmissive.r / baseColor.r;
        float t = 0f;

        while (t < 1f) {
            t += Time.deltaTime * 5f;
            _bodyMat.SetColor("_EmissiveColor", baseColor * Mathf.Lerp(currentIntensity, targetIntensity, t));
            yield return null;
        }
    }

    public void FadeDropShadow(float target) {
        if (currentlyFadingDropShadow != null) StopCoroutine(currentlyFadingDropShadow);
        currentlyFadingDropShadow = StartCoroutine(DropShadow(target));
    }

    IEnumerator DropShadow(float targetIntensity) {
        float currentIntensity = _dropShadowMat.color.a;
        Color c = _dropShadowMat.color;
        float t = 0f;

        while (t < 1f) {
            t += Time.deltaTime * 5f;
            c.a = Mathf.Lerp(currentIntensity, targetIntensity, t);
            _dropShadowMat.color = c;
            _dropShadow.localScale = new Vector3(c.a * 5f, 0.01f, c.a * 5f);
            yield return null;
        }
    }

    void OnDrawGizmosSelected() {
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position, Vector3.zero);
        Gizmos.DrawWireSphere(transform.position, 0.15f);
    }
}