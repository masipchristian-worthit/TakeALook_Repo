using UnityEngine;
using DG.Tweening;

/// <summary>
/// Componente para items recogibles del mundo.
/// Tiene un GUID único para persistencia entre escenas (no reaparece tras recoger).
/// Incluye animación idle suave (bob + rotación) y feedback al recoger.
/// </summary>
[RequireComponent(typeof(Collider))]
public class PickableItem : MonoBehaviour
{
    [Header("Datos del item")]
    [SerializeField] private ItemData itemData;
    [SerializeField] private int amount = 1;

    [Header("Persistencia (auto)")]
    [SerializeField] private string uniqueId; // se genera con menú contextual

    [Header("Idle Animation")]
    [SerializeField] private bool playIdleAnim = true;
    [SerializeField] private float bobAmount = 0.1f;
    [SerializeField] private float bobSpeed = 1.5f;
    [SerializeField] private float rotationSpeed = 30f;

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
        if (rotationSpeed != 0f)
            transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);
    }

    /// <summary>
    /// Llamado cuando el jugador interactúa con este item.
    /// </summary>
    public bool TryPickup(PlayerInventory inventory)
    {
        if (_alreadyPicked || itemData == null || inventory == null) return false;

        bool added = inventory.AddItem(itemData, amount);
        if (!added) return false;

        _alreadyPicked = true;
        GameManager.Instance?.RegisterPickedItem(uniqueId);
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
