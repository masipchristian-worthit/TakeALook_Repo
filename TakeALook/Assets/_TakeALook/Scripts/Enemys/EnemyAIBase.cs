using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// IA del enemigo con máquina de estados: Patrol → Chase → Attack → Hide.
///
/// CONFIGURACIÓN EN INSPECTOR:
/// - Waypoints: arrastra los GameObjects de waypoints (hijos del prefab o escena).
/// - Obstacle Layer: capas que bloquean la visión (Default, Walls…). NO incluir la capa del player.
/// - Target Layer: capa del player (Player).
/// - Vision modifiers: crouch reduce visión, linterna y sprint la aumentan.
/// - maxChaseDistanceFromWaypoint: límite de alejamiento del waypoint más cercano para perseguir.
/// - playerLowAmmoThreshold: si el total de balas del player es ≤ este valor, el enemigo persigue agresivamente.
/// </summary>
public class EnemyAIBase : MonoBehaviour
{
    public enum AIState { Patrol, Chase, Attack, Hide, Stunned }

    #region Inspector
    [Header("Navegación")]
    [SerializeField] NavMeshAgent agent;
    [SerializeField] Animator anim;

    [Header("Waypoints (GameObjects en escena/prefab)")]
    [SerializeField] Transform[] waypoints;
    [SerializeField] float minWaitAtWaypoint = 3f;
    [SerializeField] float maxWaitAtWaypoint = 6f;
    [Range(0f, 1f)]
    [SerializeField] float chanceToSkipWait = 0.25f;

    [Header("Detección - Rangos")]
    [SerializeField] float sightRange = 8f;
    [SerializeField] float attackRange = 2f;
    [SerializeField] float eyeHeight = 1.5f;
    [SerializeField] LayerMask targetLayer;
    [Tooltip("Capas que bloquean la línea de visión (paredes, puertas…). NO incluir la capa del player.")]
    [SerializeField] LayerMask obstacleLayer;

    [Header("Detección - Modificadores")]
    [Tooltip("Multiplica el rango cuando el player está agachado (0.5 = mitad de rango).")]
    [SerializeField] float crouchSightMultiplier = 0.5f;
    [Tooltip("Multiplica el rango cuando el player lleva la linterna encendida.")]
    [SerializeField] float flashlightSightMultiplier = 1.8f;
    [Tooltip("Multiplica el rango cuando el player corre.")]
    [SerializeField] float sprintSightMultiplier = 1.4f;

    [Header("Límite de persecución")]
    [Tooltip("Distancia máxima desde el waypoint más cercano a la que el enemigo persigue al player.")]
    [SerializeField] float maxChaseDistanceFromWaypoint = 15f;

    [Header("Delay de inicio de persecución")]
    [SerializeField] float minChaseDelay = 1f;
    [SerializeField] float maxChaseDelay = 2f;

    [Header("Ataque")]
    [SerializeField] float minAttackCooldown = 2f;
    [SerializeField] float maxAttackCooldown = 4f;
    [SerializeField] GameObject projectile;
    [SerializeField] Transform shootPoint;
    [SerializeField] float shootSpeed = 2.5f;

    [Header("Velocidades")]
    [SerializeField] float patrolSpeed = 2f;
    [SerializeField] float chaseSpeed = 4f;
    [SerializeField] float hideSpeed = 3.5f;

    [Header("Comportamiento Inteligente")]
    [Tooltip("Si el total de balas del player es ≤ este valor, el enemigo persigue en vez de esconderse.")]
    [SerializeField] int playerLowAmmoThreshold = 8;

    [Header("Stun (bala Bull)")]
    [SerializeField] float stunDuration = 2f;
    [SerializeField] GameObject stunVFX;

    [Header("Detección de atasco")]
    [SerializeField] float stuckCheckInterval = 2f;
    [SerializeField] float stuckThreshold = 0.1f;
    [SerializeField] float maxStuckDuration = 4f;

    [Header("Gizmos")]
    [SerializeField] Color visionGizmoColor = new Color(1f, 0f, 0f, 0.25f);
    #endregion

    // Referencias al player
    private Transform _target;
    private FPS_Controller _playerFPS;
    private GunSystem _playerGun;

    // Estado
    private AIState _state = AIState.Patrol;
    private bool _isStunned;

    // Patrol
    private int _currentWaypointIndex;
    private bool _waitingAtWaypoint;
    private float _waypointWaitTimer;
    private bool _walkPointSet;
    private Vector3 _walkPoint;

    // Chase delay (primera vez que ve al player)
    private bool _chaseDelayActive;
    private float _chaseDelayTimer;

    // Ataque
    private bool _attackOnCooldown;

    // Hide
    private int _hideWaypointIndex = -1;

    // Stuck detection
    private Vector3 _lastStuckPos;
    private float _lastStuckTime;
    private float _stuckTimer;

    #region Lifecycle
    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        anim = GetComponentInChildren<Animator>();

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            _target = player.transform;
            _playerFPS = player.GetComponent<FPS_Controller>();
            _playerGun = player.GetComponentInChildren<GunSystem>();
        }

        _lastStuckPos = transform.position;
        _lastStuckTime = Time.time;
    }

    private void Start()
    {
        agent.speed = patrolSpeed;

        if (waypoints != null && waypoints.Length > 0)
            _currentWaypointIndex = Random.Range(0, waypoints.Length);

        if (stunVFX != null) stunVFX.SetActive(false);
    }

    private void Update()
    {
        if (_isStunned) return;
        UpdateStateMachine();
        CheckStuck();
    }
    #endregion

    #region Máquina de Estados
    private void UpdateStateMachine()
    {
        bool canSee = CanSeePlayer();

        switch (_state)
        {
            case AIState.Patrol:
                if (canSee) EnterCombat();
                else ExecutePatrol();
                break;

            case AIState.Chase:
                if (!canSee || !IsWithinChaseRange())
                {
                    _chaseDelayActive = false;
                    TransitionTo(AIState.Patrol);
                }
                else
                {
                    float dist = Vector3.Distance(transform.position, _target.position);
                    if (dist <= attackRange) TransitionTo(AIState.Attack);
                    else ExecuteChase();
                }
                break;

            case AIState.Attack:
                if (!canSee) { TransitionTo(AIState.Patrol); }
                else
                {
                    float dist = Vector3.Distance(transform.position, _target.position);
                    // Sale de Attack si el player se aleja más de 1.2x el rango
                    if (dist > attackRange * 1.2f) TransitionTo(AIState.Chase);
                    else ExecuteAttack();
                }
                break;

            case AIState.Hide:
                if (!canSee) { TransitionTo(AIState.Patrol); }
                else if (ShouldChaseAggressively()) { EnterCombat(); }
                else ExecuteHide();
                break;
        }
    }

    private void EnterCombat()
    {
        TransitionTo(ShouldChaseAggressively() ? AIState.Chase : AIState.Hide);
    }

    // Devuelve true cuando el player tiene pocas balas (el enemigo ataca)
    private bool ShouldChaseAggressively()
    {
        if (_playerGun == null) return true;
        int total = _playerGun.GetMag(GunSystem.BulletType.Wolf) + _playerGun.GetReserve(GunSystem.BulletType.Wolf)
                  + _playerGun.GetMag(GunSystem.BulletType.Bull) + _playerGun.GetReserve(GunSystem.BulletType.Bull)
                  + _playerGun.GetMag(GunSystem.BulletType.Eagle) + _playerGun.GetReserve(GunSystem.BulletType.Eagle);
        return total <= playerLowAmmoThreshold;
    }

    private void TransitionTo(AIState newState)
    {
        if (_state == newState) return;
        _state = newState;

        switch (newState)
        {
            case AIState.Patrol:
                agent.isStopped = false;
                agent.speed = patrolSpeed;
                _chaseDelayActive = false;
                _walkPointSet = false;
                SetAnim();
                break;

            case AIState.Chase:
                agent.isStopped = false;
                agent.speed = chaseSpeed;
                _chaseDelayActive = false;
                break;

            case AIState.Attack:
                agent.isStopped = true;
                agent.ResetPath();
                SetAnim(attack: true);
                break;

            case AIState.Hide:
                agent.isStopped = false;
                agent.speed = hideSpeed;
                _hideWaypointIndex = GetFurthestWaypointFromPlayer();
                _walkPointSet = false;
                SetAnim(patrol: true);
                break;

            case AIState.Stunned:
                agent.isStopped = true;
                agent.ResetPath();
                SetAnim();
                break;
        }
    }
    #endregion

    #region Ejecución de Estados
    private void ExecutePatrol()
    {
        if (!agent.isOnNavMesh) return;

        if (_waitingAtWaypoint)
        {
            SetAnim();
            _waypointWaitTimer -= Time.deltaTime;
            if (_waypointWaitTimer <= 0f)
            {
                _waitingAtWaypoint = false;
                _walkPointSet = false;
                AdvanceWaypoint();
            }
            return;
        }

        if (!_walkPointSet)
        {
            if (waypoints != null && waypoints.Length > 0)
            {
                _walkPoint = waypoints[_currentWaypointIndex].position;
                _walkPointSet = true;
            }
            else
            {
                SearchRandomWalkPoint();
            }
        }

        if (_walkPointSet)
        {
            SetAnim(patrol: true);
            agent.SetDestination(_walkPoint);
        }

        if (_walkPointSet && !agent.pathPending && agent.remainingDistance <= 0.5f)
        {
            agent.ResetPath();

            if (Random.value < chanceToSkipWait)
            {
                _walkPointSet = false;
                AdvanceWaypoint();
            }
            else
            {
                _waitingAtWaypoint = true;
                _waypointWaitTimer = Random.Range(minWaitAtWaypoint, maxWaitAtWaypoint);
            }
        }
    }

    private void ExecuteChase()
    {
        if (!agent.isOnNavMesh) return;

        // Pausa inicial al detectar al player
        if (!_chaseDelayActive)
        {
            _chaseDelayActive = true;
            _chaseDelayTimer = Random.Range(minChaseDelay, maxChaseDelay);
            agent.ResetPath();
            SetAnim();
            return;
        }

        if (_chaseDelayTimer > 0f)
        {
            _chaseDelayTimer -= Time.deltaTime;
            agent.ResetPath();
            SetAnim();
            return;
        }

        SetAnim(chase: true);
        agent.isStopped = false;
        agent.SetDestination(_target.position);
        TryShoot();
    }

    private void ExecuteAttack()
    {
        if (!agent.isOnNavMesh) return;

        agent.isStopped = true;
        agent.ResetPath();

        // Girar hacia el player
        Vector3 dir = _target.position - transform.position;
        dir.y = 0;
        if (dir != Vector3.zero)
        {
            Quaternion look = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, look, agent.angularSpeed * Time.deltaTime);
        }

        TryShoot();
    }

    private void ExecuteHide()
    {
        if (!agent.isOnNavMesh || waypoints == null || waypoints.Length == 0)
        {
            // Sin waypoints: comportamiento de patrulla normal
            ExecutePatrol();
            return;
        }

        if (_hideWaypointIndex < 0 || _hideWaypointIndex >= waypoints.Length)
            _hideWaypointIndex = GetFurthestWaypointFromPlayer();

        Vector3 hidePos = waypoints[_hideWaypointIndex].position;
        float distToHide = Vector3.Distance(transform.position, hidePos);

        agent.SetDestination(hidePos);

        if (distToHide <= 1.2f)
        {
            // En la posición de cobertura: dispara oportunistamente
            SetAnim(attack: !_attackOnCooldown && CanSeePlayer());
            if (CanSeePlayer()) TryShoot();
        }
        else
        {
            SetAnim(patrol: true);
        }
    }
    #endregion

    #region Visión (LOS + Modificadores)
    private bool CanSeePlayer()
    {
        if (_target == null) return false;

        Vector3 eyePos = transform.position + Vector3.up * eyeHeight;
        Vector3 playerCenter = _target.position + Vector3.up * 1f;

        // Rango base con modificadores según estado del player
        float range = sightRange;

        if (_playerFPS != null)
        {
            if (_playerFPS.IsCrouchingPublic) range *= crouchSightMultiplier;
            if (_playerFPS.IsSprintingPublic) range *= sprintSightMultiplier;
        }

        if (_playerGun != null && _playerGun.IsFlashlightOn)
            range *= flashlightSightMultiplier;

        float dist = Vector3.Distance(eyePos, playerCenter);
        if (dist > range) return false;

        // Comprobar línea de visión: si algo bloquea, no ve al player
        Vector3 dir = (playerCenter - eyePos).normalized;
        if (Physics.Raycast(eyePos, dir, dist, obstacleLayer))
            return false;

        return true;
    }

    // Comprueba que el enemigo no haya perseguido al player demasiado lejos de sus waypoints
    private bool IsWithinChaseRange()
    {
        if (waypoints == null || waypoints.Length == 0) return true;

        float minDist = float.MaxValue;
        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null) continue;
            float d = Vector3.Distance(transform.position, waypoints[i].position);
            if (d < minDist) minDist = d;
        }
        return minDist <= maxChaseDistanceFromWaypoint;
    }
    #endregion

    #region Ataque
    private void TryShoot()
    {
        if (_attackOnCooldown || projectile == null || shootPoint == null) return;

        SetAnim(attack: true);

        Vector3 targetCenter = _target.position + Vector3.up * 1f;
        Vector3 dir = (targetCenter - shootPoint.position).normalized;

        var proj = Instantiate(projectile, shootPoint.position, Quaternion.identity);
        var rb = proj.GetComponent<Rigidbody>();
        if (rb != null) rb.AddForce(dir * shootSpeed, ForceMode.Impulse);

        _attackOnCooldown = true;
        Invoke(nameof(ResetAttack), Random.Range(minAttackCooldown, maxAttackCooldown));
    }

    private void ResetAttack()
    {
        _attackOnCooldown = false;
        // Sólo quitamos el flag de ataque; el SetAnim lo gestiona el estado activo
        if (anim != null) anim.SetBool("IsAttacking", false);
    }
    #endregion

    #region Stun
    public void Stun(float duration = -1f)
    {
        if (_isStunned) return;
        StartCoroutine(StunRoutine(duration > 0f ? duration : stunDuration));
    }

    private IEnumerator StunRoutine(float duration)
    {
        _isStunned = true;
        _state = AIState.Stunned;
        agent.isStopped = true;
        agent.ResetPath();

        CancelInvoke(nameof(ResetAttack));
        _attackOnCooldown = false;

        if (stunVFX != null) stunVFX.SetActive(true);
        SetAnim();
        if (anim != null) anim.SetBool("isStunned", true);

        yield return new WaitForSeconds(duration);

        _isStunned = false;
        if (anim != null) anim.SetBool("isStunned", false);
        if (stunVFX != null) stunVFX.SetActive(false);

        agent.isStopped = false;
        TransitionTo(AIState.Patrol);
    }
    #endregion

    #region Waypoints
    private void AdvanceWaypoint()
    {
        if (waypoints == null || waypoints.Length <= 1) return;
        int next;
        do { next = Random.Range(0, waypoints.Length); }
        while (next == _currentWaypointIndex);
        _currentWaypointIndex = next;
    }

    private int GetFurthestWaypointFromPlayer()
    {
        if (waypoints == null || waypoints.Length == 0) return 0;
        int idx = 0;
        float maxDist = -1f;
        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null) continue;
            float d = Vector3.Distance(waypoints[i].position, _target.position);
            if (d > maxDist) { maxDist = d; idx = i; }
        }
        return idx;
    }

    private void SearchRandomWalkPoint()
    {
        for (int i = 0; i < 5; i++)
        {
            Vector3 rnd = transform.position + new Vector3(
                Random.Range(-8f, 8f), 0, Random.Range(-8f, 8f));
            if (NavMesh.SamplePosition(rnd, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            {
                _walkPoint = hit.position;
                _walkPointSet = true;
                return;
            }
        }
    }
    #endregion

    #region Animador
    private void SetAnim(bool patrol = false, bool chase = false, bool attack = false)
    {
        if (anim == null) return;
        anim.SetBool("isPatrolling", patrol);
        anim.SetBool("isChasing", chase);
        anim.SetBool("IsAttacking", attack);
        anim.SetBool("isStunned", _isStunned);
    }
    #endregion

    #region Detección de atasco
    private void CheckStuck()
    {
        if (!agent.isOnNavMesh) return;
        if (Time.time - _lastStuckTime < stuckCheckInterval) return;

        float moved = Vector3.Distance(transform.position, _lastStuckPos);
        _stuckTimer = (moved < stuckThreshold && agent.hasPath)
            ? _stuckTimer + stuckCheckInterval
            : 0f;

        if (_stuckTimer >= maxStuckDuration)
        {
            _stuckTimer = 0f;
            TransitionTo(AIState.Patrol);
        }

        _lastStuckPos = transform.position;
        _lastStuckTime = Time.time;
    }
    #endregion

    #region Gizmos
    private void OnDrawGizmosSelected()
    {
        Vector3 eye = transform.position + Vector3.up * eyeHeight;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        Gizmos.color = visionGizmoColor;
        Gizmos.DrawWireSphere(eye, sightRange);

        if (waypoints != null && maxChaseDistanceFromWaypoint > 0f)
        {
            Gizmos.color = Color.yellow;
            foreach (var wp in waypoints)
                if (wp != null) Gizmos.DrawWireSphere(wp.position, maxChaseDistanceFromWaypoint);
        }
    }
    #endregion
}
