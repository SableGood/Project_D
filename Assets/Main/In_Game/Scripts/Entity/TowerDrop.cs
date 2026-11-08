using System.Collections.Generic;
using UnityEngine;

// v126 TR 시리즈 긴급 전개 연출: 포탑을 설치하면 하부 박스 → 포탑링 → 상부 순서로 하늘에서 떨어져 결합
//  - 포탑 본체(루트)는 처음부터 제자리 (충돌체·길막·칸 차지는 바로 적용). 하부 박스는 같은 그림의 복사본이 떨어지고,
//    닿는 순간 진짜 그림으로 바꿈. 포탑링(*_Ring)·상부(*_Top)는 실제 부품이 떨어짐
//  - 떨어지는 동안 무적(받는 피해 0), 공격·지원 효과 꺼짐 → 마지막 부품이 결합되면 작동 시작
//  - 부품 구분: 이름이 _Ring으로 끝나면 링, TurretAI.turretHead 또는 이름이 _Top이면 상부 (그 외 자식은 상부와 함께)
//  TowerBuilder.BuildTower에서 TowerDrop.Play(tower) 로 시작
public class TowerDrop : MonoBehaviour
{
    public static float DropHeight = 9f;      // 떨어지기 시작하는 높이 (m)
    public static float FallTime = 0.16f;     // 한 부품이 떨어지는 시간 (v128: 2배 빠르게)
    public static float Gap = 0.1f;           // 다음 부품이 떨어지기 시작하는 간격 (앞 부품이 닿은 뒤)
    public static float BounceTime = 0.07f;   // 닿은 뒤 살짝 튀는 시간
    public const string DeploySound = "Tower/Turret_Deploy", AssembleSound = "Tower/Turret_Assemble";
    public static float AssembleDelay = 0.05f; // 상부가 닿고 나서 결합 소리까지
    public static float BounceHeight = 0.06f;
    private const float Parked = 80f;         // 차례가 오기 전 부품은 화면 밖 높이에 숨겨 둠

    public bool Deploying { get; private set; }

    private class Part
    {
        public Transform t;          // 실제 부품 (하부는 복사본)
        public Vector3 rest;         // 결합된 위치 (월드)
        public float start;          // 떨어지기 시작하는 시각
        public bool landed;
        public float shake;
        public bool last;
    }
    private readonly List<Part> parts = new List<Part>();
    private GameObject baseCopy;
    private MeshRenderer baseRenderer;
    private Health health;
    private float savedTakenMul = 1f;
    private readonly List<Behaviour> paused = new List<Behaviour>();

    public static void Play(GameObject tower)
    {
        if (tower == null) return;
        TowerDrop d = tower.GetComponent<TowerDrop>();
        if (d == null) d = tower.AddComponent<TowerDrop>();
        d.Begin();
    }

    private void Begin()
    {
        Finish(false); // 풀에서 재사용됐거나 진행 중이었으면 정리
        parts.Clear();
        float t0 = Time.time;

        // 1) 하부 박스: 루트의 그림을 복사해서 떨어뜨림 (루트는 충돌체·길막이 있어 움직이지 않음)
        baseRenderer = GetComponent<MeshRenderer>();
        MeshFilter mf = GetComponent<MeshFilter>();
        if (baseRenderer != null && mf != null && mf.sharedMesh != null && baseRenderer.enabled)
        {
            baseCopy = new GameObject(name + "_DropBase");
            baseCopy.layer = 2; // Ignore Raycast (총알·클릭에 안 걸림)
            baseCopy.transform.SetPositionAndRotation(transform.position, transform.rotation);
            baseCopy.transform.localScale = transform.lossyScale;
            baseCopy.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            MeshRenderer r = baseCopy.AddComponent<MeshRenderer>();
            r.sharedMaterials = baseRenderer.sharedMaterials;
            r.shadowCastingMode = baseRenderer.shadowCastingMode;
            r.receiveShadows = baseRenderer.receiveShadows;
            baseRenderer.enabled = false;
            parts.Add(new Part { t = baseCopy.transform, rest = transform.position, start = t0, shake = 0.08f });
        }

        // 2) 포탑링, 3) 상부 (직속 자식 중에서)
        TurretAI ai = GetComponent<TurretAI>();
        Transform head = ai != null ? ai.turretHead : null;
        Transform ring = null, top = null;
        foreach (Transform c in transform)
        {
            string n = c.name.ToLowerInvariant();
            if (n.EndsWith("_ring")) ring = c;
            else if (c == head || n.EndsWith("_top")) top = c;
        }
        float step = FallTime + Gap;
        float next = t0 + (parts.Count > 0 ? step : 0f);
        if (ring != null) { parts.Add(new Part { t = ring, rest = ring.position, start = next, shake = 0.05f }); next += step; }
        if (top != null) parts.Add(new Part { t = top, rest = top.position, start = next, shake = 0.1f });
        if (parts.Count == 0) return;
        parts[parts.Count - 1].last = true;

        // 무적 + 작동 멈춤
        health = GetComponent<Health>();
        if (health != null) { savedTakenMul = health.damageTakenMul; health.damageTakenMul = 0f; }
        paused.Clear();
        foreach (TurretAI a in GetComponentsInChildren<TurretAI>()) if (a.enabled) { a.enabled = false; paused.Add(a); }
        foreach (SupportTower s in GetComponentsInChildren<SupportTower>()) if (s.enabled) { s.enabled = false; paused.Add(s); }
        foreach (Weapon w in GetComponentsInChildren<Weapon>()) if (w.enabled) { w.enabled = false; paused.Add(w); }

        Deploying = true;
        Step();
    }

    void LateUpdate() { if (Deploying) Step(); }

    private void Step()
    {
        float now = Time.time;
        bool allDone = true;
        // 부모(링) 먼저 → 자식이 있어도 월드 위치로 덮어써서 각자 따로 움직임
        foreach (Part p in parts)
        {
            if (p.t == null) continue;
            float u = (now - p.start) / FallTime;
            float y;
            if (u < 0f) { y = Parked; allDone = false; }
            else if (u < 1f) { y = DropHeight * (1f - u * u); allDone = false; } // 점점 빨라지며 떨어짐
            else
            {
                if (!p.landed) Land(p);
                float b = (now - p.start - FallTime) / BounceTime;
                if (b < 1f) { y = BounceHeight * Mathf.Sin(b * Mathf.PI); allDone = false; }
                else y = 0f;
            }
            if (p.t != null) p.t.position = p.rest + Vector3.up * y; // v137: 하부 복사본은 닿는 순간 지워져 p.t가 비게 됨
        }
        if (allDone) Finish(true);
    }

    private void Land(Part p)
    {
        p.landed = true;
        if (p.t != null && baseCopy != null && p.t == baseCopy.transform && baseRenderer != null)
        {
            baseRenderer.enabled = true; // 복사본 → 진짜 그림
            Destroy(baseCopy); baseCopy = null; p.t = null;
        }
        Sfx.Play(DeploySound, 0.8f, 0.06f);   // v128: 착지·부품 결합 = Deploy
        if (p.last) Invoke(nameof(PlayAssemble), AssembleDelay); // 상부까지 결합 = Assemble
        CameraController.Shake(p.shake, 0.12f);
        // 먼지: 바닥에 둥글게 퍼지는 연기
        Vector3 c = transform.position + Vector3.up * 0.1f;
        int n = p.shake >= 0.08f ? 10 : 6;
        for (int i = 0; i < n; i++)
        {
            float a = (i + Random.value * 0.5f) / n * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            HitEffects.Smoke(c + dir * 0.5f, dir * Random.Range(1.2f, 2f) + Vector3.up * 0.3f, Random.Range(0.18f, 0.28f), Random.Range(0.5f, 0.8f), 0.55f, 1f);
        }
    }

    private void PlayAssemble() { Sfx.Play(AssembleSound, 0.85f, 0.02f); }

    // land: 정상 종료 (작동 시작). false = 중간에 정리 (판매·풀 반납 등) → 그냥 제자리로
    private void Finish(bool land)
    {
        if (!Deploying && baseCopy == null) return;
        foreach (Part p in parts) if (p.t != null) p.t.position = p.rest;
        if (baseCopy != null) { Destroy(baseCopy); baseCopy = null; }
        if (baseRenderer != null) baseRenderer.enabled = true;
        if (health != null) health.damageTakenMul = savedTakenMul;
        foreach (Behaviour b in paused) if (b != null) b.enabled = true;
        paused.Clear();
        Deploying = false;
    }

    void OnDisable() { CancelInvoke(); Finish(false); }
}
