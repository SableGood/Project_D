using System.Collections.Generic;
using UnityEngine;

// v176 포탑 강화 (시험: TR010 → TR010+)
//  - 엘리트 상점에서 '강화 해금'을 사면, 해당 포탑을 고르고 E로 강화 (골드 듦)
//  - 모양: 이름에 "Barrel"이 들어간 부품을 1.5배 (길고 굵게). 뒤끝은 제자리, 앞으로 늘어나고 총구도 앞으로
//  - 능력치: 공격력·연사·사거리·체력 배율 (체력 비율 유지)
//  - 풀로 돌아갈 때(비활성) 모양을 되돌리고 이 컴포넌트를 지움 → 다시 지으면 기본 포탑
//  - 멀티 대비: 강화는 Apply 한 곳에서만 상태를 바꿈
public class TurretUpgrade : MonoBehaviour
{
    public class Spec
    {
        public string code, title;
        public float barrelScale = 1.5f, damageMul = 1.6f, fireRateMul = 1.3f, rangeMul = 1.2f, hpMul = 1.5f;
        public float costRatio = 0.6f; // 강화 비용 = 지을 때 낸 골드 × 이 비율
    }

    // 강화가 있는 포탑 (코드 → 내용). 해금 키는 RunProgress "UP_" + code
    public static readonly Dictionary<string, Spec> Specs = new Dictionary<string, Spec>
    {
        { "TR010", new Spec { code = "TR010", title = "기관포 포탑 강화" } },
    };

    public static string CodeOf(GameObject tower)
    {
        if (tower == null) return null;
        TurretAI ai = tower.GetComponentInChildren<TurretAI>(true);
        if (ai != null && ai.myData != null && !string.IsNullOrEmpty(ai.myData.entityCode) && Specs.ContainsKey(ai.myData.entityCode)) return ai.myData.entityCode;
        string n = tower.name; int k = n.IndexOf('(');
        return (k > 0 ? n.Substring(0, k) : n).Trim();
    }

    public static Spec SpecFor(GameObject tower)
    {
        string c = CodeOf(tower);
        return c != null && Specs.TryGetValue(c, out Spec s) ? s : null;
    }

    public static string UnlockKey(string code) => "UP_" + code;
    public static bool IsUnlocked(string code) => RunProgress.IsTurretUnlocked(UnlockKey(code));
    public static bool IsUpgraded(GameObject tower) => tower != null && tower.GetComponent<TurretUpgrade>() != null;

    private struct Saved { public Transform t; public Vector3 pos, scale; }
    private readonly List<Saved> saved = new List<Saved>();
    private Spec spec;

    // 강화 적용. 이미 강화됐으면 false
    public static bool Apply(GameObject tower)
    {
        Spec s = SpecFor(tower);
        if (s == null || IsUpgraded(tower)) return false;
        TurretUpgrade u = tower.AddComponent<TurretUpgrade>();
        u.spec = s;
        u.Grow();
        u.Buff();
        return true;
    }

    private void Grow()
    {
        float front = 0f; Transform barrelParent = null;
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t == transform || t.name.IndexOf("Barrel", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            MeshFilter mf = t.GetComponent<MeshFilter>();
            saved.Add(new Saved { t = t, pos = t.localPosition, scale = t.localScale });
            if (mf == null || mf.sharedMesh == null) { t.localScale *= spec.barrelScale; continue; }
            // 부모 공간에서 앞뒤(z) 끝을 재서, 뒤끝이 제자리에 있게 옮김
            ZRange(t, mf.sharedMesh.bounds, out float back0, out float front0);
            t.localScale *= spec.barrelScale;
            ZRange(t, mf.sharedMesh.bounds, out float back1, out float front1);
            t.localPosition += new Vector3(0f, 0f, back0 - back1);
            float grow = (front1 + (back0 - back1)) - front0;
            if (grow > front) { front = grow; barrelParent = t.parent; }
        }
        // 총구를 늘어난 만큼 앞으로
        if (barrelParent != null && front > 0f)
            foreach (Weapon w in GetComponentsInChildren<Weapon>(true))
            {
                if (w.muzzle == null) continue;
                saved.Add(new Saved { t = w.muzzle, pos = w.muzzle.localPosition, scale = w.muzzle.localScale });
                w.muzzle.position += barrelParent.TransformVector(new Vector3(0f, 0f, front));
            }
    }

    private static void ZRange(Transform t, Bounds b, out float min, out float max)
    {
        Matrix4x4 m = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale);
        min = float.MaxValue; max = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            float z = m.MultiplyPoint3x4(c).z;
            min = Mathf.Min(min, z); max = Mathf.Max(max, z);
        }
    }

    private void Buff()
    {
        foreach (Weapon w in GetComponentsInChildren<Weapon>(true))
        {
            w.attackDamage = Mathf.RoundToInt(w.attackDamage * spec.damageMul);
            w.attackCooldown /= spec.fireRateMul;
            w.attackRange *= spec.rangeMul;
        }
        if (TryGetComponent(out Health h) && !h.IsDead) h.ScaleMaxHealth(spec.hpMul);
    }

    void OnDisable()
    {
        // 풀로 돌아감 → 모양 되돌림 (능력치는 TurretAI.OnEnable이 데이터로 다시 채움)
        foreach (Saved s in saved) if (s.t != null) { s.t.localPosition = s.pos; s.t.localScale = s.scale; }
        saved.Clear();
        Destroy(this);
    }
}
