using UnityEngine;
using System.Collections.Generic;

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

    [Header("설치된 타워 레이어")]
    [Tooltip("건설한 타워(자식 포함)를 이 레이어로 바꿉니다. 이 레이어는 충돌 판정에도 자동 포함됩니다.")]
    public string builtTowerLayerName = "Tower";

    [Header("지면 배치")]
    [Tooltip("지면 높이를 찾을 때 위에서 아래로 레이를 쏘는 시작 높이")]
    public float groundProbeHeight = 50f;

    // 런타임 캐싱 변수들
    private GameObject[] instantiatedPreviews; // 생성된 미리보기 오브젝트들을 담아둘 배열
    private GameObject currentPreviewObj;      // 현재 화면에 띄워진 미리보기 오브젝트
    private Renderer[] currentPreviewRenderers; // 미리보기 오브젝트의 렌더러들 (자식 메쉬 포함)

    private int currentTowerIndex = 0;         // 현재 선택된 타워 번호 (0 ~ 4)
    private bool isBuildMode = false;
    private bool canBuild = false;
    private Material appliedPreviewMat;        // 미리보기에 현재 칠해진 색 (바뀔 때만 다시 칠함)
    private bool waitForMouseRelease = false; // 건설 클릭 후 버튼을 뗄 때까지 사격 차단

    // ★ 칸(그리드) 단위 점유 기록: 콜라이더/레이어 설정과 상관없이 같은 칸에 두 번 짓지 못하게 막는 2중 안전장치
    private readonly Dictionary<Vector2Int, GameObject> occupiedCells = new Dictionary<Vector2Int, GameObject>();
    private int builtTowerLayer = -1;

    // ★ 지면 배치: 프리팹의 기준점(피벗)이 어디에 있든 "모델 바닥"이 지면에 닿도록 맞춤
    private float previewBottomOffset = 0f; // 미리보기 피벗 ~ 모델 바닥 사이 높이
    private float currentGroundY = 0f;      // 현재 칸의 지면 높이

    // ★ 건설 모드이거나, 방금 건설한 클릭을 아직 누르고 있으면 true → PlayerWeaponManager가 사격하지 않음
    public bool BlocksFiring => isBuildMode || waitForMouseRelease;

    void Start()
    {
        // 미리보기 오브젝트를 담을 빈 배열 공간을 슬롯 개수(보통 5개)만큼 초기화합니다.
        instantiatedPreviews = new GameObject[previewPrefabs.Length];

        // 설치된 타워 레이어를 충돌 판정 대상에 자동 포함
        builtTowerLayer = LayerMask.NameToLayer(builtTowerLayerName);
        if (builtTowerLayer >= 0) obstacleLayer |= (1 << builtTowerLayer);
        else Debug.LogWarning($"TowerBuilder: '{builtTowerLayerName}' 레이어가 없습니다. 칸 점유 기록으로만 겹침을 막습니다.");
    }

    void Update()
    {
        if (waitForMouseRelease && !Input.GetMouseButton(0)) waitForMouseRelease = false;

        HandleInput();

        // 건설 모드 중일 때 미리보기 위치 갱신 및 좌클릭 건설 처리
        if (isBuildMode && currentPreviewObj != null)
        {
            UpdatePreviewPosition();

            if (Input.GetMouseButtonDown(0) && canBuild)
            {
                BuildTower();
                CancelBuildMode();
                waitForMouseRelease = true;
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
        appliedPreviewMat = null; // 새 미리보기는 다시 칠하도록
        previewBottomOffset = GetBottomOffset(currentPreviewObj);
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

            // ★ 높이 수정: 고정값(1.0) 대신 실제 지면 높이 + 모델 바닥 보정
            currentGroundY = GetGroundHeight(x, z);
            Vector3 snapPos = new Vector3(x, currentGroundY + previewBottomOffset, z);
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

            Vector3 halfExtents = new Vector3(gridSize * 0.45f, 0.9f, gridSize * 0.45f);
            Vector3 center = new Vector3(checkPos.x, currentGroundY + halfExtents.y + 0.05f, checkPos.z); // 지면 바로 위 공간 검사
            bool isOverlapping = Physics.CheckBox(center, halfExtents, Quaternion.identity, obstacleLayer, QueryTriggerInteraction.Ignore);

            canBuild = !isOverlapping && !IsCellOccupied(ToCell(checkPos));

            // ★ 하나의 오브젝트가 여러 머티리얼을 가져도 모두 초록/빨강으로 교체
            //   (최적화) 색이 바뀔 때만 칠하고, materials 대신 sharedMaterials를 써서 매 프레임 머티리얼 복제(메모리 누수)를 막음
            Material targetMat = canBuild ? matGreen : matRed;
            if (currentPreviewRenderers != null && targetMat != appliedPreviewMat)
            {
                for (int i = 0; i < currentPreviewRenderers.Length; i++)
                {
                    Renderer r = currentPreviewRenderers[i];
                    Material[] mats = new Material[r.sharedMaterials.Length];
                    for (int j = 0; j < mats.Length; j++) mats[j] = targetMat;
                    r.sharedMaterials = mats;
                }
                appliedPreviewMat = targetMat;
            }
        }
    }

    private void BuildTower()
    {
        Vector3 pos = currentPreviewObj.transform.position;

        // PoolManager를 이용해 선택된 타워(currentTowerIndex)를 바닥에 소환
        GameObject tower = PoolManager.Instance.Spawn(towerPrefabs[currentTowerIndex], pos, Quaternion.identity);
        if (tower == null) return;

        // ★ 높이 수정: 실제 타워 모델의 바닥이 지면에 닿도록 보정 (미리보기와 타워 프리팹의 피벗이 달라도 정확히 맞춤)
        Vector3 placed = tower.transform.position;
        placed.y = currentGroundY + GetBottomOffset(tower);
        tower.transform.position = placed;

        // ★ 겹침 버그 수정: 타워 프리팹이 Default 레이어라 충돌 판정에 안 잡히던 문제 → 설치 시 Tower 레이어로 변경
        if (builtTowerLayer >= 0) SetLayerRecursively(tower, builtTowerLayer);

        // (풀에서 재사용된 타워라면 예전 칸 기록은 지움)
        List<Vector2Int> stale = new List<Vector2Int>();
        foreach (var pair in occupiedCells) if (pair.Value == tower) stale.Add(pair.Key);
        foreach (Vector2Int c in stale) occupiedCells.Remove(c);

        occupiedCells[ToCell(pos)] = tower;
    }

    // 해당 위치의 지면 높이 (타워/플레이어/몹은 무시). 지면을 못 찾으면 0
    private float GetGroundHeight(float x, float z)
    {
        int groundMask = ~(obstacleLayer.value | (1 << 2)); // 2 = Ignore Raycast
        Vector3 origin = new Vector3(x, groundProbeHeight, z);
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundProbeHeight * 2f, groundMask, QueryTriggerInteraction.Ignore))
            return hit.point.y;
        return 0f;
    }

    // 오브젝트의 피벗에서 모델 바닥(렌더러 기준 가장 낮은 지점)까지의 높이
    private static float GetBottomOffset(GameObject obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return 0f;

        float minY = float.MaxValue;
        foreach (Renderer r in renderers)
        {
            if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
            minY = Mathf.Min(minY, r.bounds.min.y);
        }
        return minY == float.MaxValue ? 0f : obj.transform.position.y - minY;
    }

    private Vector2Int ToCell(Vector3 worldPos)
    {
        return new Vector2Int(Mathf.RoundToInt(worldPos.x / gridSize), Mathf.RoundToInt(worldPos.z / gridSize));
    }

    // 칸에 타워가 있고, 그 타워가 아직 살아있으면(활성) 점유 중
    private bool IsCellOccupied(Vector2Int cell)
    {
        if (!occupiedCells.TryGetValue(cell, out GameObject tower)) return false;
        if (tower != null && tower.activeInHierarchy) return true;
        occupiedCells.Remove(cell); // 파괴/풀 반환된 타워 칸은 비움
        return false;
    }

    private static void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform) SetLayerRecursively(child.gameObject, layer);
    }
}