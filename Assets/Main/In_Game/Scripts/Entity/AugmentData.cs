using UnityEngine;
using System.Collections.Generic;
using System.Text;

// 재능 한 줄 (Resources/Augment_Data.csv, 구글 시트 Augment_Data 탭)  ※ 코드 안의 Augment(증강) = 게임 용어 '재능'
//  코드 | 이름 | 설명 | 희귀도 | 효과 | 수치1 | 수치2 | 수치3 | 쿨타임 | 최대 중복 | 선행 재능 | 사용 | 원본 번호 | 메모
//  '쿨타임'은 스킬 재능의 재사용 대기시간(초). '선행 재능'은 'A036/A040'처럼 쓰면 그중 하나만 있어도 됨
//  '효과'는 정해진 종류 중 하나 (구글 시트 '재능 효과 유형' 탭 참고). 코드에 없는 효과는 고를 수 없게 빠짐
public class AugmentData
{
    public string code;
    public string name;
    public string description;
    public string rarity;      // 일반 / 고급 / 희귀 / 영웅 / 전설 / 신화
    public string effect;      // 효과 종류 (한글 키)
    public float value1, value2, value3;
    public float cooldown;      // 스킬 재사용 대기시간 (초)
    public int maxStacks = 1;
    public string prerequisite; // 선행 재능 코드 (비우면 없음)
    public bool enabled = true;
}

public static class AugmentDatabase
{
    public const string CsvResourceName = "Augment_Data";

    private static List<AugmentData> all;
    public static List<AugmentData> All { get { if (all == null) Load(); return all; } }

    public static AugmentData Get(string code)
    {
        foreach (AugmentData a in All) if (a.code == code) return a;
        return null;
    }

    public static void Reload() { all = null; }

    private static void Load()
    {
        all = new List<AugmentData>();
        TextAsset csv = Resources.Load<TextAsset>(CsvResourceName);
        if (csv == null)
        {
            Debug.LogWarning($"[재능] Resources/{CsvResourceName}.csv 가 없습니다. 구글 시트에서 가져오기를 실행하세요.");
            return;
        }
        List<List<string>> rows = ParseCsv(csv.text);
        if (rows.Count < 2) return;

        // 헤더 이름으로 열 찾기 (열 순서가 바뀌어도 동작)
        List<string> header = rows[0];
        int Col(string name) { for (int i = 0; i < header.Count; i++) if (header[i].Trim() == name) return i; return -1; }
        int cCode = Col("코드"), cName = Col("이름"), cDesc = Col("설명"), cRarity = Col("희귀도"), cEffect = Col("효과"),
            cV1 = Col("수치1"), cV2 = Col("수치2"), cV3 = Col("수치3"), cMax = Col("최대 중복"), cUse = Col("사용");
        int cCd = Col("쿨타임");
        if (cCd < 0) cCd = Col("쿨타임(초)");
        int cPre = Col("선행 재능");
        if (cPre < 0) cPre = Col("선행 증강");

        for (int r = 1; r < rows.Count; r++)
        {
            List<string> row = rows[r];
            string Get(int c) => c >= 0 && c < row.Count ? row[c].Trim() : "";
            string code = Get(cCode);
            if (string.IsNullOrEmpty(code)) continue;

            var a = new AugmentData
            {
                code = code,
                name = Get(cName),
                description = Get(cDesc),
                rarity = string.IsNullOrEmpty(Get(cRarity)) ? "일반" : Get(cRarity),
                effect = Get(cEffect),
                value1 = ParseFloat(Get(cV1)),
                value2 = ParseFloat(Get(cV2)),
                value3 = ParseFloat(Get(cV3)),
                cooldown = ParseFloat(Get(cCd)),
                maxStacks = Mathf.Max(1, Mathf.RoundToInt(ParseFloat(Get(cMax), 1f))),
                prerequisite = Get(cPre),
                enabled = Get(cUse).ToUpper() != "X",
            };
            if (!PlayerAugments.IsKnownEffect(a.effect))
            {
                if (a.enabled) Debug.LogWarning($"[재능] {a.code} '{a.name}': 효과 '{a.effect}'는 아직 지원하지 않아 목록에서 빠집니다.");
                a.enabled = false;
            }
            all.Add(a);
        }
    }

    private static float ParseFloat(string s, float fallback = 0f)
    {
        if (string.IsNullOrEmpty(s)) return fallback;
        s = s.Replace("%", "").Trim();
        return float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;
    }

    // 따옴표·칸 안 쉼표·칸 안 줄바꿈을 처리하는 CSV 파서
    public static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;
        text = text.TrimStart('﻿');
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else quoted = false;
                }
                else cell.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { row.Add(cell.ToString()); cell.Length = 0; }
            else if (ch == '\r') { }
            else if (ch == '\n') { row.Add(cell.ToString()); cell.Length = 0; rows.Add(row); row = new List<string>(); }
            else cell.Append(ch);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
        return rows;
    }
}
