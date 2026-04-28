using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Detecta colisiones del sistema de partículas de sangre con el suelo
/// y crea/hace crecer charcos (BloodPuddle).
/// Usa OverlapSphereNonAlloc para evitar allocations por frame.
/// </summary>
public class BloodCollisionHandler : MonoBehaviour
{
    [SerializeField] GameObject puddlePrefab;
    [SerializeField] float detectionRadius = 0.7f;
    [Tooltip("Umbral de altura para considerar que la partícula impactó en el suelo.")]
    [SerializeField] float groundYThreshold = 0.5f;

    private ParticleSystem _ps;
    private readonly List<ParticleCollisionEvent> _events = new List<ParticleCollisionEvent>();

    // Buffer estático: evita alloc por cada OverlapSphere
    private static readonly Collider[] _overlapBuffer = new Collider[8];

    private void Start() => _ps = GetComponent<ParticleSystem>();

    private void OnParticleCollision(GameObject other)
    {
        int count = _ps.GetCollisionEvents(other, _events);
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = _events[i].intersection;
            if (Mathf.Abs(pos.y) <= groundYThreshold)
                HandlePuddle(new Vector3(pos.x, 0.02f, pos.z));
        }
    }

    private void HandlePuddle(Vector3 spawnPos)
    {
        int found = Physics.OverlapSphereNonAlloc(spawnPos, detectionRadius, _overlapBuffer);
        for (int i = 0; i < found; i++)
        {
            if (_overlapBuffer[i].TryGetComponent(out BloodPuddle puddle))
            {
                puddle.Grow();
                return;
            }
        }

        // El quad por defecto de Unity está en XY, rotamos -90 en X para que quede horizontal
        Instantiate(puddlePrefab, spawnPos, Quaternion.Euler(-90f, 0f, 0f));
    }
}
