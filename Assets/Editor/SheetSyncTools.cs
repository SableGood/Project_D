using UnityEngine;
using UnityEditor;
using UnityEngine.Networking;
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

// 구글 시트 → Resources CSV 동기화
//  - 시트의 각 탭을 CSV로 내려받아 Assets/Resources/{이름}.csv 를 덮어쓰고, 바뀐 것만 CSV 임포트를 실행합니다.
//  - 사용 전 한 번: 구글 시트 [공유] → 일반 액세스 '링크가 있는 모든 사용자' / 뷰어 로 설정
//  - 덮어쓰기 전 기존 CSV는 프로젝트 폴더의 CSVBackup/날짜_시간/ 에 백업됩니다 (Assets 밖이라 게임에 포함되지 않음)
public static class SheetSyncTools
{
    // 시트 주소의 /d/ 와 /edit 사이 문자열
    private const string SpreadsheetId = "1ybyHv2cp45ZryyFi7lxufQdrQCtXEt9Bc5VrXdbP0tY";

    // 탭 목록: CSV 이름 / 탭 gid (시트 주소 끝의 #gid=숫자) / 헤더 확인용 첫 칸 / 헤더가 있는 줄 (0부터)
    private struct Tab
    {
        public string name; public int gid; public string headerFirstCell; public int headerRow;
        public Tab(string n, int g, string h, int r) { name = n; gid = g; headerFirstCell = h; headerRow = r; }
    }
    private static readonly Tab[] Tabs =
    {
        new Tab("Mob_Data",    1, "코드명", 1),   // 1번 줄은 설명 문구
        new Tab("Wave_Data",   2, "Stage",  0),
        new Tab("Weapon_Data", 0, "코드명", 0),
        new Tab("Augment_Data", 9, "코드", 0),   // 증강: 임포트 없이 게임 시작 시 CSV를 바로 읽음
    };

    private const int TimeoutSeconds = 20;

    [MenuItem("Tools/CSV 데이터 생성/0. 구글 시트에서 가져오기 + 임포트", priority = 0)]
    public static void SyncAndImport() { Sync(true); }

    [MenuItem("Tools/CSV 데이터 생성/0-1. 구글 시트에서 CSV만 가져오기", priority = 1)]
    public static void SyncOnly() { Sync(false); }

    [MenuItem("Tools/CSV 데이터 생성/0-2. 구글 시트 열기", priority = 2)]
    public static void OpenSheet()
    {
        Application.OpenURL($"https://docs.google.com/spreadsheets/d/{SpreadsheetId}/edit");
    }

    private static void Sync(bool runImport)
    {
        var downloaded = new Dictionary<string, string>();
        try
        {
            // 1) 전부 내려받고 확인부터 (하나라도 실패하면 아무 파일도 바꾸지 않음)
            for (int i = 0; i < Tabs.Length; i++)
            {
                Tab t = Tabs[i];
                EditorUtility.DisplayProgressBar("구글 시트에서 가져오기", $"{t.name} 내려받는 중...", (float)i / Tabs.Length);
                string text = Download(t, out string error);
                if (text == null)
                {
                    EditorUtility.ClearProgressBar();
                    EditorUtility.DisplayDialog("구글 시트에서 가져오기 실패", $"[{t.name}] {error}\n\n아무 파일도 바뀌지 않았습니다.", "확인");
                    return;
                }
                downloaded[t.name] = text;
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        // 2) 바뀐 파일만 백업 후 덮어쓰기
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string backupDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "CSVBackup", stamp);
        var changed = new List<string>();
        var report = new StringBuilder();
        foreach (Tab t in Tabs)
        {
            string path = $"Assets/Resources/{t.name}.csv";
            string newText = downloaded[t.name];
            string oldText = File.Exists(path) ? Normalize(File.ReadAllText(path, Encoding.UTF8)) : null;
            if (oldText == newText) { report.AppendLine($"{t.name}: 변경 없음"); continue; }

            if (oldText != null)
            {
                Directory.CreateDirectory(backupDir);
                File.Copy(path, Path.Combine(backupDir, t.name + ".csv"), true);
            }
            File.WriteAllText(path, newText, new UTF8Encoding(false)); // 기존 CSV와 같은 형식: BOM 없음, CRLF
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            changed.Add(t.name);
            report.AppendLine($"{t.name}: {DescribeChange(oldText, newText)}");
        }

        // 3) 바뀐 데이터만 임포트 (메뉴 1 → 2 → 3 순서)
        if (runImport && changed.Count > 0)
        {
            if (changed.Contains("Mob_Data")) EntityDataImporter.ImportCSVData();
            if (changed.Contains("Wave_Data")) WaveDataImporter.ImportWaveData();
            if (changed.Contains("Weapon_Data")) WeaponDataImporter.ImportWeaponData();
            AssetDatabase.SaveAssets();
        }

        if (changed.Count > 0) report.AppendLine().Append($"이전 CSV 백업: CSVBackup/{stamp}/");
        if (changed.Count > 0 && !runImport) report.AppendLine().Append("임포트는 실행하지 않았습니다. 필요한 CSV 임포트 메뉴를 직접 실행하세요.");
        string summary = report.ToString().TrimEnd();
        Debug.Log("[구글 시트 동기화]\n" + summary);
        EditorUtility.DisplayDialog("구글 시트에서 가져오기",
            (changed.Count == 0 ? "시트와 프로젝트 CSV가 이미 같습니다.\n\n" : (runImport ? "가져오기와 임포트를 마쳤습니다.\n\n" : "가져오기를 마쳤습니다.\n\n")) + summary, "확인");
    }

    private static string Download(Tab t, out string error)
    {
        error = null;
        string url = $"https://docs.google.com/spreadsheets/d/{SpreadsheetId}/export?format=csv&gid={t.gid}";
        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            req.timeout = TimeoutSeconds;
            var op = req.SendWebRequest();
            while (!op.isDone) System.Threading.Thread.Sleep(20);

            if (req.result != UnityWebRequest.Result.Success)
            {
                error = req.responseCode == 401 || req.responseCode == 403 || req.responseCode == 404
                    ? "시트에 접근할 수 없습니다. 구글 시트 [공유]에서 '링크가 있는 모든 사용자 / 뷰어'로 설정했는지 확인하세요."
                    : $"내려받기 실패 ({req.responseCode}): {req.error}";
                return null;
            }
            string text = Encoding.UTF8.GetString(req.downloadHandler.data);
            if (text.TrimStart().StartsWith("<"))
            {
                error = "CSV 대신 로그인 페이지가 왔습니다. 구글 시트 [공유]에서 '링크가 있는 모든 사용자 / 뷰어'로 설정했는지 확인하세요.";
                return null;
            }
            text = Normalize(text);

            // 탭이 맞는지 확인 (gid가 바뀌었거나 탭 순서가 달라졌을 때 엉뚱한 데이터로 덮어쓰지 않도록)
            string[] lines = text.Split('\n');
            string first = lines.Length > t.headerRow ? lines[t.headerRow].TrimStart('﻿') : "";
            if (!first.StartsWith(t.headerFirstCell))
            {
                error = $"헤더가 예상과 다릅니다 ('{t.headerFirstCell}'로 시작해야 함). SheetSyncTools.cs의 gid({t.gid})가 올바른 탭인지 확인하세요.";
                return null;
            }
            if (t.name == "Wave_Data" && text.Contains("\""))
                Debug.LogWarning("[구글 시트 동기화] Wave_Data에 쉼표가 들어간 칸이 있습니다. 웨이브 임포터는 칸 안의 쉼표를 구분하지 못하니 쉼표를 빼 주세요.");
            return text; // Normalize가 이미 CRLF로 맞춤
        }
    }

    // 줄바꿈 통일 + BOM 제거 + 끝의 빈 줄 정리 (비교용)
    private static string Normalize(string s)
    {
        s = s.TrimStart('﻿').Replace("\r\n", "\n").Replace("\r", "\n");
        return s.TrimEnd('\n').Replace("\n", "\r\n");
    }

    private static string DescribeChange(string oldText, string newText)
    {
        if (oldText == null) return "새로 만듦";
        string[] a = oldText.Split('\n'), b = newText.Split('\n');
        int diff = Math.Abs(a.Length - b.Length);
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++) if (a[i] != b[i]) diff++;
        return $"{diff}줄 바뀜 (총 {b.Length}줄)";
    }
}
