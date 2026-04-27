using UnityEngine;
using UnityEngine.AI;

public class EnemyAIBase : MonoBehaviour
{
    #region General Variables
    [Header("AI Configuration")]
    [SerializeField] NavMeshAgent agent;
    [SerializeField] Transform target;
    [SerializeField] Animator anim;
    [SerializeField] LayerMask targetLayer;
    [SerializeField] LayerMask groundLayer;

    [Header("Patroling Stats")]
    [SerializeField] float walkPointRange = 8f;
    Vector3 walkPoint;
    bool walkPointSet;

    [Header("Waypoint Patrol System")]
    [SerializeField] bool useWaypoints;
    [SerializeField] Transform[] waypoints;
    private int currentWaypointIndex;

    [Header("Waypoint Wait")]
    [SerializeField] float minWaitAtWaypointTime = 3f;
    [SerializeField] float maxWaitAtWaypointTime = 6f;
    [SerializeField, Range(0f, 1f)] float chanceToSkipWaitAtWaypoint = 0.25f;
    bool isWaitingAtWaypoint;
    float waitTimer;

    [Header("Chase Delay")]
    [SerializeField] float minChaseDelay = 1f;
    [SerializeField] float maxChaseDelay = 2f;
    bool isWaitingBeforeChase;
    float chaseDelayTimer;

    [Header("Attacking Stats")]
    [SerializeField] float minTimeBetweenAttacks = 2f;
    [SerializeField] float maxTimeBetweenAttacks = 4f;
    [SerializeField] GameObject projectile;
    [SerializeField] Transform shootPoint;
    [SerializeField] float shootSpeed = 2.5f;
    bool alreadyAttacked;

    [Header("Detection Aura")]
    [SerializeField] float sightRange = 8f;
    [SerializeField] float eyeHeight = 1.5f;
    [SerializeField] Color visionGizmoColor = new Color(1f, 0f, 0f, 0.25f);

    [Header("Attack Range")]
    [SerializeField] float attackRange = 2f;
    [SerializeField] bool targetInSightRange;
    [SerializeField] bool targetInAttackRange;

    [Header("Movement Speed")]
    [SerializeField] float patrolSpeed = 0.5f;
    [SerializeField] float chaseSpeed = 1.0f;

    [Header("Stuck Detection")]
    [SerializeField] float stuckCheckTime = 2f;
    [SerializeField] float stuckThreshold = 0.1f;
    [SerializeField] float maxStuckDuration = 3f;

    float stuckTimer;
    float lastCheckTime;
    Vector3 lastPosition;
    #endregion

    private void Awake()
    {
        GameObject playerObj = GameObject.Find("Player");
        if (playerObj != null) target = playerObj.transform;

        agent = GetComponent<NavMeshAgent>();
        anim = GetComponentInChildren<Animator>();
        lastPosition = transform.position;
        lastCheckTime = Time.time;
    }

    private void Start()
    {
        if (useWaypoints && waypoints.Length > 0)
        {
            currentWaypointIndex = Random.Range(0, waypoints.Length);
        }

        if (agent != null)
        {
            agent.speed = patrolSpeed;
        }
    }

    void Update()
    {
        EnemyStateUpdater();
        CheckIfStuck();
    }

    void EnemyStateUpdater()
    {
        targetInSightRange = CanSeeTarget();

        if (targetInSightRange)
        {
            float distance = Vector3.Distance(transform.position, target.position);
            targetInAttackRange = distance <= attackRange;
        }
        else
        {
            targetInAttackRange = false;
            isWaitingBeforeChase = false;
            chaseDelayTimer = 0f;
        }

        if (!targetInSightRange && !targetInAttackRange)
        {
            UpdateAnimatorStates(true, false);
            Patroling();
        }
        else if (targetInSightRange && !targetInAttackRange)
        {
            ChaseTarget();
        }
        else if (targetInSightRange && targetInAttackRange)
        {
            UpdateAnimatorStates(false, false);
            AttackTarget();
        }
    }

    bool CanSeeTarget()
    {
        if (target == null) return false;

        Vector3 origin = transform.position + Vector3.up * eyeHeight;

        Collider[] hits = Physics.OverlapSphere(origin, sightRange, targetLayer);

        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].transform == target)
                return true;
        }

        return false;
    }

    void Patroling()
    {
        if (!agent.isOnNavMesh) return;

        agent.speed = patrolSpeed;

        if (isWaitingAtWaypoint)
        {
            UpdateAnimatorStates(false, false);

            waitTimer -= Time.deltaTime;

            if (waitTimer <= 0f)
            {
                isWaitingAtWaypoint = false;
                walkPointSet = false;

                if (useWaypoints && waypoints.Length > 0)
                {
                    SetNextWaypoint();
                }
            }

            return;
        }

        if (!walkPointSet)
        {
            if (useWaypoints && waypoints.Length > 0)
            {
                walkPoint = waypoints[currentWaypointIndex].position;
                walkPointSet = true;
            }
            else
            {
                SearchWalkPoint();
            }
        }

        if (walkPointSet)
        {
            agent.SetDestination(walkPoint);
        }

        if (!agent.pathPending && agent.remainingDistance <= 0.5f && walkPointSet)
        {
            agent.ResetPath();

            if (useWaypoints && waypoints.Length > 0)
            {
                bool skipWait = Random.value < chanceToSkipWaitAtWaypoint;

                if (skipWait)
                {
                    walkPointSet = false;
                    SetNextWaypoint();
                }
                else
                {
                    isWaitingAtWaypoint = true;
                    waitTimer = Random.Range(minWaitAtWaypointTime, maxWaitAtWaypointTime);
                }
            }
            else
            {
                isWaitingAtWaypoint = true;
                waitTimer = Random.Range(minWaitAtWaypointTime, maxWaitAtWaypointTime);
            }
        }
    }

    void SetNextWaypoint()
    {
        if (waypoints == null || waypoints.Length == 0) return;

        int newIndex = Random.Range(0, waypoints.Length);

        while (waypoints.Length > 1 && newIndex == currentWaypointIndex)
        {
            newIndex = Random.Range(0, waypoints.Length);
        }

        currentWaypointIndex = newIndex;
    }

    void UpdateAnimatorStates(bool patrolling, bool chasing, bool attacking = false)
    {
        if (anim == null) return;

        anim.SetBool("isPatrolling", patrolling);
        anim.SetBool("isChasing", chasing);
        anim.SetBool("IsAttacking", attacking);
    }

    void SearchWalkPoint()
    {
        int attempts = 0;
        const int maxAttempts = 5;

        while (!walkPointSet && attempts < maxAttempts)
        {
            attempts++;
            Vector3 randomPoint = transform.position + new Vector3(
                Random.Range(-walkPointRange, walkPointRange),
                0,
                Random.Range(-walkPointRange, walkPointRange)
            );

            if (NavMesh.SamplePosition(randomPoint, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            {
                walkPoint = hit.position;
                walkPointSet = true;
            }
        }
    }

    void ChaseTarget()
    {
        if (!agent.isOnNavMesh) return;

        agent.speed = chaseSpeed;

        // Primera detección: se queda quieto en Idle y empieza la pausa
        if (!isWaitingBeforeChase)
        {
            isWaitingBeforeChase = true;
            chaseDelayTimer = Random.Range(minChaseDelay, maxChaseDelay);

            agent.ResetPath();
            UpdateAnimatorStates(false, false);
            return;
        }

        // Mientras espera: sigue quieto en Idle
        if (chaseDelayTimer > 0f)
        {
            chaseDelayTimer -= Time.deltaTime;
            agent.ResetPath();
            UpdateAnimatorStates(false, false);
            return;
        }

        // Cuando termina la pausa: persigue y dispara
        UpdateAnimatorStates(false, true);
        agent.SetDestination(target.position);
        TryShoot();
    }

    void AttackTarget()
    {
        if (!agent.isOnNavMesh) return;

        agent.ResetPath();

        Vector3 direction = (target.position - transform.position).normalized;
        direction.y = 0;

        if (direction != Vector3.zero)
        {
            Quaternion lookRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, lookRotation, agent.angularSpeed * Time.deltaTime);
        }

        TryShoot();
    }

    void TryShoot()
    {
        if (alreadyAttacked) return;

        UpdateAnimatorStates(false, false, true);

        Vector3 targetCenter = target.position + Vector3.up * 1f;
        Vector3 shootDir = (targetCenter - shootPoint.position).normalized;

        Rigidbody rb = Instantiate(projectile, shootPoint.position, Quaternion.identity).GetComponent<Rigidbody>();
        rb.AddForce(shootDir * shootSpeed, ForceMode.Impulse);

        alreadyAttacked = true;
        float cooldown = Random.Range(minTimeBetweenAttacks, maxTimeBetweenAttacks);
        Invoke(nameof(ResetAttack), cooldown);
    }

    void ResetAttack()
    {
        alreadyAttacked = false;
        UpdateAnimatorStates(false, false, false);
    }

    void CheckIfStuck()
    {
        if (!agent.isOnNavMesh) return;

        if (Time.time - lastCheckTime > stuckCheckTime)
        {
            float distanceMoved = Vector3.Distance(transform.position, lastPosition);

            if (distanceMoved < stuckThreshold && agent.hasPath)
            {
                stuckTimer += stuckCheckTime;
            }
            else
            {
                stuckTimer = 0;
            }

            if (stuckTimer >= maxStuckDuration)
            {
                walkPointSet = false;
                isWaitingAtWaypoint = true;
                waitTimer = Random.Range(minWaitAtWaypointTime, maxWaitAtWaypointTime);

                isWaitingBeforeChase = false;
                chaseDelayTimer = 0f;

                agent.ResetPath();
                UpdateAnimatorStates(false, false);

                stuckTimer = 0;
            }

            lastPosition = transform.position;
            lastCheckTime = Time.time;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 origin = transform.position + Vector3.up * eyeHeight;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        Gizmos.color = visionGizmoColor;
        Gizmos.DrawSphere(origin, 0.08f);
        Gizmos.DrawWireSphere(origin, sightRange);
    }
}