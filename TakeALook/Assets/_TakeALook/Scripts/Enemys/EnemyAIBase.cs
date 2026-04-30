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
    [Tooltip("Capa en la que se considera \"obstrucción de visión\" (paredes, puertas cerradas, etc). Si una pared queda entre el enemigo y el jugador, no le ve.")]
    [SerializeField] LayerMask sightObstructionLayer = ~0;

    [Header("Patrol")]
    [SerializeField] float walkPointRange = 8f;
    [SerializeField] bool useWaypoints;
    [SerializeField] Transform[] waypoints;
    [SerializeField] float minWaitAtWaypointTime = 2f;
    [SerializeField] float maxWaitAtWaypointTime = 5f;
    [SerializeField, Range(0f, 1f)] float chanceToSkipWait = 0.25f;

    [Header("Idle Cycle")]
    [Tooltip("Cada cuánto, después de patrullar, el enemigo se queda en Idle (animación) en vez de buscar nuevo punto.")]
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
    [Tooltip("Timeout de seguridad si el Animation Event de disparo nunca llega (s).")]
    [SerializeField] float roarShootDelay = 1.83f;
    [Tooltip("Timeout de seguridad si el Animation Event de fin de Roar nunca llega (s).")]
    [SerializeField] float roarTotalDuration = 2.5f;
    [Tooltip("Timeout de seguridad para la animación Being_Shot (forward + recovery).")]
    [SerializeField] float hitStunSafetyTimeout = 2.5f;

    [Header("Detection")]
    [SerializeField] float sightRange = 12f;
    [Tooltip("Ángulo total del cono de visión (grados).")]
    [SerializeField] float sightFovDegrees = 110f;
    [SerializeField] float eyeHeight = 1.5f;
    [SerializeField] float attackRange = 6f;
    [SerializeField] Color visionGizmoColor = new Color(1f, 0f, 0f, 0.25f);

    [Header("Movement Speeds")]
    [SerializeField] float patrolSpeed = 1.5f;
    [SerializeField] float chaseSpeed = 3.5f;
    [SerializeField] float rushSpeed = 5.5f;
    [SerializeField] float fleeSpeed = 5f;

    [Header("Rush (Player Out Of Ammo + Dryfire)")]
    [Tooltip("Distancia bajo la cual el rusher pasa directamente a atacar.")]
    [SerializeField] float rushAttackRange = 4f;
    [Tooltip("Tiempo durante el que el enemigo sigue rusheando tras detectar dryfire.")]
    [SerializeField] float rushPersistTime = 3.0f;

    [Header("Flee Conditions")]
    [Tooltip("HP por debajo del cual el enemigo intentará huir.")]
    [SerializeField, Range(0f, 1f)] float lowHealthThreshold = 0.25f;
    [Tooltip("Solo huye si la distancia al jugador es mayor que este valor (si está cerca, lucha).")]
    [SerializeField] float fleeMinDistance = 7f;
    [Tooltip("Distancia objetivo para alejarse del jugador al huir.")]
    [SerializeField] float fleeDistance = 15f;

    [Header("Stuck Detection")]
    [SerializeField] float stuckCheckTime = 2f;
    [SerializeField] float stuckThreshold = 0.1f;
    [SerializeField] float maxStuckDuration = 3f;

    [Header("Audio")]
    [SerializeField] string sfxRoarId   = "enemy_roar";
    [SerializeField] string sfxFootstepId = "enemy_step";
    [SerializeField] string sfxAttackShootId = "enemy_shoot";
    [SerializeField] string sfxAlertId  = "enemy_alert";
    #endregion

    // ---- Internal state ----
    Vector3 walkPoint;
    bool walkPointSet;
    int currentWaypointIndex;
    bool isWaitingAtWaypoint;
    float waitTimer;
    bool isInIdle;
    float idleTimer;
    bool isWaitingBeforeChase;
    float chaseDelayTimer;
    bool alreadyAttacked;
    bool isInvulnerable;
    bool _alertedThisSighting;

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
    static readonly int H_ShotRecover  = Animator.StringToHash("ShotRecover");
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

            // Suscripción al evento de "intento de disparo con cargador vacío" del jugador
            if (_playerGunSystem != null)
                _playerGunSystem.OnDryFireAttempt += HandleDryFireAttempt;
        }

        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (anim == null) anim = GetComponentInChildren<Animator>();
        _enemyHealth = GetComponent<EnemyHealth>();

        // Configuración para que NO atraviese muros / puertas: usa la calidad alta de avoidance
        if (agent != null)
        {
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            agent.autoTraverseOffMeshLink = true;
            agent.updateRotation = true;
        }

        lastPosition = transform.position;
        lastCheckTime = Time.time;
    }

    private void OnDestroy()
    {
        if (_playerGunSystem != null)
            _playerGunSystem.OnDryFireAttempt -= HandleDryFireAttempt;
    }

    private void Start()
    {
        if (useWaypoints && waypoints != null && waypoints.Length > 0)
            currentWaypointIndex = Random.Range(0, waypoints.Length);
        if (agent != null) agent.speed = patrolSpeed;
        ChangeState(EnemyState.Patrolling);
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
        // Mientras se ejecuta el ciclo Roar, no reevaluamos.
        if (_state == EnemyState.Attacking && alreadyAttacked)
        {
            FaceTarget();
            return;
        }

        bool inSight = CanSeeTarget();
        float dist = target != null ? Vector3.Distance(transform.position, target.position) : float.MaxValue;
        bool rushActive = inSight && Time.time < rushUntilTime;

        // Alerta única al detectar al jugador (sonido / posibles VFX)
        if (inSight && !_alertedThisSighting)
        {
            _alertedThisSighting = true;
            AudioManager.Instance?.PlaySFX(sfxAlertId, transform.position);
        }
        else if (!inSight)
        {
            _alertedThisSighting = false;
        }

        EnemyState next;
        if (!inSight)
        {
            // Si no ve al jugador, mantiene el ciclo patrulla / idle
            next = (_state == EnemyState.Idle || _state == EnemyState.Patrolling)
                 ? _state
                 : EnemyState.Patrolling;
        }
        else if (ShouldFlee(dist))
        {
            next = EnemyState.Fleeing;
        }
        else if (rushActive)
        {
            next = (dist <= rushAttackRange) ? EnemyState.Attacking : EnemyState.Rushing;
        }
        else if (dist <= attackRange)
        {
            next = EnemyState.Attacking;
        }
        else
        {
            next = EnemyState.Chasing;
        }

        ChangeState(next);

        switch (_state)
        {
            case EnemyState.Idle:       DoIdle();   break;
            case EnemyState.Patrolling: DoPatrol(); break;
            case EnemyState.Chasing:    DoChase();  break;
            case EnemyState.Rushing:    DoRush();   break;
            case EnemyState.Attacking:  DoAttack(); break;
            case EnemyState.Fleeing:    DoFlee();   break;
        }
    }

    void ChangeState(EnemyState next)
    {
        if (_state == next) return;

        // Reset de los flags asociados al estado saliente
        if (_state == EnemyState.Chasing)
        {
            isWaitingBeforeChase = false;
            chaseDelayTimer = 0f;
        }
        if (_state == EnemyState.Idle)
        {
            isInIdle = false;
            idleTimer = 0f;
        }
        if (_state == EnemyState.Patrolling)
        {
            isWaitingAtWaypoint = false;
        }

        _state = next;
    }

    bool ShouldFlee(float distToPlayer)
    {
        if (_enemyHealth == null) return false;
        return _enemyHealth.HealthPercent <= lowHealthThreshold && distToPlayer >= fleeMinDistance;
    }

    void HandleDryFireAttempt()
    {
        // El jugador intentó disparar sin balas: si lo vemos, persistimos rush.
        if (!CanSeeTarget()) return;
        rushUntilTime = Time.time + rushPersistTime;
    }
    #endregion

    #region State Behaviors
    void DoIdle()
    {
        if (agent != null && agent.isOnNavMesh) agent.ResetPath();
        SetAnimator(false, false, false);

        if (!isInIdle)
        {
            isInIdle = true;
            idleTimer = Random.Range(minIdleTime, maxIdleTime);
        }

        idleTimer -= Time.deltaTime;
        if (idleTimer <= 0f)
        {
            isInIdle = false;
            ChangeState(EnemyState.Patrolling);
        }
    }

    void DoPatrol()
    {
        if (agent == null || !agent.isOnNavMesh) return;
        agent.speed = patrolSpeed;
        SetAnimator(true, false, false);

        if (isWaitingAtWaypoint)
        {
            waitTimer -= Time.deltaTime;
            if (waitTimer <= 0f)
            {
                isWaitingAtWaypoint = false;
                walkPointSet = false;

                // Posibilidad de pasar a Idle puro entre waypoints (mejora visual y sensación de comportamiento)
                if (Random.value < chanceToIdleAfterWaypoint)
                {
                    ChangeState(EnemyState.Idle);
                    return;
                }

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
        if (agent == null || !agent.isOnNavMesh || target == null) return;
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
            return;
        }

        SetAnimator(false, true, false);
        agent.SetDestination(target.position);
    }

    void DoRush()
    {
        if (agent == null || !agent.isOnNavMesh || target == null) return;
        agent.speed = rushSpeed;
        SetAnimator(false, true, false);
        agent.SetDestination(target.position);
    }

    void DoAttack()
    {
        if (agent == null || !agent.isOnNavMesh) return;
        agent.ResetPath();
        FaceTarget();

        if (!alreadyAttacked)
        {
            _attackRoutine = StartCoroutine(AttackCoroutine());
        }
    }

    void DoFlee()
    {
        if (agent == null || !agent.isOnNavMesh || target == null) return;
        agent.speed = fleeSpeed;
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

        // Trigger Roar. El bool isAttacking se mantiene SOLO mientras el clip está activo,
        // para que el Animator no relance el ataque en bucle si el trigger ya fue consumido.
        SetAnimator(false, false, true);
        if (anim != null)
        {
            anim.ResetTrigger(H_Roar);
            anim.SetTrigger(H_Roar);
        }
        AudioManager.Instance?.PlaySFX(sfxRoarId, transform.position);

        // Esperar al Animation Event de disparo (con timeout de seguridad).
        // EL EVENTO debe llamarse OnShootAnimationEvent y estar colocado en el frame 110 del clip Roar.
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

        Vector3 aimPoint = target.position + Vector3.up * 1.0f;
        Vector3 dir = (aimPoint - shootPoint.position).normalized;

        GameObject proj = Instantiate(projectilePrefab, shootPoint.position, Quaternion.LookRotation(dir));
        // Activar siempre el GO para evitar que el prefab se quedase desactivado por error.
        if (!proj.activeSelf) proj.SetActive(true);

        // Ignorar colisión con el propio enemigo para que no se autodetone.
        Collider[] selfCols = GetComponentsInChildren<Collider>();
        Collider[] projCols = proj.GetComponentsInChildren<Collider>();
        for (int i = 0; i < selfCols.Length; i++)
            for (int j = 0; j < projCols.Length; j++)
                if (selfCols[i] != null && projCols[j] != null)
                    Physics.IgnoreCollision(selfCols[i], projCols[j], true);

        Rigidbody rb = proj.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.useGravity = false;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = dir * shootSpeed;
#else
            rb.velocity = dir * shootSpeed;
#endif
        }

        // Inicializar el script EnemyProjectile con los datos correctos
        EnemyProjectile epr = proj.GetComponent<EnemyProjectile>();
        if (epr != null) epr.Launch(dir * shootSpeed);

        AudioManager.Instance?.PlaySFX(sfxAttackShootId, shootPoint.position);
    }

    // ============================================================
    // ANIMATION EVENT HOOKS — colgar estos métodos en los clips:
    //
    //  Roar.anim:
    //   - frame 110 (disparo)  -> OnShootAnimationEvent()
    //   - último frame         -> OnRoarFinishedAnimationEvent()
    //
    //  Being_Shot.anim (forward):
    //   - último frame         -> OnHitForwardFinishedAnimationEvent()
    //
    //  Being_Shot_Recovery.anim (la animación "negativa" que vuelve a Idle):
    //   - último frame         -> OnHitRecoveryFinishedAnimationEvent()
    //
    //  *Cualquier* clip de andar:
    //   - cada paso            -> OnFootstepAnimationEvent()
    // ============================================================
    public void OnShootAnimationEvent()
    {
        _shootEventFired = true;
        FireProjectile();
    }
    public void OnRoarFinishedAnimationEvent()        => _roarFinishedEvent = true;
    public void OnHitForwardFinishedAnimationEvent()  => _hitForwardFinishedEvent = true;
    public void OnHitRecoveryFinishedAnimationEvent() => _hitRecoveryFinishedEvent = true;
    public void OnFootstepAnimationEvent()            => AudioManager.Instance?.PlaySFX(sfxFootstepId, transform.position);

    public void OnHitByNormalBullet()
    {
        if (isInvulnerable || _state == EnemyState.Dead) return;
        if (_attackRoutine != null) StopCoroutine(_attackRoutine);
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
            anim.ResetTrigger(H_ShotRecover);
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

        // Disparamos el segundo trigger (recovery) de forma EXPLÍCITA en lugar de
        // confiar en una transición Has Exit Time del propio estado a sí mismo,
        // que no se dispara porque el trigger Shot ya fue consumido.
        if (anim != null)
        {
            anim.ResetTrigger(H_Shot);
            anim.ResetTrigger(H_ShotRecover);
            anim.SetTrigger(H_ShotRecover);
        }

        // Recovery: esperar al último frame del clip de recovery
        t = 0f;
        while (!_hitRecoveryFinishedEvent && t < hitStunSafetyTimeout)
        {
            t += Time.deltaTime;
            yield return null;
        }

        if (anim != null)
        {
            anim.ResetTrigger(H_Shot);
            anim.ResetTrigger(H_ShotRecover);
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

        // Solo deshabilitamos los colliders que pueden seguir afectando al gameplay,
        // dejamos el del cadáver para que el cuerpo no se hunda en el suelo.
        // EnemyHealth se encargará después de fijar la pose y persistir el cadáver.
        Collider[] cols = GetComponentsInChildren<Collider>();
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] == null) continue;
            // Mantener triggers de daño desactivados pero mantener el collider de cuerpo si existe.
            cols[i].enabled = false;
        }

        if (anim != null)
        {
            anim.ResetTrigger(H_Roar);
            anim.ResetTrigger(H_Shot);
            anim.ResetTrigger(H_ShotRecover);
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

        // Cadáver permanente: ahora el EnemyHealth fija la pose final y persiste el cuerpo.
        _enemyHealth?.MarkAsCorpse();
    }
    #endregion

    #region Helpers
    bool CanSeeTarget()
    {
        if (target == null) return false;

        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        Vector3 targetCenter = target.position + Vector3.up * 1.0f;

        // Distancia
        float dist = Vector3.Distance(origin, targetCenter);
        if (dist > sightRange) return false;

        // Cono de visión
        Vector3 dir = (targetCenter - origin).normalized;
        float angle = Vector3.Angle(transform.forward, dir);
        if (angle > sightFovDegrees * 0.5f) return false;

        // LOS — si hay pared / puerta cerrada de por medio no nos vale.
        if (Physics.Raycast(origin, dir, out RaycastHit hit, dist, sightObstructionLayer))
        {
            // Si el primer hit no es el target, hay obstrucción
            if (hit.transform != target && !hit.transform.IsChildOf(target))
                return false;
        }

        // Confirmación con el layer del jugador (por si el filtro anterior no lo coge)
        Collider[] hits = Physics.OverlapSphere(origin, sightRange, targetLayer);
        for (int i = 0; i < hits.Length; i++)
            if (hits[i].transform == target || hits[i].transform.IsChildOf(target))
                return true;

        return true;
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
        if (agent == null || !agent.isOnNavMesh) return;
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

        // Cono de visión
        Vector3 fwd = transform.forward;
        Quaternion left = Quaternion.AngleAxis(-sightFovDegrees * 0.5f, Vector3.up);
        Quaternion right = Quaternion.AngleAxis(sightFovDegrees * 0.5f, Vector3.up);
        Gizmos.DrawRay(origin, left * fwd * sightRange);
        Gizmos.DrawRay(origin, right * fwd * sightRange);
    }
}
