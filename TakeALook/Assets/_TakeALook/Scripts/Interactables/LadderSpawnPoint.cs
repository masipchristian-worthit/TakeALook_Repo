using System.Collections;
using UnityEngine;

public class LadderSpawnPoint : MonoBehaviour
{
    [SerializeField] private string spawnPointId = "Default";

    public string SpawnPointId => spawnPointId;

    private void Start()
    {
        if (!string.IsNullOrEmpty(LadderWarpRouter.PendingSpawnPointId) &&
            LadderWarpRouter.PendingSpawnPointId == spawnPointId)
        {
            StartCoroutine(PlacePlayerHereRoutine());
        }
    }

    private IEnumerator PlacePlayerHereRoutine()
    {
        // BUG FIX: Limpiar el ID pendiente INMEDIATAMENTE para que otros LadderSpawnPoints
        // en la misma escena no intenten ejecutar el spawn también.
        LadderWarpRouter.ClearPendingSpawn();

        // Esperamos varios frames para que cualquier otro spawn inicial termine primero.
        yield return null;
        yield return null;
        yield return null;

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");

        if (playerObj == null)
        {
            Debug.LogWarning("[LadderSpawnPoint] No se encontró ningún objeto con tag Player.");
            yield break;
        }

        Rigidbody rb = playerObj.GetComponent<Rigidbody>();
        if (rb != null)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = Vector3.zero;
#else
            rb.velocity = Vector3.zero;
#endif
            rb.angularVelocity = Vector3.zero;
        }

        // BUG FIX: El CharacterController bloquea SetPosition, hay que desactivarlo antes de mover.
        CharacterController cc = playerObj.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        playerObj.transform.SetPositionAndRotation(transform.position, transform.rotation);

        if (cc != null) cc.enabled = true;

        Debug.Log("[LadderSpawnPoint] Player colocado en: " + spawnPointId);
    }
}
