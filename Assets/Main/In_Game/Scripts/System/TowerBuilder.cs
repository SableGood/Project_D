using UnityEngine;

public class TowerBuilder : MonoBehaviour
{
    [Header("건설 설정 (1~5번 슬롯)")]
    [Tooltip("순서대로 TR010 ~ TR050 타워 프리팹을 넣으세요.")]
    public GameObject[] towerPrefabs;
    [Tooltip("순서대로 TR010 ~ TR050 타워의 미리보기용 프리팹을 넣으세요.")]
    public GameObject[] previewPrefabs;
    public float gridSize = 2.0f;

    [Header("최대 건설 가능 거리")]
    public float maxBuildDistance = 10.0f;

    [Header("미리보기 색상")]
    public Material matGreen;
    public Material matRed;

    [Header("충돌 판정 레이어")]
    public LayerMask obstacleLayer;

    // 런타임 캐싱 변수들
    private GameObject[] instantiatedPreviews; // 생성된 미리보기 오브젝트들을 담아둘 배열
    private GameObject currentPreviewObj;      // 현재 화면에 띄워진 미리보기 오브젝트
    private Renderer[] currentPreviewRenderers; // 미리보기 오브젝트의 렌더러들 (자식 메쉬 포함)

    private int currentTowerIndex = 0;         // 현재 선택된 타워 번호 (0 ~ 4)
    private bool isBuildMode = false;
    private bool canBuild = false;

    void Start()
    {
        // 미리보기 오브젝트를 담을 빈 배열 공간을 슬롯 개수(보통 5개)만큼 초기화합니다.
        instantiatedPreviews = new GameObject[previewPrefabs.Length];
    }

    void Update()
    {
        HandleInput();

        // 건설 모드 중일 때 미리보기 위치 갱신 및 좌클릭 건설 처리
        if (isBuildMode && currentPreviewObj != null)
        {
            UpdatePreviewPosition();

            if (Input.GetMouseButtonDown(0) && canBuild)
            {
                BuildTower();
                CancelBuildMode();
            }
        }
    }

    private void HandleInput()
    {
        // 1번(0) ~ 5번(4) 키 입력 감지
        if (Input.GetKeyDown(KeyCode.Alpha1)) SelectTower(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) SelectTower(1);
        if (Input.GetKeyDown(KeyCode.Alpha3)) SelectTower(2);
        if (Input.GetKeyDown(KeyCode.Alpha4)) SelectTower(3);
        if (Input.GetKeyDown(KeyCode.Alpha5)) SelectTower(4);
    }

    private void SelectTower(int index)
    {
        // 배열 범위를 벗어나거나 해당 슬롯에 프리팹이 비어있으면 무시
        if (index >= towerPrefabs.Length || towerPrefabs[index] == null) return;

        // 이미 같은 타워를 들고 있는 상태에서 해당 키를 한 번 더 누르면 건설 모드 취소 (토글 기능)
        if (isBuildMode && currentTowerIndex == index)
        {
            CancelBuildMode();
            return;
        }

        // 새로운 타워 선택 및 건설 모드 진입
        currentTowerIndex = index;
        isBuildMode = true;
        StartBuildMode();
    }

    private void StartBuildMode()
    {
        // 기존에 켜져 있던 다른 미리보기 오브젝트가 있다면 끕니다.
        if (currentPreviewObj != null)
        {
            currentPreviewObj.SetActive(false);
        }

        // 해당 인덱스의 미리보기가 아직 씬에 생성되지 않았다면 최초 1회 생성 (Instantiate)
        if (instantiatedPreviews[currentTowerIndex] == null)
        {
            instantiatedPreviews[currentTowerIndex] = Instantiate(previewPrefabs[currentTowerIndex]);
        }

        // 현재 미리보기 객체 갱신 및 활성화
        currentPreviewObj = instantiatedPreviews[currentTowerIndex];
        currentPreviewObj.SetActive(true);

        // ProBuilder로 만든 타워는 자식 오브젝트 여러 개로 이루어져 있을 수 있으므로 GetComponentsInChildren 사용
        currentPreviewRenderers = currentPreviewObj.GetComponentsInChildren<Renderer>();
    }

    private void CancelBuildMode()
    {
        isBuildMode = false;
        if (currentPreviewObj != null)
        {
            currentPreviewObj.SetActive(false);
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
            currentPreviewObj.transform.position = snapPos;

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
            if (currentPreviewObj.activeSelf) currentPreviewObj.SetActive(false);
            canBuild = false;
        }
        else
        {
            if (!currentPreviewObj.activeSelf) currentPreviewObj.SetActive(true);

            Vector3 center = new Vector3(checkPos.x, 1.0f, checkPos.z);
            Vector3 halfExtents = new Vector3(gridSize * 0.45f, 0.9f, gridSize * 0.45f);
            bool isOverlapping = Physics.CheckBox(center, halfExtents, Quaternion.identity, obstacleLayer);

            canBuild = !isOverlapping;

            // ★ 수정됨: 하나의 오브젝트가 3개의 머티리얼을 가지고 있어도 모두 교체하도록 대응
            if (currentPreviewRenderers != null)
            {
                Material targetMat = canBuild ? matGreen : matRed;
                for (int i = 0; i < currentPreviewRenderers.Length; i++)
                {
                    Renderer r = currentPreviewRenderers[i];
                    Material[] mats = r.materials;
                    // 메쉬가 가진 머티리얼 개수(3개)만큼 반복해서 전부 초록/빨강으로 덮어씌움
                    for (int j = 0; j < mats.Length; j++)
                    {
                        mats[j] = targetMat;
                    }
                    r.materials = mats;
                }
            }
        }
    }

    private void BuildTower()
    {
        // PoolManager를 이용해 선택된 타워(currentTowerIndex)를 바닥에 소환
        PoolManager.Instance.Spawn(towerPrefabs[currentTowerIndex], currentPreviewObj.transform.position, Quaternion.identity);
    }
}