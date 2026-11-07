using System.Collections.Generic;
using UnityEngine;

public class TrackInfo
{
    public struct Hint { public float z; public string text; public int gear; }
    public List<float> checkpointZ = new List<float>();
    public List<Hint> hints = new List<Hint>();
    public float finishZ;
    public float length;
    public List<Vector2> openSky = new List<Vector2>();
}

/// Builds the whole course out of primitives. The course runs along +Z through a
/// tunnel: floor top at y = 0, ceiling underside at y = H. Lengths are in tiles of T metres.
public static class TrackBuilder
{
    public const float T = 18f, W = 18f, H = 12f;

    static Transform root;
    static Material floorMat, wallMat, stripMat, darkMat, hazardMat, warnMat, cpMat, finishMat;
    static Material[] gearMats;
    static TrackInfo info;

    public static TrackInfo Build(GameAssets a, Material floorRT, Material wallRT, Material stripRT)
    {
        root = new GameObject("Track").transform;
        floorMat = floorRT; wallMat = wallRT; stripMat = stripRT;
        darkMat = a.dark; hazardMat = a.hazard; warnMat = a.warning; cpMat = a.checkpoint; finishMat = a.finish;
        gearMats = a.gearMats;
        info = new TrackInfo();

        // ---------------- Sector 1: learn FLIP ----------------
        info.checkpointZ.Add(6f);
        Tunnel(0, 4);                                   // start straight
        StartLine(14f);
        Hint(1.2f, "FLOOR ENDS AHEAD  -  Shift up to 2nd [E] to drive on the CEILING", 2);
        Gate(4, 2);
        Tunnel(4, 3, floor: false);                     // floor gap, ceiling only
        Tunnel(7, 2);
        Hint(7.3f, "Barriers!  Flip between 1st [Q] and 2nd [E] to get past", 2);

        Tunnel(9, 8);                                   // zig-zag barriers
        FloorBarrier(9.6f);    Gate(9, 2);
        CeilingBarrier(12.6f); Gate(12, 1);
        FloorBarrier(15.6f);   Gate(15, 2);

        // ---------------- Sector 2: learn SLOW-MO ----------------
        Checkpoint(17);
        Tunnel(17, 2);
        Hint(17.4f, "CRUSHER HALL  -  Shift to 3rd to SLOW TIME", 3);
        Gate(19, 3);
        Tunnel(19, 8);
        CeilingPiston(19.6f, 0.00f);
        FloorPiston(20.6f, 0.30f);
        SpinnerAt(21.6f, 0f, 100f);
        CeilingPiston(22.6f, 0.55f);
        FloorPiston(23.4f, 0.15f);
        CeilingPiston(24.2f, 0.70f);
        SpinnerAt(25.2f, 45f, -110f);
        FloorPiston(26.2f, 0.45f);

        // ---------------- Sector 3: learn OVERDRIVE ----------------
        Checkpoint(27);
        Tunnel(27, 2);
        Hint(27.4f, "Shift to 4th: OVERDRIVE.  Max speed but NO BRAKES  -  hit the ramp flat out!", 4);
        Gate(29, 4);
        Tunnel(29, 9);                                  // runway
        Tunnel(38, 2, ceiling: false, walls: false);    // open sky + ramp
        Ramp(39);
        Hint(37.5f, "JUMP!", 4);
        info.openSky.Add(new Vector2(38 * T, 49 * T));
        Towers(34 * T, 52 * T, 1);
        // 40..44 chasm (nothing)
        Tunnel(45, 4, ceiling: false, walls: false);    // landing pad
        Tunnel(49, 8);
        Hint(46.5f, "4th has no brakes  -  DOWNSHIFT [Q] to slow down!", 1);

        // ---------------- Sector 4: everything ----------------
        Checkpoint(57);
        Tunnel(57, 3);
        Hint(57.4f, "FINAL SECTOR  -  ceiling slalom, then blades, then the big jump", 2);
        Gate(60, 2);
        Tunnel(60, 5, floor: false);                    // floor gap with ceiling slalom
        SlalomBlock(60.9f, -1);
        SlalomBlock(62.6f, 1);
        SlalomBlock(64.3f, -1);
        Gate(65, 3);
        Tunnel(65, 6);
        SpinnerAt(66.4f, 0f, 120f);
        CeilingPiston(67.4f, 0.2f);
        SpinnerAt(68.4f, 30f, -130f);
        FloorPiston(69.4f, 0.6f);
        Hint(70.5f, "Last jump  -  4th gear, full send!", 4);
        Gate(71, 4);
        Tunnel(71, 8);
        Tunnel(79, 1, ceiling: false, walls: false);
        Ramp(79);
        info.openSky.Add(new Vector2(79 * T, 97 * T));
        Towers(75 * T, 100 * T, 2);
        // 80..83 chasm
        Tunnel(84, 12, ceiling: false, walls: false);
        info.finishZ = 86f * T;
        FinishLine(info.finishZ);
        EndWall(96f * T);
        info.length = 96f * T;

        return info;
    }

    // ------------------------------------------------------------------ helpers

    static GameObject Box(string name, Vector3 c, Vector3 s, Material m, bool collider = true, Quaternion? rot = null)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.SetParent(root, false);
        g.transform.localPosition = c;
        g.transform.localRotation = rot ?? Quaternion.identity;
        g.transform.localScale = s;
        var r = g.GetComponent<Renderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (!collider) Object.Destroy(g.GetComponent<Collider>());
        return g;
    }

    static GameObject HazardBox(string name, Vector3 c, Vector3 s, Transform parent = null)
    {
        var g = Box(name, c, s, hazardMat);
        if (parent) g.transform.SetParent(parent, true);
        var bc = g.GetComponent<BoxCollider>();
        bc.isTrigger = true;
        bc.size = new Vector3(0.94f, 0.94f, 0.94f);
        g.AddComponent<Hazard>();
        return g;
    }

    static void Hint(float tile, string text, int gear) =>
        info.hints.Add(new TrackInfo.Hint { z = tile * T, text = text, gear = gear });

    static void Tunnel(int startTile, int count, bool floor = true, bool ceiling = true, bool walls = true)
    {
        for (int i = startTile; i < startTile + count; i++)
        {
            float zc = i * T + T * 0.5f;
            if (floor)
            {
                Box("Floor", new Vector3(0f, -0.5f, zc), new Vector3(W, 1f, T), floorMat);
                Box("EdgeL", new Vector3(-W * 0.5f + 0.2f, 0.03f, zc), new Vector3(0.25f, 0.06f, T), stripMat, false);
                Box("EdgeR", new Vector3(W * 0.5f - 0.2f, 0.03f, zc), new Vector3(0.25f, 0.06f, T), stripMat, false);
            }
            if (ceiling)
            {
                Box("Ceiling", new Vector3(0f, H + 0.5f, zc), new Vector3(W, 1f, T), floorMat);
                Box("CEdgeL", new Vector3(-W * 0.5f + 0.2f, H - 0.03f, zc), new Vector3(0.25f, 0.06f, T), stripMat, false);
                Box("CEdgeR", new Vector3(W * 0.5f - 0.2f, H - 0.03f, zc), new Vector3(0.25f, 0.06f, T), stripMat, false);
            }
            if (walls)
            {
                Box("WallL", new Vector3(-W * 0.5f - 0.5f, H * 0.5f, zc), new Vector3(1f, H, T), wallMat);
                Box("WallR", new Vector3(W * 0.5f + 0.5f, H * 0.5f, zc), new Vector3(1f, H, T), wallMat);
                // ribs give a strong sense of speed
                float z0 = i * T;
                Box("RibL", new Vector3(-W * 0.5f + 0.05f, H * 0.5f, z0), new Vector3(0.1f, H, 0.5f), stripMat, false);
                Box("RibR", new Vector3(W * 0.5f - 0.05f, H * 0.5f, z0), new Vector3(0.1f, H, 0.5f), stripMat, false);
                if (ceiling) Box("RibC", new Vector3(0f, H - 0.05f, z0), new Vector3(W, 0.1f, 0.5f), stripMat, false);
            }
            else if (floor)
            {
                // open-air section: small pylons along the edges
                float z0 = i * T;
                Box("PylonL", new Vector3(-W * 0.5f - 0.3f, 1.5f, z0), new Vector3(0.3f, 3f, 0.3f), stripMat, false);
                Box("PylonR", new Vector3(W * 0.5f + 0.3f, 1.5f, z0), new Vector3(0.3f, 3f, 0.3f), stripMat, false);
            }
        }
    }

    static void Gate(int tile, int gear)
    {
        float z = tile * T + 0.6f;
        var m = gearMats[gear - 1];
        Box("GatePostL", new Vector3(-W * 0.5f + 0.35f, H * 0.5f, z), new Vector3(0.5f, H, 0.5f), m, false);
        Box("GatePostR", new Vector3(W * 0.5f - 0.35f, H * 0.5f, z), new Vector3(0.5f, H, 0.5f), m, false);
        Box("GateTop", new Vector3(0f, H - 0.3f, z), new Vector3(W, 0.4f, 0.5f), m, false);
        Box("GateFloor", new Vector3(0f, 0.02f, z), new Vector3(W, 0.05f, 0.5f), m, false);
        // gear number "pips" so colour-blind players can read the gate too
        for (int i = 0; i < gear; i++)
        {
            float x = (i - (gear - 1) * 0.5f) * 1.4f;
            Box("Pip", new Vector3(x, H - 1.2f, z), new Vector3(0.8f, 0.8f, 0.2f), m, false);
        }
    }

    static void StartLine(float z)
    {
        for (int i = 0; i < 12; i++)
            for (int j = 0; j < 2; j++)
                if ((i + j) % 2 == 0)
                    Box("Start", new Vector3(-W * 0.5f + 0.75f + i * 1.5f, 0.02f, z + j * 1.5f), new Vector3(1.5f, 0.05f, 1.5f), finishMat, false);
    }

    static void FinishLine(float z)
    {
        for (int i = 0; i < 12; i++)
            for (int j = 0; j < 2; j++)
                if ((i + j) % 2 == 0)
                    Box("Finish", new Vector3(-W * 0.5f + 0.75f + i * 1.5f, 0.02f, z + j * 1.5f), new Vector3(1.5f, 0.05f, 1.5f), finishMat, false);
        Box("FinishPostL", new Vector3(-W * 0.5f - 0.5f, 5f, z), new Vector3(0.6f, 10f, 0.6f), finishMat, false);
        Box("FinishPostR", new Vector3(W * 0.5f + 0.5f, 5f, z), new Vector3(0.6f, 10f, 0.6f), finishMat, false);
        for (int i = 0; i < 10; i++)
            Box("FinishBanner", new Vector3(-W * 0.5f + 0.9f + i * 1.8f, 10f + (i % 2) * 0.6f, z), new Vector3(1.8f, 0.6f, 0.3f), i % 2 == 0 ? finishMat : darkMat, false);
    }

    static void Checkpoint(int tile)
    {
        float z = tile * T + T * 0.5f;
        info.checkpointZ.Add(z);
        Box("CPL", new Vector3(-W * 0.5f + 0.25f, H * 0.5f, z - 4f), new Vector3(0.3f, H, 0.3f), cpMat, false);
        Box("CPR", new Vector3(W * 0.5f - 0.25f, H * 0.5f, z - 4f), new Vector3(0.3f, H, 0.3f), cpMat, false);
        Box("CPT", new Vector3(0f, H - 0.15f, z - 4f), new Vector3(W, 0.3f, 0.3f), cpMat, false);
        Box("CPF", new Vector3(0f, 0.02f, z - 4f), new Vector3(W, 0.05f, 0.3f), cpMat, false);
    }

    static void FloorBarrier(float tile)
    {
        float z = tile * T;
        HazardBox("FloorBarrier", new Vector3(0f, 2.25f, z), new Vector3(W, 4.5f, 2f));
    }

    static void CeilingBarrier(float tile)
    {
        float z = tile * T;
        HazardBox("CeilingBarrier", new Vector3(0f, H - 2.25f, z), new Vector3(W, 4.5f, 2f));
    }

    static void SlalomBlock(float tile, int side)
    {
        float z = tile * T;
        const float w = W * 0.55f;
        HazardBox("Slalom", new Vector3(side * (W - w) * 0.5f, H - 1.6f, z), new Vector3(w, 3.2f, 3f));
    }

    static void CeilingPiston(float tile, float phase)
    {
        float z = tile * T;
        var g = HazardBox("CeilingPiston", new Vector3(0f, H * 0.5f, z), new Vector3(W, H, 4f));
        var p = g.AddComponent<Piston>();
        p.closedPos = new Vector3(0f, H * 0.5f, z);
        p.openPos = new Vector3(0f, H * 1.5f - 0.8f, z);   // peeks 0.8 m out of the ceiling
        p.phase = phase;
        p.period = 2.4f;
        g.transform.localPosition = p.openPos;
    }

    static void FloorPiston(float tile, float phase)
    {
        float z = tile * T;
        var g = HazardBox("FloorPiston", new Vector3(0f, H * 0.5f, z), new Vector3(W, H, 4f));
        var p = g.AddComponent<Piston>();
        p.closedPos = new Vector3(0f, H * 0.5f, z);
        p.openPos = new Vector3(0f, -H * 0.5f - 0.1f, z);  // fully hidden below the floor
        p.phase = phase;
        p.period = 2.4f;
        g.transform.localPosition = p.openPos;
        // warning plate on the floor
        Box("PistonWarn", new Vector3(0f, 0.025f, z), new Vector3(W - 1f, 0.05f, 4.4f), warnMat, false);
    }

    static void SpinnerAt(float tile, float offset, float speed)
    {
        float z = tile * T;
        var hub = new GameObject("Spinner");
        hub.transform.SetParent(root, false);
        hub.transform.localPosition = new Vector3(0f, H * 0.5f, z);
        var sp = hub.AddComponent<Spinner>();
        sp.offset = offset;
        sp.speed = speed;
        for (int k = 0; k < 2; k++)
        {
            var bar = HazardBox("Blade", Vector3.zero, new Vector3(17f, 1.1f, 1.1f));
            bar.transform.SetParent(hub.transform, false);
            bar.transform.localPosition = Vector3.zero;
            bar.transform.localRotation = Quaternion.Euler(0f, 0f, k * 90f);
        }
        var core = Box("SpinnerCore", Vector3.zero, new Vector3(2f, 2f, 1.6f), darkMat, false);
        core.transform.SetParent(hub.transform, false);
        core.transform.localPosition = Vector3.zero;
    }

    static void Ramp(int tile)
    {
        const float angle = 15f, len = 18f;
        float z0 = tile * T;
        float rad = angle * Mathf.Deg2Rad;
        Vector3 topMid = new Vector3(0f, Mathf.Sin(rad) * len * 0.5f, z0 + Mathf.Cos(rad) * len * 0.5f);
        Quaternion rot = Quaternion.Euler(-angle, 0f, 0f);
        Vector3 normal = rot * Vector3.up;
        Box("Ramp", topMid - normal * 0.5f, new Vector3(W, 1f, len), floorMat, true, rot);
        // glowing lip
        Vector3 lip = new Vector3(0f, Mathf.Sin(rad) * len, z0 + Mathf.Cos(rad) * len);
        Box("RampLip", lip, new Vector3(W, 0.12f, 0.4f), gearMats[3], false, rot);
        // support
        Box("RampSupport", new Vector3(0f, Mathf.Sin(rad) * len * 0.5f - 0.6f, lip.z - 0.6f), new Vector3(W, Mathf.Sin(rad) * len, 1f), darkMat, false);
    }

    /// Neon towers rising out of the void around the open-air sections.
    static void Towers(float z0, float z1, int seed)
    {
        var rng = new System.Random(seed * 7919);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        int n = (int)((z1 - z0) / 7f);
        for (int i = 0; i < n; i++)
        {
            float side = rng.Next(2) == 0 ? -1f : 1f;
            bool below = rng.NextDouble() < 0.25;
            float x = below ? R(-8f, 8f) : side * R(16f, 110f);
            float z = R(z0, z1);
            float top = below ? R(-70f, -25f) : R(-40f, 14f) - Mathf.Abs(x) * 0.08f;
            float h = R(60f, 160f);
            float w = R(3f, 9f);
            Box("Tower", new Vector3(x, top - h * 0.5f, z), new Vector3(w, h, w), darkMat, false);
            Box("TowerCap", new Vector3(x, top + 0.15f, z), new Vector3(w + 0.3f, 0.3f, w + 0.3f), stripMat, false);
            if (rng.NextDouble() < 0.5)
                Box("TowerBand", new Vector3(x, top - R(4f, 20f), z), new Vector3(w + 0.2f, 0.25f, w + 0.2f), stripMat, false);
        }
    }

    static void EndWall(float z)
    {
        Box("EndWall", new Vector3(0f, 5f, z), new Vector3(W + 4f, 10f, 1f), wallMat);
    }
}
