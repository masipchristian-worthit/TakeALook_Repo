using UnityEngine;

public class LadderSpawnPoint : MonoBehaviour
{
    [SerializeField] private string spawnPointId = "Default";

    public string SpawnPointId => spawnPointId;
}