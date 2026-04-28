using UnityEngine;

public class EnemyHealth : MonoBehaviour
{
    [Header("Salud")]
    [SerializeField] int maxHealth = 100;

    [Header("Feedback Visual")]
    [SerializeField] Material damagedMat;
    [SerializeField] MeshRenderer enemyRend;
    [SerializeField] GameObject deathVfx;

    [Header("VFX de Impacto (salpicadura de sangre)")]
    [Tooltip("Prefab de partículas que se instancia en el punto de impacto del raycast.")]
    [SerializeField] GameObject bloodSplatterPrefab;

    [Header("Stun - Bala Bull")]
    [SerializeField] float bullStunDuration = 2f;
    [Tooltip("Referencia al componente EnemyAIBase del mismo GameObject.")]
    [SerializeField] EnemyAIBase aiBase;

    private int _health;
    private Material _baseMat;
    private bool _isDead;

    private void Awake()
    {
        _health = maxHealth;
        if (enemyRend != null) _baseMat = enemyRend.material;
        if (aiBase == null) aiBase = GetComponent<EnemyAIBase>();
    }

    /// <summary>
    /// Aplica daño al enemigo.
    /// Llamado desde GunSystem.ExecuteRaycast con tipo de bala y datos de impacto.
    /// </summary>
    public void TakeDamage(int damage,
                           GunSystem.BulletType bulletType = GunSystem.BulletType.Wolf,
                           Vector3 hitPoint = default,
                           Vector3 hitNormal = default)
    {
        if (_isDead) return;

        _health -= damage;

        // Salpicadura de sangre en el punto de impacto
        if (bloodSplatterPrefab != null && hitNormal != Vector3.zero)
        {
            var vfx = Instantiate(bloodSplatterPrefab, hitPoint,
                Quaternion.LookRotation(hitNormal));
            Destroy(vfx, 5f);
        }

        // Bala Bull: stun al enemigo
        if (bulletType == GunSystem.BulletType.Bull && aiBase != null)
            aiBase.Stun(bullStunDuration);

        // Flash de material
        if (enemyRend != null && damagedMat != null)
        {
            enemyRend.material = damagedMat;
            Invoke(nameof(ResetMat), 0.1f);
        }

        if (_health <= 0) Die();
    }

    private void Die()
    {
        if (_isDead) return;
        _isDead = true;

        if (deathVfx != null)
        {
            deathVfx.SetActive(true);
            deathVfx.transform.position = transform.position;
        }

        gameObject.SetActive(false);
    }

    private void ResetMat()
    {
        if (enemyRend != null && _baseMat != null)
            enemyRend.material = _baseMat;
    }
}
