using UnityEngine;
using UnityEditor;
using System.IO;

public static class SphereMaskGenerator {
    const int Resolution = 64;
    const string SavePath = "Assets/Textures/SphereMask.asset";

    [MenuItem("Tools/Generate Sphere Fog Mask")]
    public static void Generate() {
        int res = Resolution;

        Texture3D tex = new Texture3D(res, res, res, TextureFormat.R8, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Color[] voxels = new Color[res * res * res];

        for (int z = 0; z < res; z++)
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++) {
                    float nx = (x / (res - 1f)) * 2f - 1f;
                    float ny = (y / (res - 1f)) * 2f - 1f;
                    float nz = (z / (res - 1f)) * 2f - 1f;

                    float dist = Mathf.Sqrt(nx * nx + ny * ny + nz * nz);

                    float density = Mathf.Clamp01(1f - dist);

                    density = density * density * density * (density * (density * 6f - 15f) + 10f);

                    voxels[x + y * res + z * res * res] = new Color(density, density, density, density);
                }

        tex.SetPixels(voxels);
        tex.Apply();

        string dir = Path.GetDirectoryName(SavePath);
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        AssetDatabase.CreateAsset(tex, SavePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Sphere fog mask saved to {SavePath}");
        EditorUtility.FocusProjectWindow();
        Selection.activeObject = tex;
    }
}