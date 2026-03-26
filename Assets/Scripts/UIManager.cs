using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UIManager v2 — Destination Card Layout
///
/// Info mode  (one city tapped):
///   Destination card image floats above the city dot; description text appears below.
///
/// Route mode — direct (two cities connected, no transfer):
///   Departure card + text on the LEFT.
///   Destination card + text on the RIGHT.
///   The whole panel floats above the globe centre facing the camera.
///
/// Route mode — transfer (two cities connected via an intermediate hub):
///   Departure card + text on the LEFT.
///   Transfer city card + text in the CENTRE.
///   Destination card + text on the RIGHT.
///   The whole panel floats above the globe centre facing the camera.
///
/// All panels are built programmatically in Start() if the Inspector references
/// are not already assigned, so no manual hierarchy changes are required.
///
/// Legacy serialised fields (infoCardRoot / missionCardRoot etc.) are kept so the
/// existing scene YAML does not produce missing-reference errors after recompile.
/// Those old panels are hidden on Start and are never shown.
/// </summary>
public class UIManager : MonoBehaviour
{
    // ── Legacy fields — kept so existing scene YAML references don't break ──
    [Header("Legacy Text Panels (kept for serialisation — hidden at runtime)")]
    public GameObject      infoCardRoot;
    public TextMeshProUGUI infoCardTitle;
    public TextMeshProUGUI infoCardBody;
    public GameObject      missionCardRoot;
    public TextMeshProUGUI missionCardTitle;
    public TextMeshProUGUI missionCardBody;

    // ── New Info Panel ──────────────────────────────────────────────────────
    [Header("Info Panel — auto-created if null")]
    public GameObject      infoImagePanel;   // root WorldSpace Canvas
    public Image           infoCardImage;    // destination card PNG
    public TextMeshProUGUI infoCardText;     // description text below card

    // ── New Route Dual Panel (direct routes) ────────────────────────────────
    [Header("Route Dual Panel — auto-created if null")]
    public GameObject      routeDualPanel;   // root WorldSpace Canvas
    public Image           leftCardImage;    // departure card image
    public TextMeshProUGUI leftCardText;     // departure description
    public Image           rightCardImage;   // destination card image
    public TextMeshProUGUI rightCardText;    // destination description

    // ── New Route Triple Panel (transfer routes) ─────────────────────────────
    [Header("Route Triple Panel — auto-created if null")]
    public GameObject      routeTriplePanel;  // root WorldSpace Canvas
    public Image           triLeftCardImage;  // departure card image
    public TextMeshProUGUI triLeftCardText;   // departure description
    public Image           triMidCardImage;   // transfer city card image
    public TextMeshProUGUI triMidCardText;    // transfer city description
    public Image           triRightCardImage; // destination card image
    public TextMeshProUGUI triRightCardText;  // destination description

    // ── Scene refs ──────────────────────────────────────────────────────────
    [Header("Globe reference")]
    public Transform globeTransform;

    [Header("Positioning")]
    [Tooltip("How far outward from the city dot the info panel floats (world units)")]
    public float infoCardOffset = 0.22f;

    [Tooltip("Distance from globe centre toward camera for the route panel")]
    public float routePanelDist = 0.65f;

    // ── Internal state ──────────────────────────────────────────────────────
    private CityPoint _infoCity;
    private CityPoint _routeFrom;
    private CityPoint _routeTo;
    private Transform _globeCache;   // cached in GlobeCenter() to avoid per-frame Find call

    // ── Canvas layout constants (pixels at CANVAS_SCALE) ──────────────────
    private const float CANVAS_SCALE    = 0.0012f;
    private const float INFO_W          = 300f;
    private const float INFO_H          = 440f;
    private const float ROUTE_W         = 720f;
    private const float ROUTE_H         = 420f;
    private const float TRANSFER_W      = 1080f;
    private const float TRANSFER_H      = 420f;
    private const float IMG_TOP_FRAC    = 0.60f;   // fraction of panel used by card image
    private const float ROUTE_GAP_FRAC  = 0.015f;  // gap fraction between columns

    // ── Lifecycle ───────────────────────────────────────────────────────────

    private void Awake()
    {
        HideAll();
    }

    private void Start()
    {
        // Suppress legacy panels permanently
        SafeSetActive(infoCardRoot,    false);
        SafeSetActive(missionCardRoot, false);

        // Build new panels if Inspector references weren't pre-assigned
        if (infoImagePanel == null || infoCardImage == null)
            BuildInfoPanel();

        if (routeDualPanel == null || leftCardImage == null)
            BuildRoutePanel();

        if (routeTriplePanel == null || triLeftCardImage == null)
            BuildTransferPanel();
    }

    private void LateUpdate()
    {
        if (infoImagePanel != null && infoImagePanel.activeSelf && _infoCity != null)
            FloatAboveCity(infoImagePanel, _infoCity.transform.position);

        if (routeDualPanel != null && routeDualPanel.activeSelf)
            FloatAboveGlobe(routeDualPanel);

        if (routeTriplePanel != null && routeTriplePanel.activeSelf)
            FloatAboveGlobe(routeTriplePanel);
    }

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>Show destination card + description for a single clicked city.</summary>
    public void ShowInfoCard(CityPoint city)
    {
        _infoCity = city;

        if (infoCardImage != null)
        {
            infoCardImage.sprite  = city.destinationCard;
            infoCardImage.enabled = city.destinationCard != null;
        }
        if (infoCardText != null)
            infoCardText.text = city.cityDescription;

        SafeSetActive(infoImagePanel,   true);
        SafeSetActive(routeDualPanel,   false);
        SafeSetActive(routeTriplePanel, false);
    }

    /// <summary>
    /// Show route cards.
    /// <paramref name="transfer"/> is null for a direct route (dual panel),
    /// or a valid CityPoint for a transfer route (triple panel).
    /// </summary>
    public void ShowMissionCard(CityPoint from, CityPoint transfer, CityPoint to)
    {
        _routeFrom = from;
        _routeTo   = to;

        SafeSetActive(infoImagePanel, false);

        if (transfer != null)
        {
            // Triple panel — departure | transfer | destination
            SetCardGroup(triLeftCardImage,  triLeftCardText,  from);
            SetCardGroup(triMidCardImage,   triMidCardText,   transfer);
            SetCardGroup(triRightCardImage, triRightCardText, to);

            SafeSetActive(routeDualPanel,   false);
            SafeSetActive(routeTriplePanel, true);
        }
        else
        {
            // Dual panel — departure | destination
            SetCardGroup(leftCardImage,  leftCardText,  from);
            SetCardGroup(rightCardImage, rightCardText, to);

            SafeSetActive(routeDualPanel,   true);
            SafeSetActive(routeTriplePanel, false);
        }
    }

    public void HideAll()
    {
        _infoCity  = null;
        _routeFrom = null;
        _routeTo   = null;

        SafeSetActive(infoImagePanel,   false);
        SafeSetActive(routeDualPanel,   false);
        SafeSetActive(routeTriplePanel, false);
        SafeSetActive(infoCardRoot,     false);
        SafeSetActive(missionCardRoot,  false);
    }

    // ── Positioning ─────────────────────────────────────────────────────────

    private Vector3 GlobeCenter()
    {
        if (globeTransform != null) return globeTransform.position;
        if (_globeCache != null)    return _globeCache.position;
        var g = GameObject.Find("Globe");
        if (g != null) { _globeCache = g.transform; return g.transform.position; }
        return Vector3.zero;
    }

    private void FloatAboveCity(GameObject panel, Vector3 cityWorldPos)
    {
        Vector3 center  = GlobeCenter();
        Vector3 outward = (cityWorldPos - center).normalized;
        panel.transform.position = cityWorldPos + outward * infoCardOffset;
        FaceCamera(panel);
    }

    private void FloatAboveGlobe(GameObject panel)
    {
        Camera  cam    = Camera.main;
        Vector3 center = GlobeCenter();
        Vector3 dir    = (cam != null)
            ? (cam.transform.position - center).normalized
            : Vector3.up;
        panel.transform.position = center + dir * routePanelDist;
        FaceCamera(panel);
    }

    private static void FaceCamera(GameObject panel)
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 toCamera = panel.transform.position - cam.transform.position;
        if (toCamera.sqrMagnitude > 0.0001f)
            panel.transform.rotation = Quaternion.LookRotation(toCamera);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static void SetCardGroup(Image imgComp, TextMeshProUGUI txtComp, CityPoint city)
    {
        if (imgComp != null)
        {
            imgComp.sprite  = city.destinationCard;
            imgComp.enabled = city.destinationCard != null;
        }
        if (txtComp != null)
            txtComp.text = city.cityDescription;
    }

    // ── Programmatic UI Construction ────────────────────────────────────────

    private void BuildInfoPanel()
    {
        Transform uiRoot = FindUIRoot();

        var go = CreateWorldCanvas("InfoImagePanel", uiRoot, INFO_W, INFO_H, CANVAS_SCALE);

        // Dark backdrop
        var bg = MakeRectChild(go.transform, "BG", new Vector2(0, 0), new Vector2(1, 1));
        var bgImg    = bg.gameObject.AddComponent<Image>();
        bgImg.color  = new Color(0.07f, 0.09f, 0.20f, 0.88f);

        // Destination card image (top portion)
        var imgRT    = MakeRectChild(go.transform, "CardImage",
                           new Vector2(0.04f, IMG_TOP_FRAC),
                           new Vector2(0.96f, 0.97f));
        infoCardImage                = imgRT.gameObject.AddComponent<Image>();
        infoCardImage.preserveAspect = true;
        infoCardImage.raycastTarget  = false;

        // Description text (bottom portion)
        var txtRT    = MakeRectChild(go.transform, "DescText",
                           new Vector2(0.04f, 0.03f),
                           new Vector2(0.96f, IMG_TOP_FRAC - 0.02f));
        infoCardText                    = txtRT.gameObject.AddComponent<TextMeshProUGUI>();
        infoCardText.fontSize           = 26;
        infoCardText.color              = Color.white;
        infoCardText.alignment          = TextAlignmentOptions.TopLeft;
        infoCardText.enableWordWrapping = true;
        infoCardText.overflowMode       = TextOverflowModes.Ellipsis;
        infoCardText.raycastTarget      = false;

        infoImagePanel = go;
        go.SetActive(false);
    }

    private void BuildRoutePanel()
    {
        Transform uiRoot = FindUIRoot();

        var go = CreateWorldCanvas("RouteDualPanel", uiRoot, ROUTE_W, ROUTE_H, CANVAS_SCALE);

        // Dark backdrop
        var bg = MakeRectChild(go.transform, "BG", new Vector2(0, 0), new Vector2(1, 1));
        var bgImg    = bg.gameObject.AddComponent<Image>();
        bgImg.color  = new Color(0.07f, 0.09f, 0.20f, 0.88f);

        // Thin vertical divider
        float gap = ROUTE_GAP_FRAC;
        var div    = MakeRectChild(go.transform, "Divider",
                         new Vector2(0.5f - gap * 0.5f, 0.03f),
                         new Vector2(0.5f + gap * 0.5f, 0.97f));
        var divImg = div.gameObject.AddComponent<Image>();
        divImg.color = new Color(0.5f, 0.65f, 1f, 0.35f);

        // Left group — departure
        var leftRT = MakeRectChild(go.transform, "DepartureGroup",
                         new Vector2(0.01f, 0f),
                         new Vector2(0.5f - gap, 1f));
        BuildCardGroup(leftRT, "Departure", "DEPARTURE",
                       out leftCardImage, out leftCardText);

        // Right group — destination
        var rightRT = MakeRectChild(go.transform, "DestinationGroup",
                          new Vector2(0.5f + gap, 0f),
                          new Vector2(0.99f, 1f));
        BuildCardGroup(rightRT, "Destination", "DESTINATION",
                       out rightCardImage, out rightCardText);

        routeDualPanel = go;
        go.SetActive(false);
    }

    private void BuildTransferPanel()
    {
        Transform uiRoot = FindUIRoot();

        var go = CreateWorldCanvas("RouteTriplePanel", uiRoot, TRANSFER_W, TRANSFER_H, CANVAS_SCALE);

        // Dark backdrop
        var bg = MakeRectChild(go.transform, "BG", new Vector2(0, 0), new Vector2(1, 1));
        var bgImg    = bg.gameObject.AddComponent<Image>();
        bgImg.color  = new Color(0.07f, 0.09f, 0.20f, 0.88f);

        float gap = ROUTE_GAP_FRAC * 0.66f;  // narrower gap for 3 columns

        // Divider 1 (between departure and transfer)
        float div1X = 0.333f;
        var divA    = MakeRectChild(go.transform, "Divider1",
                          new Vector2(div1X - gap * 0.5f, 0.03f),
                          new Vector2(div1X + gap * 0.5f, 0.97f));
        divA.gameObject.AddComponent<Image>().color = new Color(0.5f, 0.65f, 1f, 0.35f);

        // Divider 2 (between transfer and destination)
        float div2X = 0.667f;
        var divB    = MakeRectChild(go.transform, "Divider2",
                          new Vector2(div2X - gap * 0.5f, 0.03f),
                          new Vector2(div2X + gap * 0.5f, 0.97f));
        divB.gameObject.AddComponent<Image>().color = new Color(0.5f, 0.65f, 1f, 0.35f);

        // Left group — departure
        var leftRT = MakeRectChild(go.transform, "DepartureGroup",
                         new Vector2(0.005f, 0f),
                         new Vector2(div1X - gap, 1f));
        BuildCardGroup(leftRT, "Departure", "DEPARTURE",
                       out triLeftCardImage, out triLeftCardText);

        // Middle group — transfer city
        var midRT = MakeRectChild(go.transform, "TransferGroup",
                        new Vector2(div1X + gap, 0f),
                        new Vector2(div2X - gap, 1f));
        BuildCardGroup(midRT, "Transfer", "TRANSFER VIA",
                       out triMidCardImage, out triMidCardText);

        // Right group — destination
        var rightRT = MakeRectChild(go.transform, "DestinationGroup",
                          new Vector2(div2X + gap, 0f),
                          new Vector2(0.995f, 1f));
        BuildCardGroup(rightRT, "Destination", "DESTINATION",
                       out triRightCardImage, out triRightCardText);

        routeTriplePanel = go;
        go.SetActive(false);
    }

    private void BuildCardGroup(RectTransform parent,
                                string name, string header,
                                out Image     imgOut,
                                out TextMeshProUGUI txtOut)
    {
        // "DEPARTURE" / "TRANSFER VIA" / "DESTINATION" label at the very top
        var lblRT    = MakeRectChild(parent, name + "Label",
                           new Vector2(0.02f, 0.90f),
                           new Vector2(0.98f, 0.98f));
        var lbl              = lblRT.gameObject.AddComponent<TextMeshProUGUI>();
        lbl.text             = header;
        lbl.fontSize         = 19;
        lbl.color            = new Color(0.65f, 0.80f, 1f, 1f);
        lbl.alignment        = TextAlignmentOptions.Center;
        lbl.fontStyle        = FontStyles.Bold;
        lbl.raycastTarget    = false;

        // Card image (middle portion)
        var imgRT    = MakeRectChild(parent, name + "CardImage",
                           new Vector2(0.03f, IMG_TOP_FRAC - 0.08f),
                           new Vector2(0.97f, 0.89f));
        imgOut                = imgRT.gameObject.AddComponent<Image>();
        imgOut.preserveAspect = true;
        imgOut.raycastTarget  = false;

        // Description text (lower portion)
        var txtRT    = MakeRectChild(parent, name + "Text",
                           new Vector2(0.03f, 0.03f),
                           new Vector2(0.97f, IMG_TOP_FRAC - 0.10f));
        txtOut                    = txtRT.gameObject.AddComponent<TextMeshProUGUI>();
        txtOut.fontSize           = 21;
        txtOut.color              = Color.white;
        txtOut.alignment          = TextAlignmentOptions.TopLeft;
        txtOut.enableWordWrapping = true;
        txtOut.overflowMode       = TextOverflowModes.Ellipsis;
        txtOut.raycastTarget      = false;
    }

    // ── Static helpers ───────────────────────────────────────────────────────

    private Transform FindUIRoot()
    {
        var uiRoot = GameObject.Find("UI_Root");
        return uiRoot != null ? uiRoot.transform : transform;
    }

    private static GameObject CreateWorldCanvas(string name, Transform parent,
                                                 float w, float h, float scale)
    {
        var go            = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);

        var canvas        = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var scaler                  = go.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 4f;

        go.AddComponent<GraphicRaycaster>();

        var rt           = go.GetComponent<RectTransform>();
        rt.sizeDelta     = new Vector2(w, h);
        rt.localScale    = Vector3.one * scale;
        rt.localPosition = Vector3.zero;

        return go;
    }

    private static RectTransform MakeRectChild(Transform parent, string name,
                                               Vector2 anchorMin, Vector2 anchorMax)
    {
        var go       = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt       = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    private static void SafeSetActive(GameObject go, bool active)
    {
        if (go != null) go.SetActive(active);
    }
}
