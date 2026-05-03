using UnityEngine;

/// <summary>
/// Pickup de linterna. Activa la linterna en GunSystem.
/// NO se guarda en inventario: se consume en el momento de la recogida.
/// </summary>
[CreateAssetMenu(fileName = "Item_Flashlight", menuName = "Game/Items/Flashlight")]
public class FlashlightItemData : ItemData
{
    public override bool Use(GameObject user) => false;

    public override bool OnPickup(GameObject user, int amount, PlayerInventory inventory)
    {
        var gun = user.GetComponentInChildren<GunSystem>();
        if (gun == null) return false;
        if (gun.HasFlashlight) return false;
        gun.HasFlashlight = true;
        AudioManager.Instance?.PlaySFX(pickupSoundId, user.transform.position);
        return true;
    }
}
