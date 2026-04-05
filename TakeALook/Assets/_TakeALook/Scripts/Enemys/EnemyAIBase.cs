using UnityEngine;
using UnityEngine.AI;

public class EnemyAIBase : MonoBehaviour
{
    #region General Variables
    [Header("AI Configuration")]
    [SerializeField] NavMeshAgent agent;
    [SerializeField] Transform target;
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
    [SerializeField, Range(0f, 1f)] float chanceToSkipWaitAtWaypoint = 0.25f; // 1/4 de probabilidad
    bool isWaitingAtWaypoint;
    float waitTimer;

    [Header("Chase Delay")]
    [SerializeField] float minChaseDelay = 1f;
    [SerializeField] float maxChaseDelay = 2f;
    bool isWaitingBeforeChase;
    float chaseDelayTimer;

    [Header("Attacking Stats")]
    [SerializeField] float timeBetweenAttacks = 1f;
    [SerializeField] GameObject projectile;
    [SerializeField] Transform shootPoint;
    [SerializeField] float shootSpeedY;
    [SerializeField] float shootSpeedZ = 10f;
    bool alreadyAttacked;

    [Header("Vision Cone")]
    [SerializeField] float sightRange = 8f;
    [SerializeField, Range(0f, 360f)] float sightAngle = 90f;
    [SerializeField] float eyeHeight = 1.5f;
    [SerializeField] Color visionGizmoColor = new Color(1f, 0f, 0f, 0.25f);

    [Header("Attack Range")]
    [SerializeField] float attackRange = 2f;
    [SerializeField] bool targetInSightRange;
    [SerializeField] bool targetInAttackRange;

    [Header("Movement Speed")]
    [SerializeField] float patrolSpeed = 1.5f;   // antes 2f
    [SerializeField] float chaseSpeed = 2.2f;    // antes 3.2f

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
        }

        if (!targetInSightRange && !targetInAttackRange)
        {
            Patroling();
        }
        else if (targetInSightRange && !targetInAttackRange)
        {
            ChaseTarget();
        }
        else if (targetInSightRange && targetInAttackRange)
        {
            AttackTarget();
        }
    }

    bool CanSeeTarget()
    {
        if (target == null) return false;

        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        Vector3 targetPos = target.position;

        Collider[] hits = Physics.OverlapSphere(origin, sightRange, targetLayer);
        bool targetInsideRange = false;

        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].transform == target)
            {
                targetInsideRange = true;
                break;
            }
        }

        if (!targetInsideRange) return false;

        Vector3 directionToTarget = (targetPos - origin).normalized;
        float angleToTarget = Vector3.Angle(transform.forward, directionToTarget);

        if (angleToTarget > sightAngle * 0.5f)
            return false;

        return true;
    }

    void Patroling()
    {
        if (!agent.isOnNavMesh) return;

        agent.speed = patrolSpeed;

        if (isWaitingAtWaypoint)
        {
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

        if (!isWaitingBeforeChase)
        {
            isWaitingBeforeChase = true;
            chaseDelayTimer = Random.Range(minChaseDelay, maxChaseDelay);

            agent.ResetPath();
            return;
        }

        if (chaseDelayTimer > 0f)
        {
            chaseDelayTimer -= Time.deltaTime;
            agent.ResetPath();
            return;
        }

        agent.SetDestination(target.position);
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

        if (!alreadyAttacked)
        {
            Rigidbody rb = Instantiate(projectile, shootPoint.position, Quaternion.identity).GetComponent<Rigidbody>();
            rb.AddForce(transform.forward * shootSpeedZ + transform.up * shootSpeedY, ForceMode.Impulse);

            alreadyAttacked = true;
            Invoke(nameof(ResetAttack), timeBetweenAttacks);
        }
    }

    void ResetAttack()
    {
        alreadyAttacked = false;
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
                isWaitingAtWaypoint = false;
                isWaitingBeforeChase = false;
                agent.ResetPath();
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

        Vector3 leftBoundary = DirFromAngle(-sightAngle * 0.5f);
        Vector3 rightBoundary = DirFromAngle(sightAngle * 0.5f);

        Gizmos.DrawRay(origin, leftBoundary * sightRange);
        Gizmos.DrawRay(origin, rightBoundary * sightRange);
        Gizmos.DrawRay(origin, transform.forward * sightRange);

        int segments = 30;
        Vector3 previousPoint = origin + DirFromAngle(-sightAngle * 0.5f) * sightRange;

        for (int i = 1; i <= segments; i++)
        {
            float currentAngle = Mathf.Lerp(-sightAngle * 0.5f, sightAngle * 0.5f, i / (float)segments);
            Vector3 nextPoint = origin + DirFromAngle(currentAngle) * sightRange;

            Gizmos.DrawLine(origin, nextPoint);
            Gizmos.DrawLine(previousPoint, nextPoint);

            previousPoint = nextPoint;
        }
    }

    Vector3 DirFromAngle(float angleInDegrees)
    {
        float totalAngle = transform.eulerAngles.y + angleInDegrees;
        return new Vector3(Mathf.Sin(totalAngle * Mathf.Deg2Rad), 0f, Mathf.Cos(totalAngle * Mathf.Deg2Rad));
    }
}