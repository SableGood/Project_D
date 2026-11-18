using System.Collections.Generic;
using UnityEngine;

// v190 포탑 하부 몸체(루트의 메쉬)를 가로·세로 95%로 (높이는 그대로)
//  - 포탑 프리팹은 루트 오브젝트에 하부 몸체 메쉬(+ 메쉬 충돌체)가 있고, 상부·링·무기는 자식
//  - 루트 Transform을 줄이면 자식까지 작아지므로, 하부 메쉬만 줄인 복사본으로 바꿔 끼움 (원본 메쉬별 1회 계산·재사용)
//  - 미리보기·실제 포탑 모두. 풀에서 재사용돼도 이미 줄인 메쉬면 다시 줄이지 않음
public static class TowerBaseShrink
{
    public static float Scale = 0.95f;
    private static readonly Dictionary<Mesh, Mesh> cache = new Dictionary<Mesh, Mesh>();
    private static readonly HashSet<Mesh> shrunk = new HashSet<Mesh>();

    public static void Apply(GameObject tower)
    {
        if (tower == null || Mathf.Approximately(Scale, 1f)) return;
        MeshFilter mf = tower.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;
        Mesh src = mf.sharedMesh;
        if (shrunk.Contains(src)) return;
        if (!cache.TryGetValue(src, out Mesh m))
        {
            if (!src.isReadable) { Debug.LogWarning($"[포탑 하부] {tower.name} 메쉬를 읽을 수 없어 크기를 바꾸지 못해요"); return; }
            m = Object.Instantiate(src);
            m.name = src.name + "_95";
            Vector3[] v = m.vertices;
            for (int i = 0; i < v.Length; i++) { v[i].x *= Scale; v[i].z *= Scale; }
            m.vertices = v;
            m.RecalculateBounds();
            cache[src] = m; shrunk.Add(m);
        }
        mf.sharedMesh = m;
        MeshCollider mc = tower.GetComponent<MeshCollider>();
        if (mc != null && mc.sharedMesh == src) mc.sharedMesh = m;
    }
}
