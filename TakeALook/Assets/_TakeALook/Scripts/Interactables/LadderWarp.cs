using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class LadderWarp : MonoBehaviour
{
    public enum LadderDirection
    {
        Up,
        Down
    }

    [Header("Ladder Direction")]
    [SerializeField] private LadderDirection ladderDirection = LadderDirection.Up;

    [Header("Scene Warp")]
    [SerializeField] private string targetSceneName;
    [SerializeField] private string targetSpawnPointId = "Default";

    [Header("Interaction")]
    [SerializeField] private Transform player;
    [SerializeField] private float interactDistance = 2.2f;
    [SerializeField] private KeyCode interactKey = KeyCode.E;

    [Header("World Text")]
    [SerializeField] private TextMeshPro pressEText;
    [SerializeField] private string upText = "Press E";
    [SerializeField] private string downText = "Press E";
    [SerializeField] private bool faceCamera = true;

    [Header("Fade")]
    [SerializeField] private float fadeOutTime = 0.6f;
    [SerializeField] private float fadeInTime = 0.8f;
    [SerializeField] private bool fadeInAfterLoad = true;

    private bool _playerNear;
    private bool _isWarping;

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
    }

    private void Update()
    {
        if (_isWarping) return;
        if (player == null) return;

        float distance = Vector3.Distance(player.position, transform.position);
        _playerNear = distance <= interactDistance;

        if (pressEText != null)
            pressEText.gameObject.SetActive(_playerNear);

        if (_playerNear && Input.GetKeyDown(interactKey))
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
            Debug.LogWarning("[LadderWarp] Falta Target Scene Name.");
            return;
        }

        _isWarping = true;

        if (pressEText != null)
            pressEText.gameObject.SetActive(false);

        // Guardamos el spawn point para que LadderWarpRouter coloque al jugador al cargar la escena nueva.
        LadderWarpRouter.PendingSpawnPointId = targetSpawnPointId;
        LadderWarpRouter.PendingFadeIn = false;

        if (SceneFader.Instance != null)
        {
            if (fadeInAfterLoad)
            {
                SceneFader.Instance.FadeToSceneWithFadeIn(targetSceneName, fadeOutTime, fadeInTime);
            }
            else
            {
                SceneFader.Instance.FadeToScene(targetSceneName, fadeOutTime);
            }
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
    }
}