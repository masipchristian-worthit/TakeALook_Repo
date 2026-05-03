using UnityEngine;

public abstract class ItemData : ScriptableObject
{
    public enum PreviewMode { Sprite, Model3D }

    [Header("Identificación")]
    public string itemId;
    public string displayName = "Item";
    [TextArea] public string description;

    [Header("Visualización")]
    public PreviewMode previewMode = PreviewMode.Sprite;
    public Sprite icon;
    public GameObject model3D;

    [Header("Comportamiento")]
    public bool isStackable = true;
    public int maxStack = 99;
    public bool consumeOnUse = true;

    [Header("Audio")]
    public string pickupSoundId = "pickup_item";
    public string useSoundId = "ui_use";
    [Tooltip("SFX cuando el uso/recogida es rechazado (vida llena, reserva llena, etc).")]
    public string denySoundId = "ui_deny";

    public abstract bool Use(GameObject user);

    public virtual bool OnPickup(GameObject user, int amount, PlayerInventory inventory)
    {
        if (inventory == null) return false;
        return inventory.AddItem(this, amount);
    }
}
