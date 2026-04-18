using UnityEngine;
using System.Collections.Generic;

public class BloodCollisionHandler : MonoBehaviour
{
    public GameObject puddlePrefab;
    public float detectionRadius = 0.7f;
    public float groundYThreshold = 0.5f; //detectar el suelo

    private ParticleSystem part;
    private List<ParticleCollisionEvent> collisionEvents = new List<ParticleCollisionEvent>();

    void Start() => part = GetComponent<ParticleSystem>();

    void OnParticleCollision(GameObject other)
    {
        int numEvents = part.GetCollisionEvents(other, collisionEvents);
        for (int i = 0; i < numEvents; i++)
        {
            Vector3 pos = collisionEvents[i].intersection;

            // Si el impacto ocurre cerca de Y = 0
            if (Mathf.Abs(pos.y) <= groundYThreshold)
            {
                Debug.Log($"¡Colisión detectada en {pos}! Creando/Creciendo charco.");
                HandlePuddle(new Vector3(pos.x, 0.02f, pos.z));
            }
        }
    }

    void HandlePuddle(Vector3 spawnPos)
    {
        Collider[] closeBy = Physics.OverlapSphere(spawnPos, detectionRadius);
        BloodPuddle existing = null;

        foreach (var c in closeBy)
        {
            if (c.TryGetComponent(out BloodPuddle p)) { existing = p; break; }
        }

        if (existing != null) existing.Grow();
        else Instantiate(puddlePrefab, spawnPos, Quaternion.Euler(-90, 0, 0));
    }
}