using UnityEngine;

public static class CarnivalArena3D
{
    public const float PillarRadius = 1.05f;
    public static readonly Vector2[] Pillars =
    {
        new Vector2(-8, -8), new Vector2(-8, 8),
        new Vector2(8, -8), new Vector2(8, 8)
    };

    public static Material[] MakePalette(Material source)
    {
        Color[] colors =
        {
            new Color(.38f, .47f, .51f),
            new Color(.32f, .40f, .45f),
            new Color(.14f, .19f, .22f),
            new Color(.28f, .35f, .37f),
            new Color(.57f, .63f, .59f),
            new Color(.69f, .52f, .28f)
        };
        Material[] result = new Material[colors.Length];
        for (int i = 0; i < result.Length; i++)
        {
            Shader shader = source != null ? source.shader : Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            result[i] = source != null ? new Material(source) : new Material(shader);
            result[i].name = "Carnival Arena " + i;
            if (result[i].HasProperty("_BaseColor")) result[i].SetColor("_BaseColor", colors[i]);
            if (result[i].HasProperty("_Color")) result[i].SetColor("_Color", colors[i]);
            if (result[i].HasProperty("_Smoothness")) result[i].SetFloat("_Smoothness", .1f);
        }
        return result;
    }

    public static GameObject Build(GameObject floor, Material[] palette)
    {
        GameObject existing = GameObject.Find("Arena 3D");
        if (existing != null) return existing;
        var root = new GameObject("Arena 3D");
        if (floor != null)
        {
            floor.transform.position = new Vector3(0, -.24f, 0);
            floor.transform.localScale = new Vector3(31, .35f, 31);
            floor.GetComponent<Renderer>().sharedMaterial = palette[2];
        }

        // Real stone slabs expose side faces and subtle height variation.
        for (int x = -14; x <= 14; x += 2)
            for (int z = -14; z <= 14; z += 2)
            {
                bool light = ((x + z + 28) / 2) % 3 == 0;
                float rise = ((x * x + z * z) % 12 == 0) ? .015f : 0f;
                Piece(root.transform, PrimitiveType.Cube, "Stone slab",
                    new Vector3(x, -.145f + rise, z), new Vector3(1.94f, .22f, 1.94f),
                    palette[light ? 0 : 1], false);
            }

        for (int p = -12; p <= 12; p += 4)
        {
            Wall(root.transform, "North wall", new Vector3(p, 0, 15.35f),
                new Vector3(3.9f, 1.35f, .72f), palette);
            Wall(root.transform, "East wall", new Vector3(15.35f, 0, p),
                new Vector3(.72f, 1.35f, 3.9f), palette);
            Wall(root.transform, "South wall", new Vector3(p, 0, -15.35f),
                new Vector3(3.9f, .72f, .72f), palette);
            Wall(root.transform, "West wall", new Vector3(-15.35f, 0, p),
                new Vector3(.72f, .72f, 3.9f), palette);
        }

        foreach (Vector2 point in Pillars)
        {
            var place = new Vector3(point.x, 0, point.y);
            Piece(root.transform, PrimitiveType.Cube, "Pillar base", place + Vector3.up * .18f,
                new Vector3(1.8f, .42f, 1.8f), palette[3], true);
            Piece(root.transform, PrimitiveType.Cylinder, "Stone pillar", place + Vector3.up * 1.23f,
                new Vector3(.63f, 1.03f, .63f), palette[3], true);
            Piece(root.transform, PrimitiveType.Cube, "Pillar capital", place + Vector3.up * 2.24f,
                new Vector3(1.5f, .27f, 1.5f), palette[4], true);
            Piece(root.transform, PrimitiveType.Cube, "Pillar ornament", place + Vector3.up * 1.82f,
                new Vector3(1.28f, .12f, 1.28f), palette[5], false);
        }
        return root;
    }

    static void Wall(Transform parent, string name, Vector3 position, Vector3 size, Material[] palette)
    {
        Vector3 center = position + Vector3.up * (size.y * .5f - .05f);
        Piece(parent, PrimitiveType.Cube, name, center, size, palette[3], true);
        Vector3 capSize = new Vector3(size.x + .12f, .12f, size.z + .12f);
        Piece(parent, PrimitiveType.Cube, name + " cap", center + Vector3.up * (size.y * .5f + .05f),
            capSize, palette[4], false);
    }

    static void Piece(Transform parent, PrimitiveType type, string name, Vector3 position,
        Vector3 scale, Material material, bool solid)
    {
        var piece = GameObject.CreatePrimitive(type);
        piece.name = name;
        piece.transform.SetParent(parent, false);
        piece.transform.position = position;
        piece.transform.localScale = scale;
        piece.GetComponent<Renderer>().sharedMaterial = material;
        piece.isStatic = true;
        if (!solid)
        {
            Collider collider = piece.GetComponent<Collider>();
            if (Application.isPlaying) Object.Destroy(collider);
            else Object.DestroyImmediate(collider);
        }
    }
}
