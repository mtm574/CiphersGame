
using UnityEngine;
using System.Collections;

public class ModelJump : MonoBehaviour
{
    [Header("Jump Settings")]
    public float jumpHeight = 0.3f;
    public float jumpDuration = 0.3f;

    [Header("Delay Before Jump")]
    public float waitTime = 0f;

    private Vector3 startingPosition;
    private Coroutine jumpCoroutine;

    private void Start()
    {
        startingPosition = transform.localPosition;
    }

    public void StartJump()
    {
        if (jumpCoroutine != null)
        {
            StopCoroutine(jumpCoroutine);
        }

        // Capture the current position before jumping.
        startingPosition = transform.localPosition;

        jumpCoroutine = StartCoroutine(JumpAfterDelay());
    }

    private IEnumerator JumpAfterDelay()
    {
        yield return new WaitForSeconds(waitTime);

        float elapsedTime = 0f;

        while (elapsedTime < jumpDuration)
        {
            elapsedTime += Time.deltaTime;

            float progress = Mathf.Clamp01(elapsedTime / jumpDuration);

            float height = Mathf.Sin(progress * Mathf.PI) * jumpHeight;

            transform.localPosition = startingPosition +
                Vector3.up * height;

            yield return null;
        }

        transform.localPosition = startingPosition;

        jumpCoroutine = null;
    }

    [ContextMenu("Test Jump")]
    private void TestJump()
    {
        StartJump();
    }
}
