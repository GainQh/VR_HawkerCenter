using UnityEngine;

public class ArmSwingLocomotion : MonoBehaviour
{
    public OVRCameraRig cameraRig;
    public CharacterController characterController;

    [Header("Tuning")]
    public float sensitivity = 1.5f;     // 映射摆臂强度 -> 速度 的增益
    public float maxSpeed = 2.0f;        // 最大移动速度 (m/s)
    public float swingThreshold = 0.15f; // 摆臂速度死区 (m/s) —— 建议 0.12~0.2

    public bool enableMovement = false;

    private Transform leftHand;
    private Transform rightHand;
    private Vector3 prevLeftPos;
    private Vector3 prevRightPos;

    void Start()
    {
        leftHand = cameraRig.leftControllerAnchor != null ? cameraRig.leftControllerAnchor : cameraRig.leftHandAnchor;
        rightHand = cameraRig.rightControllerAnchor != null ? cameraRig.rightControllerAnchor : cameraRig.rightHandAnchor;

        prevLeftPos = leftHand.position;
        prevRightPos = rightHand.position;
    }

    void Update()
    {
        if (!enableMovement || cameraRig == null || characterController == null) return;

        float dt = Mathf.Max(0.0001f, Time.deltaTime);

        // 1) 计算头部 “水平前向” 作为统一基准方向（去除竖直分量）
        Vector3 headForward = cameraRig.centerEyeAnchor ? cameraRig.centerEyeAnchor.forward : transform.forward;
        headForward.y = 0f;
        if (headForward.sqrMagnitude < 1e-6f) headForward = transform.forward;
        headForward.Normalize();

        // 2) 计算手柄“速度”（m/s），再投影到 headForward（只取水平前向分量）
        Vector3 leftVel = (leftHand.position - prevLeftPos) / dt;
        Vector3 rightVel = (rightHand.position - prevRightPos) / dt;

        float leftProj = Vector3.Dot(leftVel, headForward); // m/s（正：向前，负：向后）
        float rightProj = Vector3.Dot(rightVel, headForward);

        // 使用绝对值让前后摆各半周期都贡献强度
        float swingPower = 0.5f * (Mathf.Abs(leftProj) + Mathf.Abs(rightProj)); // 单位：m/s

        // 3) 死区处理：过滤微小抖动/噪声
        float effectiveSwing = Mathf.Max(0f, swingPower - swingThreshold);

        if (effectiveSwing > 0f)
        {
            // 4) 将摆臂“速度”映射为角色移动速度（再限幅）
            float speed = Mathf.Clamp(effectiveSwing * sensitivity, 0f, maxSpeed);

            // 5) 按“头部水平前向”推进（与朝向无关，保持一致）
            Vector3 moveDir = headForward;                   // 统一水平方向
            characterController.Move(moveDir * speed * dt);  // m/s * s = m

            // 同步 rig 根位置（防漂移）
            cameraRig.transform.position = characterController.transform.position;
        }

        // 6) 更新历史位置
        prevLeftPos = leftHand.position;
        prevRightPos = rightHand.position;
    }
}
