using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class CodeDoor : MonoBehaviour
{
    [Header("Persistent Unlock")]
    [Tooltip("ID único de esta puerta. No repitas el mismo ID en dos puertas distintas.")]
    [SerializeField] string doorId = "Door_01";

    [Tooltip("Si está activado, la puerta seguirá desbloqueada aunque cambies de escena o reinicies el juego.")]
    [SerializeField] bool saveInPlayerPrefs = true;

    static HashSet<string> unlockedDoorsThisSession = new HashSet<string>();

    [Header("Final Door Warp")]
    [Tooltip("Activa esto SOLO en la puerta final. Las demás puertas deben tenerlo desactivado.")]
    [SerializeField] bool warpOnCorrectCode = false;

    [Tooltip("Escena a la que irá cuando esta puerta final acepte el código.")]
    [SerializeField] string finalSceneName = "FinalDialogueScene";

    [Tooltip("Tiempo de espera después de aceptar el código antes de cambiar de escena.")]
    [SerializeField] float delayBeforeFinalWarp = 1.4f;

    [Tooltip("Duración del fade a negro.")]
    [SerializeField] float finalFadeOutTime = 0.8f;

    [Tooltip("Duración del fade in en la escena final.")]
    [SerializeField] float finalFadeInTime = 0.8f;

    [Header("Door Movement")]
    [SerializeField] float openHeight = 3f;
    [SerializeField] float speed = 2f;

    [Header("Interaction")]
    [SerializeField] Transform interactionPoint;
    [SerializeField] float interactDistance = 2.5f;
    [SerializeField] float closePanelDistance = 3f;

    [Header("Door Auto Open")]
    [SerializeField] float openDistance = 3f;
    [SerializeField] float closeDistance = 4f;

    [Header("Code Settings")]
    [SerializeField] string correctCode = "123456";

    [Header("UI References")]
    [SerializeField] GameObject pressEText;
    [SerializeField] GameObject codePanelUI;
    [SerializeField] TMP_Text codeText;
    [SerializeField] TMP_Text feedbackText;
    [SerializeField] GraphicRaycaster panelRaycaster;
    [SerializeField] EventSystem eventSystem;

    [Header("Doorway Blockers")]
    [SerializeField] Collider[] doorwayBlockers;
    [SerializeField] float unblockAtOpenPercent = 0.9f;

    Vector3 closedPosition;
    Vector3 openPosition;

    bool isOpening;
    bool isClosing;
    bool isUnlocked;
    bool isUsingPanel;
    bool codeAccepted;
    bool inputLocked;
    bool finalWarpStarted;

    string currentCode = "";

    Transform player;

    private void Start()
    {
        closedPosition = transform.position;
        openPosition = closedPosition + Vector3.up * openHeight;

        if (string.IsNullOrWhiteSpace(doorId))
            doorId = gameObject.scene.name + "_" + gameObject.name;

        LoadDoorUnlockState();

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
            player = playerObj.transform;

        if (pressEText != null)
            pressEText.SetActive(false);

        if (codePanelUI != null)
            codePanelUI.SetActive(false);

        if (panelRaycaster == null && codePanelUI != null)
            panelRaycaster = codePanelUI.GetComponentInParent<GraphicRaycaster>();

        if (eventSystem == null)
            eventSystem = FindFirstObjectByType<EventSystem>();

        if (codeText != null)
            codeText.text = "_ _ _ _ _ _ _ _ _ _";

        if (feedbackText != null)
            feedbackText.text = "";

        if (interactionPoint == null)
            interactionPoint = transform;
    }

    private void Update()
    {
        if (player == null) return;

        float distanceToPanel = Vector3.Distance(player.position, interactionPoint.position);
        float distanceToDoor = Vector3.Distance(player.position, transform.position);

        HandleInteraction(distanceToPanel);
        HandleDoorMovement();
        UpdateDoorwayBlockers();

        if (isUnlocked && !isUsingPanel)
        {
            if (distanceToDoor <= openDistance)
            {
                OpenDoor();
            }
            else if (distanceToDoor > closeDistance)
            {
                CloseDoor();
            }
        }
    }

    void LoadDoorUnlockState()
    {
        bool unlockedInSession = unlockedDoorsThisSession.Contains(doorId);
        bool unlockedInSave = saveInPlayerPrefs && PlayerPrefs.GetInt(GetDoorSaveKey(), 0) == 1;

        isUnlocked = unlockedInSession || unlockedInSave;

        if (isUnlocked)
        {
            unlockedDoorsThisSession.Add(doorId);
            codeAccepted = false;
            inputLocked = false;
            isUsingPanel = false;
        }
    }

    void SaveDoorUnlockState()
    {
        isUnlocked = true;
        unlockedDoorsThisSession.Add(doorId);

        if (saveInPlayerPrefs)
        {
            PlayerPrefs.SetInt(GetDoorSaveKey(), 1);
            PlayerPrefs.Save();
        }
    }

    string GetDoorSaveKey()
    {
        return "CodeDoor_Unlocked_" + doorId;
    }

    void HandleInteraction(float distanceToPanel)
    {
        bool playerInRange = distanceToPanel <= interactDistance;

        if (isUnlocked)
        {
            if (pressEText != null)
                pressEText.SetActive(false);

            return;
        }

        if (!isUsingPanel)
        {
            if (pressEText != null)
                pressEText.SetActive(playerInRange);

            if (playerInRange && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                OpenCodePanel();
            }
        }
        else
        {
            if (pressEText != null)
                pressEText.SetActive(false);

            if (distanceToPanel > closePanelDistance)
            {
                CloseCodePanel();
                return;
            }

            HandleCodeInput();
        }
    }

    void HandleDoorMovement()
    {
        if (isOpening)
        {
            transform.position = Vector3.MoveTowards(transform.position, openPosition, speed * Time.deltaTime);
        }

        if (isClosing)
        {
            transform.position = Vector3.MoveTowards(transform.position, closedPosition, speed * Time.deltaTime);
        }
    }

    void OpenDoor()
    {
        isOpening = true;
        isClosing = false;
    }

    void CloseDoor()
    {
        isOpening = false;
        isClosing = true;
    }

    void OpenCodePanel()
    {
        if (isUnlocked) return;

        isUsingPanel = true;
        codeAccepted = false;
        inputLocked = false;
        currentCode = "";

        UpdateCodeText();

        if (codePanelUI != null)
            codePanelUI.SetActive(true);

        if (feedbackText != null)
            feedbackText.text = "";

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void CloseCodePanel()
    {
        isUsingPanel = false;

        if (codePanelUI != null)
            codePanelUI.SetActive(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (codeAccepted)
        {
            OpenDoor();
            codeAccepted = false;
        }
    }

    void HandleCodeInput()
    {
        if (Keyboard.current == null) return;
        if (inputLocked) return;

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            CloseCodePanel();
            return;
        }

        if (Keyboard.current.backspaceKey.wasPressedThisFrame)
        {
            PressBackspace();
        }

        if (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame)
        {
            PressEnter();
        }

        CheckNumberKey("0", Keyboard.current.digit0Key, Keyboard.current.numpad0Key);
        CheckNumberKey("1", Keyboard.current.digit1Key, Keyboard.current.numpad1Key);
        CheckNumberKey("2", Keyboard.current.digit2Key, Keyboard.current.numpad2Key);
        CheckNumberKey("3", Keyboard.current.digit3Key, Keyboard.current.numpad3Key);
        CheckNumberKey("4", Keyboard.current.digit4Key, Keyboard.current.numpad4Key);
        CheckNumberKey("5", Keyboard.current.digit5Key, Keyboard.current.numpad5Key);
        CheckNumberKey("6", Keyboard.current.digit6Key, Keyboard.current.numpad6Key);
        CheckNumberKey("7", Keyboard.current.digit7Key, Keyboard.current.numpad7Key);
        CheckNumberKey("8", Keyboard.current.digit8Key, Keyboard.current.numpad8Key);
        CheckNumberKey("9", Keyboard.current.digit9Key, Keyboard.current.numpad9Key);

        HandleMousePanelClick();
    }

    void CheckNumberKey(string number, KeyControl normalKey, KeyControl keypadKey)
    {
        if ((normalKey != null && normalKey.wasPressedThisFrame) ||
            (keypadKey != null && keypadKey.wasPressedThisFrame))
        {
            PressDigit(number);
        }
    }

    void HandleMousePanelClick()
    {
        if (Mouse.current == null) return;
        if (!Mouse.current.leftButton.wasPressedThisFrame) return;
        if (panelRaycaster == null || eventSystem == null) return;

        PointerEventData pointerData = new PointerEventData(eventSystem);
        pointerData.position = Mouse.current.position.ReadValue();

        List<RaycastResult> results = new List<RaycastResult>();
        panelRaycaster.Raycast(pointerData, results);

        if (results.Count == 0) return;

        for (int i = 0; i < results.Count; i++)
        {
            GameObject clickedObject = results[i].gameObject;
            Transform current = clickedObject.transform;

            while (current != null)
            {
                string objName = current.name.ToUpper();

                if (objName == "0" || objName == "1" || objName == "2" || objName == "3" || objName == "4" ||
                    objName == "5" || objName == "6" || objName == "7" || objName == "8" || objName == "9")
                {
                    PressDigit(objName);
                    return;
                }

                if (objName == "BACK")
                {
                    PressBackspace();
                    return;
                }

                if (objName == "CLEAR")
                {
                    PressClear();
                    return;
                }

                if (objName == "ENTER")
                {
                    PressEnter();
                    return;
                }

                current = current.parent;
            }
        }
    }

    public void PressDigit(string digit)
    {
        if (!isUsingPanel) return;
        if (inputLocked) return;
        if (currentCode.Length >= 6) return;

        currentCode += digit;
        UpdateCodeText();

        if (currentCode.Length == 6)
        {
            CheckCode();
        }
    }

    public void PressBackspace()
    {
        if (!isUsingPanel) return;
        if (currentCode.Length <= 0) return;

        currentCode = currentCode.Substring(0, currentCode.Length - 1);
        UpdateCodeText();
    }

    public void PressClear()
    {
        if (!isUsingPanel) return;

        currentCode = "";
        UpdateCodeText();

        if (feedbackText != null)
            feedbackText.text = "";
    }

    public void TestUIButton()
    {
        Debug.Log("BOTON UI ESTA RECIBIENDO EL CLICK");
    }

    public void PressEnter()
    {
        if (!isUsingPanel) return;
        CheckCode();
    }

    void UpdateCodeText()
    {
        if (codeText == null) return;

        string display = "";

        for (int i = 0; i < 6; i++)
        {
            if (i < currentCode.Length)
                display += currentCode[i] + " ";
            else
                display += "_ ";
        }

        codeText.text = display;
    }

    void CheckCode()
    {
        CancelInvoke();
        inputLocked = true;

        if (currentCode == correctCode)
        {
            SaveDoorUnlockState();

            codeAccepted = true;
            currentCode = "";

            if (codeText != null)
                codeText.text = "";

            if (feedbackText != null)
            {
                feedbackText.text = "ACCESS GRANTED";
                feedbackText.color = Color.green;
            }

            if (pressEText != null)
                pressEText.SetActive(false);

            Invoke(nameof(CloseCodePanel), 1.2f);
            Invoke(nameof(ResetCode), 1.2f);

            if (warpOnCorrectCode)
            {
                StartFinalWarp();
            }
        }
        else
        {
            currentCode = "";

            if (codeText != null)
                codeText.text = "";

            if (feedbackText != null)
            {
                feedbackText.text = "ACCESS DENIED";
                feedbackText.color = Color.red;
            }

            Invoke(nameof(ResetCode), 1.2f);
        }
    }

    void StartFinalWarp()
    {
        if (finalWarpStarted) return;

        finalWarpStarted = true;
        StartCoroutine(FinalWarpRoutine());
    }

    IEnumerator FinalWarpRoutine()
    {
        yield return new WaitForSeconds(delayBeforeFinalWarp);

        if (string.IsNullOrEmpty(finalSceneName))
        {
            Debug.LogWarning("[CodeDoor] La puerta final tiene Warp On Correct Code activado, pero no tiene Final Scene Name.");
            yield break;
        }

        if (SceneFader.Instance != null)
        {
            SceneFader.Instance.FadeToSceneWithFadeIn(finalSceneName, finalFadeOutTime, finalFadeInTime);
        }
        else
        {
            Debug.LogWarning("[CodeDoor] No hay SceneFader.Instance. Cargando escena final sin fade.");
            SceneManager.LoadScene(finalSceneName);
        }
    }

    void ResetCode()
    {
        currentCode = "";
        inputLocked = false;
        UpdateCodeText();

        if (feedbackText != null)
            feedbackText.text = "";
    }

    void UpdateDoorwayBlockers()
    {
        if (doorwayBlockers == null || doorwayBlockers.Length == 0) return;

        float totalDistance = Vector3.Distance(closedPosition, openPosition);
        float currentDistance = Vector3.Distance(transform.position, closedPosition);
        float openPercent = totalDistance > 0f ? currentDistance / totalDistance : 0f;

        bool shouldBlock = openPercent < unblockAtOpenPercent;

        foreach (Collider col in doorwayBlockers)
        {
            if (col != null)
                col.enabled = shouldBlock;
        }
    }

    public bool IsUsingPanel()
    {
        return isUsingPanel && codePanelUI != null && codePanelUI.activeSelf;
    }

    private void OnDrawGizmosSelected()
    {
        Transform point = interactionPoint != null ? interactionPoint : transform;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(point.position, interactDistance);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(point.position, closePanelDistance);
    }
}