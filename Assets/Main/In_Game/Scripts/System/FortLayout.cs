using System.Collections.Generic;
using UnityEngine;

// v151 요새 맵 배치 (Resources/Map/Fort_Layout.txt). 요새 맵(IslandMap.IsFort)일 때 PlayArea가 Apply를 부름
//  - 줄마다: 이름 숫자... (맵 가운데 기준 m, +x 동쪽, +z 북쪽)
//      player x z            플레이어 시작 위치 (GameManager.spawnPoint를 여기로 옮김)
//      base x z 반가로 반세로   기지 안쪽 (몹이 이 밖에 있고 벽이 서 있으면 벽부터 때림)
//      wall x z 각도 길이      입구 벽 자리 (FortWall)
//      spawn x z             몹 스폰 위치 (WaveManager 기본 스폰 포인트를 이걸로 바꿈)
public static class FortLayout
{
    public const string LayoutPath = "Map/Fort_Layout";

    public static bool Active { get; private set; }
    public static Vector3 PlayerStart { get; private set; }
    public static Vector3 BaseCenter { get; private set; }
    public static Vector2 BaseHalf { get; private set; }
    public static Vector3 WallPos { get; private set; }
    public static float WallYaw { get; private set; }
    public static float WallLength { get; private set; } = 3.6f;
    public static readonly List<Vector3> Spawns = new List<Vector3>();

    // 기지 안쪽인지 (margin만큼 넓혀서)
    public static bool InsideBase(Vector3 p, float margin = 0f)
    {
        if (!Active) return true;
        return Mathf.Abs(p.x - BaseCenter.x) <= BaseHalf.x + margin && Mathf.Abs(p.z - BaseCenter.z) <= BaseHalf.y + margin;
    }

    public static void Apply()
    {
        Active = false;
        Spawns.Clear();
        TextAsset t = Resources.Load<TextAsset>(LayoutPath);
        if (t == null) { Debug.LogWarning("[요새 맵] 배치 파일이 없음: " + LayoutPath); return; }
        Vector3 c = IslandMap.MapBounds.center;
        Vector3 W(float x, float z) => new Vector3(c.x + x, c.y, c.z + z);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (string raw in t.text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            string[] s = line.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
            float F(int i) => i < s.Length && float.TryParse(s[i], System.Globalization.NumberStyles.Float, inv, out float v) ? v : 0f;
            switch (s[0])
            {
                case "player": PlayerStart = W(F(1), F(2)); break;
                case "base": BaseCenter = W(F(1), F(2)); BaseHalf = new Vector2(F(3), F(4)); break;
                case "wall": WallPos = W(F(1), F(2)); WallYaw = F(3); WallLength = Mathf.Max(1f, F(4)); break;
                case "spawn": Spawns.Add(W(F(1), F(2))); break;
            }
        }
        Active = true;

        // 플레이어 시작 위치
        GameManager gm = GameManager.Instance != null ? GameManager.Instance : Object.FindFirstObjectByType<GameManager>();
        if (gm != null)
        {
            if (gm.spawnPoint == null) gm.spawnPoint = new GameObject("PlayerSpawn").transform;
            gm.spawnPoint.SetPositionAndRotation(PlayerStart, Quaternion.identity);
        }

        // 몹 스폰 위치 (씬의 스폰 포인트는 섬 맵 기준이라 쓰지 않음)
        WaveManager wm = WaveManager.Instance != null ? WaveManager.Instance : Object.FindFirstObjectByType<WaveManager>();
        if (wm != null && Spawns.Count > 0)
        {
            Transform root = new GameObject("FortSpawns").transform;
            Transform[] pts = new Transform[Spawns.Count];
            for (int i = 0; i < Spawns.Count; i++)
            {
                pts[i] = new GameObject("Fort_Spawn_" + (i + 1)).transform;
                pts[i].SetParent(root, false);
                pts[i].position = Spawns[i];
            }
            wm.defaultSpawnPoints = pts;
        }

        FortWall.Create(WallPos, WallYaw, WallLength);
        Debug.Log($"[요새 맵] 시작 {PlayerStart}, 스폰 {Spawns.Count}곳, 입구 벽 {WallPos}");
    }
}
