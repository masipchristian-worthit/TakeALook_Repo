using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
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
    [SerializeField] private Vector2 panelHiddenAnchoredPos = new Vector2(0, 600);
    [SerializeField] private Vector2 panelShownAnchoredPos = Vector2.zero;
    [SerializeField] private float panelTransitionTime = 0.45f;
    [SerializeField] private Ease panelOpenEase = Ease.OutCubic;
    [SerializeField] private Ease panelCloseEase = Ease.InCubic;

    [Header("Refs - Carruseles")]
    [SerializeField] private CarouselUI ammoCarousel;
    [SerializeField] private CarouselUI inventoryCarousel;
    [SerializeField] private InventoryCarouselFeed inventoryFeed;

    [Header("Refs - Tabs")]
    [SerializeField] private RectTransform tabsRoot;
    [SerializeField] private TabButton ammoTabButton;
    [SerializeField] private TabButton inventoryTabButton;

    [Header("Refs - Auxiliares")]
    [SerializeField] private GameObject ammoTabContent;
    [SerializeField] private GameObject inventoryTabContent;

    [Header("Player")]
    [SerializeField] private PlayerInventory playerInventory;

    [Header("Audio")]
    [SerializeField] private string openSfxId = "ui_open";
    [SerializeField] private string closeSfxId = "ui_close";
    [SerializeField] private string tabSwapSfxId = "ui_swap";
    [SerializeField] private string useSfxId = "ui_use";
    [SerializeField] private string denySfxId = "ui_deny";

    [Header("Estado de inicio")]
    [SerializeField] private bool startHidden = true;

    public bool IsOpen { get; private set; }
    public TabType ActiveTab { get; private set; } = TabType.Ammo;
    public FocusLevel Focus { get; private set; } = FocusLevel.Tabs;

    [System.Serializable]
    public class TabButton
    {
        public RectTransform root;
        public Image background;
        public TMP_Text label;
        public Color activeColor = Color.white;
        public Color inactiveColor = new Color(1, 1, 1, 0.4f);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (startHidden) HideInstant();
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

        if (Focus == FocusLevel.Tabs)
        {
            SwitchTab(ActiveTab == TabType.Ammo ? TabType.Inventory : TabType.Ammo);
        }
        else // Items
        {
            GetActiveCarousel()?.Previous();
            if (ActiveTab == TabType.Inventory) inventoryFeed?.SyncSelectionFromCarousel();
        }
    }

    public void OnUISliderRight(InputAction.CallbackContext ctx)
    {
        if (!ctx.performed || !IsOpen) return;

        if (Focus == FocusLevel.Tabs)
        {
            SwitchTab(ActiveTab == TabType.Ammo ? TabType.Inventory : TabType.Ammo);
        }
        else
        {
            GetActiveCarousel()?.Next();
            if (ActiveTab == TabType.Inventory) inventoryFeed?.SyncSelectionFromCarousel();
        }
    }

    public void OnUIInteract(InputAction.CallbackContext ctx)
    {
        if (!ctx.performed || !IsOpen) return;

        if (Focus == FocusLevel.Tabs)
        {
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

        if (panelRoot != null) panelRoot.anchoredPosition = panelHiddenAnchoredPos;
        if (panelCanvasGroup != null) panelCanvasGroup.alpha = 0f;

        Sequence seq = DOTween.Sequence().SetUpdate(true); // unscaled, por si pausas
        if (panelRoot != null) seq.Join(panelRoot.DOAnchorPos(panelShownAnchoredPos, panelTransitionTime).SetEase(panelOpenEase));
        if (panelCanvasGroup != null) seq.Join(panelCanvasGroup.DOFade(1f, panelTransitionTime * 0.8f));
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
        if (panelRoot != null) seq.Join(panelRoot.DOAnchorPos(panelHiddenAnchoredPos, panelTransitionTime).SetEase(panelCloseEase));
        if (panelCanvasGroup != null) seq.Join(panelCanvasGroup.DOFade(0f, panelTransitionTime * 0.8f));
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

    private void HideInstant()
    {
        if (panelRoot != null) panelRoot.anchoredPosition = panelHiddenAnchoredPos;
        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.alpha = 0f;
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

        ApplyTabVisuals();

        if (changed && !instant) AudioManager.Instance?.PlayUI(tabSwapSfxId);

        // Cuando cambias de tab, el foco vuelve al nivel Tabs (la sub-selección no se conserva)
        if (Focus != FocusLevel.Tabs && changed) SetFocus(FocusLevel.Tabs);

        // Refresh focus en carruseles
        ammoCarousel?.SetFocus(type == TabType.Ammo && Focus == FocusLevel.Items);
        inventoryCarousel?.SetFocus(type == TabType.Inventory && Focus == FocusLevel.Items);
    }

    private void ApplyTabVisuals()
    {
        ApplyTabVisual(ammoTabButton, ActiveTab == TabType.Ammo);
        ApplyTabVisual(inventoryTabButton, ActiveTab == TabType.Inventory);
    }

    private void ApplyTabVisual(TabButton tab, bool isActive)
    {
        if (tab == null || tab.root == null) return;

        Color targetCol = isActive ? tab.activeColor : tab.inactiveColor;
        if (tab.background != null) tab.background.DOColor(targetCol, 0.18f).SetUpdate(true).SetLink(tab.root.gameObject);
        if (tab.label != null) tab.label.DOColor(targetCol, 0.18f).SetUpdate(true).SetLink(tab.root.gameObject);
        tab.root.DOScale(isActive ? 1.08f : 1f, 0.18f).SetUpdate(true).SetLink(tab.root.gameObject);
    }
    #endregion

    #region Focus
    public void SetFocus(FocusLevel level)
    {
        Focus = level;
        bool itemsFocused = (level == FocusLevel.Items);

        ammoCarousel?.SetFocus(itemsFocused && ActiveTab == TabType.Ammo);
        inventoryCarousel?.SetFocus(itemsFocused && ActiveTab == TabType.Inventory);

        // Pequeño feedback visual en tabs cuando dejan de tener el foco
        if (tabsRoot != null)
            tabsRoot.DOScale(itemsFocused ? 0.92f : 1f, 0.18f).SetUpdate(true).SetLink(tabsRoot.gameObject);
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

        if (ActiveTab == TabType.Inventory && playerInventory != null)
        {
            playerInventory.CurrentIndex = carousel.CenterIndex;
            bool used = playerInventory.UseCurrentItem();
            AudioManager.Instance?.PlayUI(used ? useSfxId : denySfxId);
        }
        else
        {
            // Tab Ammo: el Use() del AmmoTypeData cambia el tipo de bala activo del arma
            entry.Value.data.Use(playerInventory != null ? playerInventory.gameObject : gameObject);
        }
    }
    #endregion
}
