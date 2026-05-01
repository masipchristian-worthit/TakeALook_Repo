using UnityEngine;

public class SpaceBackgroundMotion : MonoBehaviour
{
    [Header("Movimiento suave")]
    [SerializeField] private float moveAmountX = 25f;
    [SerializeField] private float moveAmountY = 12f;
    [SerializeField] private float moveSpeedX = 0.08f;
    [SerializeField] private float moveSpeedY = 0.05f;

    [Header("Zoom suave")]
    [SerializeField] private bool useZoom = true;
    [SerializeField] private float zoomAmount = 0.025f;
    [SerializeField] private float zoomSpeed = 0.06f;

    private RectTransform rectTransform;
    private Vector2 startAnchoredPosition;
    private Vector3 startScale;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        startAnchoredPosition = rectTransform.anchoredPosition;
        startScale = rectTransform.localScale;
    }

    private void Update()
    {
        float offsetX = Mathf.Sin(Time.time * moveSpeedX) * moveAmountX;
        float offsetY = Mathf.Cos(Time.time * moveSpeedY) * moveAmountY;

        rectTransform.anchoredPosition = startAnchoredPosition + new Vector2(offsetX, offsetY);

        if (useZoom)
        {
            float zoom = 1f + Mathf.Sin(Time.time * zoomSpeed) * zoomAmount;
            rectTransform.localScale = startScale * zoom;
        }
    }
}