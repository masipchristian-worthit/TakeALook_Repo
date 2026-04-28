using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Manager global del estado del juego. Persistente entre escenas.
/// Mantiene el registro de:
///   - Items recogidos (por GUID) -> evita que reaparezcan al volver a la escena.
///   - Enemigos muertos (por GUID) -> evita que reaparezcan.
///   - Estado del temporizador global.
///   - Sala actual del jugador.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Estado Persistente")]
    [SerializeField] private List<string> pickedItemIds = new List<string>();
    [SerializeField] private List<string> killedEnemyIds = new List<string>();

    [Header("Run Stats")]
    public float runTime;
    public string currentRoomName = "—";

    public event System.Action<string> OnRoomChanged;
    public event System.Action OnInventoryChanged;

    // HashSet para lookup O(1) - se rebuild on Awake desde la lista serializada
    private HashSet<string> _pickedSet;
    private HashSet<string> _killedSet;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        RebuildSets();
    }

    private void Update()
    {
        runTime += Time.deltaTime;
    }

    private void RebuildSets()
    {
        _pickedSet = new HashSet<string>(pickedItemIds);
        _killedSet = new HashSet<string>(killedEnemyIds);
    }

    #region Item Persistence
    public bool IsItemPicked(string id)
    {
        return !string.IsNullOrEmpty(id) && _pickedSet.Contains(id);
    }

    public void RegisterPickedItem(string id)
    {
        if (string.IsNullOrEmpty(id) || _pickedSet.Contains(id)) return;
        _pickedSet.Add(id);
        pickedItemIds.Add(id);
        OnInventoryChanged?.Invoke();
    }
    #endregion

    #region Enemy Persistence
    public bool IsEnemyKilled(string id)
    {
        return !string.IsNullOrEmpty(id) && _killedSet.Contains(id);
    }

    public void RegisterKilledEnemy(string id)
    {
        if (string.IsNullOrEmpty(id) || _killedSet.Contains(id)) return;
        _killedSet.Add(id);
        killedEnemyIds.Add(id);
    }
    #endregion

    #region Room Tracking
    public void SetCurrentRoom(string roomName)
    {
        if (currentRoomName == roomName) return;
        currentRoomName = roomName;
        OnRoomChanged?.Invoke(roomName);
    }
    #endregion

    #region Reset (para nueva partida)
    public void ResetRunState()
    {
        pickedItemIds.Clear();
        killedEnemyIds.Clear();
        _pickedSet.Clear();
        _killedSet.Clear();
        runTime = 0f;
        currentRoomName = "—";
    }
    #endregion
}