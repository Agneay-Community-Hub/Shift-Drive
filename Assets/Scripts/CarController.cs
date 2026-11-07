using UnityEngine;
using UnityEngine.InputSystem;

/// Arcade hover-suspension car. Each gear changes the rules:
///   1 GRIP      - normal driving, strong brakes, sharp steering
///   2 FLIP      - gravity reverses; drive on the ceiling
///   3 SLOW-MO   - the world (obstacles, music) slows down
///   4 OVERDRIVE - huge top speed, loose grip, NO BRAKES
public class CarController : MonoBehaviour
{
    public static readonly float[] TopSpeed  = { 0f, 34f, 42f, 48f, 92f };
    public static readonly float[] Accel     = { 0f, 26f, 20f, 18f, 24f };
    public static readonly float[] SteerRate = { 0f, 120f, 105f, 110f, 55f };
    public static readonly string[] GearName = { "", "GRIP", "FLIP", "SLOW-MO", "OVERDRIVE" };
    public static readonly string[] GearDesc =
    {
        "",
        "Normal gravity. Strong brakes.",
        "Gravity reversed. Drive on the ceiling.",
        "Time slows down around you.",
        "Top speed. NO BRAKES.",
    };

    public const float Gravity = 32f;
    const float RideHeight = 0.68f, RayLength = 1.7f, SpringK = 260f, SpringD = 30f;

    public int Gear { get; private set; } = 1;
    public bool Grounded { get; private set; }
    public bool InputEnabled { get; set; }
    public bool AutoBrake { get; set; }
    public bool Wrecked { get; private set; }
    public float ForwardSpeed { get; private set; }
    public float SteerInput { get; private set; }
    public bool BrakeDenied { get; private set; }
    public int ShiftCount { get; set; }
    // Used by the -autopilot test mode.
    public bool ExternalInput;
    public float ExtSteer, ExtThrottle, ExtBrake;
    public Vector3 GravityDir => Gear == 2 ? Vector3.up : Vector3.down;
    public Rigidbody Body => rb;
    public Vector3 Velocity => rb ? rb.linearVelocity : Vector3.zero;

    public event System.Action<int, int> Shifted;
    public event System.Action Wreck;
    public event System.Action<float> Landed;

    Rigidbody rb;
    BoxCollider col;
    Transform visualRoot;
    Transform[] wheels = new Transform[4];
    TrailRenderer[] trails = new TrailRenderer[2];
    Light underGlow;
    Material accentMat;
    float shiftCooldown, steerIn, throttleIn, brakeIn, airTime, wheelSpin, lean;
    bool wasGrounded;

    // ------------------------------------------------------------------ construction

    public static CarController Create(GameAssets a, Material accent)
    {
        var go = new GameObject("Car");
        go.layer = 2; // Ignore Raycast: suspension rays never hit ourselves
        var car = go.AddComponent<CarController>();
        car.accentMat = accent;

        var rb = go.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.freezeRotation = true;
        rb.mass = 1f;
        rb.linearDamping = 0f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        car.rb = rb;

        var col = go.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.15f, 0f);
        col.size = new Vector3(1.9f, 0.7f, 4.0f);
        var pm = new PhysicsMaterial("CarSlide")
        {
            dynamicFriction = 0f, staticFriction = 0f, bounciness = 0.1f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
        };
        col.material = pm;
        car.col = col;

        var vis = new GameObject("Visual").transform;
        vis.SetParent(go.transform, false);
        car.visualRoot = vis;

        Part(vis, "Body",    new Vector3(0f, 0.02f, 0f),    new Vector3(1.9f, 0.45f, 4.0f), a.carBody);
        Part(vis, "Nose",    new Vector3(0f, -0.02f, 2.1f), new Vector3(1.6f, 0.3f, 0.5f), a.carBody);
        Part(vis, "Cabin",   new Vector3(0f, 0.45f, -0.25f), new Vector3(1.35f, 0.42f, 1.8f), a.carGlass);
        Part(vis, "StripeL", new Vector3(-0.96f, 0.05f, 0f), new Vector3(0.04f, 0.1f, 3.8f), accent);
        Part(vis, "StripeR", new Vector3(0.96f, 0.05f, 0f),  new Vector3(0.04f, 0.1f, 3.8f), accent);
        Part(vis, "Tail",    new Vector3(0f, 0.12f, -2.01f), new Vector3(1.7f, 0.12f, 0.04f), accent);
        Part(vis, "WingL",   new Vector3(-0.7f, 0.5f, -1.75f), new Vector3(0.08f, 0.35f, 0.25f), a.carBody);
        Part(vis, "WingR",   new Vector3(0.7f, 0.5f, -1.75f),  new Vector3(0.08f, 0.35f, 0.25f), a.carBody);
        Part(vis, "Wing",    new Vector3(0f, 0.7f, -1.8f),  new Vector3(2.0f, 0.06f, 0.55f), accent);
        Part(vis, "HeadL",   new Vector3(-0.6f, 0.05f, 2.36f), new Vector3(0.4f, 0.1f, 0.04f), a.headlight);
        Part(vis, "HeadR",   new Vector3(0.6f, 0.05f, 2.36f),  new Vector3(0.4f, 0.1f, 0.04f), a.headlight);

        Vector3[] wp = { new(-1.0f, -0.2f, 1.3f), new(1.0f, -0.2f, 1.3f), new(-1.0f, -0.2f, -1.3f), new(1.0f, -0.2f, -1.3f) };
        for (int i = 0; i < 4; i++)
        {
            var holder = new GameObject("Wheel" + i).transform;
            holder.SetParent(vis, false);
            holder.localPosition = wp[i];
            var w = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(w.GetComponent<Collider>());
            w.layer = 2;
            w.transform.SetParent(holder, false);
            w.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            w.transform.localScale = new Vector3(0.72f, 0.16f, 0.72f);
            w.GetComponent<Renderer>().sharedMaterial = a.carWheel;
            var hub = Part(holder, "Hub", new Vector3(wp[i].x > 0 ? 0.17f : -0.17f, 0f, 0f), new Vector3(0.02f, 0.3f, 0.3f), accent);
            hub.layer = 2;
            car.wheels[i] = holder;
        }

        for (int i = 0; i < 2; i++)
        {
            var t = new GameObject("Trail" + i);
            t.transform.SetParent(go.transform, false);
            t.transform.localPosition = new Vector3(i == 0 ? -0.85f : 0.85f, 0.1f, -2.0f);
            var tr = t.AddComponent<TrailRenderer>();
            tr.sharedMaterial = a.trail;
            tr.time = 0.35f;
            tr.minVertexDistance = 0.3f;
            tr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.28f), new Keyframe(1f, 0f));
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;
            car.trails[i] = tr;
        }

        var glow = new GameObject("UnderGlow");
        glow.transform.SetParent(go.transform, false);
        glow.transform.localPosition = new Vector3(0f, -0.3f, 0f);
        var l = glow.AddComponent<Light>();
        l.type = LightType.Point;
        l.range = 7f;
        l.intensity = 4f;
        car.underGlow = l;

        var head = new GameObject("Headlight");
        head.transform.SetParent(go.transform, false);
        head.transform.localPosition = new Vector3(0f, 0.3f, 2.3f);
        head.transform.localRotation = Quaternion.Euler(8f, 0f, 0f);
        var hl = head.AddComponent<Light>();
        hl.type = LightType.Spot;
        hl.range = 45f;
        hl.spotAngle = 55f;
        hl.intensity = 6f;
        hl.color = new Color(0.9f, 0.95f, 1f);

        car.ApplyGearVisuals();
        return car;
    }

    static GameObject Part(Transform parent, string name, Vector3 pos, Vector3 size, Material m)
    {
        var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
        p.name = name;
        p.layer = 2;
        Object.Destroy(p.GetComponent<Collider>());
        p.transform.SetParent(parent, false);
        p.transform.localPosition = pos;
        p.transform.localScale = size;
        p.GetComponent<Renderer>().sharedMaterial = m;
        return p;
    }

    // ------------------------------------------------------------------ gears

    public void SetGear(int g)
    {
        g = Mathf.Clamp(g, 1, 4);
        if (g == Gear || shiftCooldown > 0f || Wrecked) return;
        int old = Gear;
        Gear = g;
        shiftCooldown = 0.15f;
        ShiftCount++;
        ApplyGearVisuals();
        Shifted?.Invoke(old, g);
    }

    void ApplyGearVisuals()
    {
        Color c = GameAssets.GearColor(Gear);
        if (underGlow) underGlow.color = c;
        foreach (var tr in trails)
        {
            if (!tr) continue;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            tr.colorGradient = grad;
        }
    }

    // ------------------------------------------------------------------ input

    void Update()
    {
        var kb = Keyboard.current;
        var gp = Gamepad.current;
        float steer = 0f, thr = 0f, brk = 0f;
        bool up = false, down = false;
        int direct = 0;

        if (kb != null)
        {
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) steer -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) steer += 1f;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) thr = 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) brk = 1f;
            up |= kb.eKey.wasPressedThisFrame || kb.leftShiftKey.wasPressedThisFrame || kb.rightShiftKey.wasPressedThisFrame;
            down |= kb.qKey.wasPressedThisFrame || kb.rightCtrlKey.wasPressedThisFrame;
            if (kb.digit1Key.wasPressedThisFrame) direct = 1;
            if (kb.digit2Key.wasPressedThisFrame) direct = 2;
            if (kb.digit3Key.wasPressedThisFrame) direct = 3;
            if (kb.digit4Key.wasPressedThisFrame) direct = 4;
        }
        if (gp != null)
        {
            steer += gp.leftStick.x.ReadValue();
            thr = Mathf.Max(thr, gp.rightTrigger.ReadValue(), gp.buttonSouth.isPressed ? 1f : 0f);
            brk = Mathf.Max(brk, gp.leftTrigger.ReadValue(), gp.buttonEast.isPressed ? 1f : 0f);
            up |= gp.rightShoulder.wasPressedThisFrame || gp.buttonNorth.wasPressedThisFrame;
            down |= gp.leftShoulder.wasPressedThisFrame || gp.buttonWest.wasPressedThisFrame;
            if (gp.dpad.up.wasPressedThisFrame) direct = 1;
            if (gp.dpad.right.wasPressedThisFrame) direct = 2;
            if (gp.dpad.down.wasPressedThisFrame) direct = 3;
            if (gp.dpad.left.wasPressedThisFrame) direct = 4;
        }

        if (ExternalInput)
        {
            steer = ExtSteer; thr = ExtThrottle; brk = ExtBrake; up = down = false; direct = 0;
        }
        steerIn = Mathf.Clamp(steer, -1f, 1f);
        throttleIn = thr;
        brakeIn = brk;
        SteerInput = InputEnabled ? steerIn : 0f;

        if (InputEnabled && !Wrecked)
        {
            if (direct != 0) SetGear(direct);
            else if (up) SetGear(Gear + 1);
            else if (down) SetGear(Gear - 1);
        }

        AnimateVisuals(Time.deltaTime);
    }

    void AnimateVisuals(float dt)
    {
        if (!visualRoot) return;
        wheelSpin += ForwardSpeed / 0.36f * Mathf.Rad2Deg * dt;
        float steerAngle = SteerInput * 22f;
        for (int i = 0; i < 4; i++)
        {
            if (!wheels[i]) continue;
            float yaw = i < 2 ? steerAngle : 0f;
            wheels[i].localRotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(wheelSpin, 0f, 0f);
        }
        float speedFactor = Mathf.Clamp01(Mathf.Abs(ForwardSpeed) / 30f);
        lean = Mathf.Lerp(lean, -SteerInput * 5f * speedFactor, 1f - Mathf.Exp(-8f * dt));
        visualRoot.localRotation = Quaternion.Euler(0f, 0f, lean);

        bool emit = Mathf.Abs(ForwardSpeed) > 14f;
        foreach (var tr in trails) if (tr) tr.emitting = emit;
    }

    // ------------------------------------------------------------------ physics

    void FixedUpdate()
    {
        if (Wrecked) return;
        float dt = Time.fixedDeltaTime;
        shiftCooldown -= dt;

        Vector3 up = -GravityDir;
        Vector3 v = rb.linearVelocity;
        Quaternion rot = rb.rotation;
        Vector3 carUp = rot * Vector3.up;

        Grounded = false;
        RaycastHit hit = default;
        if (Vector3.Dot(carUp, up) > 0.6f &&
            Physics.Raycast(rb.position, -carUp, out hit, RayLength, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
            Vector3.Dot(hit.normal, up) > 0.5f)
        {
            Grounded = true;
        }

        float steer = InputEnabled ? steerIn : 0f;
        float thr = InputEnabled ? throttleIn : 0f;
        float brk = InputEnabled ? brakeIn : 0f;
        if (AutoBrake) { thr = 0f; brk = 1f; }
        BrakeDenied = Gear == 4 && brk > 0.1f && !AutoBrake && InputEnabled;

        if (Grounded)
        {
            Vector3 n = hit.normal;
            Vector3 fwd = Vector3.ProjectOnPlane(rot * Vector3.forward, n).normalized;
            Vector3 right = Vector3.Cross(n, fwd);
            float vf = Vector3.Dot(v, fwd), vl = Vector3.Dot(v, right), vn = Vector3.Dot(v, n);
            float top = TopSpeed[Gear];

            if (thr > 0.01f && vf < top) vf = Mathf.MoveTowards(vf, top, Accel[Gear] * thr * dt);
            if (vf > top) vf = Mathf.MoveTowards(vf, top, 26f * dt); // engine braking after a downshift

            if (AutoBrake) vf = Mathf.MoveTowards(vf, 0f, 40f * dt);
            else if (brk > 0.01f && Gear != 4)
            {
                if (vf > 0.5f) vf = Mathf.MoveTowards(vf, 0f, 50f * brk * dt);
                else vf = Mathf.MoveTowards(vf, -12f, 14f * brk * dt);
            }
            else if (thr <= 0.01f && Gear != 4) vf = Mathf.MoveTowards(vf, 0f, 5f * dt);

            float grip = Gear == 4 ? 4f : 12f;
            vl = Mathf.Lerp(vl, 0f, 1f - Mathf.Exp(-grip * dt));

            float comp = RideHeight - hit.distance;
            vn += (comp * SpringK - vn * SpringD) * dt;
            if (hit.distance < RideHeight * 0.6f && vn < 0f) vn = 0f;

            v = fwd * vf + right * vl + n * vn;
            v += GravityDir * Gravity * dt;

            float steerFactor = Mathf.Clamp(vf / 10f, -1f, 1f);
            rot = Quaternion.AngleAxis(steer * SteerRate[Gear] * steerFactor * dt, n) * rot;
            Vector3 f2 = Vector3.ProjectOnPlane(rot * Vector3.forward, n);
            if (f2.sqrMagnitude > 0.001f)
                rot = Quaternion.Slerp(rot, Quaternion.LookRotation(f2.normalized, n), 1f - Mathf.Exp(-14f * dt));
        }
        else
        {
            v += GravityDir * Gravity * dt;
            rot = Quaternion.AngleAxis(steer * SteerRate[Gear] * 0.5f * dt, up) * rot;

            // Roll so the wheels face the new "down" (this is what flips us onto the ceiling).
            Vector3 f = rot * Vector3.forward;
            Vector3 cu = Vector3.ProjectOnPlane(rot * Vector3.up, f);
            Vector3 du = Vector3.ProjectOnPlane(up, f);
            if (du.sqrMagnitude > 0.01f && cu.sqrMagnitude > 0.01f)
            {
                float roll = Vector3.SignedAngle(cu, du, f);
                if (Mathf.Abs(roll) > 175f) roll = 180f;
                float step = Mathf.Clamp(roll, -420f * dt, 420f * dt);
                rot = Quaternion.AngleAxis(step, f) * rot;
            }
            // Ease the nose back towards level.
            Vector3 fwdNow = rot * Vector3.forward;
            Vector3 lvl = Vector3.ProjectOnPlane(fwdNow, up);
            if (lvl.sqrMagnitude > 0.01f)
            {
                Quaternion fix = Quaternion.FromToRotation(fwdNow, lvl.normalized);
                rot = Quaternion.Slerp(Quaternion.identity, fix, 1f - Mathf.Exp(-1.5f * dt)) * rot;
            }
            airTime += dt;
        }

        if (Grounded && !wasGrounded && airTime > 0.25f) Landed?.Invoke(airTime);
        if (Grounded) airTime = 0f;
        wasGrounded = Grounded;

        rb.linearVelocity = v;
        rb.MoveRotation(rot);
        ForwardSpeed = Vector3.Dot(v, rot * Vector3.forward);
    }

    // ------------------------------------------------------------------ wreck / respawn

    void OnTriggerEnter(Collider c)
    {
        if (!Wrecked && c.GetComponentInParent<Hazard>() != null) DoWreck();
    }

    public void DoWreck()
    {
        if (Wrecked) return;
        Wrecked = true;
        Wreck?.Invoke();
        rb.linearVelocity = Vector3.zero;
        rb.isKinematic = true;
        col.enabled = false;
        visualRoot.gameObject.SetActive(false);
        underGlow.enabled = false;
        foreach (var tr in trails) { tr.emitting = false; tr.Clear(); }
    }

    public void Respawn(Vector3 pos)
    {
        Wrecked = false;
        rb.isKinematic = false;
        transform.SetPositionAndRotation(pos, Quaternion.identity);
        rb.position = pos;
        rb.rotation = Quaternion.identity;
        rb.linearVelocity = Vector3.zero;
        col.enabled = true;
        visualRoot.gameObject.SetActive(true);
        underGlow.enabled = true;
        int old = Gear;
        Gear = 1;
        ForwardSpeed = 0f;
        airTime = 0f;
        wasGrounded = true;
        ApplyGearVisuals();
        foreach (var tr in trails) tr.Clear();
        if (old != 1) Shifted?.Invoke(old, 1);
    }
}
