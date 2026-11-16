
using UnityEngine;
using System.Collections;

public class JumpWaveController : MonoBehaviour
{
    [Header("Audio")]
    public AudioSource audioSource;

    [Header("Models (Left to Right)")]
    public Transform[] models;

    [Header("Jump Settings")]
    public float jumpHeight = 0.3f;
    public float jumpDuration = 0.3f;

    [Header("Wave Settings")]
    public float delayBetweenModels = 0.15f;
    public float delayBetweenWaves = 0.5f;

    private Vector3[] startingPositions;
    private bool wavesStarted = false;

    private void Start()
    {
        // Save each model's original local position.
        startingPositions = new Vector3[models.Length];

        for (int i = 0; i < models.Length; i++)
        {
            if (models[i] != null)
                startingPositions[i] = models[i].localPosition;
        }
    }

    public void StartJumping()
    {
        if (wavesStarted)
            return;

        wavesStarted = true;
        StartCoroutine(PlayWaves());
    }

    private IEnumerator PlayWaves()
    {
        bool leftToRight = true;

        // Continue jumping even after the audio finishes.
        while (true)
        {
            if (leftToRight)
            {
                for (int i = 0; i < models.Length; i++)
                {
                    if (models[i] != null)
                        StartCoroutine(JumpModel(i));

                    yield return new WaitForSeconds(delayBetweenModels);
                }
            }
            else
            {
                for (int i = models.Length - 1; i >= 0; i--)
                {
                    if (models[i] != null)
                        StartCoroutine(JumpModel(i));

                    yield return new WaitForSeconds(delayBetweenModels);
                }
            }

            leftToRight = !leftToRight;

            yield return new WaitForSeconds(delayBetweenWaves);
        }
    }

    private IEnumerator JumpModel(int index)
    {
        float elapsedTime = 0f;
        Vector3 startPosition = startingPositions[index];

        while (elapsedTime < jumpDuration)
        {
            elapsedTime += Time.deltaTime;

            float progress = Mathf.Clamp01(elapsedTime / jumpDuration);
            float height = Mathf.Sin(progress * Mathf.PI) * jumpHeight;

            models[index].localPosition =
                startPosition + Vector3.up * height;

            yield return null;
        }

        models[index].localPosition = startPosition;
    }
}
