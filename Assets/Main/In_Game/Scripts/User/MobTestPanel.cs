using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

// 몹 테스트 패널 (Wave Start 버튼 아래에 자동 생성, F2로 숨기기/보이기)
//  - 몹 버튼: 해당 몹 1마리를 플레이어 앞에 소환하고, 아래 정보창에서 스탯/AI 상태를 실시간 표시
//  - 순차 테스트: 목록의 몹을 1마리씩 차례로 소환 (처치하거나 '다음'을 누르면 다음 몹)
//  - AI 정지 / 플레이어 무적: 스탯과 동작을 천천히 관찰할 때 사용
//  - 테스트로 소환한 몹은 웨이브 진행(남은 몹 수)에 영향을 주지 않습니다.
//
// 몹 목록: Resources/Data/MobTestRoster.asset  ([Tools/몹/1. 몹 프리팹 정리] 실행 시 자동 갱신)
public class MobTestPanel : MonoBehaviour
{
    public static MobTestPanel Instance { get; private set; }

    [Header("설정")]
    public KeyCode toggleKey = KeyCode.F2;
    [Tooltip("플레이어 앞 몇 미터에 소환할지")]
    public float spawnDistance = 8f;
    public float panelWidth = 430f;

    private MobTestRoster roster;
    private RectTransform anchorTarget;   // Wave Start 버튼
    private RectTransform canvasRect;
    private RectTransform panel;
    private Font font;

    private Text infoText;
    private Text sequenceButtonText;
    private Text pauseButtonText;
    private Text godButtonText;
    private Text animButtonText;
    private Text animSpeedButtonText;
    private Text slotButtonText;
    private Text slotMarkerButtonText;
    private GameObject lastPrefab;        // 마지막으로 소환한 몹 (무리 소환용)
    public int hordeCount = 10;           // 무리 소환 마릿수 (슬롯 8개 + 대기 2마리)
    private List<string> animClipNames;   // 테스트 패널에서 고를 수 있는 걷기 클립 이름 (몹 프리팹 기준)
    private static readonly float[] AnimSpeedSteps = { 1f, 1.25f, 1.5f, 0.5f, 0.75f };
    private int animSpeedStep;

    private readonly List<GameObject> spawned = new List<GameObject>();
    private GameObject watched;
    private MobAI watchedAI;
    private Health watchedHealth;
    private Weapon watchedWeapon;
    private KnockbackReceiver watchedKnockback;
    private SpriteFrameAnimator watchedAnim;
    private float watchedSpawnTime;
    private float watchedDeathTime = -1f;

    private Health playerHealth;
    private float playerLastDamage;
    private float playerTotalDamage;
    private int playerHitCount;

    private Coroutine sequenceRoutine;
    private bool sequenceSkip;
    private float nextInfoTime;
    private float nextLayoutTime;

    // WaveManager가 시작할 때 호출
    public static void Create(Button waveStartButton)
    {
        if (Instance != null) return;

        MobTestRoster roster = Resources.Load<MobTestRoster>("Data/MobTestRoster");
        if (roster == null || roster.mobs.Count == 0)
        {
            Debug.Log("[몹 테스트] 몹 목록이 없습니다. Tools/몹/1. 몹 프리팹 정리 를 실행하면 테스트 패널이 생깁니다.");
            return;
        }

        GameObject go = new GameObject("MobTestPanel");
        Instance = go.AddComponent<MobTestPanel>();
        Instance.roster = roster;
        Instance.anchorTarget = waveStartButton != null ? waveStartButton.GetComponent<RectTransform>() : null;
        Instance.Build();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (playerHealth != null) playerHealth.OnDamaged -= OnPlayerDamaged;
        MobAI.AiPaused = false;
        SpriteFrameAnimator.TestClipOverride = -2;
        SpriteFrameAnimator.TestSpeedMultiplier = 1f;
        MeleeSlotRing.SystemEnabled = true;
        MeleeSlotRing.ShowMarkers = false;
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey)) panel.gameObject.SetActive(!panel.gameObject.activeSelf);

        TrackPlayer();

        if (!panel.gameObject.activeSelf) return;

        if (Time.unscaledTime >= nextLayoutTime)
        {
            nextLayoutTime = Time.unscaledTime + 0.5f;
            UpdatePanelPosition();
        }

        if (Time.unscaledTime >= nextInfoTime)
        {
            nextInfoTime = Time.unscaledTime + 0.1f;
            RefreshInfo();
        }
    }

    // ------------------------------------------------------------------
    // 소환 / 제거
    // ------------------------------------------------------------------
    private GameObject SpawnMob(GameObject prefab)
    {
        if (prefab == null || PoolManager.Instance == null) return null;

        Vector3 pos = GetSpawnPosition(out Quaternion rot);
        GameObject mob = PoolManager.Instance.Spawn(prefab, pos, rot);
        if (mob == null) return null;

        // 웨이브에서 쓰던 몹이 재사용된 경우, 죽어도 웨이브 남은 수가 줄지 않도록 연결 해제
        MobTracker tracker = mob.GetComponent<MobTracker>();
        if (tracker != null) tracker.Initialize(null);

        spawned.Add(mob);
        Watch(mob);
        lastPrefab = prefab;
        return mob;
    }

    // 마지막으로 소환한 몹을 플레이어 주변 원형으로 여러 마리 소환 (둘러싸기 테스트)
    private void SpawnHorde()
    {
        GameObject prefab = lastPrefab != null ? lastPrefab : (roster.mobs.Count > 0 ? roster.mobs[0] : null);
        if (prefab == null || PoolManager.Instance == null) return;
        Transform player = GameManager.Instance != null ? GameManager.Instance.playerTransform : null;
        Vector3 origin = player != null ? player.position : Vector3.zero;

        GameObject first = null;
        for (int i = 0; i < hordeCount; i++)
        {
            float a = (i / (float)hordeCount) * Mathf.PI * 2f + Random.Range(-0.15f, 0.15f);
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 candidate = origin + dir * spawnDistance;
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 4f, NavMesh.AllAreas)) continue;
            GameObject mob = PoolManager.Instance.Spawn(prefab, hit.position, Quaternion.LookRotation(-dir));
            if (mob == null) continue;
            MobTracker tracker = mob.GetComponent<MobTracker>();
            if (tracker != null) tracker.Initialize(null);
            spawned.Add(mob);
            if (first == null) first = mob;
        }
        if (first != null) { Watch(first); lastPrefab = prefab; }
    }

    private void ToggleSlots()
    {
        MeleeSlotRing.SystemEnabled = !MeleeSlotRing.SystemEnabled;
        if (!MeleeSlotRing.SystemEnabled)
            foreach (MeleeSlotRing r in FindObjectsByType<MeleeSlotRing>(FindObjectsSortMode.None)) r.ReleaseAll();
        UpdateSlotButtons();
    }

    private void ToggleSlotMarkers()
    {
        MeleeSlotRing.ShowMarkers = !MeleeSlotRing.ShowMarkers;
        UpdateSlotButtons();
    }

    private void UpdateSlotButtons()
    {
        if (slotButtonText == null) return;
        slotButtonText.text = MeleeSlotRing.SystemEnabled ? "근접 슬롯: <color=#80FF80>켜짐</color>" : "근접 슬롯: <color=#FF8080>꺼짐</color>";
        slotMarkerButtonText.text = MeleeSlotRing.ShowMarkers ? "슬롯 표시: <color=#80FF80>켜짐</color>" : "슬롯 표시: 꺼짐";
    }

    private Vector3 GetSpawnPosition(out Quaternion rotation)
    {
        Transform player = GameManager.Instance != null ? GameManager.Instance.playerTransform : null;
        Vector3 origin = player != null ? player.position : Vector3.zero;
        Vector3 forward = player != null ? player.forward : Vector3.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        forward.Normalize();

        // 정면 → 실패하면 주변 여러 방향을 시도
        for (int i = 0; i < 8; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 45f, 0f) * forward;
            Vector3 candidate = origin + dir * spawnDistance;
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            {
                rotation = Quaternion.LookRotation(-dir);
                return hit.position;
            }
        }
        rotation = Quaternion.LookRotation(-forward);
        return origin + forward * spawnDistance;
    }

    private void Despawn(GameObject mob)
    {
        if (mob == null || !mob.activeInHierarchy) return;
        if (mob.TryGetComponent(out PooledObject pooled)) pooled.ReleaseToPool();
        else Destroy(mob);
    }

    private void ClearAll()
    {
        StopSequence();
        foreach (GameObject m in spawned) Despawn(m);
        spawned.Clear();
    }

    private void Watch(GameObject mob)
    {
        watched = mob;
        watchedAI = mob.GetComponent<MobAI>();
        watchedHealth = mob.GetComponent<Health>();
        watchedWeapon = mob.GetComponentInChildren<Weapon>();
        watchedKnockback = mob.GetComponent<KnockbackReceiver>();
        watchedAnim = mob.GetComponentInChildren<SpriteFrameAnimator>(true);
        watchedSpawnTime = Time.time;
        watchedDeathTime = -1f;
        playerLastDamage = 0f;
        playerTotalDamage = 0f;
        playerHitCount = 0;
    }

    // ------------------------------------------------------------------
    // 순차 테스트 (테스트 웨이브)
    // ------------------------------------------------------------------
    private void OnSequenceButton()
    {
        if (sequenceRoutine == null) sequenceRoutine = StartCoroutine(SequenceRoutine());
        else sequenceSkip = true; // 진행 중이면 '다음 몹'
    }

    private IEnumerator SequenceRoutine()
    {
        for (int i = 0; i < roster.mobs.Count; i++)
        {
            sequenceSkip = false;
            GameObject mob = SpawnMob(roster.mobs[i]);
            sequenceButtonText.text = $"다음 몹 ▶  ({i + 1}/{roster.mobs.Count})";

            while (mob != null && mob.activeInHierarchy && !sequenceSkip) yield return null;
            if (sequenceSkip) Despawn(mob);

            yield return new WaitForSeconds(1f);
        }
        sequenceRoutine = null;
        sequenceButtonText.text = "순차 테스트 시작";
    }

    private void StopSequence()
    {
        if (sequenceRoutine != null) StopCoroutine(sequenceRoutine);
        sequenceRoutine = null;
        if (sequenceButtonText != null) sequenceButtonText.text = "순차 테스트 시작";
    }

    // ------------------------------------------------------------------
    // 토글
    // ------------------------------------------------------------------
    private void TogglePause()
    {
        MobAI.AiPaused = !MobAI.AiPaused;
        pauseButtonText.text = MobAI.AiPaused ? "몹 AI 정지: <color=#FF8080>켜짐</color>" : "몹 AI 정지: 꺼짐";
    }

    private void ToggleGod()
    {
        if (playerHealth == null) return;
        playerHealth.invincible = !playerHealth.invincible;
        UpdateGodText();
    }

    private void UpdateGodText()
    {
        bool on = playerHealth != null && playerHealth.invincible;
        godButtonText.text = on ? "플레이어 무적: <color=#80FF80>켜짐</color>" : "플레이어 무적: 꺼짐";
    }

    // ------------------------------------------------------------------
    // 걷기 애니메이션 비교 (모든 몹에 적용: 프리팹 설정 → 클립1 → 클립2 → ... → 애니 끔 → 프리팹 설정)
    // ------------------------------------------------------------------
    private void CycleAnimClip()
    {
        int cur = SpriteFrameAnimator.TestClipOverride;
        int next;
        if (cur == -2) next = 0;
        else if (cur == -1) next = -2;
        else next = cur + 1 >= animClipNames.Count ? -1 : cur + 1;
        SpriteFrameAnimator.TestClipOverride = next;
        UpdateAnimButtons();
    }

    private void CycleAnimSpeed()
    {
        animSpeedStep = (animSpeedStep + 1) % AnimSpeedSteps.Length;
        SpriteFrameAnimator.TestSpeedMultiplier = AnimSpeedSteps[animSpeedStep];
        UpdateAnimButtons();
    }

    private void UpdateAnimButtons()
    {
        if (animButtonText == null) return;
        int cur = SpriteFrameAnimator.TestClipOverride;
        string label = cur == -2 ? "프리팹 설정" : cur == -1 ? "<color=#FF8080>끔(정지 이미지)</color>"
            : (cur < animClipNames.Count ? $"<color=#80FF80>{animClipNames[cur]}</color>" : "?");
        animButtonText.text = $"걷기 애니: {label}";
        animSpeedButtonText.text = $"애니 속도 x{SpriteFrameAnimator.TestSpeedMultiplier:0.##}";
    }

    // 몹 목록 중 애니메이션이 있는 프리팹에서 클립 이름 수집
    private List<string> CollectAnimClipNames()
    {
        var names = new List<string>();
        foreach (GameObject prefab in roster.mobs)
        {
            if (prefab == null) continue;
            SpriteFrameAnimator a = prefab.GetComponentInChildren<SpriteFrameAnimator>(true);
            if (a == null) continue;
            for (int i = 0; i < a.walkClips.Count; i++)
            {
                SpriteClip c = a.walkClips[i];
                string n = $"{c.name} ({c.frames.Length}장)";
                if (i < names.Count) { if (!names[i].Contains(n)) names[i] += " / " + n; }
                else names.Add(n);
            }
        }
        return names;
    }

    // ------------------------------------------------------------------
    // 플레이어 피격 기록
    // ------------------------------------------------------------------
    private void TrackPlayer()
    {
        Transform p = GameManager.Instance != null ? GameManager.Instance.playerTransform : null;
        Health h = p != null ? p.GetComponent<Health>() : null;
        if (h == playerHealth) return;

        if (playerHealth != null) playerHealth.OnDamaged -= OnPlayerDamaged;
        playerHealth = h;
        if (playerHealth != null) playerHealth.OnDamaged += OnPlayerDamaged;
        if (godButtonText != null) UpdateGodText();
    }

    private void OnPlayerDamaged(float amount)
    {
        playerLastDamage = amount;
        playerTotalDamage += amount;
        playerHitCount++;
    }

    // ------------------------------------------------------------------
    // 정보창
    // ------------------------------------------------------------------
    private void RefreshInfo()
    {
        if (watched == null || watchedAI == null)
        {
            infoText.text = "몹 버튼을 누르면 해당 몹 1마리가 플레이어 앞에 나타나고\n여기에 스탯과 AI 상태가 표시됩니다.\n\n<color=#AAAAAA>※ 버튼은 탑뷰(커서 보이는 상태)에서 클릭하세요. Tab: 시점 전환</color>";
            return;
        }

        EntityData d = watchedAI.myData;
        string code = d != null ? d.entityCode : watched.name;
        bool alive = watched.activeInHierarchy && watchedHealth != null && !watchedHealth.IsDead;
        if (!alive && watchedDeathTime < 0f) watchedDeathTime = Time.time;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"<b><size=22>[{code}]</size></b>  {TypeLabel(watchedAI)}");

        if (watchedHealth != null)
        {
            string hpColor = alive ? "FFFFFF" : "FF6060";
            sb.AppendLine($"체력 <color=#{hpColor}>{Mathf.CeilToInt(watchedHealth.CurrentHealth)} / {watchedHealth.maxHealth:0}</color>    방어 {watchedHealth.Defense:0.#}");
        }
        if (watchedWeapon != null)
        {
            float perSec = watchedWeapon.attackCooldown > 0f ? 1f / watchedWeapon.attackCooldown : 0f;
            sb.AppendLine($"공격력 {watchedWeapon.attackDamage}    공격속도 {perSec:0.##}/초 ({watchedWeapon.attackCooldown:0.##}초마다)    사거리 {watchedWeapon.attackRange:0.#}");
        }
        if (d != null) sb.AppendLine($"이동속도 {d.moveSpeed:0.#}    넉백 저항 {(watchedKnockback != null ? watchedKnockback.resistance : 0f) * 100f:0}%");
        sb.AppendLine($"<color=#AAAAAA>행동: {BehaviorLabel(watchedAI)}</color>");
        if (watchedAnim != null)
        {
            SpriteClip clip = watchedAnim.CurrentClip;
            if (clip == null) sb.AppendLine("<color=#AAAAAA>애니: 끔 (정지 이미지)</color>");
            else
            {
                string frame = watchedAnim.IsWalking ? $"{watchedAnim.CurrentFrameNumber}/{clip.frames.Length}" : "정지";
                sb.AppendLine($"<color=#AAAAAA>애니: {clip.name} {frame}  ({clip.fps * SpriteFrameAnimator.TestSpeedMultiplier:0.#}fps 기준, 이동 속도에 맞춰 변함)</color>");
            }
        }
        sb.AppendLine("──────────────");

        if (alive)
        {
            sb.AppendLine($"AI 상태: <color=#FFD060>{watchedAI.StateLabel}</color>");
            Transform t = watchedAI.CurrentTarget;
            if (t != null)
            {
                float dist = Vector3.Distance(watched.transform.position, t.position);
                string targetName = t.GetComponent<PlayerController>() != null ? "플레이어" : t.name.Replace("(Clone)", "");
                sb.AppendLine($"목표: {targetName}  (거리 {dist:0.0})");
            }
            sb.AppendLine($"생존 시간 {Time.time - watchedSpawnTime:0.0}초");
        }
        else
        {
            float life = (watchedDeathTime > 0f ? watchedDeathTime : Time.time) - watchedSpawnTime;
            sb.AppendLine($"<color=#FF8080>처치됨</color>  (처치까지 {life:0.0}초)");
        }
        sb.AppendLine($"마지막 행동: {watchedAI.LastAction}   (명중 {watchedAI.HitCount} / 빗나감 {watchedAI.MissCount})");
        if (watchedAI.type != MobAI.MobType.Ranged) sb.AppendLine($"근접 슬롯: {watchedAI.SlotInfo}");
        sb.AppendLine("──────────────");

        if (playerHealth != null)
        {
            sb.AppendLine($"플레이어가 받은 피해: 마지막 {playerLastDamage:0.#} / 누적 {playerTotalDamage:0.#} ({playerHitCount}회)");
            sb.Append($"플레이어 체력 {Mathf.CeilToInt(playerHealth.CurrentHealth)} / {playerHealth.maxHealth:0}    방어 {playerHealth.Defense:0.#}");
        }
        else sb.Append("<color=#AAAAAA>플레이어가 아직 없습니다 (캐릭터 선택 후 표시)</color>");

        infoText.text = sb.ToString();
    }

    private static string TypeLabel(MobAI ai)
    {
        switch (ai.type)
        {
            case MobAI.MobType.Melee: return "근거리";
            case MobAI.MobType.Ranged: return ai.aimLockTime > 0f ? "원거리 · 저격" : "원거리";
            default: return "복합";
        }
    }

    private static string BehaviorLabel(MobAI ai)
    {
        List<string> tags = new List<string>();
        if (ai.type != MobAI.MobType.Ranged) tags.Add($"예비동작 {ai.meleeWindup:0.##}초");
        if (ai.aimLockTime > 0f) tags.Add($"조준 {ai.aimLockTime:0.#}초 후 확정타");
        if (ai.targetPriority == MobAI.TargetPriority.TowerFirst) tags.Add("타워 우선");
        if (ai.accelerateWhileMoving) tags.Add("이동 가속");
        if (ai.retreatWhenTooClose) tags.Add("근접 시 후퇴");
        return tags.Count > 0 ? string.Join(" · ", tags) : "기본";
    }

    // ------------------------------------------------------------------
    // UI 생성
    // ------------------------------------------------------------------
    private void Build()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1;
        // ★ 글자 번짐 방지: 글자 메쉬를 화면 픽셀에 딱 맞춰 그림
        //   (가운데 정렬 글자/버튼 글자는 위치가 0.5픽셀 단위로 어긋나기 쉬워, 끄면 글자 텍스처가 보간되어 뭉개짐)
        canvas.pixelPerfect = true;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        canvasRect = (RectTransform)transform;

        font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Arial" }, 32);
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // 패널 (내용 크기에 맞춰 세로로 늘어남)
        panel = NewRect("Panel", transform);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0f, 1f);
        panel.sizeDelta = new Vector2(panelWidth, 0f);
        Image bg = panel.gameObject.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.6f);
        VerticalLayoutGroup vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 10, 8, 10);
        vlg.spacing = 6f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        ContentSizeFitter fit = panel.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        NewText(panel, $"<b>몹 테스트</b>  <color=#AAAAAA>({toggleKey} 키로 숨기기)</color>", 20, TextAnchor.UpperLeft, 28f);

        // 몹 버튼 (4열)
        RectTransform grid = NewGrid(panel, 4, 36f);
        foreach (GameObject prefab in roster.mobs)
        {
            if (prefab == null) continue;
            GameObject captured = prefab;
            NewButton(grid, prefab.name.Replace("Ent_", ""), () => SpawnMob(captured));
        }

        // 제어 버튼 (2열)
        RectTransform controls = NewGrid(panel, 2, 36f);
        sequenceButtonText = NewButton(controls, "순차 테스트 시작", OnSequenceButton);
        NewButton(controls, "순차 중지", StopSequence);
        pauseButtonText = NewButton(controls, "몹 AI 정지: 꺼짐", TogglePause);
        godButtonText = NewButton(controls, "플레이어 무적: 꺼짐", ToggleGod);
        NewButton(controls, "테스트 몹 전부 제거", ClearAll);
        NewButton(controls, $"무리 소환 x{hordeCount}", SpawnHorde);
        slotButtonText = NewButton(controls, "근접 슬롯", ToggleSlots);
        slotMarkerButtonText = NewButton(controls, "슬롯 표시", ToggleSlotMarkers);
        UpdateSlotButtons();

        // 걷기 애니메이션 비교 (애니메이션이 있는 몹이 하나라도 있을 때만)
        animClipNames = CollectAnimClipNames();
        if (animClipNames.Count > 0)
        {
            RectTransform animRow = NewGrid(panel, 1, 36f); // 클립 이름이 길어서 한 줄에 하나씩
            animButtonText = NewButton(animRow, "걷기 애니", CycleAnimClip);
            animSpeedButtonText = NewButton(animRow, "애니 속도", CycleAnimSpeed);
            UpdateAnimButtons();
        }

        // 정보창
        infoText = NewText(panel, "", 17, TextAnchor.UpperLeft, -1f);

        UpdatePanelPosition();
    }

    // Wave Start 버튼 바로 아래에 배치 (버튼을 못 찾으면 화면 왼쪽 위)
    private void UpdatePanelPosition()
    {
        Vector2 local;
        if (anchorTarget != null && anchorTarget.gameObject.activeInHierarchy)
        {
            Vector3[] corners = new Vector3[4];
            anchorTarget.GetWorldCorners(corners); // 0: 왼쪽 아래
            Canvas c = anchorTarget.GetComponentInParent<Canvas>();
            Camera cam = (c != null && c.renderMode != RenderMode.ScreenSpaceOverlay) ? c.worldCamera : null;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
            screen.y -= 8f;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out local);
        }
        else if (anchorTarget != null)
        {
            return; // 웨이브 진행 중(버튼 숨김)에는 마지막 위치 유지
        }
        else
        {
            local = new Vector2(-canvasRect.rect.width * 0.5f + 20f, canvasRect.rect.height * 0.5f - 20f);
        }

        // 화면 밖으로 나가지 않도록 보정
        float halfW = canvasRect.rect.width * 0.5f, halfH = canvasRect.rect.height * 0.5f;
        float h = panel.rect.height;
        local.x = Mathf.Clamp(local.x, -halfW + 10f, halfW - panelWidth - 10f);
        local.y = Mathf.Clamp(local.y, -halfH + h + 10f, halfH - 10f);
        // 정수 좌표로 (소수점 위치는 글자를 흐리게 만듦)
        panel.anchoredPosition = new Vector2(Mathf.Round(local.x), Mathf.Round(local.y));
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private RectTransform NewGrid(Transform parent, int columns, float cellHeight)
    {
        RectTransform rt = NewRect("Grid", parent);
        GridLayoutGroup g = rt.gameObject.AddComponent<GridLayoutGroup>();
        float spacing = 6f;
        float cellW = Mathf.Floor((panelWidth - 20f - spacing * (columns - 1)) / columns);
        g.cellSize = new Vector2(cellW, cellHeight);
        g.spacing = new Vector2(spacing, spacing);
        g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        g.constraintCount = columns;
        return rt;
    }

    private Text NewText(Transform parent, string content, int size, TextAnchor align, float fixedHeight)
    {
        RectTransform rt = NewRect("Text", parent);
        Text t = rt.gameObject.AddComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.color = Color.white;
        t.alignment = align;
        t.supportRichText = true;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        t.text = content;
        if (fixedHeight > 0f)
        {
            LayoutElement le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = fixedHeight;
        }
        return t;
    }

    private Text NewButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
    {
        RectTransform rt = NewRect("Button", parent);
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(0.22f, 0.22f, 0.25f, 0.95f);
        Button b = rt.gameObject.AddComponent<Button>();
        ColorBlock cb = b.colors;
        cb.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
        cb.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        b.colors = cb;
        b.onClick.AddListener(onClick);

        RectTransform tr = NewRect("Label", rt);
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
        tr.offsetMin = tr.offsetMax = Vector2.zero;
        Text t = tr.gameObject.AddComponent<Text>();
        t.font = font;
        t.fontSize = 17;
        t.color = Color.white;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; // 칸이 좁아도 줄바꿈/잘림 없이
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.supportRichText = true;
        t.raycastTarget = false;
        t.text = label;
        return t;
    }
}
