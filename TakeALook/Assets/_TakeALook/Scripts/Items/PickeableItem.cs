using UnityEngine;
using DG.Tweening;

/// <summary>
/// Componente para items recogibles del mundo.
/// Tiene un GUID único para persistencia entre escenas (no reaparece tras recoger).
/// Por defecto los items están QUIETOS (no rotan ni hacen bob): se quiere que respeten
/// la pose con la que fueron colocados en la escena. Si se desea reactivar el efecto
/// idle se pueden poner los flags playIdleAnim/rotationSpeed por encima de cero.
/// </summary>
[RequireComponent(typeof(Collider))]
public class PickableItem : MonoBehaviour
{
    [Header("Datos del item")]
    [SerializeField] private ItemData itemData;
    [SerializeField] private int amount = 1;

    [Header("Persistencia (auto)")]
    [SerializeField] private string uniqueId; // se genera con menú contextual

    [Header("Idle Animation (por defecto APAGADO)")]
    [Tooltip("Activa para hacer un bob vertical suave. Por defecto OFF: el item se queda quieto.")]
    [SerializeField] private bool playIdleAnim = false;
    [SerializeField] private float bobAmount = 0.1f;
    [SerializeField] private float bobSpeed = 1.5f;
    [Tooltip("Grados/seg de rotación sobre Y. Por defecto 0 = no rota.")]
    [SerializeField] private float rotationSpeed = 0f;

    [Header("Pickup Feedback")]
    [SerializeField] private GameObject pickupVFX;
    [SerializeField] private float pickupAnimTime = 0.35f;

    private Vector3 _startPos;
    private Tween _bobTween;
    private bool _alreadyPicked;

    public ItemData Data => itemData;
    public int Amount => amount;
    public string UniqueId => uniqueId;

    private void Awake()
    {
        _startPos = transform.position;
    }

    private void Start()
    {
        // Si ya fue recogido en una sesión anterior, desactivar
        if (GameManager.Instance != null && GameManager.Instance.IsItemPicked(uniqueId))
        {
            gameObject.SetActive(false);
            return;
        }

        if (playIdleAnim) StartIdleAnim();
    }

    private void StartIdleAnim()
    {
        _bobTween = transform.DOMoveY(_startPos.y + bobAmount, 1f / Mathf.Max(0.01f, bobSpeed))
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo)
            .SetLink(gameObject);
    }

    private void Update()
    {
        // Sólo rotamos si rotationSpeed > 0 (por defecto 0 = quieto).
        if (rotationSpeed > 0.0001f)
            transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);
    }

    /// <summary>
    /// Llamado cuando el jugador interactúa con este item.
    /// La lógica concreta (añadir a inventario / consumir directo) la decide ItemData.OnPickup.
    /// </summary>
    public bool TryPickup(PlayerInventory inventory)
    {
        if (_alreadyPicked || itemData == null || inventory == null) return false;

        // Cada ItemData decide si va al inventario o se consume al recoger (Pistola / Linterna).
        bool ok = itemData.OnPickup(inventory.gameObject, amount, inventory);
        if (!ok)
        {
            // Recogida rechazada (inventario lleno, ya recogida, etc): feedback de denegación.
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

        // Animación de pop antes de destruir
        _bobTween?.Kill();
        Sequence pop = DOTween.Sequence();
        pop.Append(transform.DOScale(transform.localScale * 1.3f, pickupAnimTime * 0.4f).SetEase(Ease.OutBack));
        pop.Append(transform.DOScale(Vector3.zero, pickupAnimTime * 0.6f).SetEase(Ease.InBack));
        pop.OnComplete(() => Destroy(gameObject));

        return true;
    }

    private void OnDestroy()
    {
        _bobTween?.Kill();
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
