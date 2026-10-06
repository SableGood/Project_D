using UnityEngine;
using UnityEngine.Pool;

public class PooledObject : MonoBehaviour
{
    public IObjectPool<GameObject> pool;

    // Destroy(gameObject) 대신 호출할 함수
    public void ReleaseToPool()
    {
        if (pool != null)
            pool.Release(gameObject);
        else
            Destroy(gameObject); // 풀 매니저가 파괴된 씬 전환 등의 예외 상황 대비 안전장치
    }
}