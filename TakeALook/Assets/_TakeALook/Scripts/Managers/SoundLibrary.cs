using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Biblioteca de sonidos. Asignas clips con un ID string para llamarlos desde código.
///
/// IDs estándar usados por el sistema actual (puedes añadir más):
///
///   ── UI ──────────────────────────────────────────────
///     ui_open, ui_close          → abrir / cerrar panel UI
///     ui_swap                    → cambiar de pestaña / cambio de bala (carrusel)
///     ui_move                    → mover selección dentro del carrusel
///     ui_interact                → confirmar / entrar al nivel de items
///     ui_use, ui_deny            → usar item / acción denegada
///
///   ── Player ──────────────────────────────────────────
///     player_step                → paso individual (cíclico, lo dispara el FPS_Controller)
///     player_hurt, player_heal, player_death
///
///   ── Weapon ──────────────────────────────────────────
///     gun_draw, gun_sheath
///     gun_shoot_wolf, gun_shoot_bull, gun_shoot_eagle  → disparo por tipo de bala
///     gun_reload, gun_inspect
///     gun_empty                  → intento de disparo sin balas (dryfire)
///     gun_empty_reload           → intento de recargar con cargador vacío y SIN reservas
///     gun_swap                   → cambio de bala completado
///     flashlight_on, flashlight_off
///
///   ── Enemy ───────────────────────────────────────────
///     enemy_alert, enemy_roar, enemy_step
///     enemy_shoot                → disparo de proyectil
///     enemy_projectile_hit       → impacto del proyectil del enemigo
///     enemy_headshot, enemy_bodyshot, enemy_death, enemy_death_electric
///
///   ── World / Pickups ─────────────────────────────────
///     pickup_item, pickup_ammo, pickup_medkit
///     ladder_use                 → escalera (cambio de escena)
///
///   ── Timer (CountdownTimer) ──────────────────────────
///     timer_tick, timer_low, timer_critical
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
