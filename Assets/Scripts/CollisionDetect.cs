using UnityEngine;

/// <summary>
/// ���ԵĽ���ǲ����λ�ƽ��и�С����Ƭ����ײ�����
/// </summary>
public class CollisionDetect : MonoBehaviour
{
    public Vector3 targetPosition;
    public Vector3 tinyStep;

    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.MovePosition(targetPosition);
    }

    private void Update() {
        //rb.MovePosition(transform.position + tinyStep * Time.deltaTime);
    }
}
