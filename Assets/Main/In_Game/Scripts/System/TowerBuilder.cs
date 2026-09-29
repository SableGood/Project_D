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
        // 1번 키를 누를 때마다 건설 모드 토글
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            isBuildMode = !isBuildMode;
            if (isBuildMode) StartBuildMode();
            else CancelBuildMode();
        }

        // 건설 모드 중일 때 미리보기 위치 갱신 및 좌클릭 건설 처리
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
        // [최적화] 미리보기 객체가 없다면 최초 1회만 Instantiate로 생성합니다.
        if (currentPreview == null && previewPrefab != null)
        {
            currentPreview = Instantiate(previewPrefab);
            previewRenderer = currentPreview.GetComponent<Renderer>();
        }
        // [최적화] 이미 만들어둔 객체가 있다면 파괴/재생성하지 않고 활성화(켜기)만 수행합니다.
        else if (currentPreview != null)
        {
            currentPreview.SetActive(true);
        }
    }

    private void CancelBuildMode()
    {
        isBuildMode = false;
        // [최적화] Destroy 대신 비활성화(끄기)하여 메모리 할당 부하(GC 스파이크)를 원천 차단합니다.
        if (currentPreview != null)
        {
            currentPreview.SetActive(false);
        }
    }

    private void UpdatePreviewPosition()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

        if (groundPlane.Raycast(ray, out float rayDistance))
        {
            Vector3 hitPoint = ray.GetPoint(rayDistance);

            // 바둑판 스냅 로직
            float x = Mathf.Round(hitPoint.x / gridSize) * gridSize;
            float z = Mathf.Round(hitPoint.z / gridSize) * gridSize;

            Vector3 snapPos = new Vector3(x, 1.0f, z);
            currentPreview.transform.position = snapPos;

            // 위치 변경 후 설치 가능 여부 검사
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
        // [최적화] 타워 설치 시에도 Instantiate 대신 시스템에 구현해 둔 PoolManager를 적극 사용합니다.
        PoolManager.Instance.Spawn(towerPrefab, currentPreview.transform.position, Quaternion.identity);
    }
}