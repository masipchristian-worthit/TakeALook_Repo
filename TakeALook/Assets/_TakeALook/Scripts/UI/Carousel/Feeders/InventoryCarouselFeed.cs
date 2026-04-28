using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Alimenta un CarouselUI con los items del PlayerInventory.
/// Sincroniza el índice central con CurrentIndex del inventario.
/// </summary>
public class InventoryCarouselFeed : MonoBehaviour
{
    [SerializeField] private CarouselUI carousel;
    [SerializeField] private PlayerInventory inventory;

    private void OnEnable()
    {
        if (inventory != null) inventory.OnInventoryChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        if (inventory != null) inventory.OnInventoryChanged -= Refresh;
    }

    private void Refresh()
    {
        if (carousel == null || inventory == null) return;

        var entries = new List<CarouselUI.Entry>(inventory.Slots.Count);
        foreach (var slot in inventory.Slots)
            entries.Add(new CarouselUI.Entry { data = slot.data, count = slot.count });

        carousel.SetEntries(entries, inventory.CurrentIndex, animate: true);
    }

    /// <summary>
    /// Llamado por el UIManager cuando el jugador navega por el carrusel.
    /// Sincroniza el inventario con el carrusel.
    /// </summary>
    public void SyncSelectionFromCarousel()
    {
        if (inventory == null || carousel == null) return;
        inventory.CurrentIndex = carousel.CenterIndex;
    }
}