using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Alimenta un CarouselUI con los tipos de bala.
/// Reordena la lista para mostrar SIEMPRE primero el tipo de bala actualmente en uso.
/// Actualiza el contador (cargador / reserva) en cada cambio.
/// </summary>
public class AmmoCarouselFeed : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private CarouselUI carousel;
    [SerializeField] private GunSystem gunSystem;

    [Header("Data Wolf/Bull/Eagle")]
    [SerializeField] private AmmoTypeData wolfData;
    [SerializeField] private AmmoTypeData bullData;
    [SerializeField] private AmmoTypeData eagleData;

    [Header("Behavior")]
    [Tooltip("Si está activo, el carrusel siempre rota para que el tipo activo del arma quede en el centro al refrescar.")]
    [SerializeField] private bool keepActiveTypeAtCenter = true;

    [Header("Display")]
    [Tooltip("Formato: {0}=cargador {1}=capacidad {2}=reserva. Ej: '{0}/{1}  •  {2}'")]
    [SerializeField] private string countFormat = "{0}/{1}  •  {2}";

    private float _refreshTimer;
    private const float REFRESH_INTERVAL = 0.1f;
    private GunSystem.BulletType _lastActive;

    private void OnEnable()
    {
        BuildAndPushEntries(true);
    }

    private void Update()
    {
        // Polling ligero para refrescar contadores y orden
        _refreshTimer += Time.unscaledDeltaTime;
        if (_refreshTimer < REFRESH_INTERVAL) return;
        _refreshTimer = 0f;

        if (gunSystem == null) return;
        var active = gunSystem.CurrentBulletType;
        bool typeChanged = (active != _lastActive);
        _lastActive = active;

        BuildAndPushEntries(typeChanged && keepActiveTypeAtCenter);
    }

    private AmmoTypeData GetDataFor(GunSystem.BulletType type)
    {
        switch (type)
        {
            case GunSystem.BulletType.Wolf: return wolfData;
            case GunSystem.BulletType.Bull: return bullData;
            case GunSystem.BulletType.Eagle: return eagleData;
        }
        return null;
    }

    private void BuildAndPushEntries(bool reorderToActive)
    {
        if (carousel == null || gunSystem == null) return;

        var entries = new List<CarouselUI.Entry>();
        var order = new GunSystem.BulletType[] { GunSystem.BulletType.Wolf, GunSystem.BulletType.Bull, GunSystem.BulletType.Eagle };

        foreach (var t in order)
        {
            var data = GetDataFor(t);
            if (data == null) continue;
            int mag = gunSystem.GetMag(t);
            int reserve = gunSystem.GetReserve(t);
            int cap = gunSystem.GetMagCapacity(t);
            int count = mag + reserve;

            // Truco: usamos count como "cantidad" para que el slot muestre el formato.
            // El slot muestra "x{count}" por defecto; aquí lo personalizamos vía data.displayName parcial.
            // Para simplicidad, el slot muestra count - el formato extendido se podría implementar en CarouselSlot
            // si añades un campo extra. Aquí usamos count = mag (lo que suele importar más).
            entries.Add(new CarouselUI.Entry { data = data, count = mag });
        }

        // Reordenar: tipo activo primero (centro)
        int activeIdx = 0;
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].data is AmmoTypeData ad && ad.bulletType == gunSystem.CurrentBulletType)
            { activeIdx = i; break; }

        // El polling no dispara estática (no es acción del usuario); sólo ScrollTo/Use lo hacen.
        carousel.SetEntries(entries, reorderToActive ? activeIdx : (int?)null);
    }
}
