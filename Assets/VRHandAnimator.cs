using UnityEngine;
using UnityEngine.InputSystem;

public class VRHandAnimator : MonoBehaviour
{
    [Header("Component References")]
    [SerializeField] private Animator animator;

    [Header("Input Actions")]
    [SerializeField] private InputActionProperty gripAnimationAction;
    [SerializeField] private InputActionProperty triggerAnimationAction;

    [Header("Hand Settings")]
    [Tooltip("Check this if this script is on the Right Controller")]
    [SerializeField] private bool isRightHand = false;

    private int gripParam;
    private int triggerParam;

    void Start()
    {
        string prefix = isRightHand ? "Right" : "Left";
        gripParam = Animator.StringToHash(prefix + "Grip");
        triggerParam = Animator.StringToHash(prefix + "Trigger");
    }

    void Update()
    {
        // Read how far the user is squeezing the buttons (0.0 to 1.0)
        float gripValue = ReadAction(gripAnimationAction);
        float triggerValue = ReadAction(triggerAnimationAction);

        // Send those values to the Animator
        if (animator != null)
        {
            animator.SetFloat(gripParam, gripValue);
            animator.SetFloat(triggerParam, triggerValue);
        }
    }

    private static float ReadAction(InputActionProperty property)
    {
        var action = property.action;
        return action != null && action.enabled ? Mathf.Clamp01(action.ReadValue<float>()) : 0f;
    }
}
