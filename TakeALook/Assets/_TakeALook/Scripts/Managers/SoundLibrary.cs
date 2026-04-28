using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Biblioteca de sonidos. Asignas clips con un ID string para llamarlos desde código.
/// IDs estándar usados por el sistema (puedes añadir más):
///   ui_open, ui_close, ui_swap, ui_select, ui_use, ui_deny
///   pickup_item, pickup_ammo, pickup_medkit
///   timer_tick, timer_low, timer_critical
///   gun_swap, flashlight_on, flashlight_off
/// </summary>
[CreateAssetMenu(fileName = "SoundLibrary", menuName = "Game/Audio/Sound Library")]
public class SoundLibrary : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public string id;
        public AudioClip clip;
    }

    public List<Entry> entries = new List<Entry>();
}