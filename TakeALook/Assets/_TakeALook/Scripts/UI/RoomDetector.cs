using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Va en el GameObject del player. Mantiene la lista de zonas en las que está dentro
/// y comunica al GameManager la sala de mayor prioridad.
/// </summary>
public class RoomDetector : MonoBehaviour
{
    private readonly List<RoomZone> _activeZones = new List<RoomZone>();

    public void EnterZone(RoomZone zone)
    {
        if (zone == null || _activeZones.Contains(zone)) return;
        _activeZones.Add(zone);
        UpdateActiveRoom();
    }

    public void ExitZone(RoomZone zone)
    {
        if (zone == null) return;
        _activeZones.Remove(zone);
        UpdateActiveRoom();
    }

    private void UpdateActiveRoom()
    {
        if (_activeZones.Count == 0)
        {
            GameManager.Instance?.SetCurrentRoom("—");
            return;
        }

        // Sala con mayor prioridad
        RoomZone top = _activeZones[0];
        for (int i = 1; i < _activeZones.Count; i++)
            if (_activeZones[i].Priority > top.Priority) top = _activeZones[i];

        GameManager.Instance?.SetCurrentRoom(top.RoomName);
    }
}