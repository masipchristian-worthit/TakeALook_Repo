using UnityEngine;

// Requiere: Collider con Is Trigger = true + Rigidbody en el prefab del proyectil.
// El prefab puede llevar VFX hijo y un MeshRenderer; los activamos en Awake/OnEnable
// por si se hubiesen guardado desactivados en el prefab.
[RequireComponent(typeof(Rigidbody))]
public class EnemyProjectile : MonoBehaviour
{
    [Header("Damage")]
    [SerializeField] float damage = 15f;
    [SerializeField] float lifetime = 6f;

    [Header("Motion (fallback if Launch isn't called)")]
    [Tooltip("Velocidad por defecto si nadie llama a Launch(). Se reemplaza al llamar Launch().")]
    [SerializeField] float fallbackSpeed = 18f;

    [Header("Impact")]
    [SerializeField] GameObject impactVFX;
    [SerializeField] string sfxImpactId = "enemy_projectile_hit";

    private Rigidbody _rb;
    private bool _launched;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (_rb != null)
        {
            _rb.useGravity = false;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        // Asegura que renderers y VFX hijos se vean.
        Renderer[] rends = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++)
            if (rends[i] != null) rends[i].enabled = true;

        ParticleSystem[] ps = GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < ps.Length; i++)
        {
            if (ps[i] == null) continue;
            if (!ps[i].gameObject.activeSelf) ps[i].gameObject.SetActive(true);
            ps[i].Play(true);
        }
    }

    void OnEnable()
    {
        Destroy(gameObject, lifetime);
    }

    void Start()
    {
        // Si nadie llamó a Launch (por ej. testing manual), aplicamos un movimiento por defecto.
        if (!_launched && _rb != null)
        {
#if UNITY_6000_0_OR_NEWER
            _rb.linearVelocity = transform.forward * fallbackSpeed;
#else
            _rb.velocity = transform.forward * fallbackSpeed;
#endif
        }
    }

    /// <summary>
    /// El AI llama a esto justo después de Instantiate para garantizar que el proyectil
    /// se mueva con la velocidad correcta sin depender del orden de Awake/Start.
    /// </summary>
    public void Launch(Vector3 velocity)
    {
        if (_rb == null) _rb = GetComponent<Rigidbody>();
        if (_rb != null)
        {
            _rb.useGravity = false;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
#if UNITY_6000_0_OR_NEWER
            _rb.linearVelocity = velocity;
#else
            _rb.velocity = velocity;
#endif
        }
        if (velocity.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(velocity.normalized);
        _launched = true;
    }

    void OnTriggerEnter(Collider other)
    {
        // Player hitbox
        if (other.CompareTag("PlayerHitbox") || other.CompareTag("Player"))
        {
            PlayerHealth health = other.GetComponentInParent<PlayerHealth>();
            if (health == null) health = other.GetComponent<PlayerHealth>();
            health?.TakeDamage(damage);
            Impact(other.ClosestPoint(transform.position), -transform.forward);
            return;
        }

        // Si choca con un objeto que no es el propio enemigo emisor, también explotamos
        // (paredes, suelo, puertas, etc). Esto evita que atraviese muros.
        if (!other.isTrigger)
        {
            Impact(other.ClosestPoint(transform.position), -transform.forward);
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        Vector3 point = collision.contactCount > 0 ? collision.contacts[0].point : transform.position;
        Vector3 normal = collision.contactCount > 0 ? collision.contacts[0].normal : -transform.forward;
        Impact(point, normal);
    }

    void Impact(Vector3 point, Vector3 normal)
    {
        if (impactVFX != null)
            Instantiate(impactVFX, point, Quaternion.LookRotation(normal));
        if (!string.IsNullOrEmpty(sfxImpactId))
            AudioManager.Instance?.PlaySFX(sfxImpactId, point);
        Destroy(gameObject);
    }
}
