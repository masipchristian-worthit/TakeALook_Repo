using UnityEngine;

/// <summary>
/// Pack de munición. Se guarda en el INVENTARIO (carrusel de items).
/// Al usarlo desde el carrusel, suma munición a la reserva del arma del tipo indicado y
/// se consume el pack.
/// Regla estricta: si la suma superase la reserva máxima, NO se consume el pack.
/// </summary>
[CreateAssetMenu(fileName = "Item_AmmoPack", menuName = "Game/Items/AmmoPack")]
public class AmmoPackData : ItemData
{
    public GunSystem.BulletType ammoType;
    public int amount = 12;

    public override bool Use(GameObject user)
    {
        var gun = user.GetComponentInChildren<GunSystem>();
        if (gun == null) return false;

        if (!gun.CanAddAmmoFully(ammoType, amount))
        {
            AudioManager.Instance?.PlayUI(denySoundId);
            return false;
        }

        bool added = gun.AddAmmo(ammoType, amount);
        if (added) AudioManager.Instance?.PlayUI(useSoundId);
        return added;
    }
}
