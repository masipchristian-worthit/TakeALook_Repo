using UnityEngine;
using System.Collections.Generic;

public class BloodCollisionHandler : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] GameObject puddlePrefab;

    [Header("Settings")]
    [SerializeField] float detectionRadius = 0.7f;
    [SerializeField] float spawnHeightOffset = 0.02f;
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
        catch { return; }

        for (int i = 0; i < count; i++)
        {
            Vector3 pos = _events[i].intersection;
            Vector3 normal = _events[i].normal;

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

        Instantiate(puddlePrefab, spawnPos, Quaternion.Euler(-90f, 0f, 0f));
    }
}