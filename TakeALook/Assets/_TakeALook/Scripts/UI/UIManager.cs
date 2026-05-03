using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using DG.Tweening;

/// <summary>
/// Manager principal de la UI desplegable.
/// 
/// MODELO DE FOCO (dos niveles):
///   Nivel 0 (Tabs):   UISliderLeft/Right cambia entre pestañas Ammo / Inventory.
///                     UIInteract entra al nivel de items dentro de la pestaña activa.
///   Nivel 1 (Items):  UISliderLeft/Right cambia el item seleccionado del carrusel activo.
///                     UIInteract usa el item.
///                     ToggleUI (TAB) o UIBack vuelve al Nivel 0 (o cierra si estás ahí).
///
/// PAUSA TÁCTICA (RE4): el panel NO pausa el juego con timeScale.
/// El bloqueo de input del jugador lo gestionan FPS_Controller y GunSystem
/// leyendo IsUIPanelOpen() de este manager.
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    public enum TabType { Ammo, Inventory }
    public enum FocusLevel { Tabs, Items }

    [Header("Refs - Panel")]
    [SerializeField] private RectTransform panelRoot;
    [SerializeField] private CanvasGroup panelCanvasGroup;
    [Tooltip("Posición cuando el panel está completamente fuera de pantalla (solo se usa en startHidden=true / Awake).")]
    [SerializeField] private Vector2 panelOffscreenPos = new Vector2(-820f, 0f);
    [Tooltip("Posición de reposo: el panel asoma ligeramente por el borde izquierdo.")]
    [SerializeField] private Vector2 panelPeekPos = new Vector2(-680f, 0f);
    [Tooltip("Posición completamente desplegado en el lateral izquierdo.")]
    [SerializeField] private Vector2 panelShownPos = new Vector2(0f, 0f);
    [Tooltip("Alpha del panel en estado peek (casi oculto).")]
    [SerializeField] [Range(0f, 1f)] private float peekAlpha = 0.75f;
    [SerializeField] private float panelTransitionTime = 0.45f;
    [SerializeField] private Ease panelOpenEase = Ease.OutCubic;
    [SerializeField] private Ease panelCloseEase = Ease.InCubic;

    [Header("Refs - Carruseles")]
    [SerializeField] private CarouselUI ammoCarousel;
    [SerializeField] private CarouselUI inventoryCarousel;
    [SerializeField] private InventoryCarouselFeed inventoryFeed;

    [Header("Refs - Tabs")]
    [SerializeField] private RectTransform tabsRoot;

    [Header("Refs - Auxiliares")]
    [SerializeField] private GameObject ammoTabContent;
    [SerializeField] private GameObject inventoryTabContent;

    [Header("Player")]
    [SerializeField] private PlayerInventory playerInventory;

    [Header("Audio")]
    [SerializeField] private string openSfxId = "ui_open";
    [SerializeField] private string closeSfxId = "ui_close";
    [SerializeField] private string tabSwapSfxId = "ui_swap";
    [Tooltip("Sonido cuando se mueve la selección dentro del carrusel (siguiente/anterior).")]
    [SerializeField] private string moveSfxId = "ui_move";
    [Tooltip("Sonido al confirmar / interactuar en la UI (entrar a items, etc).")]
    [SerializeField] private string interactSfxId = "ui_interact";
    [SerializeField] private string useSfxId = "ui_use";
    [SerializeField] private string denySfxId = "ui_deny";

    [Header("Estado de inicio")]
    [Tooltip("True: empieza completamente fuera de pantalla (cutscenes/debug). False: empieza en peek (normal).")]
    [SerializeField] private bool startHidden = false;

    public bool IsOpen { get; private set; }
    public TabType ActiveTab { get; private set; } = TabType.Ammo;
    public FocusLevel Focus { get; private set; } = FocusLevel.Tabs;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (startHidden) HideInstant();
        else ShowPeekInstant();
    }

    public bool IsUIPanelOpen() => IsOpen;

    #region Input handlers (PlayerInput - SendMessages or Invoke Unity Events)

    public void OnToggleUI(InputAction.CallbackContext ctx)
    {
        if (!ctx.performed) return;
        if (!IsOpen) Open();
        else
        {
            // Si estás en items, primero subes a tabs; segunda pulsación cierra.
            if (Focus == FocusLevel.Items) SetFocus(FocusLevel.Tabs);
            else Close();
        }
    }

    public void OnUISliderLeft(InputAction.CallbackContext ctx)
    {
        if (!ctx.performed || !IsOpen) return;

        bool shiftPressed = Keyboard.current?.shiftKey.isPressed ?? false;

        if (Focus == FocusLevel.Tabs)
        {
            SwitchTab(ActiveTab == TabType.Ammo ? TabType.Inventory : TabType.Ammo);
        }
        else // Items
        {
            if (shiftPressed)
            {
                SwitchTab(ActiveTab == TabType.Ammo ? TabType.Inventory : TabType.Ammo);
                AudioManager.Instance?.PlayUI(tabSwapSfxId);
            }
            else
            {
                GetActiveCarousel()?.Previous();
                AudioManager.Instance?.PlayUI(moveSfxId);
                if (ActiveTab == TabType.Inventory) inventoryFeed?.SyncSelectionFromCarousel();
            }
        }
    }

    public void OnUISliderRight(InputAction.CallbackContext ctx)
    {
        if (!ctx.performed || !IsOpen) return;

        bool shiftPressed = Keyboard.current?.shiftKey.isPressed ?? false;

        if (Focus == FocusLevel.Tabs)
        {
            SwitchTab(ActiveTab == TabType.Ammo ? TabType.Inventory : TabType.Ammo);
        }
        else // Items
        {
            if (shiftPressed)
            {
                SwitchTab(ActiveTab == TabType.Ammo ? TabType.Inventory : TabType.Ammo);
                AudioManager.Instance?.PlayUI(tabSwapSfxId);
            }
            else
            {
                GetActiveCarousel()?.Next();
                AudioManager.Instance?.PlayUI(moveSfxId);
                if (ActiveTab == TabType.Inventory) inventoryFeed?.SyncSelectionFromCarousel();
            }
        }
    }

    public void OnUIInteract(InputAction.CallbackContext ctx)
    {
        if (!ctx.performed || !IsOpen) return;

        if (Focus == FocusLevel.Tabs)
        {
            AudioManager.Instance?.PlayUI(interactSfxId);
            SetFocus(FocusLevel.Items);
        }
        else // Items
        {
            UseSelected();
        }
    }
    #endregion

    #region Open/Close
    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        AudioManager.Instance?.PlayUI(openSfxId);

        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.blocksRaycasts = true;
            panelCanvasGroup.interactable = true;
        }

        // Parte desde la posición peek actual hacia la posición desplegada
        Sequence seq = DOTween.Sequence().SetUpdate(true);
        if (panelRoot != null) seq.Join(panelRoot.DOAnchorPos(panelShownPos, panelTransitionTime).SetEase(panelOpenEase));
        if (panelCanvasGroup != null) seq.Join(panelCanvasGroup.DOFade(1f, panelTransitionTime * 0.6f));
        seq.SetLink(gameObject);

        SetFocus(FocusLevel.Tabs);
        SwitchTab(ActiveTab, instant: true);
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;

        AudioManager.Instance?.PlayUI(closeSfxId);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        Sequence seq = DOTween.Sequence().SetUpdate(true);
        if (panelRoot != null) seq.Join(panelRoot.DOAnchorPos(panelPeekPos, panelTransitionTime).SetEase(panelCloseEase));
        if (panelCanvasGroup != null) seq.Join(panelCanvasGroup.DOFade(peekAlpha, panelTransitionTime * 0.8f));
        seq.OnComplete(() =>
        {
            if (panelCanvasGroup != null)
            {
                panelCanvasGroup.blocksRaycasts = false;
                panelCanvasGroup.interactable = false;
            }
            ItemPreview3D.Instance?.Hide();
        });
        seq.SetLink(gameObject);
    }

    // Completamente fuera de pantalla + invisible. Usado solo en startHidden.
    private void HideInstant()
    {
        if (panelRoot != null) panelRoot.anchoredPosition = panelOffscreenPos;
        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.alpha = 0f;
            panelCanvasGroup.blocksRaycasts = false;
            panelCanvasGroup.interactable = false;
        }
        IsOpen = false;
    }

    // Estado de reposo: el panel asoma ligeramente por la izquierda.
    private void ShowPeekInstant()
    {
        if (panelRoot != null) panelRoot.anchoredPosition = panelPeekPos;
        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.alpha = peekAlpha;
            panelCanvasGroup.blocksRaycasts = false;
            panelCanvasGroup.interactable = false;
        }
        IsOpen = false;
    }
    #endregion

    #region Tabs
    public void SwitchTab(TabType type, bool instant = false)
    {
        bool changed = (ActiveTab != type);
        ActiveTab = type;

        if (ammoTabContent != null) ammoTabContent.SetActive(type == TabType.Ammo);
        if (inventoryTabContent != null) inventoryTabContent.SetActive(type == TabType.Inventory);

        if (changed && !instant) AudioManager.Instance?.PlayUI(tabSwapSfxId);

        // El foco se PRESERVA al cambiar de pestaña: si estabas dentro de los items
        // del carrusel de munición y saltas al de inventario, sigues con el slot
        // central seleccionado en el nuevo carrusel — sin tener que volver a entrar.
        // Esto permite navegar entre carruseles sin depender de TAB.
        SetFocus(Focus);
    }

    #endregion

    #region Focus
    public void SetFocus(FocusLevel level)
    {
        Focus = level;
        bool itemsFocused = (level == FocusLevel.Items);

        CarouselUI.FocusState activeState = itemsFocused
            ? CarouselUI.FocusState.Focused
            : CarouselUI.FocusState.Preview;

        // El carrusel inactivo se queda en Preview (no Unfocused) para que su slot
        // central siga visible/encendido al pasar de una pestaña a otra. Sólo el
        // carrusel activo entra en Focused cuando estamos en el nivel de items.
        // Inactivo primero, activo después: si comparten el mismo CanvasGroup,
        // el activo siempre gana la última escritura del alpha.
        if (ActiveTab == TabType.Ammo)
        {
            inventoryCarousel?.SetFocusState(CarouselUI.FocusState.Preview);
            ammoCarousel?.SetFocusState(activeState);
        }
        else
        {
            ammoCarousel?.SetFocusState(CarouselUI.FocusState.Preview);
            inventoryCarousel?.SetFocusState(activeState);
        }

        // No tocamos tabsRoot.localScale: en la escena actual ese ref está
        // apuntando a un nodo que arrastra todo el panel y al entrar a items
        // hacía que la UI entera se viera más pequeña. El feedback de foco ya
        // lo da el alpha del carrusel y el borde de foco.
    }
    #endregion

    #region Use
    private CarouselUI GetActiveCarousel()
    {
        return ActiveTab == TabType.Ammo ? ammoCarousel : inventoryCarousel;
    }

    private void UseSelected()
    {
        var carousel = GetActiveCarousel();
        var entry = carousel?.CurrentEntry;

        if (entry == null || entry.Value.data == null)
        {
            AudioManager.Instance?.PlayUI(denySfxId);
            return;
        }

        bool consumed = false;
        if (ActiveTab == TabType.Inventory && playerInventory != null)
        {
            playerInventory.CurrentIndex = carousel.CenterIndex;
            consumed = playerInventory.UseCurrentItem();
            AudioManager.Instance?.PlayUI(consumed ? useSfxId : denySfxId);
        }
        else
        {
            // Tab Ammo: el Use() del AmmoTypeData cambia el tipo de bala activo del arma
            consumed = entry.Value.data.Use(playerInventory != null ? playerInventory.gameObject : gameObject);
        }

        // Estática de "refresco" tras consumir un item.
        if (consumed) carousel.FlashStatic();
    }
    #endregion
}
