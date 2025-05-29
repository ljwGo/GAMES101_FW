using UnityEngine;

/// <summary>
/// 测试的结果是不会对位移进行更小的碎片化碰撞检测了
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
