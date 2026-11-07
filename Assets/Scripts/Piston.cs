using UnityEngine;

/// A crusher block that slams shut and retracts on a loop, driven by WorldClock.
public class Piston : MonoBehaviour
{
    public Vector3 openPos, closedPos;
    public float period = 2.4f;
    public float phase;

    public static event System.Action<Vector3> Slammed;

    Rigidbody rb;
    float lastClosure;

    void Awake()
    {
        rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    public static float Closure(float p)
    {
        p = Mathf.Repeat(p, 1f);
        if (p < 0.45f) return 0f;
        if (p < 0.52f) { float t = (p - 0.45f) / 0.07f; return t * t; }
        if (p < 0.75f) return 1f;
        float u = (p - 0.75f) / 0.25f;
        return 1f - u * u * (3f - 2f * u);
    }

    void FixedUpdate()
    {
        float c = Closure(WorldClock.Time / period + phase);
        rb.MovePosition(Vector3.Lerp(openPos, closedPos, c));
        if (c >= 1f && lastClosure < 1f) Slammed?.Invoke(closedPos);
        lastClosure = c;
    }
}
