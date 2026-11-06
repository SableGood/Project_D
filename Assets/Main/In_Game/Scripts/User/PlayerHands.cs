using UnityEngine;

// 새 몸 그림(PlayerBodyAnimator.armRigTest)에서 무기를 잡는 손 두 개를 무기 위에 붙임
//  WeaponVisual(100)이 무기 각도·반동을 정한 뒤에 위치를 맞춰야 손이 무기에서 떨어지지 않으므로 늦게 실행
//  PlayerBodyAnimator가 자동으로 붙임
[DefaultExecutionOrder(200)]
public class PlayerHands : MonoBehaviour
{
    [HideInInspector] public PlayerBodyAnimator body;

    void LateUpdate()
    {
        if (body != null) body.UpdateHands();
    }
}
