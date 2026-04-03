using UnityEngine;

public class DoorOpen : MonoBehaviour
{
    [Header("Door Settings")]
    [SerializeField] float openHeight = 3f;
    [SerializeField] float speed = 2f;

    [Header("Interaction Mode")]
    [SerializeField] bool autoOpen = false;

    [Header("Reopen Settings")]
    [SerializeField] bool canReopenAfterPassing = true;

    [Header("Distances")]
    [SerializeField] float openDistance = 3f;
    [SerializeField] float closeDistance = 4f;

    Vector3 closedPosition;
    Vector3 openPosition;

    bool isOpening;
    bool isClosing;
    bool playerHasPassed;

    Transform player;

    private void Start()
    {
        closedPosition = transform.position;
        openPosition = closedPosition + Vector3.up * openHeight;

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
            player = playerObj.transform;
    }

    private void Update()
    {
        if (player == null) return;

        float distance = Vector3.Distance(player.position, transform.position);

        if (autoOpen && distance <= openDistance)
        {
            OpenDoor();
        }

        if (distance > closeDistance)
        {
            isOpening = false;
            isClosing = true;
        }

        if (isOpening)
        {
            transform.position = Vector3.Lerp(transform.position, openPosition, speed * Time.deltaTime);
        }

        if (isClosing)
        {
            transform.position = Vector3.Lerp(transform.position, closedPosition, speed * Time.deltaTime);
        }
    }

    public void OpenDoor()
    {
        if (!canReopenAfterPassing && playerHasPassed)
            return;

        isOpening = true;
        isClosing = false;
    }

    public void MarkPlayerPassed()
    {
        playerHasPassed = true;
    }
}