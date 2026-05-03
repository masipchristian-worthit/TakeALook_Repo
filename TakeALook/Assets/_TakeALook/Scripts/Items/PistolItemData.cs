using UnityEngine;

/// <summary>
/// Pickup de pistola. Activa la posibilidad de sacar/usar el arma.
/// NO se guarda en inventario: se consume en el momento de la recogida.
/// </summary>
[CreateAssetMenu(fileName = "Item_Pistol", menuName = "Game/Items/Pistol")]
public class PistolItemData : ItemData
{
    public override bool Use(GameObject user) => false;

    public override bool OnPickup(GameObject user, int amount, PlayerInventory inventory)
    {
        var gun = user.GetComponentInChildren<GunSystem>();
        if (gun == null) return false;
        if (gun.HasPistol) return false;
        gun.HasPistol = true;
        AudioManager.Instance?.PlaySFX(pickupSoundId, user.transform.position);
        return true;
    }
}
