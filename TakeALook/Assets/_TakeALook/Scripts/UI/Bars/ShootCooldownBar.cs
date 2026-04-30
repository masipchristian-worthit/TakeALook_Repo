using UnityEngine;
using UnityEngine.UI;

// Coloca este script en un GameObject de UI y asigna el Slider y el GunSystem.
// El Slider debe tener minValue=0, maxValue=1.
// La barra arranca al disparar (llena) y se vacía cuando el cooldown termina.
public class ShootCooldownBar : MonoBehaviour
{
    [SerializeField] Slider slider;
    [SerializeField] GunSystem gunSystem;

    void Awake()
    {
        if (gunSystem == null) gunSystem = FindFirstObjectByType<GunSystem>();
        if (slider != null) { slider.minValue = 0f; slider.maxValue = 1f; slider.value = 0f; }
    }

    void Update()
    {
        if (gunSystem == null || slider == null) return;
        slider.value = gunSystem.ShootCooldownNormalized;
    }
}