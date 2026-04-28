using UnityEngine;

/// <summary>
/// Zona de habitación. Pon este componente en un GameObject con un Collider marcado como Trigger.
/// El nombre se mostrará en la UI cuando el jugador esté dentro.
/// </summary>
[RequireComponent(typeof(Collider))]
public class RoomZone : MonoBehaviour
{
    [Header("Identidad de Sala")]
    [SerializeField] private string roomName = "Sala sin nombre";
    [Tooltip("Prioridad cuando dos zonas se solapan. Mayor número gana.")]
    [SerializeField] private int priority = 0;

    public string RoomName => roomName;
    public int Priority => priority;

    private void Reset()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        var detector = other.GetComponent<RoomDetector>();
        if (detector != null) detector.EnterZone(this);
    }

    private void OnTriggerExit(Collider other)
    {
        var detector = other.GetComponent<RoomDetector>();
        if (detector != null) detector.ExitZone(this);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        var col = GetComponent<Collider>();
        if (col == null) return;

        Gizmos.color = new Color(0.3f, 1f, 0.6f, 0.15f);
        Gizmos.matrix = transform.localToWorldMatrix;

        if (col is BoxCollider box)
            Gizmos.DrawCube(box.center, box.size);
        else if (col is SphereCollider sph)
            Gizmos.DrawSphere(sph.center, sph.radius);
    }
#endif
}