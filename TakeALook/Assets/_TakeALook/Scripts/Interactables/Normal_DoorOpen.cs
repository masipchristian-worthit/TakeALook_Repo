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

    [Header("Blocker")]
    [SerializeField] Collider[] doorwayBlockers;
    [SerializeField] float unblockAtOpenPercent = 0.9f;

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

        TryFindPlayer();
    }

    void TryFindPlayer()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
            player = playerObj.transform;
    }

    private void Update()
    {
        if (player == null) { TryFindPlayer(); if (player == null) return; }

        float distance = Vector3.Distance(player.position, closedPosition);

        if (!canReopenAfterPassing && playerHasPassed)
        {
            isOpening = false;
            isClosing = true;
        }
        else
        {
            if (autoOpen && distance <= openDistance)
            {
                OpenDoor();
            }

            if (distance > closeDistance)
            {
                isOpening = false;
                isClosing = true;
            }
        }

        if (isOpening)
        {
            transform.position = Vector3.MoveTowards(transform.position, openPosition, speed * Time.deltaTime);
        }

        if (isClosing)
        {
            transform.position = Vector3.MoveTowards(transform.position, closedPosition, speed * Time.deltaTime);
        }

        UpdateBlocker();
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

    void UpdateBlocker()
    {
        if (doorwayBlockers == null || doorwayBlockers.Length == 0) return;

        float total = Vector3.Distance(closedPosition, openPosition);
        float current = Vector3.Distance(closedPosition, transform.position);
        float openPercent = total > 0f ? current / total : 0f;

        bool shouldBlock = openPercent < unblockAtOpenPercent;

        foreach (Collider col in doorwayBlockers)
        {
            if (col != null)
                col.enabled = shouldBlock;
        }
    }
}