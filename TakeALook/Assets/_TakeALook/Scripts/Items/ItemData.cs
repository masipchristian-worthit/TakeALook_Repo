using UnityEngine;

/// <summary>
/// Datos base de un item. Soporta tanto sprite 2D como modelo 3D (configurable por item).
/// El campo previewMode determina qué muestra el carrusel cuando este item está activo.
/// </summary>
public abstract class ItemData : ScriptableObject
{
    public enum PreviewMode { Sprite, Model3D }

    [Header("Identificación")]
    public string itemId; // string único, ej: "medkit_small"
    public string displayName = "Item";
    [TextArea] public string description;

    [Header("Visualización")]
    public PreviewMode previewMode = PreviewMode.Sprite;
    public Sprite icon;          // Para previewMode = Sprite
    public GameObject model3D;   // Para previewMode = Model3D - prefab que se instancia en el preview area

    [Header("Comportamiento")]
    public bool isStackable = true;
    public int maxStack = 99;
    public bool consumeOnUse = true;

    [Header("Audio")]
    public string pickupSoundId = "pickup_item";
    public string useSoundId = "ui_use";
    [Tooltip("SFX cuando el uso/recogida es rechazado (vida llena, reserva llena, etc).")]
    public string denySoundId = "ui_deny";

    /// <summary>
    /// Llamado al pulsar UIInteract con este item seleccionado en el carrusel del inventario.
    /// Devuelve true si se consumió el uso (para descontar el stack).
    /// Para items que NO se usan desde inventario (Pistola, Linterna), devuelve false.
    /// </summary>
    public abstract bool Use(GameObject user);

    /// <summary>
    /// Llamado cuando el jugador recoge un PickableItem del mundo.
    /// Comportamiento por defecto: añadir al inventario.
    /// Override para items que se "consumen" inmediatamente al recoger (Pistola, Linterna).
    /// Devuelve true si la recogida fue válida (item se destruye y suena pickup).
    /// </summary>
    public virtual bool OnPickup(GameObject user, int amount, PlayerInventory inventory)
    {
        if (inventory == null) return false;
        return inventory.AddItem(this, amount);
    }
}

/// <summary>
/// Botiquín. Cura HP al jugador. Variantes Low/Mid/High se hacen como assets distintos
/// con healAmount diferente — la lógica es la misma.
/// </summary>
[CreateAssetMenu(fileName = "Item_MedKit", menuName = "Game/Items/MedKit")]
public class MedKitData : ItemData
{
    public float healAmount = 50f;

    public override bool Use(GameObject user)
    {
        var health = user.GetComponentInChildren<PlayerHealth>();
        if (health == null) return false;

        // No se puede curar al máximo: feedback de denegación.
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

/// <summary>
/// Pack de munición. Se guarda en el INVENTARIO (carrusel de items).
/// Al usarlo desde el carrusel, suma munición a la reserva del arma del tipo indicado y
/// se consume el pack. El carrusel de munición se actualiza automáticamente porque
/// AmmoCarouselFeed hace polling de las reservas.
///
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

        // Estricto: si no cabe el pack entero, denegamos (no se consume parcialmente).
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

/// <summary>
/// Pickup de pistola. Activa la posibilidad de sacar/usar el arma.
/// NO se guarda en inventario: se consume en el momento de la recogida.
/// </summary>
[CreateAssetMenu(fileName = "Item_Pistol", menuName = "Game/Items/Pistol")]
public class PistolItemData : ItemData
{
    public override bool Use(GameObject user) => false; // no usable desde inventario

    public override bool OnPickup(GameObject user, int amount, PlayerInventory inventory)
    {
        var gun = user.GetComponentInChildren<GunSystem>();
        if (gun == null) return false;
        if (gun.HasPistol) return false; // ya recogida
        gun.HasPistol = true;
        AudioManager.Instance?.PlaySFX(pickupSoundId, user.transform.position);
        return true;
    }
}

/// <summary>
/// Pickup de linterna. Activa la linterna en GunSystem.
/// NO se guarda en inventario: se consume en el momento de la recogida.
/// </summary>
[CreateAssetMenu(fileName = "Item_Flashlight", menuName = "Game/Items/Flashlight")]
public class FlashlightItemData : ItemData
{
    public override bool Use(GameObject user) => false; // no usable desde inventario

    public override bool OnPickup(GameObject user, int amount, PlayerInventory inventory)
    {
        var gun = user.GetComponentInChildren<GunSystem>();
        if (gun == null) return false;
        if (gun.HasFlashlight) return false; // ya recogida
        gun.HasFlashlight = true;
        AudioManager.Instance?.PlaySFX(pickupSoundId, user.transform.position);
        return true;
    }
}
