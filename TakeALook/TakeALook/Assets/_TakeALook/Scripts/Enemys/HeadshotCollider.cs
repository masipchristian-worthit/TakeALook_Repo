using UnityEngine;

public class HeadshotCollider : MonoBehaviour
{
    [SerializeField] private EnemyHealth target;
    [SerializeField] private float damageMultiplier = 2f;

    public EnemyHealth Target => target;

    private void OnValidate()
    {
        if (target == null)
            target = GetComponentInParent<EnemyHealth>();
    }

    public void ApplyDamage(int damage, bool isBullBullet)
    {
        if (target != null)
        {
            int multipliedDamage = Mathf.RoundToInt(damage * damageMultiplier);
            target.TakeDamage(multipliedDamage, isBullBullet);
        }
    }
}
