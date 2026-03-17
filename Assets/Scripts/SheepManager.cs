using System.Collections.Generic;
using UnityEngine;

public class SheepManager : MonoBehaviour {
    public GameObject planet;
    public GameObject sheepPrefab;
    public int initialSheepCount = 5;

    float _planetRadius;
    public List<Sheep> sheep = new();
    public float spawnAnimationDuration = 2f;

    void Awake() {
        _planetRadius = planet.GetComponent<Renderer>().bounds.extents.magnitude / Mathf.Sqrt(3f);
    }

    void Start() {
        for (int i = 0; i < initialSheepCount; i++)
            SpawnSheep(i, initialSheepCount);
    }

    public Sheep SpawnSheep(int index = 0, int total = 1) {
        GameObject go = Instantiate(sheepPrefab, Vector3.zero, Quaternion.identity);
        go.transform.SetParent(transform, worldPositionStays: true);

        Sheep s = go.GetComponent<Sheep>();
        s.planet = transform;
        s.planetRadius = _planetRadius;
        s.manager = this;

        if (total > 1)
            PlaceEvenly(s, index, total);

        sheep.Add(s);
        return s;
    }

    public void QueueSpawn(Vector3 worldPosition) {
        Sheep s = SpawnSheep();
        s.PlaceOnSphere(worldPosition);
    }

    void PlaceEvenly(Sheep s, int index, int total) {
        float goldenRatio = (1f + Mathf.Sqrt(5f)) / 2f;
        float theta = Mathf.Acos(1f - 2f * (index + 0.5f) / total);
        float phi = 2f * Mathf.PI * index / goldenRatio;

        Vector3 dir = new Vector3(
            Mathf.Sin(theta) * Mathf.Cos(phi),
            Mathf.Cos(theta),
            Mathf.Sin(theta) * Mathf.Sin(phi)
        );

        s.transform.position = dir * (_planetRadius + s.surfaceOffset);
    }

    void OnDrawGizmosSelected() {
        Gizmos.color = new Color(0f, 1f, 0.5f, 0.25f);
        Gizmos.DrawWireSphere(Vector3.zero, _planetRadius);
    }
}