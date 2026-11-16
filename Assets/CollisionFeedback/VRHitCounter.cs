
using UnityEngine;
using UnityEngine.UI;

public class VRHitCounter : MonoBehaviour
{
    public VRHitFlash hitFlash;

    private int hitCount = 0;
    private bool gameOver = false;

    private Text hitCounter;
    private Text gameOverText;

    void Start()
    {
        CreateUI();
    }

    void CreateUI()
    {
        GameObject canvasObject = new GameObject(
            "VR Hit Counter UI",
            typeof(Canvas)
        );

        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        RectTransform canvasRect =
            canvasObject.GetComponent<RectTransform>();

        canvasRect.sizeDelta = new Vector2(1000, 600);
        canvasRect.localPosition = new Vector3(0, 0, 1);
        canvasRect.localScale = Vector3.one * 0.001f;

        // X counter in the top-right corner
        hitCounter = CreateText(
            canvasObject.transform,
            "Hit Counter",
            new Vector2(0.82f, 0.9f),
            new Vector2(300, 120),
            70
        );

        hitCounter.alignment = TextAnchor.MiddleRight;
        hitCounter.text = "";

        // Game Over message in the center
        gameOverText = CreateText(
            canvasObject.transform,
            "Game Over",
            new Vector2(0.5f, 0.5f),
            new Vector2(900, 200),
            100
        );

        gameOverText.text = "GAME OVER";
        gameOverText.enabled = false;
    }

    Text CreateText(
        Transform parent,
        string objectName,
        Vector2 anchor,
        Vector2 size,
        int fontSize)
    {
        GameObject textObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(Text)
        );

        textObject.transform.SetParent(parent, false);

        RectTransform rect =
            textObject.GetComponent<RectTransform>();

        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;

        Text text = textObject.GetComponent<Text>();

        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.color = Color.white;

        return text;
    }

    public void RegisterHit()
    {
        if (gameOver)
            return;

        hitCount++;

        // Update the X counter
        hitCounter.text = string.Join(
            "   ",
            new string('X', hitCount).ToCharArray()
        );

        // Third hit ends the game
        if (hitCount >= 3)
        {
            gameOver = true;

            gameOverText.enabled = true;

            // Keep the screen red
            if (hitFlash != null)
                hitFlash.Flash();

            return;
        }

        // Normal hit: trigger the existing red flash
        if (hitFlash != null)
            hitFlash.Flash();
    }
}
