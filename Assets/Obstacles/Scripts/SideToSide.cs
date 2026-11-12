using UnityEngine;

public class SideToSide : MonoBehaviour
{
    [Tooltip("How far to move each way from the start position, in meters.")]
    public float distance = 1f;

    [Tooltip("How fast it moves back and forth.")]
    public float speed = 1f;

    [Tooltip("Axis to move along, in the object's local space.")]
    public Vector3 direction = Vector3.right;

    Vector3 startLocalPos;

    void Start()
    {
        startLocalPos = transform.localPosition;
    }

    void Update()
    {
        float offset = Mathf.Sin(Time.time * speed) * distance;
        transform.localPosition = startLocalPos + direction.normalized * offset;
    }
}