using UnityEngine;

public static class CarnivalSkillVisuals
{
    public static void ShowSector(string name, Vector2 origin, Vector2 direction,
        float reach, float degrees, Color color, float duration)
    {
        var source = Resources.Load<Material>("CarnivalGround");
        if (source == null) return;
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        const int steps = 24;
        var vertices = new Vector3[steps + 2];
        var triangles = new int[steps * 3];
        for (int i = 0; i <= steps; i++)
        {
            float angle = Mathf.Deg2Rad * (-degrees * .5f + degrees * i / steps);
            vertices[i + 1] = new Vector3(Mathf.Sin(angle) * reach, 0, Mathf.Cos(angle) * reach);
            if (i == steps) continue;
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = i + 2;
        }
        var mesh = new Mesh { vertices = vertices, triangles = triangles };
        mesh.RecalculateNormals();
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var material = new Material(source);
        material.SetFloat("_Surface", 1);
        material.SetFloat("_Blend", 0);
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        material.color = color;
        go.GetComponent<MeshRenderer>().material = material;
        go.transform.position = new Vector3(origin.x, .05f, origin.y);
        go.transform.rotation = Quaternion.LookRotation(new Vector3(direction.x, 0, direction.y));
        Object.Destroy(go, duration);
        Object.Destroy(mesh, duration);
        Object.Destroy(material, duration);
    }
}
