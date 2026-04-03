using UnityEngine;

public class DoorPassTrigger : MonoBehaviour
{
    [SerializeField] DoorOpen normalDoor;
    [SerializeField] BigDoorController bigDoor;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        if (normalDoor != null)
        {
            normalDoor.MarkPlayerPassed();
        }

        if (bigDoor != null)
        {
            bigDoor.MarkPlayerPassed();
        }
    }
}
