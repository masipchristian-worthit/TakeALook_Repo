using UnityEngine;

public class BigDoorController : MonoBehaviour
{
    [Header("Door Parts")]
    [SerializeField] Transform leftDoor;
    [SerializeField] Transform rightDoor;

    [Header("Door Settings")]
    [SerializeField] float openDistanceSide = 1.5f;
    [SerializeField] float speed = 2f;

    [Header("Interaction Mode")]
    [SerializeField] bool autoOpen = false;

    [Header("Reopen Settings")]
    [SerializeField] bool canReopenAfterPassing = true;

    [Header("Distances")]
    [SerializeField] float openDistance = 3f;
    [SerializeField] float closeDistance = 5f;

    Vector3 leftClosedPos;
    Vector3 rightClosedPos;
    Vector3 leftOpenPos;
    Vector3 rightOpenPos;

    bool isOpening;
    bool isClosing;
    bool playerHasPassed;

    Transform player;

    private void Start()
    {
        leftClosedPos = leftDoor.localPosition;
        rightClosedPos = rightDoor.localPosition;

        leftOpenPos = leftClosedPos + Vector3.left * openDistanceSide;
        rightOpenPos = rightClosedPos + Vector3.right * openDistanceSide;

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
            leftDoor.localPosition = Vector3.Lerp(leftDoor.localPosition, leftOpenPos, speed * Time.deltaTime);
            rightDoor.localPosition = Vector3.Lerp(rightDoor.localPosition, rightOpenPos, speed * Time.deltaTime);
        }

        if (isClosing)
        {
            leftDoor.localPosition = Vector3.Lerp(leftDoor.localPosition, leftClosedPos, speed * Time.deltaTime);
            rightDoor.localPosition = Vector3.Lerp(rightDoor.localPosition, rightClosedPos, speed * Time.deltaTime);
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