using UnityEngine;

// Requiere: Collider con Is Trigger = true + Rigidbody en el prefab del proyectil
[RequireComponent(typeof(Rigidbody))]
public class EnemyProjectile : MonoBehaviour
{
    [SerializeField] float damage = 15f;
    [SerializeField] float lifetime = 6f;

    void Start() => Destroy(gameObject, lifetime);

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("PlayerHitbox")) return;

        PlayerHealth health = other.GetComponentInParent<PlayerHealth>();
        if (health == null) health = other.GetComponent<PlayerHealth>();
        health?.TakeDamage(damage);

        Destroy(gameObject);
    }
}