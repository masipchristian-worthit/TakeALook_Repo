using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// HUD de arma: slider de recarga (gris translúcido, ancho) y slider de cooldown de disparo (más corto).
/// Ambos aparecen al iniciar la acción y desaparecen al terminar.
/// Añade este script a un GameObject de Canvas junto con dos Slider + CanvasGroup por cada barra.
/// </summary>
public class WeaponHUD : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private GunSystem gunSystem;

    [Header("Barra de Recarga (ancha)")]
    [SerializeField] private CanvasGroup reloadBarGroup;
    [SerializeField] private Slider reloadBar;

    [Header("Barra de Cooldown de Disparo (corta)")]
    [SerializeField] private CanvasGroup shootBarGroup;
    [SerializeField] private Slider shootBar;

    [Header("Fade")]
    [SerializeField] private float fadeInDuration = 0.05f;
    [SerializeField] private float fadeOutDuration = 0.12f;

    private bool _reloadVisible;
    private bool _shootVisible;

    private void Awake()
    {
        SetAlpha(reloadBarGroup, 0f);
        SetAlpha(shootBarGroup, 0f);
    }

    private void Update()
    {
        if (gunSystem == null) return;

        // --- Barra de recarga ---
        bool showReload = gunSystem.IsReloading;
        if (showReload != _reloadVisible)
        {
            _reloadVisible = showReload;
            FadeTo(reloadBarGroup, showReload ? 1f : 0f, showReload ? fadeInDuration : fadeOutDuration);
        }
        if (reloadBar != null && showReload)
            reloadBar.value = gunSystem.ReloadProgress;

        // --- Barra de cooldown de disparo ---
        // Visible mientras se dispara o mientras el cooldown no esté completo (< 99%)
        bool showShoot = gunSystem.IsShooting ||
                         (!gunSystem.IsReloading && gunSystem.ShootCooldownProgress < 0.99f);
        if (showShoot != _shootVisible)
        {
            _shootVisible = showShoot;
            FadeTo(shootBarGroup, showShoot ? 1f : 0f, showShoot ? fadeInDuration : fadeOutDuration);
        }
        if (shootBar != null && showShoot)
            shootBar.value = gunSystem.ShootCooldownProgress;
    }

    static void SetAlpha(CanvasGroup g, float a)
    {
        if (g != null) g.alpha = a;
    }

    static void FadeTo(CanvasGroup g, float target, float duration)
    {
        if (g == null) return;
        g.DOKill();
        g.DOFade(target, duration).SetUpdate(true);
    }
}
