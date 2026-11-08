using UnityEngine;
using UnityEngine.EventSystems;

// v129 마우스를 올리면 살짝 커지는 UI (메인 메뉴 버튼 등). 시간이 멈춰 있어도 동작
public class UIHoverScale : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public float hoverScale = 1.05f;
    public float speed = 18f;
    public bool Hovered { get; private set; }
    private Vector3 baseScale = Vector3.one;
    private bool init;

    void OnEnable() { if (!init) { baseScale = transform.localScale; init = true; } }
    public void OnPointerEnter(PointerEventData e) { Hovered = true; }
    public void OnPointerExit(PointerEventData e) { Hovered = false; }

    void Update()
    {
        Vector3 target = baseScale * (Hovered ? hoverScale : 1f);
        transform.localScale = Vector3.Lerp(transform.localScale, target, 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime));
    }
}
