using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// Slot individual del carrusel. Representa visualmente un item.
/// Maneja transiciones cuando cambia el item asignado y el feedback de selección.
/// El comportamiento al seleccionar/deseleccionar lo gestiona el carrusel padre.
/// </summary>
public class CarouselSlot : MonoBehaviour
{
    [Header("Refs UI")]
    [SerializeField] private RectTransform root;
    [SerializeField] private Image iconImage;
    [SerializeField] private RawImage modelPreview;     // se activará cuando el item tenga modelo 3D
    [SerializeField] private TMP_Text nameLabel;
    [SerializeField] private TMP_Text countLabel;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Visual")]
    [SerializeField] private Color labelColor = Color.white;
    [SerializeField] private float labelFontSize = 18f;

    private ItemData _data;
    private int _count;
    private bool _isCenter;

    public ItemData Data => _data;
    public RectTransform Root => root != null ? root : (RectTransform)transform;

    private void Awake()
    {
        if (root == null) root = (RectTransform)transform;
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        ApplyLabelStyle();
    }

    private void ApplyLabelStyle()
    {
        if (nameLabel != null)
        {
            nameLabel.color = labelColor;
            nameLabel.fontSize = labelFontSize;
        }
        if (countLabel != null)
        {
            countLabel.color = labelColor;
            countLabel.fontSize = labelFontSize;
        }
    }

    public void SetData(ItemData data, int count)
    {
        _data = data;
        _count = count;

        if (data == null)
        {
            if (iconImage != null) { iconImage.enabled = false; }
            if (modelPreview != null) modelPreview.enabled = false;
            if (nameLabel != null) nameLabel.text = "";
            if (countLabel != null) countLabel.text = "";
            return;
        }

        if (data.previewMode == ItemData.PreviewMode.Sprite)
        {
            if (iconImage != null) { iconImage.enabled = true; iconImage.sprite = data.icon; }
            if (modelPreview != null) modelPreview.enabled = false;
        }
        else // Model3D - preview lo gestiona el ItemPreview3D singleton (sólo en el slot central)
        {
            if (iconImage != null) { iconImage.enabled = data.icon != null; iconImage.sprite = data.icon; } // fallback
            if (modelPreview != null) modelPreview.enabled = false;
        }

        if (nameLabel != null) nameLabel.text = data.displayName;
        if (countLabel != null) countLabel.text = data.isStackable ? $"x{count}" : "";
    }

    public void SetCenter(bool isCenter)
    {
        _isCenter = isCenter;
        if (canvasGroup != null) canvasGroup.alpha = isCenter ? 1f : 0.55f;
    }

    public void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
    }
}
