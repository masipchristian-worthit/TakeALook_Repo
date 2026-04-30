using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class EnemyAIBase : MonoBehaviour
{
    private enum EnemyState { Patrolling, Chasing, Rushing, Attacking, Hiding, Stunned, Dead }
    private EnemyState _state = EnemyState.Patrolling;

    #region Inspector
    [Header("AI Configuration")]
    [SerializeField] NavMeshAgent agent;
    [SerializeField] Transform target;
    [SerializeField] Animator anim;
    [SerializeField] LayerMask targetLayer;

    [Header("Patrol")]
    [SerializeField] float walkPointRange = 8f;
    [SerializeField] bool useWaypoints;
    [SerializeField] Transform[] waypoints;
    [SerializeField] float minWaitAtWaypointTime = 3f;
    [SerializeField] float maxWaitAtWaypointTime = 6f;
    [SerializeField, Range(0f, 1f)] float chanceToSkipWait = 0.25f;

    [Header("Chase Delay")]
    [SerializeField] float minChaseDelay = 0.5f;
    [SerializeField] float maxChaseDelay = 1.2f;

    [Header("Attack")]
    [SerializeField] GameObject projectilePrefab;
    [SerializeField] Transform shootPoint;
    [SerializeField] float shootSpeed = 15f;
    [SerializeField] float minTimeBetweenAttacks = 3f;
    [SerializeField] float maxTimeBetweenAttacks = 6f;
    [Tooltip("Timeout de seguridad si el Animation Event de disparo nunca llega.")]
    [SerializeField] float roarShootDelay = 1.83f;
    [Tooltip("Timeout de seguridad si el Animation Event de fin de Roar nunca llega.")]
    [SerializeField] float roarTotalDuration = 2.5f;
    [Tooltip("Timeout de seguridad para la animación Being_Shot (forward + recovery).")]
    [SerializeField] float hitStunSafetyTimeout = 2.5f;

    [Header("Detection")]
    [SerializeField] float sightRange = 8f;
    [SerializeField] float eyeHeight = 1.5f;
    [SerializeField] float attackRange = 6f;
    [SerializeField] Color visionGizmoColor = new Color(1f, 0f, 0f, 0.25f);

    [Header("Movement Speeds")]
    [SerializeField] float patrolSpeed = 1.5f;
    [SerializeField] float chaseSpeed = 3.5f;
    [SerializeField] float rushSpeed = 5.5f;
    [SerializeField] float fleeSpeed = 5f;

    [Header("Rush (Player Out Of Ammo)")]
    [Tooltip("Distancia bajo la cual el rusher pasa directamente a atacar.")]
    [SerializeField] float rushAttackRange = 4f;
    [Tooltip("Tiempo extra que sigue rusheando tras detectar mag vacío, por si el jugador recarga rápido.")]
    [SerializeField] float rushPersistTime = 1.5f;

    [Header("Hide Conditions")]
    [Tooltip("HP por debajo del cual el enemigo intentará huir.")]
    [SerializeField, Range(0f, 1f)] float lowHealthThreshold = 0.2f;
    [SerializeField] float fleeDistance = 15f;

    [Header("Stuck Detection")]
    [SerializeField] float stuckCheckTime = 2f;
    [SerializeField] float stuckThreshold = 0.1f;
    [SerializeField] float maxStuckDuration = 3f;
    #endregion

    // Internal state
    Vector3 walkPoint;
    bool walkPointSet;
    int currentWaypointIndex;
    bool isWaitingAtWaypoint;
    float waitTimer;
    bool isWaitingBeforeChase;
    float chaseDelayTimer;
    bool alreadyAttacked;
    bool isInvulnerable;

    // Flags driven from Animation Events on the enemy clips.
    bool _shootEventFired;
    bool _roarFinishedEvent;
    bool _hitForwardFinishedEvent;
    bool _hitRecoveryFinishedEvent;
    float stuckTimer;
    float lastCheckTime;
    Vector3 lastPosition;
    float rushUntilTime;

    // Component refs
    PlayerHealth _playerHealth;
    GunSystem _playerGunSystem;
    EnemyHealth _enemyHealth;

    // Animator hashes
    static readonly int H_IsPatrolling = Animator.StringToHash("isPatrolling");
    static readonly int H_IsChasing    = Animator.StringToHash("isChasing");
    static readonly int H_IsAttacking  = Animator.StringToHash("isAttacking");
    static readonly int H_Roar         = Animator.StringToHash("Roar");
    static readonly int H_Shot         = Animator.StringToHash("Shot");
    static readonly int H_Death        = Animator.StringToHash("Death");
    static readonly int H_Electrocuted = Animator.StringToHash("Electrocuted");

    // Public API
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
        }

        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (anim == null) anim = GetComponentInChildren<Animator>();
        _enemyHealth = GetComponent<EnemyHealth>();

        lastPosition = transform.position;
        lastCheckTime = Time.time;
    }

    private void Start()
    {
        if (useWaypoints && waypoints != null && waypoints.Length > 0)
            currentWaypointIndex = Random.Range(0, waypoints.Length);
        if (agent != null) agent.speed = patrolSpeed;
    }

    void Update()
    {
        if (_state == EnemyState.Stunned || _state == EnemyState.Dead) return;
        EvaluateState();
        CheckIfStuck();
    }

    #region State Machine
    void EvaluateState()
    {
        // Si estamos atacando, terminamos el ciclo (Roar + cooldown) antes de reevaluar.
        // Evita que el Roar se cancele a medias y que el Animator entre/salga del estado en bucle.
        if (_state == EnemyState.Attacking && alreadyAttacked)
        {
            FaceTarget();
            return;
        }

        bool inSight = CanSeeTarget();
        float dist = target != null ? Vector3.Distance(transform.position, target.position) : float.MaxValue;

        // Refrescar persistencia de rush si el jugador está sin balas
        if (IsPlayerOutOfAmmo()) rushUntilTime = Time.time + rushPersistTime;
        bool rushActive = inSight && Time.time < rushUntilTime;

        EnemyState next;
        if (!inSight)
        {
            next = EnemyState.Patrolling;
        }
        else if (rushActive)
        {
            // Si está pegado al jugador y vacío, ataca igualmente
            next = (dist <= rushAttackRange) ? EnemyState.Attacking : EnemyState.Rushing;
        }
        else if (dist <= attackRange)
        {
            next = EnemyState.Attacking;
        }
        else if (ShouldHide())
        {
            next = EnemyState.Hiding;
        }
        else
        {
            next = EnemyState.Chasing;
        }

        ChangeState(next);

        switch (_state)
        {
            case EnemyState.Patrolling: DoPatrol(); break;
            case EnemyState.Chasing:    DoChase();  break;
            case EnemyState.Rushing:    DoRush();   break;
            case EnemyState.Attacking:  DoAttack(); break;
            case EnemyState.Hiding:     DoFlee();   break;
        }
    }

    void ChangeState(EnemyState next)
    {
        if (_state == next) return;

        if (next != EnemyState.Chasing)
        {
            isWaitingBeforeChase = false;
            chaseDelayTimer = 0f;
        }

        _state = next;
    }

    bool ShouldHide()
    {
        // Solo huyen si están realmente al borde de la muerte.
        return _enemyHealth != null && _enemyHealth.HealthPercent <= lowHealthThreshold;
    }

    bool IsPlayerOutOfAmmo()
    {
        return _playerGunSystem != null && _playerGunSystem.IsCurrentMagEmpty;
    }
    #endregion

    #region State Behaviors
    void DoPatrol()
    {
        if (!agent.isOnNavMesh) return;
        agent.speed = patrolSpeed;
        SetAnimator(true, false, false);

        if (isWaitingAtWaypoint)
        {
            waitTimer -= Time.deltaTime;
            if (waitTimer <= 0f)
            {
                isWaitingAtWaypoint = false;
                walkPointSet = false;
                if (useWaypoints && waypoints != null && waypoints.Length > 0) SetNextWaypoint();
            }
            return;
        }

        if (!walkPointSet)
        {
            if (useWaypoints && waypoints != null && waypoints.Length > 0)
            {
                walkPoint = waypoints[currentWaypointIndex].position;
                walkPointSet = true;
            }
            else SearchWalkPoint();

            if (!walkPointSet) return;
        }

        agent.SetDestination(walkPoint);

        if (!agent.pathPending && agent.remainingDistance <= 0.5f)
        {
            agent.ResetPath();
            walkPointSet = false;
            bool skip = Random.value < chanceToSkipWait;
            if (skip && useWaypoints && waypoints != null && waypoints.Length > 0)
                SetNextWaypoint();
            else
            {
                isWaitingAtWaypoint = true;
                waitTimer = Random.Range(minWaitAtWaypointTime, maxWaitAtWaypointTime);
            }
        }
    }

    void DoChase()
    {
        if (!agent.isOnNavMesh || target == null) return;
        agent.speed = chaseSpeed;

        if (!isWaitingBeforeChase)
        {
            isWaitingBeforeChase = true;
            chaseDelayTimer = Random.Range(minChaseDelay, maxChaseDelay);
            agent.ResetPath();
            SetAnimator(false, false, false);
            return;
        }

        if (chaseDelayTimer > 0f)
        {
            chaseDelayTimer -= Time.deltaTime;
            agent.ResetPath();
            SetAnimator(false, false, false);
            return;
        }

        SetAnimator(false, true, false);
        agent.SetDestination(target.position);
    }

    void DoRush()
    {
        if (!agent.isOnNavMesh || target == null) return;
        agent.speed = rushSpeed;
        SetAnimator(false, true, false);
        agent.SetDestination(target.position);
    }

    void DoAttack()
    {
        if (!agent.isOnNavMesh) return;
        agent.ResetPath();
        FaceTarget();

        if (!alreadyAttacked)
        {
            _attackRoutine = StartCoroutine(AttackCoroutine());
        }
    }

    void DoFlee()
    {
        if (!agent.isOnNavMesh || target == null) return;
        agent.speed = fleeSpeed;
        // Animación de correr para huir
        SetAnimator(false, true, false);

        Vector3 fleeDir = (transform.position - target.position).normalized;
        Vector3 fleeTarget = transform.position + fleeDir * fleeDistance;
        if (NavMesh.SamplePosition(fleeTarget, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            agent.SetDestination(hit.position);
    }

    Coroutine _attackRoutine;

    IEnumerator AttackCoroutine()
    {
        alreadyAttacked = true;
        _shootEventFired = false;
        _roarFinishedEvent = false;

        // Trigger Roar. El bool isAttacking se mantiene SOLO mientras se reproduce el clip,
        // así el Animator no puede relanzarse en bucle si el trigger ya fue consumido.
        SetAnimator(false, false, true);
        if (anim != null) { anim.ResetTrigger(H_Roar); anim.SetTrigger(H_Roar); }

        // Esperar al Animation Event de disparo (con timeout de seguridad)
        float t = 0f;
        while (!_shootEventFired && t < roarShootDelay + 1f)
        {
            if (_state == EnemyState.Dead || _state == EnemyState.Stunned) { _attackRoutine = null; yield break; }
            t += Time.deltaTime;
            yield return null;
        }
        if (!_shootEventFired) FireProjectile(); // fallback si no hay evento

        // Esperar al Animation Event de fin de Roar (con timeout de seguridad)
        t = 0f;
        float remainingTimeout = Mathf.Max(0f, roarTotalDuration - roarShootDelay) + 1f;
        while (!_roarFinishedEvent && t < remainingTimeout)
        {
            if (_state == EnemyState.Dead || _state == EnemyState.Stunned) { _attackRoutine = null; yield break; }
            t += Time.deltaTime;
            yield return null;
        }

        // Liberar el bool DESPUÉS de que el clip haya terminado de reproducirse
        if (anim != null) anim.SetBool(H_IsAttacking, false);

        // Cooldown entre ataques
        float cooldown = Random.Range(minTimeBetweenAttacks, maxTimeBetweenAttacks);
        if (cooldown > 0f) yield return new WaitForSeconds(cooldown);

        alreadyAttacked = false;
        _attackRoutine = null;
    }
    #endregion

    #region Combat / Animation Events
    void FireProjectile()
    {
        if (projectilePrefab == null || shootPoint == null || target == null) return;
        Vector3 dir = (target.position + Vector3.up * 1f - shootPoint.position).normalized;
        GameObject proj = Instantiate(projectilePrefab, shootPoint.position, Quaternion.LookRotation(dir));
        Rigidbody rb = proj.GetComponent<Rigidbody>();
        if (rb != null) rb.linearVelocity = dir * shootSpeed;
    }

    // ============================================================
    // ANIMATION EVENT HOOKS — colgar estos métodos en los clips:
    //
    //  Roar.anim:
    //   - frame del disparo  -> OnShootAnimationEvent()
    //   - último frame       -> OnRoarFinishedAnimationEvent()
    //
    //  Being_Shot.anim (forward):
    //   - último frame       -> OnHitForwardFinishedAnimationEvent()
    //
    //  Being_Shot_Recovery.anim (la animación "negativa" que vuelve a Idle):
    //   - último frame       -> OnHitRecoveryFinishedAnimationEvent()
    // ============================================================
    public void OnShootAnimationEvent()
    {
        _shootEventFired = true;
        FireProjectile();
    }
    public void OnRoarFinishedAnimationEvent()        => _roarFinishedEvent = true;
    public void OnHitForwardFinishedAnimationEvent()  => _hitForwardFinishedEvent = true;
    public void OnHitRecoveryFinishedAnimationEvent() => _hitRecoveryFinishedEvent = true;

    public void OnHitByNormalBullet()
    {
        if (isInvulnerable || _state == EnemyState.Dead) return;
        StopAllCoroutines();
        _attackRoutine = null;
        StartCoroutine(HitStunRoutine());
    }

    IEnumerator HitStunRoutine()
    {
        _state = EnemyState.Stunned;
        isInvulnerable = true;
        alreadyAttacked = false;
        _hitForwardFinishedEvent = false;
        _hitRecoveryFinishedEvent = false;

        if (agent != null && agent.isOnNavMesh) agent.ResetPath();
        SetAnimator(false, false, false);

        if (anim != null)
        {
            anim.ResetTrigger(H_Roar);
            anim.ResetTrigger(H_Shot);
            anim.SetTrigger(H_Shot);
        }

        // Forward: esperar al Animation Event del último frame del clip Being_Shot
        float t = 0f;
        while (!_hitForwardFinishedEvent && t < hitStunSafetyTimeout)
        {
            t += Time.deltaTime;
            yield return null;
        }

        // Recovery: esperar al Animation Event del último frame del clip "shoot negativo" que devuelve a Idle
        t = 0f;
        while (!_hitRecoveryFinishedEvent && t < hitStunSafetyTimeout)
        {
            t += Time.deltaTime;
            yield return null;
        }

        // Forzar pose neutra por si los eventos no llegaron a tiempo
        if (anim != null)
        {
            anim.ResetTrigger(H_Shot);
            anim.SetBool(H_IsPatrolling, false);
            anim.SetBool(H_IsChasing, false);
            anim.SetBool(H_IsAttacking, false);
        }

        isInvulnerable = false;
        _state = EnemyState.Patrolling;
    }

    public void OnKilledByNormalBullet()
    {
        StopAllCoroutines();
        _attackRoutine = null;
        StartCoroutine(DeathSequence(H_Death));
    }

    public void OnKilledByBullBullet()
    {
        StopAllCoroutines();
        _attackRoutine = null;
        StartCoroutine(DeathSequence(H_Electrocuted));
    }

    IEnumerator DeathSequence(int triggerHash)
    {
        _state = EnemyState.Dead;
        isInvulnerable = true;
        if (agent != null) agent.enabled = false;

        Collider[] cols = GetComponentsInChildren<Collider>();
        for (int i = 0; i < cols.Length; i++) cols[i].enabled = false;

        if (anim != null)
        {
            // Limpiar triggers que pudieran reactivarse
            anim.ResetTrigger(H_Roar);
            anim.ResetTrigger(H_Shot);
            anim.SetBool(H_IsPatrolling, false);
            anim.SetBool(H_IsChasing, false);
            anim.SetBool(H_IsAttacking, false);
            anim.ResetTrigger(triggerHash);
            anim.SetTrigger(triggerHash);
        }

        yield return null;
        if (anim != null)
            while (anim.IsInTransition(0)) yield return null;

        float len = 1.5f;
        if (anim != null)
        {
            AnimatorStateInfo info = anim.GetCurrentAnimatorStateInfo(0);
            len = info.length > 0.1f ? info.length : 1.5f;
        }
        yield return new WaitForSeconds(len);

        _enemyHealth?.StartDeathFade();
    }
    #endregion

    #region Helpers
    bool CanSeeTarget()
    {
        if (target == null) return false;
        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        Collider[] hits = Physics.OverlapSphere(origin, sightRange, targetLayer);
        for (int i = 0; i < hits.Length; i++)
            if (hits[i].transform == target) return true;
        return false;
    }

    void FaceTarget()
    {
        if (target == null) return;
        Vector3 dir = (target.position - transform.position).normalized;
        dir.y = 0;
        if (dir != Vector3.zero)
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, Quaternion.LookRotation(dir), agent.angularSpeed * Time.deltaTime);
    }

    void SetAnimator(bool patrolling, bool chasing, bool attacking)
    {
        if (anim == null) return;
        anim.SetBool(H_IsPatrolling, patrolling);
        anim.SetBool(H_IsChasing, chasing);
        anim.SetBool(H_IsAttacking, attacking);
    }

    void SearchWalkPoint()
    {
        for (int i = 0; i < 5; i++)
        {
            Vector3 candidate = transform.position + new Vector3(
                Random.Range(-walkPointRange, walkPointRange), 0,
                Random.Range(-walkPointRange, walkPointRange));
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            {
                walkPoint = hit.position;
                walkPointSet = true;
                return;
            }
        }
    }

    void SetNextWaypoint()
    {
        if (waypoints == null || waypoints.Length == 0) return;
        int next = Random.Range(0, waypoints.Length);
        while (waypoints.Length > 1 && next == currentWaypointIndex)
            next = Random.Range(0, waypoints.Length);
        currentWaypointIndex = next;
        walkPoint = waypoints[currentWaypointIndex].position;
        walkPointSet = true;
    }

    void CheckIfStuck()
    {
        if (!agent.isOnNavMesh) return;
        if (Time.time - lastCheckTime > stuckCheckTime)
        {
            float moved = Vector3.Distance(transform.position, lastPosition);
            stuckTimer = (moved < stuckThreshold && agent.hasPath) ? stuckTimer + stuckCheckTime : 0f;

            if (stuckTimer >= maxStuckDuration)
            {
                walkPointSet = false;
                isWaitingAtWaypoint = true;
                waitTimer = Random.Range(minWaitAtWaypointTime, maxWaitAtWaypointTime);
                isWaitingBeforeChase = false;
                chaseDelayTimer = 0f;
                agent.ResetPath();
                SetAnimator(false, false, false);
                stuckTimer = 0f;
            }

            lastPosition = transform.position;
            lastCheckTime = Time.time;
        }
    }
    #endregion

    private void OnDrawGizmosSelected()
    {
        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, rushAttackRange);
        Gizmos.color = visionGizmoColor;
        Gizmos.DrawWireSphere(origin, sightRange);
    }
}
