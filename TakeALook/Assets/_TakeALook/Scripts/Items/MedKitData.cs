using UnityEngine;

/// <summary>
/// Botiquín. Cura HP al jugador. Variantes Low/Mid/High se hacen como assets distintos
/// con healAmount diferente.
/// </summary>
[CreateAssetMenu(fileName = "Item_MedKit", menuName = "Game/Items/MedKit")]
public class MedKitData : ItemData
{
    public float healAmount = 50f;

    public override bool Use(GameObject user)
    {
        var health = user.GetComponentInChildren<PlayerHealth>();
        if (health == null) return false;

        if (Mathf.Approximately(health.CurrentHP, health.MaxHP))
        {
            AudioManager.Instance?.PlayUI(denySoundId);
            return false;
        }

        health.Heal(healAmount);
        AudioManager.Instance?.PlayUI(useSoundId);
        return true;
    }
}
