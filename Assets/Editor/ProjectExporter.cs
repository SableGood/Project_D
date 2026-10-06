using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;

public class ProjectExporter : MonoBehaviour
{
    [MenuItem("Tools/프로젝트 구조 내보내기 (TXT)")]
    public static void ExportProjectStructure()
    {
        string targetPath = "Assets";
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== Project_D Assets Structure ===");

        // Assets 폴더 내의 모든 파일 경로를 가져옵니다.
        string[] files = Directory.GetFiles(targetPath, "*.*", SearchOption.AllDirectories);

        foreach (string file in files)
        {
            // .meta 파일은 구조 파악에 불필요하므로 제외합니다.
            if (file.EndsWith(".meta")) continue;

            // 윈도우 경로 슬래시(\)를 통일(/)하여 보기 좋게 만듭니다.
            sb.AppendLine(file.Replace("\\", "/"));
        }

        // 프로젝트 최상위 경로(Assets 폴더 바깥)에 텍스트 파일로 저장합니다.
        string savePath = Application.dataPath + "/../ProjectStructure.txt";
        File.WriteAllText(savePath, sb.ToString());

        Debug.Log($"프로젝트 구조 추출 완료! 파일 위치: {savePath}");
        // 저장된 폴더를 자동으로 열어줍니다.
        EditorUtility.RevealInFinder(savePath);
    }
}