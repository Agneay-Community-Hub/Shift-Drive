using UnityEngine;

/// Chase camera whose "up" follows the car, so flipping onto the ceiling rolls the whole view.
public class ChaseCamera : MonoBehaviour
{
    public CarController car;
    public bool orbit;
    // Trailer: hold the camera at a fixed spot and track the car.
    public bool useFixed;
    public Vector3 fixedPos;
    bool wasFixed;
    public Camera Cam { get; private set; }

    Vector3 smUp = Vector3.up;
    float shake, orbitAngle, fovKick;

    void Awake() { Cam = GetComponent<Camera>(); }

    public void Shake(float amount) { shake = Mathf.Max(shake, amount); }
    public void Kick(float fov) { fovKick += fov; }

    public void Snap()
    {
        if (!car) return;
        smUp = car.transform.up;
        transform.position = car.transform.position - car.transform.forward * 7f + smUp * 2.4f;
        transform.rotation = Quaternion.LookRotation(car.transform.position + car.transform.forward * 4f - transform.position, smUp);
    }

    void LateUpdate()
    {
        if (!car) return;
        float dt = Time.deltaTime;
        Transform t = car.transform;
        float spd = Mathf.Abs(car.ForwardSpeed);

        if (orbit)
        {
            orbitAngle += dt * 18f;
            Vector3 off = Quaternion.Euler(0f, orbitAngle, 0f) * new Vector3(0f, 0f, -8.5f);
            transform.position = Vector3.Lerp(transform.position, t.position + off + Vector3.up * 2.2f, 1f - Mathf.Exp(-4f * dt));
            transform.rotation = Quaternion.LookRotation(t.position + Vector3.up * 0.4f - transform.position, Vector3.up);
            Cam.fieldOfView = 55f;
            smUp = Vector3.up;
            return;
        }

        if (useFixed)
        {
            transform.position = fixedPos;
            transform.rotation = Quaternion.LookRotation(t.position - fixedPos, Vector3.up);
            Cam.fieldOfView = 48f;
            wasFixed = true;
            return;
        }
        if (wasFixed) { wasFixed = false; Snap(); }

        smUp = Vector3.Slerp(smUp, t.up, 1f - Mathf.Exp(-5f * dt)).normalized;
        Vector3 fwd = Vector3.ProjectOnPlane(t.forward, smUp);
        if (fwd.sqrMagnitude < 0.01f) fwd = t.forward;
        fwd.Normalize();

        float back = 7f + spd * 0.035f;
        Vector3 desired = t.position - fwd * back + smUp * 2.4f;
        transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-16f * dt));

        Vector3 look = t.position + smUp * 0.9f + fwd * 4f;
        Quaternion rot = Quaternion.LookRotation(look - transform.position, smUp);

        if (shake > 0.001f)
        {
            rot *= Quaternion.Euler(Random.Range(-1f, 1f) * shake * 2.5f, Random.Range(-1f, 1f) * shake * 2.5f, Random.Range(-1f, 1f) * shake * 1.5f);
            shake = Mathf.MoveTowards(shake, 0f, dt * 2.5f);
        }
        transform.rotation = rot;

        float targetFov = 62f + spd * 0.22f + (car.Gear == 4 ? 6f : 0f) - (car.Gear == 3 ? 4f : 0f);
        fovKick = Mathf.MoveTowards(fovKick, 0f, dt * 30f);
        Cam.fieldOfView = Mathf.Lerp(Cam.fieldOfView, Mathf.Clamp(targetFov + fovKick, 50f, 100f), 1f - Mathf.Exp(-4f * dt));
    }
}
