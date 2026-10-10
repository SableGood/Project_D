using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public class WaveDataImporter : MonoBehaviour
{
    [MenuItem("Tools/CSV 데이터 생성/2. 웨이브 세팅 (Wave_Data)")]
    public static void ImportWaveData()
    {
        TextAsset csvData = Resources.Load<TextAsset>("Wave_Data");
        if (csvData == null)
        {
            Debug.LogError("Resources 폴더에 Wave_Data.csv 파일을 찾을 수 없습니다!");
            return;
        }

        string savePath = "Assets/Resources/Data/Waves";
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Data")) AssetDatabase.CreateFolder("Assets/Resources", "Data");
        if (!AssetDatabase.IsValidFolder(savePath)) AssetDatabase.CreateFolder("Assets/Resources/Data", "Waves");

        string[] rows = csvData.text.Split(new char[] { '\n' });

        Dictionary<int, List<WaveMobInfo>> waveDict = new Dictionary<int, List<WaveMobInfo>>();
        Dictionary<int, int> waveToStageDict = new Dictionary<int, int>(); // ★ 웨이브 번호에 해당하는 스테이지를 기억할 딕셔너리

        int currentStageIndex = 1;
        int currentWaveIndex = -1;
        int currentSpawnPoint = 0; // ★ D열(Spawn Point). 0 = 지정 없음(무작위)

        for (int i = 1; i < rows.Length; i++)
        {
            string row = rows[i].Trim();
            if (string.IsNullOrEmpty(row)) continue;

            string[] cols = row.Split(',');
            if (cols.Length < 6) continue;

            // ★ A열(인덱스 0) 파싱: 스테이지 번호 갱신
            if (!string.IsNullOrEmpty(cols[0].Trim()) && float.TryParse(cols[0].Trim(), out float parsedStage))
            {
                currentStageIndex = (int)parsedStage;
            }

            // ★ B열(인덱스 1) 파싱: 웨이브 번호 갱신 및 현재 스테이지 매핑
            if (!string.IsNullOrEmpty(cols[1].Trim()) && float.TryParse(cols[1].Trim(), out float parsedWave))
            {
                currentWaveIndex = (int)parsedWave;
                waveToStageDict[currentWaveIndex] = currentStageIndex;
                currentSpawnPoint = 0; // 새 웨이브가 시작되면 스폰 포인트 초기화
            }

            // ★ D열(인덱스 3) 파싱: 스폰 포인트 번호 갱신 (아래 행들은 같은 포인트를 이어서 사용)
            if (!string.IsNullOrEmpty(cols[3].Trim()) && float.TryParse(cols[3].Trim(), out float parsedPoint))
            {
                currentSpawnPoint = (int)parsedPoint;
            }

            if (currentWaveIndex == -1) continue;

            string entCode = cols[4].Trim();
            string countStr = cols[5].Trim();

            if (!string.IsNullOrEmpty(entCode) && !string.IsNullOrEmpty(countStr))
            {
                string mobPrefabName = entCode;

                float countFloat = 0;
                float.TryParse(countStr, out countFloat);
                int count = (int)countFloat;

                if (count <= 0) continue;

                string prefabPath = $"Assets/Main/In_Game/Main_Prefabs/Mobs/{mobPrefabName}.prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

                if (prefab != null)
                {
                    WaveMobInfo info = new WaveMobInfo();
                    info.mobPrefab = prefab;
                    info.spawnCount = count;
                    info.spawnPointIndex = currentSpawnPoint;

                    if (!waveDict.ContainsKey(currentWaveIndex))
                    {
                        waveDict[currentWaveIndex] = new List<WaveMobInfo>();
                    }

                    // 같은 몹이라도 스폰 포인트가 다르면 별도 항목으로 유지
                    WaveMobInfo existingInfo = waveDict[currentWaveIndex].Find(x => x.mobPrefab == prefab && x.spawnPointIndex == currentSpawnPoint);
                    if (existingInfo != null)
                    {
                        existingInfo.spawnCount += count;
                    }
                    else
                    {
                        waveDict[currentWaveIndex].Add(info);
                    }
                }
                else
                {
                    Debug.LogWarning($"[{currentWaveIndex} 웨이브] 프리팹을 찾을 수 없습니다: {prefabPath}");
                }
            }
        }

        foreach (var kvp in waveDict)
        {
            int waveNum = kvp.Key;
            string assetPath = $"{savePath}/Wave_{waveNum:D2}.asset";

            WaveData waveAsset = AssetDatabase.LoadAssetAtPath<WaveData>(assetPath);
            bool isNew = false;

            if (waveAsset == null)
            {
                waveAsset = ScriptableObject.CreateInstance<WaveData>();
                isNew = true;
            }

            // ★ 저장된 스테이지 번호 부여
            waveAsset.stageNumber = waveToStageDict.ContainsKey(waveNum) ? waveToStageDict[waveNum] : 1;
            waveAsset.mobList = kvp.Value;

            if (isNew) AssetDatabase.CreateAsset(waveAsset, assetPath);
            else EditorUtility.SetDirty(waveAsset);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("CSV 웨이브 데이터 자동 생성 완료! (스테이지 정보 포함)");
    }
}