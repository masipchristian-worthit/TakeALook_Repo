using System.Collections;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class EnemyAIBase : MonoBehaviour
{
    private enum EnemyState { Idle, Patrolling, Chasing, Rushing, Attacking, Fleeing, Stunned, Dead }
    private EnemyState _state = EnemyState.Idle;

    #region Inspector
    [Header("AI Configuration")]
    [SerializeField] NavMeshAgent agent;
    [SerializeField] Transform target;
    [SerializeField] Animator anim;
    [SerializeField] LayerMask targetLayer;
    [SerializeField] LayerMask sightObstructionLayer = ~0;

    [Header("Patrol")]
    [SerializeField] float walkPointRange = 8f;
    [SerializeField] bool useWaypoints;
    [SerializeField] Transform[] waypoints;
    [SerializeField] float minWaitAtWaypointTime = 2f;
    [SerializeField] float maxWaitAtWaypointTime = 5f;
    [SerializeField, Range(0f, 1f)] float chanceToSkipWait = 0.25f;

    [Header("Idle Cycle")]
    [SerializeField, Range(0f, 1f)] float chanceToIdleAfterWaypoint = 0.35f;
    [SerializeField] float minIdleTime = 1.5f;
    [SerializeField] float maxIdleTime = 4f;

    [Header("Chase Delay")]
    [SerializeField] float minChaseDelay = 0.4f;
    [SerializeField] float maxChaseDelay = 1.0f;

    [Header("Attack")]
    [SerializeField] GameObject projectilePrefab;
    [SerializeField] Transform shootPoint;
    [SerializeField] float shootSpeed = 18f;
    [SerializeField] float minTimeBetweenAttacks = 3f;
    [SerializeField] float maxTimeBetweenAttacks = 6f;
    [SerializeField] float roarTotalDuration = 2.5f;
    [SerializeField] float hitStunSafetyTimeout = 2.5f;

    [Header("Detection")]
    [SerializeField] float sightRange = 12f;
    [SerializeField] float sightFovDegrees = 110f;
    [SerializeField] float eyeHeight = 1.5f;
    [SerializeField] float attackRange = 6f;
    [SerializeField] Color visionGizmoColor = new Color(1f, 0f, 0f, 0.25f);

    [Header("Movement Speeds")]
    [SerializeField] float patrolSpeed = 1.5f;
    [SerializeField] float chaseSpeed = 3.5f;
    [SerializeField] float rushSpeed = 5.5f;
    [SerializeField] float fleeSpeed = 5f;

    [Header("Rush")]
    [SerializeField] float rushAttackRange = 4f;
    [SerializeField] float rushPersistTime = 3.0f;

    [Header("Flee Conditions")]
    [SerializeField, Range(0f, 1f)] float lowHealthThreshold = 0.25f;
    [SerializeField] float fleeMinDistance = 7f;
    [SerializeField] float fleeDistance = 15f;

    [Header("Stuck Detection")]
    [SerializeField] float stuckCheckTime = 2f;
    [SerializeField] float stuckThreshold = 0.1f;
    [SerializeField] float maxStuckDuration = 3f;

    [Header("Audio")]
    [SerializeField] string sfxRoarId = "enemy_roar";
    [SerializeField] string sfxFootstepId = "enemy_step";
    [SerializeField] string sfxAttackShootId = "enemy_shoot";
    [SerializeField] string sfxAlertId = "enemy_alert";
    #endregion

    Vector3 walkPoint; bool walkPointSet; int currentWaypointIndex;
    bool isWaitingAtWaypoint; float waitTimer;
    bool isInIdle; float idleTimer;
    bool isWaitingBeforeChase; float chaseDelayTimer;
    bool alreadyAttacked; bool isInvulnerable; bool _alertedThisSighting;
    bool _roarFinishedEvent;
    bool _hitForwardFinishedEvent; bool _hitRecoveryFinishedEvent;
    bool _wasAlertedByShot;
    float stuckTimer; float lastCheckTime; Vector3 lastPosition; float rushUntilTime;

    PlayerHealth _playerHealth; GunSystem _playerGunSystem;
    FPS_Controller _playerController; EnemyHealth _enemyHealth;

    static readonly int H_IsPatrolling = Animator.StringToHash("isPatrolling");
    static readonly int H_IsChasing = Animator.StringToHash("isChasing");
    static readonly int H_IsAttacking = Animator.StringToHash("isAttacking");
    static readonly int H_Roar = Animator.StringToHash("Roar");
    static readonly int H_Shot = Animator.StringToHash("Shot");
    static readonly int H_ShotRecover = Animator.StringToHash("ShotRecover");
    static readonly int H_Death = Animator.StringToHash("Death");
    static readonly int H_Electrocuted = Animator.StringToHash("Electrocuted");

    public bool IsInvulnerable => isInvulnerable;
    public bool IsDead => _state == EnemyState.Dead;

    private void Awake()
    {
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            target = playerObj.transform;
            _playerHealth = playerObj.GetComponentInChildren<PlayerHealth>();
            _playerGunSystem = playerObj.GetComponentInChildren<GunSystem>();
            _playerController = playerObj.GetComponentInChildren<FPS_Controller>();
            if (_playerGunSystem != null) _playerGunSystem.OnDryFireAttempt += HandleDryFireAttempt;
        }

        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (anim == null) anim = GetComponentInChildren<Animator>();
        _enemyHealth = GetComponent<EnemyHealth>();

        // Asegura que el GameObject del Animator pueda recibir eventos de animación
        // (footstep, roar, hit, death). Sin esto, los AnimationEvent fallan con
        // "has no receiver" cuando el Animator vive en un hijo distinto al del EnemyAIBase.
        if (anim != null && anim.gameObject != gameObject
            && anim.GetComponent<AnimationEventForwarder>() == null
            && anim.GetComponent<EnemyAIBase>() == null)
        {
            anim.gameObject.AddComponent<AnimationEventForwarder>();
        }

        if (agent != null)
        {
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            agent.autoTraverseOffMeshLink = true; agent.updateRotation = true;
        }
        lastPosition = transform.position; lastCheckTime = Time.time;
    }

    private void OnDestroy()
    {
        if (_playerGunSystem != null) _playerGunSystem.OnDryFireAttempt -= HandleDryFireAttempt;
    }

    private void Start()
    {
        if (useWaypoints && waypoints != null && waypoints.Length > 0) currentWaypointIndex = Random.Range(0, waypoints.Length);
        if (agent != null) agent.speed = patrolSpeed;
        ChangeState(EnemyState.Patrolling);
    }

    void Update()
    {
        if (_state == EnemyState.Stunned || _state == EnemyState.Dead) return;
        EvaluateState(); CheckIfStuck();
    }

    void EvaluateState()
    {
        if (_state == EnemyState.Attacking && alreadyAttacked) { FaceTarget(); return; }

        bool inSight = CanSeeTarget();
        float dist = target != null ? Vector3.Distance(transform.position, target.position) : float.MaxValue;
        bool rushActive = inSight && Time.time < rushUntilTime;

        if (inSight && !_alertedThisSighting) { _alertedThisSighting = true; AudioManager.Instance?.PlaySFX(sfxAlertId, transform.position); }
        else if (!inSight) _alertedThisSighting = false;

        EnemyState next;
        if (!inSight) next = (_state == EnemyState.Idle || _state == EnemyState.Patrolling) ? _state : EnemyState.Patrolling;
        else if (ShouldFlee(dist)) next = EnemyState.Fleeing;
        else if (rushActive) next = (dist <= rushAttackRange) ? EnemyState.Attacking : EnemyState.Rushing;
        else if (dist <= attackRange) next = EnemyState.Attacking;
        else next = EnemyState.Chasing;

        ChangeState(next);

        switch (_state)
        {
            case EnemyState.Idle: DoIdle(); break;
            case EnemyState.Patrolling: DoPatrol(); break;
            case EnemyState.Chasing: DoChase(); break;
            case EnemyState.Rushing: DoRush(); break;
            case EnemyState.Attacking: DoAttack(); break;
            case EnemyState.Fleeing: DoFlee(); break;
        }
    }

    void ChangeState(EnemyState next)
    {
        if (_state == next) return;
        if (_state == EnemyState.Chasing) { isWaitingBeforeChase = false; chaseDelayTimer = 0f; }
        if (_state == EnemyState.Idle) { isInIdle = false; idleTimer = 0f; }
        if (_state == EnemyState.Patrolling) isWaitingAtWaypoint = false;
        _state = next;
    }

    bool ShouldFlee(float distToPlayer) => _enemyHealth != null && _enemyHealth.HealthPercent <= lowHealthThreshold && distToPlayer >= fleeMinDistance;
    void HandleDryFireAttempt() { if (CanSeeTarget()) rushUntilTime = Time.time + rushPersistTime; }

    void DoIdle()
    {
        if (agent != null && agent.isOnNavMesh) agent.ResetPath();
        SetAnimator(false, false, false);
        if (!isInIdle) { isInIdle = true; idleTimer = Random.Range(minIdleTime, maxIdleTime); }
        idleTimer -= Time.deltaTime;
        if (idleTimer <= 0f) { isInIdle = false; ChangeState(EnemyState.Patrolling); }
    }

    void DoPatrol()
    {
        if (agent == null || !agent.isOnNavMesh) return;
        agent.speed = patrolSpeed; SetAnimator(true, false, false);

        if (isWaitingAtWaypoint)
        {
            waitTimer -= Time.deltaTime;
            if (waitTimer <= 0f)
            {
                isWaitingAtWaypoint = false; walkPointSet = false;
                if (Random.value < chanceToIdleAfterWaypoint) { ChangeState(EnemyState.Idle); return; }
                if (useWaypoints && waypoints != null && waypoints.Length > 0) SetNextWaypoint();
            }
            return;
        }
        if (!walkPointSet)
        {
            if (useWaypoints && waypoints != null && waypoints.Length > 0) { walkPoint = waypoints[currentWaypointIndex].position; walkPointSet = true; }
            else SearchWalkPoint();
            if (!walkPointSet) return;
        }

        agent.SetDestination(walkPoint);
        if (!agent.pathPending && agent.remainingDistance <= 0.5f)
        {
            agent.ResetPath(); walkPointSet = false;
            bool skip = Random.value < chanceToSkipWait;
            if (skip && useWaypoints && waypoints != null && waypoints.Length > 0) SetNextWaypoint();
            else { isWaitingAtWaypoint = true; waitTimer = Random.Range(minWaitAtWaypointTime, maxWaitAtWaypointTime); }
        }
    }

    void DoChase()
    {
        if (agent == null || !agent.isOnNavMesh || target == null) return;
        agent.speed = chaseSpeed;
        if (!isWaitingBeforeChase) { isWaitingBeforeChase = true; chaseDelayTimer = Random.Range(minChaseDelay, maxChaseDelay); agent.ResetPath(); SetAnimator(false, false, false); return; }
        if (chaseDelayTimer > 0f) { chaseDelayTimer -= Time.deltaTime; return; }
        SetAnimator(false, true, false); agent.SetDestination(target.position);
    }

    void DoRush() { if (agent == null || !agent.isOnNavMesh || target == null) return; agent.speed = rushSpeed; SetAnimator(false, true, false); agent.SetDestination(target.position); }
    void DoAttack() { if (agent == null || !agent.isOnNavMesh) return; agent.ResetPath(); FaceTarget(); if (!alreadyAttacked) _attackRoutine = StartCoroutine(AttackCoroutine()); }
    void DoFlee()
    {
        if (agent == null || !agent.isOnNavMesh || target == null) return;
        agent.speed = fleeSpeed; SetAnimator(false, true, false);
        Vector3 fleeDir = (transform.position - target.position).normalized; Vector3 fleeTarget = transform.position + fleeDir * fleeDistance;
        if (NavMesh.SamplePosition(fleeTarget, out NavMeshHit hit, 5f, NavMesh.AllAreas)) agent.SetDestination(hit.position);
    }

    Coroutine _attackRoutine;
    IEnumerator AttackCoroutine()
    {
        alreadyAttacked = true;
        _roarFinishedEvent = false;

        // Detener movimiento COMPLETAMENTE durante el roar
        if (agent != null && agent.isOnNavMesh)
        {
            agent.ResetPath();
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
        }

        // Quitar animaciones de movimiento (no debe moverse sin patrol ni chase)
        SetAnimator(false, false, false);

        // Triggerear roar
        if (anim != null)
        {
            anim.ResetTrigger(H_Roar);
            anim.SetTrigger(H_Roar);
        }
        AudioManager.Instance?.PlaySFX(sfxRoarId, transform.position);

        // Disparar el proyectil a mitad de la duración del roar
        yield return new WaitForSeconds(roarTotalDuration * 0.5f);
        FireProjectile();

        // Esperar a que termine la animación de roar
        float t = roarTotalDuration * 0.5f;
        while (t < roarTotalDuration + 1f)
        {
            if (_state == EnemyState.Dead || _state == EnemyState.Stunned)
            {
                if (agent != null && agent.isOnNavMesh) agent.isStopped = false;
                alreadyAttacked = false;
                _attackRoutine = null;
                yield break;
            }
            // Forzar quietud absoluta durante el roar
            if (agent != null && agent.isOnNavMesh) agent.velocity = Vector3.zero;
            FaceTarget();
            t += Time.deltaTime;
            yield return null;
        }

        // Reanudar capacidad de movimiento al terminar el roar
        if (agent != null && agent.isOnNavMesh) agent.isStopped = false;

        // Cooldown después del ataque
        float cooldown = Random.Range(minTimeBetweenAttacks, maxTimeBetweenAttacks);
        if (cooldown > 0f) yield return new WaitForSeconds(cooldown);

        // Reposicionarse un poco si sigue en rango (sabe dónde está el player => isChasing + chaseSpeed)
        float dist = target != null ? Vector3.Distance(transform.position, target.position) : float.MaxValue;
        if (dist <= attackRange * 1.3f && agent != null && agent.isOnNavMesh)
        {
            Vector3 dodgeDir = Random.onUnitSphere;
            dodgeDir.y = 0;
            Vector3 newPos = transform.position + dodgeDir.normalized * 2f;

            if (NavMesh.SamplePosition(newPos, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                agent.speed = chaseSpeed;
                SetAnimator(false, true, false);
                agent.SetDestination(hit.position);
            }

            yield return new WaitForSeconds(0.5f);
        }

        alreadyAttacked = false; _attackRoutine = null;
    }

    void FireProjectile()
    {
        if (projectilePrefab == null || shootPoint == null) return;
        Vector3 aimPoint = target.position + Vector3.up * 1.0f; Vector3 dir = (aimPoint - shootPoint.position).normalized;
        GameObject proj = Instantiate(projectilePrefab, shootPoint.position, Quaternion.LookRotation(dir));
        if (!proj.activeSelf) proj.SetActive(true);

        Collider[] selfCols = GetComponentsInChildren<Collider>(); Collider[] projCols = proj.GetComponentsInChildren<Collider>();
        for (int i = 0; i < selfCols.Length; i++) for (int j = 0; j < projCols.Length; j++) if (selfCols[i] != null && projCols[j] != null) Physics.IgnoreCollision(selfCols[i], projCols[j], true);

        Rigidbody rb = proj.GetComponent<Rigidbody>();
        if (rb != null) { rb.useGravity = false; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; rb.linearVelocity = dir * shootSpeed; }
        EnemyProjectile epr = proj.GetComponent<EnemyProjectile>(); if (epr != null) epr.Launch(dir * shootSpeed);
        AudioManager.Instance?.PlaySFX(sfxAttackShootId, shootPoint.position);
    }

    // Llamado por animation event en el frame exacto del disparo dentro del Roar
    public void OnRoarAttackFrameEvent()
    {
        FireProjectile();
    }

    // Llamado por animation event en el último frame del Roar
    public void OnRoarFinishedAnimationEvent()
    {
        _roarFinishedEvent = true;
    }

    public void OnHitForwardFinishedAnimationEvent()
    {
        _hitForwardFinishedEvent = true;
        if (_state == EnemyState.Stunned && anim != null)
        {
            anim.ResetTrigger(H_Shot);
            anim.ResetTrigger(H_ShotRecover);
            anim.SetTrigger(H_ShotRecover);
        }
    }

    public void OnHitRecoveryFinishedAnimationEvent() => _hitRecoveryFinishedEvent = true;
    public void OnFootstepAnimationEvent() => AudioManager.Instance?.PlaySFX(sfxFootstepId, transform.position);

    public void OnHitByNormalBullet() { if (isInvulnerable || _state == EnemyState.Dead) return; _wasAlertedByShot = true; if (_attackRoutine != null) StopCoroutine(_attackRoutine); _attackRoutine = null; StartCoroutine(HitStunRoutine()); }

    IEnumerator HitStunRoutine()
    {
        _state = EnemyState.Stunned; isInvulnerable = true; alreadyAttacked = false;
        _hitForwardFinishedEvent = false;
        _hitRecoveryFinishedEvent = false;

        if (agent != null && agent.isOnNavMesh) agent.ResetPath();
        SetAnimator(false, false, false);

        if (anim != null) { anim.ResetTrigger(H_Roar); anim.ResetTrigger(H_ShotRecover); anim.ResetTrigger(H_Shot); anim.SetTrigger(H_Shot); }

        // Fase 1: esperar que termine Beeing_Shot_First
        float t = 0f;
        while (!_hitForwardFinishedEvent && t < hitStunSafetyTimeout)
        {
            if (_state == EnemyState.Dead) yield break;
            t += Time.deltaTime; yield return null;
        }

        // Safety: si el animation event no llegó, disparamos ShotRecover nosotros
        if (!_hitForwardFinishedEvent && anim != null)
        {
            anim.ResetTrigger(H_Shot); anim.ResetTrigger(H_ShotRecover); anim.SetTrigger(H_ShotRecover);
        }

        // Fase 2: esperar que termine Beeing_Shot_Recover
        t = 0f;
        while (!_hitRecoveryFinishedEvent && t < hitStunSafetyTimeout)
        {
            if (_state == EnemyState.Dead) yield break;
            t += Time.deltaTime; yield return null;
        }

        if (anim != null) { anim.ResetTrigger(H_Shot); anim.ResetTrigger(H_ShotRecover); SetAnimator(false, false, false); }

        bool shouldChase = _wasAlertedByShot && target != null;
        _wasAlertedByShot = false;

        if (shouldChase)
        {
            // Si el jugador está a espaldas del enemigo, gira rápido antes de moverse
            Vector3 toPlayer = target.position - transform.position; toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(toPlayer.normalized);
                float elapsed = 0f;
                while (elapsed < 0.45f)
                {
                    if (_state == EnemyState.Dead) yield break;
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, 600f * Time.deltaTime);
                    elapsed += Time.deltaTime;
                    yield return null;
                }
            }
            rushUntilTime = Time.time + rushPersistTime;
            _alertedThisSighting = false;
            isInvulnerable = false;
            _state = EnemyState.Chasing;
        }
        else
        {
            isInvulnerable = false;
            _state = EnemyState.Patrolling;
        }
    }

    public void OnKilledByNormalBullet() { StopAllCoroutines(); _attackRoutine = null; StartCoroutine(DeathSequence(H_Death)); }
    public void OnKilledByBullBullet() { StopAllCoroutines(); _attackRoutine = null; StartCoroutine(DeathSequence(H_Electrocuted)); }

    IEnumerator DeathSequence(int triggerHash)
    {
        _state = EnemyState.Dead; isInvulnerable = true;

        if (agent != null)
        {
            if (agent.isOnNavMesh) { agent.ResetPath(); agent.isStopped = true; }
            agent.updatePosition = false; agent.updateRotation = false; agent.enabled = false;
        }

        var rb = GetComponent<Rigidbody>();
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.isKinematic = true; }

        Collider[] cols = GetComponentsInChildren<Collider>();
        for (int i = 0; i < cols.Length; i++) if (cols[i] != null) cols[i].enabled = false;

        if (anim != null)
        {
            anim.applyRootMotion = false;
            SetAnimator(false, false, false);
            anim.ResetTrigger(triggerHash); anim.SetTrigger(triggerHash);
        }

        yield break;
    }

    public void OnDeathEvent()
    {
        _enemyHealth?.MarkAsCorpse();
    }

    bool CanSeeTarget()
    {
        if (target == null) return false;
        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        float playerHeadOffset = (_playerController != null) ? _playerController.CurrentHeadHeight : 1.0f;
        Vector3 targetCenter = target.position + Vector3.up * playerHeadOffset;

        float dist = Vector3.Distance(origin, targetCenter); if (dist > sightRange) return false;
        Vector3 dir = (targetCenter - origin).normalized; float angle = Vector3.Angle(transform.forward, dir); if (angle > sightFovDegrees * 0.5f) return false;
        if (Physics.Raycast(origin, dir, out RaycastHit hit, dist, sightObstructionLayer)) if (hit.transform != target && !hit.transform.IsChildOf(target)) return false;
        Collider[] hits = Physics.OverlapSphere(origin, sightRange, targetLayer);
        for (int i = 0; i < hits.Length; i++) if (hits[i].transform == target || hits[i].transform.IsChildOf(target)) return true;
        return true;
    }

    void FaceTarget()
    {
        if (target == null) return;
        Vector3 dir = (target.position - transform.position).normalized; dir.y = 0;
        if (dir != Vector3.zero) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), agent.angularSpeed * Time.deltaTime);
    }

    void SetAnimator(bool patrolling, bool chasing, bool attacking) { if (anim == null) return; anim.SetBool(H_IsPatrolling, patrolling); anim.SetBool(H_IsChasing, chasing); anim.SetBool(H_IsAttacking, attacking); }
    void SearchWalkPoint() { for (int i = 0; i < 5; i++) { Vector3 candidate = transform.position + new Vector3(Random.Range(-walkPointRange, walkPointRange), 0, Random.Range(-walkPointRange, walkPointRange)); if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, NavMesh.AllAreas)) { walkPoint = hit.position; walkPointSet = true; return; } } }
    void SetNextWaypoint() { if (waypoints == null || waypoints.Length == 0) return; int next = Random.Range(0, waypoints.Length); while (waypoints.Length > 1 && next == currentWaypointIndex) next = Random.Range(0, waypoints.Length); currentWaypointIndex = next; walkPoint = waypoints[currentWaypointIndex].position; walkPointSet = true; }
    void CheckIfStuck()
    {
        if (agent == null || !agent.isOnNavMesh) return;
        if (Time.time - lastCheckTime > stuckCheckTime)
        {
            float moved = Vector3.Distance(transform.position, lastPosition);
            stuckTimer = (moved < stuckThreshold && agent.hasPath) ? stuckTimer + stuckCheckTime : 0f;
            if (stuckTimer >= maxStuckDuration) { walkPointSet = false; isWaitingAtWaypoint = true; waitTimer = Random.Range(minWaitAtWaypointTime, maxWaitAtWaypointTime); isWaitingBeforeChase = false; chaseDelayTimer = 0f; agent.ResetPath(); SetAnimator(false, false, false); stuckTimer = 0f; }
            lastPosition = transform.position; lastCheckTime = Time.time;
        }
    }
}