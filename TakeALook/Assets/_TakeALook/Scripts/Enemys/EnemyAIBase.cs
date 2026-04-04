using UnityEngine;
using UnityEngine.AI; //Libreria de componentes NavMesh

public class EnemyAIBase : MonoBehaviour
{
    #region General Variables
    [Header("AI Configuration")]
    [SerializeField] NavMeshAgent agent; //Ref al cerebro del agente
    [SerializeField] Transform target; //Ref al target a perseguir (Variable)
    [SerializeField] LayerMask targetLayer; //Define layer del target (Detecciones)
    [SerializeField] LayerMask groundLayer; //Define layer del suelo (Evita ir a zonas sin suelo)

    [Header("Patroling Stats")]
    [SerializeField] float walkPointRange = 10f; //Radio máximo para determinar puntos a perseguir
    Vector3 walkPoint; //Posición del punto random a perseguir
    bool walkPointSet; //Hay punto a perseguir generado? Si es false, genera uno

    [Header("Attacking Stats")]
    [SerializeField] float timeBetweenAttacks = 1f; //Cooldown entre ataques
    [SerializeField] GameObject projectile; //Ref a la bala física que dispara el enemigo
    [SerializeField] Transform shootPoint; //Posición desde la que dispara la bala
    [SerializeField] float shootSpeedY; //Fuerza de disparo hacia arriba (Catapulta)
    [SerializeField] float shootSpeedZ = 10f; //Fuerza de disparp hacia adelante (Siempre está)
    bool alreadyAttacked; //Si es verdadero no stackea ataques y entra en esperaa entre ataques

    [Header("States & Detection")]
    [SerializeField] float sightRange = 8f; //Radio del detector de persecución
    [SerializeField] float attackRange = 2f; //Radio del detector de ataque
    [SerializeField] bool targetInSightRange; //Determina si es verdadero que podemos perseguir al target
    [SerializeField] bool targetInAttackRange; //Determina si es verdadero que podemos atacar al target

    [Header("Stuck Detection")]
    [SerializeField] float stuckCheckTime = 2f; //Tiempo que el agente espera estando quieto antes de darse cuenta de que está stuck
    [SerializeField] float stuckThreshold = 0.1f; //Margen de detección de stuck
    [SerializeField] float maxStuckDuration = 3f; //Tiempo máximo de estar stuck

    float stuckTimer; //Reloj que cuanta el tiempo de estar stuck
    float lastCheckTime; //Tiempo de checkeo previo de stuck
    Vector3 lastPosition; //Posición del último walkpoint perseguido
    #endregion

    // Start is called once before the first execution of Update after the MonoBehaviour is created

    private void Awake()
    {
        target = GameObject.Find("Player").transform;
        agent = GetComponent<NavMeshAgent>();
        lastPosition = transform.position;
        lastCheckTime = Time.time;
    }

    // Update is called once per frame
    void Update()
    {
        EnemyStateUpdater();
    }

    void EnemyStateUpdater()
    {
        //Metodo que se encarga de gestionar el cambio de estados del enemigo

        //1 - Cambio de estado de los bools
        //Primero detectamos si los targets están en visión
        Collider[] hits = Physics.OverlapSphere(transform.position, sightRange, targetLayer);
        targetInSightRange = hits.Length > 0;
        //Segundo, si están en visión detectamos si además están en ataque
        if (targetInSightRange)
        {
            float distance = Vector3.Distance(transform.position, target.position);
            targetInAttackRange = distance <= attackRange;
        }
        else
        {
            targetInAttackRange = false;
        }


        //2 - Cambio de estados según booleanos
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

    void Patroling()
    {
        Debug.Log("Enemigo en estado patrulla");
    }

    void ChaseTarget()
    {
        //Acción que le dice al agente que persiga al target
        agent.SetDestination(target.position);
    }

    void AttackTarget()
    {
        //Acción que contiene la logica de ataque
        //1 - Hacer que el agente se quede quieto (perseguirse a si mismo)
        agent.SetDestination(transform.position);
        //2 - Aplicar una rotación suavizada para que el agente mire al target antess de atacar
        Vector3 direction = (target.position - transform.position).normalized;
        if (direction != Vector3.zero)
        {
            Quaternion lookRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, lookRotation, agent.angularSpeed * Time.deltaTime);
        }

        //3 - Se ataca (solo si no se está atacando)
        if (!alreadyAttacked)
        {
            Rigidbody rb = Instantiate(projectile, shootPoint.position, Quaternion.identity).GetComponent<Rigidbody>();
            rb.AddForce(transform.forward * shootSpeedZ, ForceMode.Impulse);
            alreadyAttacked = true;
            Invoke(nameof(ResetAattack), timeBetweenAttacks);
        }
    }
    void ResetAattack()
    {
        alreadyAttacked = false;
    }

    private void OnDrawGizmosSelected()
    {
        if (Application.isPlaying) return; //Si estamos jugando en build, no se ejecuta el resto del código

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, sightRange);
    }

}
