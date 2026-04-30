using UnityEngine;
using UnityEngine.UI;

// Coloca este script en un GameObject de UI y asigna el Slider y el FPS_Controller.
// El Slider debe tener minValue=0, maxValue=1.
// La barra se reduce al correr y se recarga al parar.
public class SprintStaminaBar : MonoBehaviour
{
    [SerializeField] Slider slider;
    [SerializeField] FPS_Controller controller;

    void Awake()
    {
        if (controller == null) controller = FindFirstObjectByType<FPS_Controller>();
        if (slider != null) { slider.minValue = 0f; slider.maxValue = 1f; slider.value = 1f; }
    }

    void Update()
    {
        if (controller == null || slider == null) return;
        slider.value = controller.SprintStaminaNormalized;
    }
}
