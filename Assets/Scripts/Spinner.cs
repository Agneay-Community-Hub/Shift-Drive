using UnityEngine;

/// Rotating blade assembly, driven by WorldClock.
public class Spinner : MonoBehaviour
{
    public float speed = 90f;
    public float offset;
    Rigidbody rb;

    void Awake()
    {
        rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    void FixedUpdate()
    {
        rb.MoveRotation(Quaternion.Euler(0f, 0f, offset + speed * WorldClock.Time));
    }
}
