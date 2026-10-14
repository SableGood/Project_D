using System.Collections.Generic;
using UnityEngine;

// 3D 외곽선(Toon Lit의 OUTLINE 패스)용 '부드러운 노멀' 굽기
//  - 모서리가 각진 모델(ProBuilder 큐브 등)은 면마다 노멀이 달라서, 그대로 부풀리면 모서리에 틈이 생깁니다.
//  - 같은 위치에 있는 정점들의 노멀을 평균내 UV3 채널에 저장 → 셰이더가 이 방향으로 외곽선을 부풀림
//  - 같은 메쉬는 한 번만 계산해서 재사용 (포탑을 여러 개 지어도 비용 없음)
//  * 게임 시작 시 씬의 Toon Lit 오브젝트, 포탑 건설 시 그 포탑에 자동 적용됩니다.
public static class OutlineNormals
{
    const string OutlineProperty = "_OutlineWidth";
    private static readonly Dictionary<Mesh, Mesh> cache = new Dictionary<Mesh, Mesh>();
    private static readonly HashSet<Mesh> baked = new HashSet<Mesh>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BakeSceneOnStart()
    {
        foreach (MeshFilter mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            BakeFilter(mf);
    }

    // 오브젝트(자식 포함)의 외곽선 메쉬 준비
    public static void Bake(GameObject root)
    {
        if (root == null) return;
        foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            BakeFilter(mf);
    }

    private static void BakeFilter(MeshFilter mf)
    {
        if (mf == null) return;
        Mesh src = mf.sharedMesh;
        if (src == null || baked.Contains(src)) return;

        // 외곽선 셰이더를 쓰는 렌더러만
        MeshRenderer mr = mf.GetComponent<MeshRenderer>();
        if (mr == null || !UsesOutline(mr)) return;

        if (!cache.TryGetValue(src, out Mesh result))
        {
            result = CreateSmoothed(src);
            cache[src] = result;
        }
        if (result != null && result != src) mf.sharedMesh = result;
    }

    private static bool UsesOutline(Renderer r)
    {
        foreach (Material m in r.sharedMaterials)
            if (m != null && m.HasProperty(OutlineProperty)) return true;
        return false;
    }

    private static Mesh CreateSmoothed(Mesh src)
    {
        if (!src.isReadable) return null; // 읽기 불가 메쉬(임포트 설정)는 기본 노멀 사용

        Vector3[] verts = src.vertices;
        Vector3[] normals = src.normals;
        if (verts.Length == 0 || normals.Length != verts.Length) return null;

        // 같은 위치끼리 묶어서, 서로 다른 노멀들을 평균
        var groups = new Dictionary<Vector3Int, List<Vector3>>();
        var keys = new Vector3Int[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 p = verts[i];
            Vector3Int k = new Vector3Int(Mathf.RoundToInt(p.x * 10000f), Mathf.RoundToInt(p.y * 10000f), Mathf.RoundToInt(p.z * 10000f));
            keys[i] = k;
            if (!groups.TryGetValue(k, out List<Vector3> list)) groups[k] = list = new List<Vector3>(4);
            Vector3 n = normals[i];
            bool dup = false;
            foreach (Vector3 e in list) if (Vector3.Dot(e, n) > 0.999f) { dup = true; break; }
            if (!dup) list.Add(n);
        }

        var smooth = new List<Vector3>(verts.Length);
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 sum = Vector3.zero;
            foreach (Vector3 n in groups[keys[i]]) sum += n;
            smooth.Add(sum.sqrMagnitude > 1e-6f ? sum.normalized : normals[i]);
        }

        Mesh copy = Object.Instantiate(src);
        copy.name = src.name + "_Outline";
        copy.SetUVs(3, smooth);
        baked.Add(copy);
        return copy;
    }
}
