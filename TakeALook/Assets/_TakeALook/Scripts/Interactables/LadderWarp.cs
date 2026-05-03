using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using TMPro;

public class LadderWarp : MonoBehaviour
{
    public enum LadderDirection
    {
        Up,
        Down
    }

    [Header("Ladder Identity")]
    [Tooltip("ID único de ESTA escalera. Cuando otra LadderWarp use este mismo ID en su 'Target Ladder Id', el jugador aparecerá aquí al cargar la escena.")]
    [SerializeField] private string ladderId = "Ladder_01";

    [Header("Ladder Direction")]
    [SerializeField] private LadderDirection ladderDirection = LadderDirection.Up;

    [Header("Scene Warp")]
    [SerializeField] private string targetSceneName;
    [Tooltip("ID de la LadderWarp destino en la otra escena. Tiene que coincidir con su 'Ladder Id'.")]
    [SerializeField] private string targetLadderId = "Ladder_02";

    [Header("Spawn Offset (al recibir al jugador)")]
    [Tooltip("Desplazamiento local desde esta escalera donde aparece el jugador al llegar. Útil para que no quede dentro del collider de la escalera.")]
    [SerializeField] private Vector3 spawnOffset = new Vector3(0f, 0f, 1f);
    [Tooltip("Si está activo, el jugador mira hacia esta escalera al aparecer. Si no, hereda la rotación del transform.")]
    [SerializeField] private bool faceThisLadderOnArrival = true;
    [Tooltip("Cuántos metros máximo puede moverse la depenetración automática para encontrar espacio libre.")]
    [SerializeField] private float maxPushDistance = 3f;

    [Header("Interaction")]
    [SerializeField] private Transform player;
    [SerializeField] private float interactDistance = 2.2f;

    [Header("World Text")]
    [SerializeField] private TextMeshPro pressEText;
    [SerializeField] private string upText = "Press E";
    [SerializeField] private string downText = "Press E";
    [SerializeField] private bool faceCamera = true;

    [Header("Fade")]
    [SerializeField] private float fadeOutTime = 0.6f;
    [SerializeField] private float fadeInTime = 0.8f;
    [SerializeField] private bool fadeInAfterLoad = true;

    public string LadderId => ladderId;

    private bool _playerNear;
    private bool _isWarping;

    private bool IsInputBlocked() =>
        (UIManager.Instance != null && UIManager.Instance.IsUIPanelOpen());

    private void Start()
    {
        if (player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
                player = playerObj.transform;
        }

        if (pressEText != null)
        {
            pressEText.text = ladderDirection == LadderDirection.Up ? upText : downText;
            pressEText.gameObject.SetActive(false);
        }

        // Si soy el destino del último warp, coloco al jugador aquí.
        if (!string.IsNullOrEmpty(LadderWarpRouter.PendingSpawnPointId) &&
            LadderWarpRouter.PendingSpawnPointId == ladderId)
        {
            StartCoroutine(PlacePlayerHereRoutine());
        }
    }

    private IEnumerator PlacePlayerHereRoutine()
    {
        LadderWarpRouter.ClearPendingSpawn();

        // Esperar a que la física haya corrido al menos un tick y los colliders de la escena estén activos.
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj == null)
        {
            Debug.LogWarning("[LadderWarp] No se encontró Player al intentar colocarlo en " + ladderId);
            yield break;
        }

        Rigidbody rb = playerObj.GetComponent<Rigidbody>();
        CapsuleCollider cap = playerObj.GetComponent<CapsuleCollider>();
        CharacterController cc = playerObj.GetComponent<CharacterController>();

        // Volver kinematic temporalmente: así SetPositionAndRotation no lucha con la física
        // y no hay penetración de colliders que fuerce rotación.
        bool wasKinematic = false;
        if (rb != null)
        {
            wasKinematic = rb.isKinematic;
            rb.isKinematic = true;
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = Vector3.zero;
#else
            rb.velocity = Vector3.zero;
#endif
            rb.angularVelocity = Vector3.zero;
        }
        if (cc != null) cc.enabled = false;

        Vector3 worldSpawn = transform.TransformPoint(spawnOffset);
        worldSpawn = FindClearSpawnPosition(cap, worldSpawn);

        Quaternion worldRot;
        if (faceThisLadderOnArrival)
        {
            // Proyectar al plano horizontal: el cuerpo del player solo rota en Y (yaw).
            // Sin esto, si el spawnOffset tiene Y != 0, LookRotation inclina el cuerpo.
            Vector3 lookDir = transform.position - worldSpawn;
            lookDir.y = 0f;
            worldRot = lookDir.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(lookDir, Vector3.up)
                : Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        }
        else
        {
            worldRot = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        }

        playerObj.transform.SetPositionAndRotation(worldSpawn, worldRot);

        // Dejar pasar un FixedUpdate con el cuerpo kinematic para que Unity registre la posición.
        yield return new WaitForFixedUpdate();

        // Restaurar physics state.
        if (rb != null)
        {
            rb.isKinematic = wasKinematic;
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = Vector3.zero;
#else
            rb.velocity = Vector3.zero;
#endif
            rb.angularVelocity = Vector3.zero;
        }
        if (cc != null) cc.enabled = true;

        Debug.Log($"[LadderWarp] Player colocado en escalera '{ladderId}' en {worldSpawn}.");
    }

    // Usa ComputePenetration para encontrar un punto sin solapamiento.
    // Itera hasta resolver todas las penetraciones o hasta maxPushDistance.
    private Vector3 FindClearSpawnPosition(CapsuleCollider cap, Vector3 startPos)
    {
        float radius    = cap != null ? cap.radius        : 0.35f;
        float height    = cap != null ? cap.height        : 1.8f;
        Vector3 center  = cap != null ? cap.center        : new Vector3(0f, 0.9f, 0f);
        float halfInner = Mathf.Max(0f, height * 0.5f - radius);

        // Máscara: excluir triggers y la layer del player.
        int layerMask = ~LayerMask.GetMask("Player");

        Vector3 pos = startPos;

        // Hasta 8 iteraciones de depenetración.
        for (int iter = 0; iter < 8; iter++)
        {
            Vector3 p1 = pos + center + Vector3.up * halfInner;
            Vector3 p2 = pos + center - Vector3.up * halfInner;

            Collider[] hits = Physics.OverlapCapsule(p1, p2, radius, layerMask, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0) break; // Libre.

            bool pushed = false;
            foreach (Collider col in hits)
            {
                if (cap != null && col == cap) continue;

                if (Physics.ComputePenetration(
                    cap, pos, Quaternion.identity,
                    col, col.transform.position, col.transform.rotation,
                    out Vector3 dir, out float dist))
                {
                    pos += dir * (dist + 0.02f);
                    pushed = true;
                    break; // Re-evaluar con la nueva posición.
                }
            }

            if (!pushed) break;

            // Limitar el desplazamiento total respecto al spawn original.
            if (Vector3.Distance(pos, startPos) > maxPushDistance)
            {
                Debug.LogWarning($"[LadderWarp] Depenetración excedió {maxPushDistance}m. Usando spawn original.");
                return startPos;
            }
        }

        return pos;
    }

    private void Update()
    {
        if (_isWarping) return;
        if (player == null) return;

        float distance = Vector3.Distance(player.position, transform.position);
        _playerNear = distance <= interactDistance;

        bool showPrompt = _playerNear && !IsInputBlocked();
        if (pressEText != null)
            pressEText.gameObject.SetActive(showPrompt);

        if (_playerNear && !IsInputBlocked() && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
        {
            Warp();
        }

        if (faceCamera && pressEText != null && pressEText.gameObject.activeSelf && Camera.main != null)
        {
            pressEText.transform.LookAt(Camera.main.transform);
            pressEText.transform.Rotate(0f, 180f, 0f);
        }
    }

    private void Warp()
    {
        if (_isWarping) return;

        if (string.IsNullOrEmpty(targetSceneName))
        {
            Debug.LogWarning($"[LadderWarp] '{ladderId}' no tiene Target Scene Name.");
            return;
        }

        if (string.IsNullOrEmpty(targetLadderId))
        {
            Debug.LogWarning($"[LadderWarp] '{ladderId}' no tiene Target Ladder Id. El jugador aparecerá en el spawn por defecto de la escena destino.");
        }

        _isWarping = true;

        if (pressEText != null)
            pressEText.gameObject.SetActive(false);

        LadderWarpRouter.PendingSpawnPointId = targetLadderId;
        LadderWarpRouter.PendingFadeIn = fadeInAfterLoad;

        Debug.Log($"[LadderWarp] '{ladderId}' -> escena '{targetSceneName}', destino '{targetLadderId}'.");

        if (SceneFader.Instance != null)
        {
            if (fadeInAfterLoad)
                SceneFader.Instance.FadeToSceneWithFadeIn(targetSceneName, fadeOutTime, fadeInTime);
            else
                SceneFader.Instance.FadeToScene(targetSceneName, fadeOutTime);
        }
        else
        {
            Debug.LogWarning("[LadderWarp] No hay SceneFader.Instance. Cargando escena sin fade.");
            SceneManager.LoadScene(targetSceneName);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = ladderDirection == LadderDirection.Up ? Color.cyan : Color.magenta;
        Gizmos.DrawWireSphere(transform.position, interactDistance);

        // Dibuja el punto de spawn al recibir al jugador.
        Vector3 worldSpawn = transform.TransformPoint(spawnOffset);
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(worldSpawn, 0.25f);
        Gizmos.DrawLine(transform.position, worldSpawn);
    }
}
