using UnityEngine;

/// <summary>
/// Marcador en la escena de destino. El jugador se reposicionará aquí al cargar la
/// escena si el spawnPointId coincide con el que envió el LadderWarp emisor.
/// </summary>
public class LadderSpawnPoint : MonoBehaviour
{
    [SerializeField] private string spawnPointId = "Default";
    public string SpawnPointId => spawnPointId;

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.5f);
        Gizmos.DrawSphere(transform.position, 0.25f);
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(transform.position, transform.forward * 1f);
    }
}
