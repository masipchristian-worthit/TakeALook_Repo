using UnityEngine;

/// <summary>
/// Datos visuales/identificativos de un tipo de bala (Wolf, Bull, Eagle).
/// Estos NO están en el inventario - se usan sólo para representarlos en el carrusel de munición.
/// Su Use() cambia el tipo activo del arma.
/// </summary>
[CreateAssetMenu(fileName = "AmmoType_", menuName = "Game/Items/Ammo Type Display")]
public class AmmoTypeData : ItemData
{
    public GunSystem.BulletType bulletType;

    public override bool Use(GameObject user)
    {
        var gun = user.GetComponentInChildren<GunSystem>();
        if (gun == null) return false;
        gun.SetBulletType(bulletType);
        AudioManager.Instance?.PlayUI("gun_swap");
        return true; // no se consume; consumeOnUse debería ser false en el asset
    }
}