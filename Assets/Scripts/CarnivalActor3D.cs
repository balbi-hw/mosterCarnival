using UnityEngine;

public static class CarnivalActor3D
{
    static Material NewMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var material = new Material(shader);
        material.color = color;
        return material;
    }

    public static Transform Character(string name, bool human, Vector2 ground, float height,
        float radius, Color color)
    {
        var root = new GameObject(name);
        root.transform.position = new Vector3(ground.x, 0, ground.y);
        var source = Resources.Load<GameObject>("Carnival3D/character-" + (human ? "human" : "orc"));
        if (source != null)
        {
            var model = Object.Instantiate(source, root.transform);
            model.name = "3D model";
            NormalizeModel(model.transform, height);
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                var old = renderer.sharedMaterials;
                var materials = new Material[old.Length];
                for (int i = 0; i < old.Length; i++)
                {
                    Color original = old[i] != null && old[i].HasProperty("_Color") ? old[i].color : Color.white;
                    materials[i] = NewMaterial(Color.Lerp(original, color, .9f));
                }
                renderer.materials = materials;
            }
        }
        else
        {
            var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = "3D fallback";
            capsule.transform.SetParent(root.transform, false);
            capsule.transform.localPosition = Vector3.up * height * .5f;
            capsule.transform.localScale = new Vector3(radius * 2f, height * .5f, radius * 2f);
            capsule.GetComponent<Renderer>().material = NewMaterial(color);
            Object.Destroy(capsule.GetComponent<Collider>());
        }
        var collider = root.AddComponent<CapsuleCollider>();
        collider.radius = radius;
        collider.height = Mathf.Max(height, radius * 2f);
        collider.center = Vector3.up * (collider.height * .5f);
        return root.transform;
    }

    static void NormalizeModel(Transform model, float height)
    {
        var renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        if (bounds.size.y > .001f) model.localScale *= height / bounds.size.y;
        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        model.position += Vector3.up * (model.parent.position.y - bounds.min.y);
    }

    public static void Recolor(Transform root, Color color)
    {
        foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            foreach (var material in renderer.materials)
                if (material != null) material.color = color;
    }

    static Transform Rod(Transform parent, string name, Vector3 a, Vector3 b, float thickness, Color color)
    {
        var rod = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        rod.name = name;
        rod.transform.SetParent(parent, false);
        rod.transform.localPosition = (a + b) * .5f;
        rod.transform.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
        rod.transform.localScale = new Vector3(thickness, Vector3.Distance(a, b) * .5f, thickness);
        rod.GetComponent<Renderer>().material = NewMaterial(color);
        Object.Destroy(rod.GetComponent<Collider>());
        return rod.transform;
    }

    public static Transform Weapon(string name, Vector2 ground, int kind)
    {
        var root = new GameObject(name);
        root.transform.position = new Vector3(ground.x, .65f, ground.y);
        if (kind == 0)
        {
            var sword = Resources.Load<GameObject>("Carnival3D/weapon-sword");
            if (sword != null)
            {
                var model = Object.Instantiate(sword, root.transform);
                NormalizeModel(model.transform, .7f);
                foreach (var renderer in model.GetComponentsInChildren<Renderer>())
                    renderer.material = NewMaterial(new Color(.87f, .93f, 1f));
            }
            else Rod(root.transform, "Blade", Vector3.zero, Vector3.up * .7f, .08f, Color.white);
        }
        else if (kind == 1)
        {
            Color wood = new Color(.56f, .32f, .13f);
            Rod(root.transform, "Bow upper", new Vector3(0, 0, 0), new Vector3(.17f, .34f, 0), .035f, wood);
            Rod(root.transform, "Bow lower", new Vector3(.17f, .34f, 0), new Vector3(0, .68f, 0), .035f, wood);
            Rod(root.transform, "Bow string", Vector3.zero, new Vector3(0, .68f, 0), .01f, Color.white);
        }
        else
        {
            Rod(root.transform, "Wand", Vector3.zero, Vector3.up * .62f, .045f,
                new Color(.48f, .28f, .13f));
            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "Wand crystal";
            orb.transform.SetParent(root.transform, false);
            orb.transform.localPosition = Vector3.up * .66f;
            orb.transform.localScale = Vector3.one * .19f;
            orb.GetComponent<Renderer>().material = NewMaterial(new Color(.55f, .49f, 1f));
            Object.Destroy(orb.GetComponent<Collider>());
        }
        return root.transform;
    }

    public static Transform Projectile(string name, Vector2 ground, Vector2 velocity,
        bool arrow, bool hostile, float radius)
    {
        var root = new GameObject(name);
        root.transform.position = new Vector3(ground.x, .65f, ground.y);
        if (arrow)
        {
            Rod(root.transform, "Arrow shaft", new Vector3(0, 0, -.42f), new Vector3(0, 0, .42f),
                .055f, new Color(1f, .75f, .26f));
            var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tip.transform.SetParent(root.transform, false);
            tip.transform.localPosition = Vector3.forward * .47f;
            tip.transform.localScale = new Vector3(.17f, .17f, .23f);
            tip.GetComponent<Renderer>().material = NewMaterial(new Color(.92f, .96f, 1f));
            Object.Destroy(tip.GetComponent<Collider>());
        }
        else
        {
            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.transform.SetParent(root.transform, false);
            orb.transform.localScale = Vector3.one * radius * 2f;
            orb.GetComponent<Renderer>().material = NewMaterial(hostile ?
                new Color(1f, .3f, .23f) : new Color(.54f, .55f, 1f));
            Object.Destroy(orb.GetComponent<Collider>());
        }
        if (velocity.sqrMagnitude > .001f)
            root.transform.rotation = Quaternion.LookRotation(new Vector3(velocity.x, 0, velocity.y));
        return root.transform;
    }
}
