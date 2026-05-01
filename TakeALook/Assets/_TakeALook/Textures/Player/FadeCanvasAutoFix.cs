using UnityEngine;
using DG.Tweening;

public class FadeCanvasAutoFix : MonoBehaviour
{
    [Header("Fade In")]
    [SerializeField] private float fadeInDuration = 1.2f;
    [SerializeField] private float startDelay = 0.05f;

    private void Start()
    {
        Invoke(nameof(StartFadeIn), startDelay);
    }

    private void StartFadeIn()
    {
        CanvasGroup[] groups = FindObjectsByType<CanvasGroup>(FindObjectsSortMode.None);

        foreach (CanvasGroup group in groups)
        {
            string lowerName = group.gameObject.name.ToLower();

            if (lowerName.Contains("fade") || lowerName.Contains("black") || group.CompareTag("FadeCanvas"))
            {
                group.DOKill();

                group.alpha = 1f;
                group.interactable = true;
                group.blocksRaycasts = true;

                group.DOFade(0f, fadeInDuration)
                    .SetUpdate(true)
                    .SetEase(Ease.InOutQuad)
                    .OnComplete(() =>
                    {
                        group.interactable = false;
                        group.blocksRaycasts = false;
                    });
            }
        }
    }
}