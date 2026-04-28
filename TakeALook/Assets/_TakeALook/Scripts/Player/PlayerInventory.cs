using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Inventario del jugador. Lista de slots con datos + cantidad.
/// Soporta items iniciales para testing (configurables en inspector).
/// </summary>
public class PlayerInventory : MonoBehaviour
{
    [Serializable]
    public class InventorySlot
    {
        public ItemData data;
        public int count;
    }

    [Serializable]
    public class StartingItem
    {
        public ItemData data;
        public int amount = 1;
        [Tooltip("Desactiva esto cuando termines de testear para que no aparezca al iniciar.")]
        public bool enabled = true;
    }

    [Header("Inventario")]
    [SerializeField] private List<InventorySlot> slots = new List<InventorySlot>();
    [SerializeField] private int maxSlots = 12;

    [Header("Test Loadout (desactivar al pasar a producción)")]
    [SerializeField] private List<StartingItem> startingItems = new List<StartingItem>();

    public IReadOnlyList<InventorySlot> Slots => slots;

    public event Action OnInventoryChanged;
    public event Action<ItemData, int> OnItemAdded;
    public event Action<ItemData> OnItemUsed;

    private int _currentIndex;
    public int CurrentIndex
    {
        get => _currentIndex;
        set
        {
            if (slots.Count == 0) { _currentIndex = 0; return; }
            // Wrap-around
            int n = slots.Count;
            _currentIndex = ((value % n) + n) % n;
        }
    }

    public InventorySlot CurrentSlot => slots.Count == 0 ? null : slots[_currentIndex];

    private void Start()
    {
        // Cargar items iniciales para testing
        foreach (var s in startingItems)
        {
            if (s.enabled && s.data != null) AddItem(s.data, s.amount);
        }
    }

    public bool AddItem(ItemData data, int amount = 1)
    {
        if (data == null || amount <= 0) return false;

        // Si es stackable, intentar añadir a slot existente
        if (data.isStackable)
        {
            foreach (var slot in slots)
            {
                if (slot.data == data && slot.count < data.maxStack)
                {
                    int canAdd = Mathf.Min(amount, data.maxStack - slot.count);
                    slot.count += canAdd;
                    amount -= canAdd;
                    if (amount <= 0)
                    {
                        OnItemAdded?.Invoke(data, canAdd);
                        OnInventoryChanged?.Invoke();
                        return true;
                    }
                }
            }
        }

        // Crear nuevos slots para el remanente
        while (amount > 0)
        {
            if (slots.Count >= maxSlots) return false;
            int toAdd = data.isStackable ? Mathf.Min(amount, data.maxStack) : 1;
            slots.Add(new InventorySlot { data = data, count = toAdd });
            amount -= toAdd;
        }

        OnItemAdded?.Invoke(data, amount);
        OnInventoryChanged?.Invoke();
        return true;
    }

    public bool UseCurrentItem()
    {
        var slot = CurrentSlot;
        if (slot == null || slot.data == null || slot.count <= 0) return false;

        bool consumed = slot.data.Use(gameObject);
        if (!consumed) return false;

        OnItemUsed?.Invoke(slot.data);

        if (slot.data.consumeOnUse)
        {
            slot.count--;
            if (slot.count <= 0)
            {
                slots.RemoveAt(_currentIndex);
                if (_currentIndex >= slots.Count) _currentIndex = Mathf.Max(0, slots.Count - 1);
            }
            OnInventoryChanged?.Invoke();
        }

        return true;
    }

    public void Next()
    {
        if (slots.Count == 0) return;
        CurrentIndex = _currentIndex + 1;
        OnInventoryChanged?.Invoke();
    }

    public void Previous()
    {
        if (slots.Count == 0) return;
        CurrentIndex = _currentIndex - 1;
        OnInventoryChanged?.Invoke();
    }
}