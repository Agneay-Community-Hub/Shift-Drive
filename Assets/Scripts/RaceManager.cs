using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Boots the game: builds the world, owns race state, audio, post-processing and the HUD.
[RequireComponent(typeof(GameAssets))]
public class RaceManager : MonoBehaviour
{
    enum State { Title, Countdown, Racing, Finished }

    const float GoldTime = 45f, SilverTime = 60f, BronzeTime = 80f;
    const string BestKey = "shiftdrive_best_v1";

    GameAssets A;
    CarController car;
    ChaseCamera cam;
    TrackInfo track;
    Material floorRT, wallRT, stripRT, accentRT;

    AudioSource engineSrc, musicSrc, sfxSrc;
    AudioClip[] shiftClips = new AudioClip[5];
    AudioClip shiftDownClip, crashClip, thumpClip, chimeClip, beepClip, goClip, deniedClip, landClip, finishClip;

    Bloom bloom; ColorAdjustments colorAdj; ChromaticAberration chroma; Vignette vignette; LensDistortion lens;

    State state;
    float countdown, raceTime, wreckTimer, hintTimer, cpFlash, deniedFlash, shiftFlash, deniedSfxTimer, titleTime;
    int cpIndex, nextHint, wrecks, lastBeep;
    string hintText = "";
    int hintGear = 1;
    Color curColor;
    float best;
    bool newRecord;
    Texture2D white;
    GUIStyle style;

    // ------------------------------------------------------------------ boot

    void Awake()
    {
        A = GetComponent<GameAssets>();
        Time.fixedDeltaTime = 0.01f;
        QualitySettings.vSyncCount = 1;
        Application.targetFrameRate = 120;
        best = PlayerPrefs.GetFloat(BestKey, 0f);

        floorRT = new Material(A.gridFloor);
        wallRT = new Material(A.gridWall);
        stripRT = new Material(A.strip);
        accentRT = new Material(A.carAccent);

        SetupEnvironment();
        track = TrackBuilder.Build(A, floorRT, wallRT, stripRT);

        car = CarController.Create(A, accentRT);
        car.Shifted += OnShifted;
        car.Wreck += OnWreck;
        car.Landed += OnLanded;
        Piston.Slammed += OnPistonSlam;

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var c = camGo.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = RenderSettings.fogColor;
        c.nearClipPlane = 0.1f;
        c.farClipPlane = 1200f;
        c.fieldOfView = 60f;
        var data = c.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        camGo.AddComponent<AudioListener>();
        cam = camGo.AddComponent<ChaseCamera>();
        cam.car = car;

        SetupPost();
        SetupAudio();
        BuildSun();

        white = new Texture2D(1, 1);
        white.SetPixel(0, 0, Color.white);
        white.Apply();

        curColor = GameAssets.GearColor(1);
        TrailerRecorder.ActivateIfRequested();
        ToTitle();

        if (Autopilot.ArgDir() != null)
        {
            autopilot = gameObject.AddComponent<Autopilot>();
            autopilot.car = car;
            autopilot.stateProvider = () => state.ToString();
            autopilot.timeProvider = () => raceTime;
        }
        if (TrailerRecorder.Active)
        {
            var rec = gameObject.AddComponent<TrailerRecorder>();
            rec.car = car;
            rec.cam = cam;
            rec.stateProvider = () => state.ToString();
        }
    }

    Autopilot autopilot;

    void OnDestroy() { Piston.Slammed -= OnPistonSlam; }

    void SetupEnvironment()
    {
        Color fog = new Color(0.035f, 0.015f, 0.07f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 40f;
        RenderSettings.fogEndDistance = 330f;
        RenderSettings.fogColor = fog;
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.22f, 0.2f, 0.32f);

        var sun = new GameObject("Light").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 0.7f;
        sun.color = new Color(0.85f, 0.8f, 1f);
        sun.shadows = LightShadows.None;
        sun.transform.rotation = Quaternion.Euler(55f, -25f, 0f);
    }

    Transform sun;

    /// Retro striped sun that hangs on the horizon ahead; only visible when fog opens up outdoors.
    void BuildSun()
    {
        sun = new GameObject("Sun").transform;
        var disc = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(disc.GetComponent<Collider>());
        disc.transform.SetParent(sun, false);
        disc.transform.localScale = new Vector3(260f, 260f, 10f);
        disc.GetComponent<Renderer>().sharedMaterial = A.gearMats[3];
        for (int i = 0; i < 6; i++)
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(bar.GetComponent<Collider>());
            bar.transform.SetParent(sun, false);
            bar.transform.localPosition = new Vector3(0f, -12f - i * 18f, -8f);
            bar.transform.localScale = new Vector3(280f, 3f + i * 2.2f, 1f);
            bar.GetComponent<Renderer>().sharedMaterial = A.dark;
        }
    }

    void SetupPost()
    {
        var vol = new GameObject("PostFX").AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 10f;
        var p = ScriptableObject.CreateInstance<VolumeProfile>();
        bloom = p.Add<Bloom>(true);
        bloom.intensity.value = 1.3f;
        bloom.threshold.value = 0.85f;
        bloom.scatter.value = 0.65f;
        vignette = p.Add<Vignette>(true);
        vignette.intensity.value = 0.3f;
        vignette.smoothness.value = 0.45f;
        colorAdj = p.Add<ColorAdjustments>(true);
        colorAdj.saturation.value = 0f;
        colorAdj.contrast.value = 10f;
        chroma = p.Add<ChromaticAberration>(true);
        chroma.intensity.value = 0.05f;
        lens = p.Add<LensDistortion>(true);
        lens.intensity.value = 0f;
        vol.sharedProfile = p;
    }

    void SetupAudio()
    {
        engineSrc = gameObject.AddComponent<AudioSource>();
        engineSrc.clip = Synth.Engine();
        engineSrc.loop = true;
        engineSrc.volume = 0.18f;
        engineSrc.Play();

        musicSrc = gameObject.AddComponent<AudioSource>();
        musicSrc.clip = Synth.Music();
        musicSrc.loop = true;
        musicSrc.volume = 0.32f;
        musicSrc.Play();

        sfxSrc = gameObject.AddComponent<AudioSource>();

        for (int g = 1; g <= 4; g++) shiftClips[g] = Synth.Sweep("shift" + g, 300f + g * 120f, 700f + g * 220f, 0.16f, 2, 0.35f);
        shiftDownClip = Synth.Sweep("shiftdown", 700f, 260f, 0.16f, 2, 0.3f);
        crashClip = Synth.Crash();
        thumpClip = Synth.Thump();
        chimeClip = Synth.Chime(880f, 1318.5f);
        finishClip = Synth.Chime(659.3f, 1318.5f);
        beepClip = Synth.Sweep("beep", 600f, 600f, 0.18f, 0, 0.5f);
        goClip = Synth.Sweep("go", 1200f, 1200f, 0.4f, 0, 0.5f);
        deniedClip = Synth.Sweep("denied", 180f, 140f, 0.22f, 1, 0.35f);
        landClip = Synth.Thump();
    }

    // ------------------------------------------------------------------ state changes

    Vector3 SpawnPoint(int cp) => new Vector3(0f, 0.75f, track.checkpointZ[cp]);

    void ToTitle()
    {
        state = State.Title;
        titleTime = 0f;
        cpIndex = 0;
        car.Respawn(SpawnPoint(0));
        car.InputEnabled = false;
        car.AutoBrake = false;
        cam.orbit = true;
        cam.transform.position = car.transform.position + new Vector3(0f, 3f, -9f);
    }

    void StartCountdown()
    {
        state = State.Countdown;
        countdown = 3f;
        lastBeep = 4;
        raceTime = 0f;
        wrecks = 0;
        cpIndex = 0;
        nextHint = 0;
        hintTimer = 0f;
        newRecord = false;
        car.Respawn(SpawnPoint(0));
        car.ShiftCount = 0;
        car.InputEnabled = false;
        car.AutoBrake = false;
        cam.orbit = false;
        cam.Snap();
    }

    void Finish()
    {
        state = State.Finished;
        car.InputEnabled = false;
        car.AutoBrake = true;
        sfxSrc.PlayOneShot(finishClip, 1f);
        if (best <= 0f || raceTime < best)
        {
            newRecord = true;
            best = raceTime;
            PlayerPrefs.SetFloat(BestKey, best);
            PlayerPrefs.Save();
        }
    }

    void RespawnAtCheckpoint()
    {
        float cpz = track.checkpointZ[cpIndex];
        car.Respawn(SpawnPoint(cpIndex));
        cam.Snap();
        nextHint = 0;
        while (nextHint < track.hints.Count && track.hints[nextHint].z < cpz - 6f) nextHint++;
    }

    // ------------------------------------------------------------------ events

    void OnShifted(int from, int to)
    {
        shiftFlash = 1f;
        sfxSrc.PlayOneShot(to > from ? shiftClips[to] : shiftDownClip, 0.9f);
        cam.Kick(to > from ? 4f : -2f);
        if (to == 2 || from == 2) cam.Shake(0.15f);
    }

    void OnWreck()
    {
        wrecks++;
        wreckTimer = 1.1f;
        sfxSrc.PlayOneShot(crashClip, 1f);
        cam.Shake(1f);
        SpawnDebris(car.transform.position, car.Velocity);
    }

    void OnLanded(float air)
    {
        float k = Mathf.Clamp01(air / 1.5f);
        cam.Shake(0.15f + 0.5f * k);
        sfxSrc.PlayOneShot(landClip, 0.3f + 0.6f * k);
    }

    void OnPistonSlam(Vector3 pos)
    {
        float d = Vector3.Distance(pos, car.transform.position);
        if (d > 60f) return;
        float k = 1f - d / 60f;
        cam.Shake(0.35f * k);
        sfxSrc.PlayOneShot(thumpClip, 0.7f * k);
    }

    void SpawnDebris(Vector3 pos, Vector3 vel)
    {
        for (int i = 0; i < 18; i++)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.layer = 2;
            g.transform.position = pos + Random.insideUnitSphere * 1.2f;
            g.transform.rotation = Random.rotation;
            g.transform.localScale = Vector3.one * Random.Range(0.2f, 0.6f);
            g.GetComponent<Renderer>().sharedMaterial = i % 3 == 0 ? accentRT : A.carBody;
            var rb = g.AddComponent<Rigidbody>();
            rb.linearVelocity = vel * 0.35f + Random.insideUnitSphere * 14f;
            rb.angularVelocity = Random.insideUnitSphere * 15f;
            g.AddComponent<Debris>().life = Random.Range(1.8f, 3f);
        }
    }

    // ------------------------------------------------------------------ loop

    bool StartPressed()
    {
        var kb = Keyboard.current; var gp = Gamepad.current; var ms = Mouse.current;
        return (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) ||
               (gp != null && (gp.startButton.wasPressedThisFrame || gp.buttonSouth.wasPressedThisFrame)) ||
               (ms != null && ms.leftButton.wasPressedThisFrame);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        var kb = Keyboard.current;
        var gp = Gamepad.current;

        WorldClock.Tick(dt, car.Gear == 3 && !car.Wrecked ? 0.3f : 1f);

        switch (state)
        {
            case State.Title:
                titleTime += dt;
                if (titleTime > 0.3f && (StartPressed() || (autopilot && titleTime > (TrailerRecorder.Active ? 5f : 2f)))) StartCountdown();
                break;

            case State.Countdown:
                countdown -= dt;
                int sec = Mathf.CeilToInt(countdown);
                if (sec < lastBeep && sec > 0) { sfxSrc.PlayOneShot(beepClip, 0.8f); lastBeep = sec; }
                if (countdown <= 0f)
                {
                    state = State.Racing;
                    car.InputEnabled = true;
                    sfxSrc.PlayOneShot(goClip, 0.9f);
                }
                break;

            case State.Racing:
                raceTime += dt;
                UpdateRace();
                if (kb != null && kb.rKey.wasPressedThisFrame && !car.Wrecked) RespawnAtCheckpoint();
                if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (gp != null && gp.selectButton.wasPressedThisFrame)) StartCountdown();
                break;

            case State.Finished:
                if (StartPressed()) StartCountdown();
                if (kb != null && kb.escapeKey.wasPressedThisFrame) ToTitle();
                break;
        }

        if (car.Wrecked)
        {
            wreckTimer -= dt;
            if (wreckTimer <= 0f && state != State.Title) RespawnAtCheckpoint();
        }

        hintTimer -= dt;
        cpFlash -= dt;
        deniedFlash -= dt;
        deniedSfxTimer -= dt;
        shiftFlash = Mathf.MoveTowards(shiftFlash, 0f, dt * 3f);

        if (car.BrakeDenied && state == State.Racing)
        {
            deniedFlash = 0.6f;
            if (deniedSfxTimer <= 0f) { sfxSrc.PlayOneShot(deniedClip, 0.7f); deniedSfxTimer = 0.5f; }
        }

        UpdateLook(dt);
        UpdateAudio(dt);
    }

    void UpdateRace()
    {
        Vector3 p = car.transform.position;
        bool onTrackHeight = p.y > -2f && p.y < 20f;

        if (!car.Wrecked && (p.y < -35f || p.y > 45f || Mathf.Abs(p.x) > 40f)) car.DoWreck();

        if (!car.Wrecked && onTrackHeight)
        {
            if (cpIndex + 1 < track.checkpointZ.Count && p.z > track.checkpointZ[cpIndex + 1] - 4f)
            {
                cpIndex++;
                cpFlash = 1.6f;
                sfxSrc.PlayOneShot(chimeClip, 0.8f);
            }
            while (nextHint < track.hints.Count && p.z > track.hints[nextHint].z)
            {
                hintText = track.hints[nextHint].text;
                hintGear = track.hints[nextHint].gear;
                hintTimer = 4.5f;
                nextHint++;
            }
            if (p.z > track.finishZ) Finish();
        }
    }

    void UpdateLook(float dt)
    {
        Color target = GameAssets.GearColor(car.Gear);
        curColor = Color.Lerp(curColor, target, 1f - Mathf.Exp(-8f * dt));
        floorRT.SetColor("_EmissionColor", curColor * 1.4f);
        wallRT.SetColor("_EmissionColor", curColor * 0.7f);
        stripRT.SetColor("_EmissionColor", curColor * 3.5f);
        accentRT.SetColor("_EmissionColor", curColor * 3f);

        float spd01 = Mathf.Clamp01(Mathf.Abs(car.ForwardSpeed) / CarController.TopSpeed[4]);
        bool slow = car.Gear == 3;
        bool over = car.Gear == 4;
        float k = 1f - Mathf.Exp(-6f * dt);
        colorAdj.saturation.value = Mathf.Lerp(colorAdj.saturation.value, slow ? -45f : 10f, k);
        vignette.intensity.value = Mathf.Lerp(vignette.intensity.value, slow ? 0.48f : 0.28f + spd01 * 0.1f, k);
        chroma.intensity.value = Mathf.Lerp(chroma.intensity.value, (slow ? 0.35f : over ? 0.15f + 0.45f * spd01 : 0.05f) + shiftFlash * 0.5f, k * 2f);
        lens.intensity.value = Mathf.Lerp(lens.intensity.value, over ? -0.28f * spd01 : 0f, k);
        bloom.intensity.value = 1.3f + shiftFlash * 2.5f;
        float cz = car.transform.position.z;
        bool open = false;
        foreach (var r in track.openSky) if (cz > r.x - 60f && cz < r.y) open = true;
        RenderSettings.fogStartDistance = Mathf.Lerp(RenderSettings.fogStartDistance, open ? 120f : 40f, 1f - Mathf.Exp(-2f * dt));
        RenderSettings.fogEndDistance = Mathf.Lerp(RenderSettings.fogEndDistance, open ? 1100f : 330f, 1f - Mathf.Exp(-2f * dt));
        if (sun) sun.position = new Vector3(0f, 70f, cam.transform.position.z + 850f);
        if (Camera.main) Camera.main.backgroundColor = RenderSettings.fogColor;
    }

    void UpdateAudio(float dt)
    {
        float top = CarController.TopSpeed[car.Gear];
        float rev = Mathf.Clamp01(Mathf.Abs(car.ForwardSpeed) / top);
        float pitch = 0.55f + rev * 1.35f;
        if (car.Gear == 3) pitch *= 0.85f;
        if (!car.Grounded) pitch += 0.15f;
        engineSrc.pitch = Mathf.Lerp(engineSrc.pitch, pitch, 1f - Mathf.Exp(-10f * dt));
        engineSrc.volume = car.Wrecked ? 0f : (state == State.Title ? 0.08f : 0.14f + rev * 0.1f);
        musicSrc.pitch = Mathf.Lerp(musicSrc.pitch, WorldClock.Scale < 0.99f ? 0.78f : 1f, 1f - Mathf.Exp(-4f * dt));
    }

    // ------------------------------------------------------------------ HUD

    static string FormatTime(float t)
    {
        if (t <= 0f) return "--:--.--";
        int m = (int)(t / 60f);
        float s = t - m * 60f;
        return $"{m:00}:{s:00.00}";
    }

    string Medal(float t, out Color c)
    {
        if (t <= GoldTime) { c = new Color(1f, 0.82f, 0.2f); return "GOLD"; }
        if (t <= SilverTime) { c = new Color(0.8f, 0.85f, 0.95f); return "SILVER"; }
        if (t <= BronzeTime) { c = new Color(0.85f, 0.5f, 0.25f); return "BRONZE"; }
        c = Color.gray; return "FINISHED";
    }

    void Rect(Rect r, Color c) { var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = old; }

    void Text(Rect r, string s, int size, Color c, TextAnchor a = TextAnchor.MiddleCenter, bool shadow = true)
    {
        style.fontSize = size;
        style.alignment = a;
        if (shadow)
        {
            style.normal.textColor = new Color(0f, 0f, 0f, c.a * 0.75f);
            GUI.Label(new Rect(r.x + size * 0.06f, r.y + size * 0.06f, r.width, r.height), s, style);
        }
        style.normal.textColor = c;
        GUI.Label(r, s, style);
    }

    void OnGUI()
    {
        if (TrailerRecorder.Active) return; // clean frames; captions are added in the edit
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, wordWrap = true, richText = true };
        }
        float s = Screen.height / 1080f;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
        float W = Screen.width / s, H = 1080f;

        if (state == State.Title) { DrawTitle(W, H); return; }

        DrawGearPanel(W, H);

        // timer
        Text(new Rect(0, 18, W, 70), FormatTime(Mathf.Max(raceTime, 0.0001f)), 60, Color.white);
        Text(new Rect(0, 82, W, 34), "BEST  " + FormatTime(best), 24, new Color(1f, 1f, 1f, 0.6f));
        Text(new Rect(30, 24, 500, 40), $"SECTOR {cpIndex + 1}/{track.checkpointZ.Count}", 28, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft);
        Text(new Rect(30, 62, 500, 34), $"WRECKS {wrecks}   SHIFTS {car.ShiftCount}", 22, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleLeft);

        // progress bar
        float prog = Mathf.Clamp01(car.transform.position.z / track.finishZ);
        Rect pb = new Rect(W * 0.5f - 300, 124, 600, 6);
        Rect(pb, new Color(1f, 1f, 1f, 0.15f));
        Rect(new Rect(pb.x, pb.y, pb.width * prog, pb.height), curColor);
        foreach (float cz in track.checkpointZ)
            Rect(new Rect(pb.x + pb.width * Mathf.Clamp01(cz / track.finishZ) - 1, pb.y - 4, 3, 14), new Color(1f, 1f, 1f, 0.7f));

        if (hintTimer > 0f && state == State.Racing)
        {
            float a = Mathf.Clamp01(hintTimer * 2f);
            Color gc = GameAssets.GearColor(hintGear);
            Rect hr = new Rect(W * 0.5f - 560, 170, 1120, 78);
            Rect(hr, new Color(0f, 0f, 0f, 0.55f * a));
            Rect(new Rect(hr.x, hr.y, 8, hr.height), new Color(gc.r, gc.g, gc.b, a));
            Text(new Rect(hr.x + 20, hr.y, hr.width - 40, hr.height), hintText, 28, new Color(1f, 1f, 1f, a));
        }

        if (cpFlash > 0f) Text(new Rect(0, 270, W, 60), "CHECKPOINT", 44, new Color(1f, 1f, 1f, Mathf.Clamp01(cpFlash)));
        if (deniedFlash > 0f && !car.Wrecked) Text(new Rect(0, H * 0.5f + 60, W, 70), "NO BRAKES IN 4TH!  DOWNSHIFT [Q]", 46, new Color(1f, 0.5f, 0.1f, Mathf.Clamp01(deniedFlash * 2f)));
        if (car.Wrecked && state != State.Finished) Text(new Rect(0, H * 0.5f - 80, W, 120), "WRECKED", 110, new Color(1f, 0.15f, 0.2f));
        if (car.Gear == 2 && state == State.Racing && !car.Wrecked && car.transform.position.y > 14f)
            Text(new Rect(0, H * 0.5f - 160, W, 60), "NO CEILING HERE!  SHIFT DOWN!", 40, new Color(1f, 0.3f, 0.8f));

        if (state == State.Countdown)
        {
            int n = Mathf.CeilToInt(countdown);
            float frac = countdown - Mathf.Floor(countdown);
            Text(new Rect(0, H * 0.5f - 160, W, 260), n.ToString(), (int)(160 + 80 * frac), Color.white);
        }
        else if (state == State.Racing && raceTime < 1f)
        {
            Text(new Rect(0, H * 0.5f - 160, W, 260), "GO!", 180, new Color(1f, 1f, 1f, 1f - raceTime));
        }

        if (state == State.Finished) DrawFinish(W, H);

        Text(new Rect(30, H - 50, W - 60, 30),
            "W/S  throttle / brake    A/D  steer    E or SHIFT  gear up    Q  gear down    1-4  pick gear    R  reset    ESC  restart",
            18, new Color(1f, 1f, 1f, 0.45f), TextAnchor.MiddleLeft, false);
    }

    void DrawGearPanel(float W, float H)
    {
        float bx = W - 30 - 4 * 96, by = H - 240;
        float spd = Mathf.Abs(car.ForwardSpeed) * 3.6f;
        Text(new Rect(bx - 200, by - 120, 4 * 96 + 200, 100), $"{spd:0}<size=34> km/h</size>", 84, Color.white, TextAnchor.LowerRight);

        // speed bar with gear top-speed marker
        Rect sb = new Rect(bx, by - 14, 4 * 96 - 12, 8);
        Rect(sb, new Color(1f, 1f, 1f, 0.15f));
        Rect(new Rect(sb.x, sb.y, sb.width * Mathf.Clamp01(Mathf.Abs(car.ForwardSpeed) / CarController.TopSpeed[4]), sb.height), curColor);
        float mk = CarController.TopSpeed[car.Gear] / CarController.TopSpeed[4];
        Rect(new Rect(sb.x + sb.width * mk - 2, sb.y - 6, 4, 20), Color.white);

        for (int g = 1; g <= 4; g++)
        {
            Rect r = new Rect(bx + (g - 1) * 96, by, 84, 84);
            Color gc = GameAssets.GearColor(g);
            bool on = car.Gear == g;
            Rect(r, on ? new Color(gc.r, gc.g, gc.b, 0.95f) : new Color(0f, 0f, 0f, 0.5f));
            Rect(new Rect(r.x, r.yMax - 6, r.width, 6), gc);
            Text(r, g.ToString(), on ? 58 : 44, on ? Color.black : new Color(gc.r, gc.g, gc.b, 0.9f), TextAnchor.MiddleCenter, !on);
            Text(new Rect(r.x - 8, r.yMax + 2, r.width + 16, 26), CarController.GearName[g], 15, on ? Color.white : new Color(1f, 1f, 1f, 0.5f));
        }
        Text(new Rect(bx - 300, by + 118, 4 * 96 + 300, 34), CarController.GearDesc[car.Gear], 22, curColor, TextAnchor.MiddleRight);
    }

    void DrawTitle(float W, float H)
    {
        Rect(new Rect(0, 0, W, H), new Color(0.02f, 0f, 0.05f, 0.55f));
        float t = Time.time;
        // "SHIFT" with each letter cycling through gear colours
        string word = "SHIFT";
        float lx = W * 0.5f - 330;
        for (int i = 0; i < word.Length; i++)
        {
            Color c = GameAssets.GearColor(1 + (int)(t * 1.5f + i) % 4);
            Text(new Rect(lx + i * 120, 120, 140, 170), word[i].ToString(), 150, c);
        }
        Text(new Rect(0, 270, W, 110), "D R I V E", 80, Color.white);
        Text(new Rect(0, 380, W, 44), "A racing game where every gear changes the rules.", 30, new Color(1f, 1f, 1f, 0.8f));

        float y = 470;
        for (int g = 1; g <= 4; g++)
        {
            Color gc = GameAssets.GearColor(g);
            Rect r = new Rect(W * 0.5f - 420, y, 70, 70);
            Rect(r, gc);
            Text(r, g.ToString(), 50, Color.black, TextAnchor.MiddleCenter, false);
            Text(new Rect(r.xMax + 24, y, 300, 70), CarController.GearName[g], 38, gc, TextAnchor.MiddleLeft);
            Text(new Rect(r.xMax + 290, y, 520, 70), CarController.GearDesc[g], 26, Color.white, TextAnchor.MiddleLeft);
            y += 86;
        }

        float pulse = 0.55f + 0.45f * Mathf.Sin(t * 4f);
        Text(new Rect(0, 840, W, 60), "PRESS  ENTER / SPACE / (A)  TO RACE", 38, new Color(1f, 1f, 1f, pulse));
        Text(new Rect(0, 900, W, 40), "BEST  " + FormatTime(best) + $"      GOLD {GoldTime:0}s  ·  SILVER {SilverTime:0}s  ·  BRONZE {BronzeTime:0}s", 24, new Color(1f, 1f, 1f, 0.6f));
        Text(new Rect(0, 960, W, 40),
            "W/S throttle/brake  ·  A/D steer  ·  E or SHIFT gear up  ·  Q gear down  ·  1-4 pick gear  ·  R reset  ·  ESC restart  ·  Gamepad supported",
            20, new Color(1f, 1f, 1f, 0.5f));
        Text(new Rect(0, 1020, W, 40), "Made for GitHub Game Off 2026  -  theme: SHIFT", 18, new Color(1f, 1f, 1f, 0.35f));
    }

    void DrawFinish(float W, float H)
    {
        Rect(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, 0.55f));
        string medal = Medal(raceTime, out Color mc);
        Text(new Rect(0, 200, W, 140), "FINISH", 120, Color.white);
        Text(new Rect(0, 350, W, 120), FormatTime(raceTime), 110, mc);
        Text(new Rect(0, 470, W, 70), medal, 60, mc);
        if (newRecord) Text(new Rect(0, 545, W, 50), "NEW PERSONAL BEST!", 36, new Color(1f, 0.9f, 0.3f, 0.6f + 0.4f * Mathf.Sin(Time.time * 6f)));
        Text(new Rect(0, 610, W, 40), $"BEST {FormatTime(best)}     WRECKS {wrecks}     SHIFTS {car.ShiftCount}", 30, new Color(1f, 1f, 1f, 0.8f));
        Text(new Rect(0, 680, W, 40), $"GOLD {GoldTime:0}s  ·  SILVER {SilverTime:0}s  ·  BRONZE {BronzeTime:0}s", 24, new Color(1f, 1f, 1f, 0.55f));
        Text(new Rect(0, 780, W, 60), "ENTER / SPACE / (A) to race again     ESC for title", 32, new Color(1f, 1f, 1f, 0.6f + 0.4f * Mathf.Sin(Time.time * 4f)));
    }
}
