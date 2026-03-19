using UnityEngine;
using TMPro;

public class UIManager : MonoBehaviour
{
    [Header("Info Card")]
    public GameObject      infoCardRoot;
    public TextMeshProUGUI infoCardTitle;
    public TextMeshProUGUI infoCardBody;

    [Header("Mission Card")]
    public GameObject      missionCardRoot;
    public TextMeshProUGUI missionCardTitle;
    public TextMeshProUGUI missionCardBody;

    [Header("Globe reference")]
    public Transform globeTransform;

    [Header("Float offset above point")]
    public float cardOffset = 0.18f;

    private CityPoint _infoCity;
    private CityPoint _missionFrom;
    private CityPoint _missionTo;

    private void Awake()
    {
        HideAll();
    }

    private void LateUpdate()
    {
        if (infoCardRoot.activeSelf && _infoCity != null)
            PlaceCard(infoCardRoot, _infoCity.transform.position);

        if (missionCardRoot.activeSelf && _missionFrom != null && _missionTo != null)
        {
            Vector3 mid = (_missionFrom.transform.position + _missionTo.transform.position) * 0.5f;
            PlaceCard(missionCardRoot, mid);
        }
    }

    public void ShowInfoCard(CityPoint city)
    {
        _infoCity = city;
        if (infoCardTitle != null) infoCardTitle.text = city.cityName;
        if (infoCardBody  != null) infoCardBody .text = city.cityDescription;

        infoCardRoot   .SetActive(true);
        missionCardRoot.SetActive(false);
    }

    public void ShowMissionCard(CityPoint from, CityPoint to, string missionText)
    {
        _missionFrom = from;
        _missionTo   = to;
        if (missionCardTitle != null) missionCardTitle.text = $"{from.cityName}  →  {to.cityName}";
        if (missionCardBody  != null) missionCardBody .text = missionText;

        infoCardRoot   .SetActive(false);
        missionCardRoot.SetActive(true);
    }

    public void HideAll()
    {
        _infoCity    = null;
        _missionFrom = null;
        _missionTo   = null;
        if (infoCardRoot    != null) infoCardRoot   .SetActive(false);
        if (missionCardRoot != null) missionCardRoot.SetActive(false);
    }

    private Vector3 GetGlobeCenter()
    {
        if (globeTransform != null) return globeTransform.position;
        var g = GameObject.Find("Globe");
        if (g != null) { globeTransform = g.transform; return g.transform.position; }
        return Vector3.zero;
    }

    private void PlaceCard(GameObject card, Vector3 worldAnchorPos)
    {
        Vector3 center  = GetGlobeCenter();
        Vector3 outward = (worldAnchorPos - center).normalized;
        card.transform.position = worldAnchorPos + outward * cardOffset;

        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 dir = card.transform.position - cam.transform.position;
            if (dir.sqrMagnitude > 0.0001f)
                card.transform.rotation = Quaternion.LookRotation(dir);
        }
    }
}
