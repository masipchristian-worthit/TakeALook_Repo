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

    /// <summary>
    /// Llamado al pulsar UIInteract con este item seleccionado.
    /// Devuelve true si se consumió el uso (para descontar).
    /// </summary>
    public abstract bool Use(GameObject user);
}

/// <summary>
/// Botiquín. Cura HP al jugador.
/// </summary>
[CreateAssetMenu(fileName = "Item_MedKit", menuName = "Game/Items/MedKit")]
public class MedKitData : ItemData
{
    public float healAmount = 50f;

    public override bool Use(GameObject user)
    {
        var health = user.GetComponent<PlayerHealth>();
        if (health == null) return false;
        if (Mathf.Approximately(health.CurrentHP, health.MaxHP)) return false; // ya al máximo

        health.Heal(healAmount);
        AudioManager.Instance?.PlayUI(useSoundId);
        return true;
    }
}

/// <summary>
/// Pack de munición. Añade balas al cargador de reserva del tipo indicado.
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

        bool added = gun.AddAmmo(ammoType, amount);
        if (added) AudioManager.Instance?.PlayUI(useSoundId);
        return added;
    }
}

/// <summary>
/// Pickup de linterna. Activa la linterna en GunSystem.
/// </summary>
[CreateAssetMenu(fileName = "Item_Flashlight", menuName = "Game/Items/Flashlight")]
public class FlashlightItemData : ItemData
{
    public override bool Use(GameObject user)
    {
        var gun = user.GetComponentInChildren<GunSystem>();
        if (gun == null) return false;
        gun.HasFlashlight = true;
        AudioManager.Instance?.PlayUI(useSoundId);
        return true;
    }
}
