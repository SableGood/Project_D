using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

// 2D 프레임 애니메이션 (여러 장의 스프라이트를 번갈아 보여주는 방식)
//  - Unity Animator/애니메이션 클립 없이, SpriteRenderer.sprite만 바꿉니다. (가볍고 단순)
//  - 이동 중이면 걷기 클립 재생, 멈추면 기본 이미지(idleSprite)로 돌아감
//  - 이동 속도에 맞춰 재생 속도 자동 조절 (가속하는 몹 등)
//  - 좌우 반전(flipX), 피격 색(color)은 MobAI가 따로 처리하므로 그대로 유지됩니다.
//
// 프레임 이미지 규칙: Assets/Resources/Image/Mobs/Anim/{몹코드}/{클립이름}_{번호}.png
//   예) Anim/MM001/Walk6_01.png ~ Walk6_06.png
//   [Tools/몹/1. 몹 프리팹 정리] 실행 시 자동으로 이 컴포넌트가 붙고 클립이 채워집니다.
[System.Serializable]
public class SpriteClip
{
    public string name = "Walk";
    public Sprite[] frames;
    [Tooltip("초당 프레임 수 (이동 속도 동기화 전 기준값)")]
    public float fps = 10f;
    [Tooltip("켜면 1-2-3-2-1-2-3... 처럼 왕복 재생 (프레임 수가 적을 때 더 자연스러울 수 있음)")]
    public bool pingPong;

    // 한 바퀴에 보여주는 프레임 수
    public int Length
    {
        get
        {
            if (frames == null || frames.Length == 0) return 0;
            return (pingPong && frames.Length > 2) ? frames.Length * 2 - 2 : frames.Length;
        }
    }

    // 재생 순서상 i번째 → 실제 프레임 번호
    public int FrameAt(int i)
    {
        int n = frames.Length;
        if (!pingPong || n <= 2) return i % n;
        int cycle = n * 2 - 2;
        i %= cycle;
        return i < n ? i : cycle - i;
    }
}

[RequireComponent(typeof(SpriteRenderer))]
public class SpriteFrameAnimator : MonoBehaviour
{
    // ── 테스트 패널용 전역 설정 ──
    // -2 = 각 프리팹 설정대로, -1 = 애니메이션 끔(정지 이미지), 0 이상 = 해당 번호의 걷기 클립 강제
    public static int TestClipOverride = -2;
    public static float TestSpeedMultiplier = 1f;

    [Header("기본 이미지 (멈춰 있을 때)")]
    public Sprite idleSprite;
    [Tooltip("켜면 멈춰 있을 때 기본 이미지 대신 걷기 첫 프레임을 보여줌 (기본 이미지와 걷기 그림이 달라 튀어 보일 때)")]
    public bool idleUsesFirstWalkFrame = false;

    [Header("걷기 클립 (여러 개면 walkClipIndex로 선택)")]
    public List<SpriteClip> walkClips = new List<SpriteClip>();
    public int walkClipIndex = 0;

    [Header("재생")]
    [Tooltip("이동 속도에 비례해 재생 속도 조절 (최고 속도 = fps 그대로)")]
    public bool syncWithMoveSpeed = true;
    [Tooltip("이 속도(m/s) 이상일 때만 걷는 것으로 판단")]
    public float moveThreshold = 0.15f;

    private SpriteRenderer sr;
    private NavMeshAgent agent;
    private Vector3 lastPos;
    private float phase;
    private int shownStep = -1;

    // 정보 표시용
    public bool IsWalking { get; private set; }
    public int CurrentFrameNumber => (IsWalking && CurrentClip != null) ? CurrentClip.FrameAt(shownStep) + 1 : 0;

    public SpriteClip CurrentClip
    {
        get
        {
            int i = TestClipOverride >= -1 ? TestClipOverride : walkClipIndex;
            return (i >= 0 && i < walkClips.Count) ? walkClips[i] : null;
        }
    }

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        agent = GetComponentInParent<NavMeshAgent>();
        if (idleSprite == null) idleSprite = sr.sprite;
    }

    void OnEnable()
    {
        phase = 0f;
        shownStep = -1;
        IsWalking = false;
        lastPos = transform.position;
        ShowIdle();
    }

    void Update()
    {
        // 현재 이동 속도
        float speed;
        if (agent != null && agent.enabled && agent.isOnNavMesh) speed = agent.velocity.magnitude;
        else speed = (transform.position - lastPos).magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
        lastPos = transform.position;

        SpriteClip clip = CurrentClip;
        if (clip == null || clip.Length == 0 || speed < moveThreshold)
        {
            IsWalking = false;
            phase = 0f;
            shownStep = -1;
            ShowIdle();
            return;
        }

        IsWalking = true;
        float rate = clip.fps * TestSpeedMultiplier;
        if (syncWithMoveSpeed && agent != null && agent.speed > 0.01f)
            rate *= Mathf.Clamp(speed / agent.speed, 0.4f, 1.5f);

        phase = (phase + rate * Time.deltaTime) % clip.Length;
        int step = Mathf.Clamp((int)phase, 0, clip.Length - 1);
        if (step == shownStep) return; // 같은 프레임이면 아무것도 안 함

        shownStep = step;
        Sprite s = clip.frames[clip.FrameAt(step)];
        if (s != null) sr.sprite = s;
    }

    private void ShowIdle()
    {
        Sprite target = idleSprite;
        if (idleUsesFirstWalkFrame)
        {
            SpriteClip clip = CurrentClip;
            if (clip != null && clip.frames != null && clip.frames.Length > 0 && clip.frames[0] != null) target = clip.frames[0];
        }
        if (target != null && sr.sprite != target) sr.sprite = target;
    }
}
