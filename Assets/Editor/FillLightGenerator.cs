using UnityEngine;
using System.IO;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class IESGenerator : MonoBehaviour {
    [Header("Intensity")]
    public float peakIntensity = 1000f;

    [Header("Falloff")]
    [Range(0f, 175f)]
    public float falloffStartAngle = 85f;
    [Range(5f, 180f)]
    public float falloffEndAngle = 100f;
    [Range(0.1f, 5f)]
    public float falloffCurve = 2f;

    [Header("Slice Settings")]
    [Range(1, 12)]
    public int totalSlices = 6;

    [Header("Light Placement")]
    public float lightDistance = 20f;
    public float lightIntensity = 500f;
    public float lightRange = 40f;

    [Header("Output")]
    public string fileName = "HemisphericalFill";

    [ContextMenu("Generate All Slices")]
    public void GenerateAllSlices() {
        for (int i = 0; i < totalSlices; i++) {
            GenerateSliceIES(i);
        }
        Debug.Log("All " + totalSlices + " slices generated!");
    }

    [ContextMenu("Place Lights In Scene")]
    public void PlaceLightsInScene() {
#if UNITY_EDITOR
        GameObject existing = GameObject.Find("FillLights");
        if (existing != null) DestroyImmediate(existing);

        GameObject parent = new GameObject("FillLights");
        parent.transform.position = Vector3.zero;

        for (int i = 0; i < totalSlices; i++) {
            float angle = (360f / totalSlices) * i;
            float rad = angle * Mathf.Deg2Rad;
            Vector3 pos = new Vector3(
                Mathf.Sin(rad) * lightDistance,
                0f,
                Mathf.Cos(rad) * lightDistance
            );

            GameObject lightObj = new GameObject("FillLight_" + i);
            lightObj.transform.parent = parent.transform;
            lightObj.transform.position = pos;

            // Point toward planet center
            lightObj.transform.LookAt(Vector3.zero);

            Light light = lightObj.AddComponent<Light>();
            light.type = LightType.Spot;
            light.spotAngle = 170f;
            light.innerSpotAngle = 150f;
            light.intensity = lightIntensity;
            light.range = lightRange;
            light.shadows = LightShadows.None;

            // Get or add HD light data
            var hdLight = lightObj.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();
            if (hdLight == null)
                hdLight = lightObj.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();

            // Disable range attenuation
            var serializedObject = new UnityEditor.SerializedObject(hdLight);
            var rangeAttenuationProp = serializedObject.FindProperty("m_ApplyRangeAttenuation");
            if (rangeAttenuationProp != null) {
                rangeAttenuationProp.boolValue = false;
                serializedObject.ApplyModifiedProperties();
            }

            // Assign IES
            string iesPath = "Assets/" + fileName + "_slice" + i + ".ies";
            var iesAsset = AssetDatabase.LoadAssetAtPath<Texture>(iesPath);
            if (iesAsset != null) {
                var iesProperty = serializedObject.FindProperty("m_IESSpot");
                if (iesProperty != null) {
                    iesProperty.objectReferenceValue = iesAsset;
                    serializedObject.ApplyModifiedProperties();
                }
                else {
                    Debug.LogWarning("Could not find IES spot property for light " + i);
                }
            }
            else {
                Debug.LogWarning("IES not found at: " + iesPath + " — generate slices first!");
            }
        }

        Debug.Log("Placed " + totalSlices + " spot fill lights!");
#endif
    }

    private void GenerateSliceIES(int sliceIndex) {
        falloffEndAngle = Mathf.Max(falloffEndAngle, falloffStartAngle + 5f);

        float sliceWidth = 360f / totalSlices;
        float sliceCenter = sliceIndex * sliceWidth;
        float halfSlice = sliceWidth / 2f;
        int numHorizAngles = 73;

        System.Text.StringBuilder ies = new System.Text.StringBuilder();
        ies.AppendLine("IESNA:LM-63-2002");
        ies.AppendLine("[TITLE] Hemisphere Slice " + sliceIndex);
        ies.AppendLine("[MANUFAC] Custom");
        ies.AppendLine("TILT=NONE");
        ies.AppendLine($"1 1000.0 1.0 37 {numHorizAngles} 1 1 0.0 0.0 0.0");
        ies.AppendLine("1.0 1.0 0.0");

        // Vertical angles
        System.Text.StringBuilder vertAngles = new System.Text.StringBuilder();
        for (int i = 0; i < 37; i++) {
            vertAngles.Append((i * 5).ToString());
            if (i < 36) vertAngles.Append(" ");
        }
        ies.AppendLine(vertAngles.ToString());

        // Horizontal angles 0-360 in 5 degree steps
        System.Text.StringBuilder horizAngles = new System.Text.StringBuilder();
        for (int i = 0; i < numHorizAngles; i++) {
            horizAngles.Append((i * 5).ToString());
            if (i < numHorizAngles - 1) horizAngles.Append(" ");
        }
        ies.AppendLine(horizAngles.ToString());

        // Candela values for each horizontal angle
        for (int h = 0; h < numHorizAngles; h++) {
            float horizAngle = h * 5f;
            float angleDiff = Mathf.Abs(Mathf.DeltaAngle(horizAngle, sliceCenter));
            float horizIntensity;

            float edgeSoftness = 5f;
            if (angleDiff <= halfSlice - edgeSoftness)
                horizIntensity = 1f;
            else if (angleDiff <= halfSlice)
                horizIntensity = 1f - ((angleDiff - (halfSlice - edgeSoftness)) / edgeSoftness);
            else
                horizIntensity = 0f;

            System.Text.StringBuilder candela = new System.Text.StringBuilder();
            for (int v = 0; v < 37; v++) {
                float angle = v * 5f;
                float vertValue;

                if (angle <= falloffStartAngle)
                    vertValue = peakIntensity;
                else if (angle <= falloffEndAngle) {
                    float t = (angle - falloffStartAngle) /
                             (falloffEndAngle - falloffStartAngle);
                    vertValue = Mathf.Lerp(peakIntensity, 0f, Mathf.Pow(t, falloffCurve));
                }
                else
                    vertValue = 0f;

                candela.Append((vertValue * horizIntensity).ToString("F2"));
                if (v < 36) candela.Append(" ");
            }
            ies.AppendLine(candela.ToString());
        }

        string path = Application.dataPath + "/" + fileName + "_slice" + sliceIndex + ".ies";
        File.WriteAllText(path, ies.ToString());

#if UNITY_EDITOR
        AssetDatabase.Refresh();
#endif
    }
}