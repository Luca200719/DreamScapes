using UnityEngine;

public class DayNightCycle : MonoBehaviour {
    public float dayDuration = 120f;

    float _timeOfDay = 0f;
    int _dayCount = 0;

    public float TimeOfDay => _timeOfDay;
    public int DayCount => _dayCount;
    public bool IsDay => _timeOfDay < 0.5f;

    public Gradient skyboxGradient;
    public MeshRenderer skyboxRenderer;
    Material _skyboxMat;

    public Gradient grassGradient;
    public Gradient litGradient;
    public MeshRenderer planetRenderer;
    Material _planetMat;

    public GameObject sunLight;
    Light _sunLight;

    public GameObject sunMesh;
    Material _sunMat;
    
    public Transform fillLightsContainer;
    Light[] fillLights;
    public Transform complementLightsContainer;
    Light[] complementLights;

    static readonly int BaseColor = Shader.PropertyToID("_Base_Color");
    static readonly int StarBrightness = Shader.PropertyToID("_Star_Brightness");
    static readonly int SkyboxColor = Shader.PropertyToID("_Skybox_Color");
    static readonly int GrassColor = Shader.PropertyToID("_Grass_Color");
    static readonly int LitColor = Shader.PropertyToID("_Lit_Color");
    static readonly int SunBaseColor = Shader.PropertyToID("_Base_Color");
    static readonly int SunIntensity = Shader.PropertyToID("_Intensity");

    void Start() {
        _skyboxMat = skyboxRenderer.material;
        _planetMat = planetRenderer.material;

        _sunLight = sunLight.GetComponent<Light>();

        _sunMat = sunMesh.GetComponent<Renderer>().material;

        fillLights = fillLightsContainer.GetComponentsInChildren<Light>();
        complementLights = complementLightsContainer.GetComponentsInChildren<Light>();
    }

    void Update() {
        _timeOfDay += Time.deltaTime / dayDuration;

        if (_timeOfDay >= 1f) {
            _timeOfDay -= 1f;
            _dayCount++;
        }

        float lightAngle = Mathf.Sin(_timeOfDay * Mathf.PI);
        _sunLight.intensity = Mathf.Max(3000f, lightAngle * 15000f);
        _sunLight.colorTemperature = 11000f - lightAngle * 5500f;

        transform.rotation = Quaternion.Euler(_timeOfDay * 360f - 122.02f, -30f, 0f);

        Color skyboxColor = skyboxGradient.Evaluate(_timeOfDay);
        Color litColor = litGradient.Evaluate(_timeOfDay);

        _skyboxMat.SetColor(BaseColor, skyboxColor);
        _skyboxMat.SetFloat(StarBrightness, 1 - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((lightAngle - _timeOfDay * 0.25f) * 1.7f)));
        _planetMat.SetColor(SkyboxColor, skyboxColor);
        _planetMat.SetColor(GrassColor, grassGradient.Evaluate(_timeOfDay));
        _planetMat.SetColor(LitColor, litColor);

        float sunStep = Mathf.SmoothStep(0f, 1f, lightAngle);

        sunMesh.transform.localScale = Vector3.one * (25f + sunStep * 22.5f);

        _sunMat.SetColor(SunBaseColor, litColor);
        _sunMat.SetFloat(SunIntensity, 40000f + sunStep * 40000f);


        Color complementColor = Color.Lerp(litColor, Color.grey, 0.7f) * 2f;

        foreach (Light light in fillLights) {
            light.color = skyboxColor * (1f - sunStep * 0.5f);
        }

        foreach (Light light in complementLights) {
            light.color = complementColor;
        }
    }
}