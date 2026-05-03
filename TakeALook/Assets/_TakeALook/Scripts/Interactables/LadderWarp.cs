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
        // Limpiamos YA el ID pendiente para que ninguna otra LadderWarp con el mismo id intente
        // colocar al jugador a la vez (caso raro pero conviene blindarlo).
        LadderWarpRouter.ClearPendingSpawn();

        // Esperamos varios frames para que cualquier spawn por defecto de la escena termine antes.
        yield return null;
        yield return null;
        yield return null;

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj == null)
        {
            Debug.LogWarning("[LadderWarp] No se encontró Player al intentar colocarlo en " + ladderId);
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

        CharacterController cc = playerObj.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        Vector3 worldSpawn = transform.TransformPoint(spawnOffset);
        Quaternion worldRot = faceThisLadderOnArrival
            ? Quaternion.LookRotation(transform.position - worldSpawn, Vector3.up)
            : transform.rotation;

        // Si la rotación calculada queda con eje Y horrible (porque el offset es vertical), cae a la del transform.
        if (faceThisLadderOnArrival && (transform.position - worldSpawn).sqrMagnitude < 0.0001f)
            worldRot = transform.rotation;

        playerObj.transform.SetPositionAndRotation(worldSpawn, worldRot);

        if (cc != null) cc.enabled = true;

        Debug.Log($"[LadderWarp] Player colocado en escalera '{ladderId}'.");
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
