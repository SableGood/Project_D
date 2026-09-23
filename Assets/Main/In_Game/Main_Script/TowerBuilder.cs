using UnityEngine;

public class TowerBuilder : MonoBehaviour
{
    [Header("건설 설정")]
    public GameObject towerPrefab;
    public GameObject previewPrefab;
    public float gridSize = 2.0f;

    [Header("최대 건설 가능 거리")]
    public float maxBuildDistance = 10.0f;

    [Header("미리보기 색상")]
    public Material matGreen;
    public Material matRed;

    [Header("충돌 판정 레이어")]
    public LayerMask obstacleLayer;

    private GameObject currentPreview;
    private Renderer previewRenderer;
    private bool isBuildMode = false;
    private bool canBuild = false;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            isBuildMode = !isBuildMode;
            if (isBuildMode) StartBuildMode();
            else CancelBuildMode();
        }

        if (isBuildMode && currentPreview != null)
        {
            UpdatePreviewPosition();

            if (Input.GetMouseButtonDown(0) && canBuild)
            {
                BuildTower();
                CancelBuildMode();
            }
        }
    }

    private void StartBuildMode()
    {
        if (currentPreview == null && previewPrefab != null)
        {
            currentPreview = Instantiate(previewPrefab);
            previewRenderer = currentPreview.GetComponent<Renderer>();
        }
    }

    private void CancelBuildMode()
    {
        isBuildMode = false;
        if (currentPreview != null) Destroy(currentPreview);
    }

    private void UpdatePreviewPosition()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

        if (groundPlane.Raycast(ray, out float rayDistance))
        {
            Vector3 hitPoint = ray.GetPoint(rayDistance);

            float x = Mathf.Round(hitPoint.x / gridSize) * gridSize;
            float z = Mathf.Round(hitPoint.z / gridSize) * gridSize;

            Vector3 snapPos = new Vector3(x, 1.0f, z);
            currentPreview.transform.position = snapPos;

            CheckPlacementValidity(snapPos);
        }
    }

    private void CheckPlacementValidity(Vector3 checkPos)
    {
        Vector2 playerPos2D = new Vector2(transform.position.x, transform.position.z);
        Vector2 checkPos2D = new Vector2(checkPos.x, checkPos.z);
        float distance = Vector2.Distance(playerPos2D, checkPos2D);

        bool isTooFar = distance > maxBuildDistance;

        if (isTooFar)
        {
            // 거리가 멀면 미리보기 오브젝트 자체를 꺼버림 (설치 불가 상태)
            if (currentPreview.activeSelf) currentPreview.SetActive(false);
            canBuild = false;
        }
        else
        {
            // 거리 안으로 들어오면 다시 켜고 겹침 검사 진행
            if (!currentPreview.activeSelf) currentPreview.SetActive(true);

            Vector3 center = new Vector3(checkPos.x, 1.0f, checkPos.z);
            Vector3 halfExtents = new Vector3(gridSize * 0.45f, 0.9f, gridSize * 0.45f);
            bool isOverlapping = Physics.CheckBox(center, halfExtents, Quaternion.identity, obstacleLayer);

            canBuild = !isOverlapping;

            if (previewRenderer != null)
            {
                previewRenderer.material = canBuild ? matGreen : matRed;
            }
        }
    }

    private void BuildTower()
    {
        Instantiate(towerPrefab, currentPreview.transform.position, Quaternion.identity);
    }
}