using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class EnemyProjectile : MonoBehaviour
{
    [Header("Damage")]
    [SerializeField] float damage = 15f;
    [SerializeField] float lifetime = 6f;

    [Header("Motion")]
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

        Renderer[] rends = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++) if (rends[i] != null) rends[i].enabled = true;

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
        if (!_launched && _rb != null)
        {
            _rb.linearVelocity = transform.forward * fallbackSpeed;
        }
    }

    void Update()
    {
        if (_rb == null || _rb.linearVelocity.sqrMagnitude < 0.1f)
        {
            transform.position += transform.forward * fallbackSpeed * Time.deltaTime;
        }
    }

    public void Launch(Vector3 velocity)
    {
        if (_rb == null) _rb = GetComponent<Rigidbody>();
        if (_rb != null)
        {
            _rb.useGravity = false;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.linearVelocity = velocity;
        }
        if (velocity.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(velocity.normalized);
        _launched = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("PlayerHitbox") || other.CompareTag("Player"))
        {
            PlayerHealth health = other.GetComponentInParent<PlayerHealth>() ?? other.GetComponent<PlayerHealth>();
            health?.TakeDamage(damage);

            if (impactVFX != null) Instantiate(impactVFX, transform.position, Quaternion.identity);
            if (!string.IsNullOrEmpty(sfxImpactId)) AudioManager.Instance?.PlaySFX(sfxImpactId, transform.position);

            Destroy(gameObject);
            return;
        }

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
        if (impactVFX != null) Instantiate(impactVFX, point, Quaternion.LookRotation(normal));
        if (!string.IsNullOrEmpty(sfxImpactId)) AudioManager.Instance?.PlaySFX(sfxImpactId, point);
        Destroy(gameObject);
    }
}