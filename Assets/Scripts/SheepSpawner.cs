using UnityEngine;

public class SheepSpawner : MonoBehaviour {

    [Header("Sheep")]
    public GameObject sheepPrefab;
    public int initialSheepCount = 5;

    private float _planetRadius;
    private readonly System.Collections.Generic.List<Sheep> _activeSheep = new();

    void Awake() {
        _planetRadius = transform.GetComponent<Renderer>().bounds.extents.magnitude / Mathf.Sqrt(3f);
    }

    void Start() {
        for (int i = 0; i < initialSheepCount; i++)
            SpawnSheep(i, initialSheepCount);
    }

    public Sheep SpawnSheep(int index = 0, int total = 1) {
        GameObject go = Instantiate(sheepPrefab, Vector3.zero, Quaternion.identity);
        go.transform.SetParent(transform, worldPositionStays: true);

        Sheep sheep = go.GetComponent<Sheep>();

        sheep.planet = transform;
        sheep.planetRadius = _planetRadius;

        if (total > 1)
            PlaceEvenly(sheep, index, total);

        _activeSheep.Add(sheep);
        return sheep;
    }

    public void RemoveSheep() {
        if (_activeSheep.Count == 0) return;
        Sheep last = _activeSheep[^1];
        _activeSheep.RemoveAt(_activeSheep.Count - 1);
        Destroy(last.gameObject);
    }

    void PlaceEvenly(Sheep sheep, int index, int total) {
        float goldenRatio = (1f + Mathf.Sqrt(5f)) / 2f;
        float theta = Mathf.Acos(1f - 2f * (index + 0.5f) / total);
        float phi = 2f * Mathf.PI * index / goldenRatio;

        Vector3 dir = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));

        sheep.transform.position = dir * (_planetRadius + sheep.surfaceOffset);
    }

    void PlaceRandomly(Sheep sheep) {
        sheep.PlaceOnSphere(Random.onUnitSphere * 1000f);
    }

    void OnDrawGizmosSelected() {
        Gizmos.color = new Color(0f, 1f, 0.5f, 0.25f);
        Gizmos.DrawWireSphere(Vector3.zero, _planetRadius);
    }
}