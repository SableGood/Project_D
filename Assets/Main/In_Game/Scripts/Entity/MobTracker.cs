using UnityEngine;

public class MobTracker : MonoBehaviour
{
    private WaveManager waveManager;

    public void Initialize(WaveManager manager)
    {
        waveManager = manager;
    }

    // ★ 수정: OnDestroy() 대신 오브젝트 풀에 의해 비활성화될 때 호출되는 OnDisable() 사용
    private void OnDisable()
    {
        if (waveManager != null)
        {
            waveManager.UnregisterMob(gameObject);
        }
    }
}