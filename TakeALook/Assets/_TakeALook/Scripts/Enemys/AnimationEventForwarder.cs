using UnityEngine;

public class AnimationEventForwarder : MonoBehaviour
{
    private EnemyAIBase aiBase;

    void Awake()
    {
        aiBase = GetComponentInParent<EnemyAIBase>();
    }

    public void OnFootstepAnimationEvent() => aiBase?.OnFootstepAnimationEvent();
    public void OnRoarFinishedAnimationEvent() => aiBase?.OnRoarFinishedAnimationEvent();
    public void OnHitForwardFinishedAnimationEvent() => aiBase?.OnHitForwardFinishedAnimationEvent();
    public void OnHitRecoveryFinishedAnimationEvent() => aiBase?.OnHitRecoveryFinishedAnimationEvent();
    public void OnDeathEvent() => aiBase?.OnDeathEvent();
}