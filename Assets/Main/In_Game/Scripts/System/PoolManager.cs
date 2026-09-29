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
            pools[prefab] = new ObjectPool<GameObject>(
                createFunc: () => {
                    GameObject obj = Instantiate(prefab);
                    // 객체가 자신이 돌아갈 풀을 기억할 수 있도록 추적 컴포넌트 부착
                    PooledObject pooledObj = obj.AddComponent<PooledObject>();
                    pooledObj.pool = pools[prefab];
                    return obj;
                },
                actionOnGet: obj => {
                    obj.transform.position = position;
                    obj.transform.rotation = rotation;
                    obj.SetActive(true);
                },
                actionOnRelease: obj => obj.SetActive(false),
                actionOnDestroy: obj => Destroy(obj),
                collectionCheck: false, // 성능을 위해 중복 반환 검사 해제 (안정화 후 false 권장)
                defaultCapacity: 50,
                maxSize: 500 // 메모리 오버플로우를 막기 위한 최대치 제한
            );
        }

        return pools[prefab].Get();
    }
}