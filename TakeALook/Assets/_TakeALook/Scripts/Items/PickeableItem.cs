using UnityEngine;

/// <summary>
/// Componente para items recogibles del mundo.
/// Tiene un GUID único para persistencia entre escenas (no reaparece tras recoger).
/// Los items SIEMPRE están quietos: respetan la pose con la que se colocaron en escena.
/// No hay bob ni rotación idle ni animación de pickup — se eliminó por diseño para
/// que todos los prefabs existentes se comporten igual sin tener que tocarlos uno a uno.
/// </summary>
[RequireComponent(typeof(Collider))]
public class PickableItem : MonoBehaviour
{
    [Header("Datos del item")]
    [SerializeField] private ItemData itemData;
    [SerializeField] private int amount = 1;

    [Header("Persistencia (auto)")]
    [SerializeField] private string uniqueId; // se genera con menú contextual

    [Header("Pickup Feedback")]
    [SerializeField] private GameObject pickupVFX;

    private bool _alreadyPicked;

    public ItemData Data => itemData;
    public int Amount => amount;
    public string UniqueId => uniqueId;

    private void Start()
    {
        if (GameManager.Instance != null && GameManager.Instance.IsItemPicked(uniqueId))
            gameObject.SetActive(false);
    }

    /// <summary>
    /// Llamado cuando el jugador interactúa con este item.
    /// La lógica concreta (añadir a inventario / consumir directo) la decide ItemData.OnPickup.
    /// </summary>
    public bool TryPickup(PlayerInventory inventory)
    {
        if (_alreadyPicked || itemData == null || inventory == null) return false;

        bool ok = itemData.OnPickup(inventory.gameObject, amount, inventory);
        if (!ok)
        {
            AudioManager.Instance?.PlaySFX(itemData.denySoundId, transform.position);
            return false;
        }

        _alreadyPicked = true;
        GameManager.Instance?.RegisterPickedItem(uniqueId);

        // El sonido de recogida lo dispara OnPickup de los items que se consumen al recoger
        // (Pistola/Linterna). Para los que van al inventario, lo disparamos aquí.
        if (!(itemData is PistolItemData) && !(itemData is FlashlightItemData))
            AudioManager.Instance?.PlaySFX(itemData.pickupSoundId, transform.position);

        if (pickupVFX != null) Instantiate(pickupVFX, transform.position, Quaternion.identity);

        Destroy(gameObject);
        return true;
    }

    [ContextMenu("Generar GUID único")]
    private void GenerateGuid()
    {
        uniqueId = System.Guid.NewGuid().ToString();
    }

    private void OnValidate()
    {
        if (string.IsNullOrEmpty(uniqueId))
            uniqueId = System.Guid.NewGuid().ToString();
    }
}
