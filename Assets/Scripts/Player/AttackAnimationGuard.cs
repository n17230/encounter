using UnityEngine;

// Attached to every one-shot attack state (Attack_TwoHand/Attack_Bow/Attack_Magic)
// on CharacterIdle_M/F.controller. Sets "isAttacking" true for the duration of
// the state and false again on exit - the 7 weaponPose/showCombatIdle idle-swap
// Any State transitions each require isAttacking == false, so they can no
// longer preempt an in-progress attack animation the instant the character is
// standing still (previously they had no exit-time gate and would snap straight
// back to the matching idle before the attack clip had a chance to play).
public class AttackAnimationGuard : StateMachineBehaviour
{
    private static readonly int IsAttacking = Animator.StringToHash("isAttacking");

    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        animator.SetBool(IsAttacking, true);
    }

    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        animator.SetBool(IsAttacking, false);
    }
}
