using UnityEngine;

// Asignar este script al GameObject "HeadShot Collider" del enemigo.
// El GunSystem detecta este componente al hacer raycast y aplica el multiplicador
// de daño antes de avisar al EnemyHealth correspondiente.
[RequireComponent(typeof(Collider))]
public class HeadshotCollider : MonoBehaviour
{
    [Header("Damage")]
    [Tooltip("Multiplicador aplicado al daño base de la bala cuando impacta este collider.")]
    [SerializeField, Min(0f)] float damageMultiplier = 2f;

    [Header("Refs")]
    [Tooltip("EnemyHealth al que se reenvía el daño. Si está vacío se busca en el padre.")]
    [SerializeField] EnemyHealth target;

    public float DamageMultiplier => damageMultiplier;
    public EnemyHealth Target => target;

    void Awake()
    {
        if (target == null) target = GetComponentInParent<EnemyHealth>();
    }

    public void ApplyDamage(int baseDamage, bool isBullBullet)
    {
        if (target == null) return;
        int finalDamage = Mathf.Max(0, Mathf.RoundToInt(baseDamage * damageMultiplier));
        target.TakeDamage(finalDamage, isBullBullet);
    }
}
