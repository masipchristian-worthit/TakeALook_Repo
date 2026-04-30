using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Detecta colisiones del sistema de partículas de sangre con el suelo
/// y crea/hace crecer charcos (BloodPuddle).
///
/// Cambios respecto a la versión previa:
///  - Soporta el caso en el que el ParticleSystem está en un hijo (auto-buscamos).
///  - El "es suelo" ahora se basa en la NORMAL del impacto, no en el Y absoluto del mundo.
///    El usuario puede tener escenarios con el suelo a alturas distintas y la versión
///    anterior no detectaba bien los impactos.
///  - Sanitizamos contra puddlePrefab nulo y contra GetCollisionEvents fallando.
/// </summary>
public class BloodCollisionHandler : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] GameObject puddlePrefab;

    [Header("Settings")]
    [SerializeField] float detectionRadius = 0.7f;
    [Tooltip("Altura mínima para asumir que el quad del charco no se superpone con la pared.")]
    [SerializeField] float spawnHeightOffset = 0.02f;
    [Tooltip("Coseno mínimo del ángulo entre la normal del impacto y +Y para considerarlo suelo. 1=plano, 0.5≈60°.")]
    [SerializeField, Range(0f, 1f)] float groundNormalThreshold = 0.7f;

    private ParticleSystem _ps;
    private readonly List<ParticleCollisionEvent> _events = new List<ParticleCollisionEvent>();
    private static readonly Collider[] _overlapBuffer = new Collider[8];

    private void Awake()
    {
        _ps = GetComponent<ParticleSystem>();
        if (_ps == null) _ps = GetComponentInChildren<ParticleSystem>();
    }

    private void OnParticleCollision(GameObject other)
    {
        if (_ps == null || puddlePrefab == null) return;

        int count;
        try { count = _ps.GetCollisionEvents(other, _events); }
        catch { return; } // por si el PS aún no está inicializado

        for (int i = 0; i < count; i++)
        {
            Vector3 pos = _events[i].intersection;
            Vector3 normal = _events[i].normal;

            // Solo nos interesa el suelo (normal apuntando hacia arriba)
            if (Vector3.Dot(normal.normalized, Vector3.up) < groundNormalThreshold)
                continue;

            HandlePuddle(new Vector3(pos.x, pos.y + spawnHeightOffset, pos.z));
        }
    }

    private void HandlePuddle(Vector3 spawnPos)
    {
        int found = Physics.OverlapSphereNonAlloc(spawnPos, detectionRadius, _overlapBuffer);
        for (int i = 0; i < found; i++)
        {
            if (_overlapBuffer[i] != null && _overlapBuffer[i].TryGetComponent(out BloodPuddle puddle))
            {
                puddle.Grow();
                return;
            }
        }

        // El quad por defecto de Unity está en XY, rotamos -90 en X para que quede horizontal
        Instantiate(puddlePrefab, spawnPos, Quaternion.Euler(-90f, 0f, 0f));
    }
}
