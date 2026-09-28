using UnityEngine;

public class MobTracker : MonoBehaviour
{
    // 이 몹을 관리하는 웨이브 매니저의 참조 변수
    private WaveManager waveManager;

    // 몹이 생성될 때 웨이브 매니저와 연결해 주는 초기화 함수
    public void Initialize(WaveManager manager)
    {
        waveManager = manager;
    }

    // 유니티 오브젝트가 파괴될 때(사망 또는 삭제) 자동으로 호출되는 기본 함수
    private void OnDestroy()
    {
        // 웨이브 매니저가 정상적으로 존재할 때만 실행
        if (waveManager != null)
        {
            // 웨이브 매니저에게 자신이 파괴되었음을 알림
            waveManager.UnregisterMob(gameObject);
        }
    }
}