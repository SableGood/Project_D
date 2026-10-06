using UnityEngine;
using UnityEngine.Pool;
using System.Collections.Generic;

public class PoolManager : MonoBehaviour
{
    public static PoolManager Instance { get; private set; }

    // 원본 프리팹을 키(Key)로 사용하여 무기별 투사체, 몬스터별로 독립된 풀을 자동 관리
    private Dictionary<GameObject, IObjectPool<GameObject>> pools = new Dictionary<GameObject, IObjectPool<GameObject>>();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // Instantiate를 대체할 풀링 전용 소환 함수
    public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (!pools.ContainsKey(prefab))
        {
            // 해당 프리팹의 풀이 최초로 요청될 때 동적 생성
            ObjectPool<GameObject> newPool = null;
            newPool = new ObjectPool<GameObject>(
                createFunc: () => {
                    GameObject obj = Instantiate(prefab);
                    obj.SetActive(false); // 위치가 정해지기 전에 OnEnable이 실행되지 않도록
                    // 객체가 자신이 돌아갈 풀을 기억할 수 있도록 추적 컴포넌트 부착
                    PooledObject pooledObj = obj.AddComponent<PooledObject>();
                    pooledObj.pool = newPool;
                    return obj;
                },
                actionOnGet: obj => { }, // 위치/회전/활성화는 아래 Spawn에서 매번 새 값으로 처리
                actionOnRelease: obj => obj.SetActive(false),
                actionOnDestroy: obj => Destroy(obj),
                collectionCheck: false,
                defaultCapacity: 50,
                maxSize: 500
            );
            pools[prefab] = newPool;
        }

        GameObject spawned = pools[prefab].Get();
        spawned.transform.SetPositionAndRotation(position, rotation); // 호출할 때마다 새 위치 적용
        spawned.SetActive(true);
        return spawned;
    }
}