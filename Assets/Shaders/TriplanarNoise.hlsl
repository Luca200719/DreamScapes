float2 hash2D(float2 p) {
    p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
    return frac(sin(p) * 43758.5453123) * 2.0 - 1.0;
}

float gradientNoise(float2 p) {
    float2 ip = floor(p);
    float2 fp = frac(p);
    float2 u  = fp * fp * fp * (fp * (fp * 6.0 - 15.0) + 10.0);

    float a = dot(hash2D(ip + float2(0, 0)), fp - float2(0, 0));
    float b = dot(hash2D(ip + float2(1, 0)), fp - float2(1, 0));
    float c = dot(hash2D(ip + float2(0, 1)), fp - float2(0, 1));
    float d = dot(hash2D(ip + float2(1, 1)), fp - float2(1, 1));

    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

float3 noiseNormal(float2 p, float strength) {
    const float eps = 0.005;
    float h0 = gradientNoise(p);
    float hx = gradientNoise(p + float2(eps, 0));
    float hy = gradientNoise(p + float2(0, eps));
    return normalize(float3((hx - h0) / eps * strength, (hy - h0) / eps * strength, 1.0));
}

float3 whiteout(float3 n, float3 b)
{
    return normalize(float3(n.xy + b.xy, n.z * b.z));
}

void TriplanarGradientNoise_float(float3 WorldPosition, float3 WorldNormal, float Scale, float BlendSharpness, float NormalStrength, out float3 Out)
{
    float3 pos = WorldPosition * Scale;
    float3 N = normalize(WorldNormal);
    
    float3 tnX = noiseNormal(pos.yz, NormalStrength);
    float3 tnY = noiseNormal(pos.xz, NormalStrength);
    float3 tnZ = noiseNormal(pos.xy, NormalStrength);
    
    float3 blend = pow(max(abs(N) - 0.05, 0.0), BlendSharpness);
    blend /= max(blend.x + blend.y + blend.z, 0.0001);
    
    float3 nX = whiteout(tnX.xyz, N.yzx).zxy;
    
    float3 nY = whiteout(tnY.xyz, N.xzy).xzy;
    
    float3 nZ = whiteout(tnZ.xyz, N.xyz);
    
    Out = normalize(nX * blend.x + nY * blend.y + nZ * blend.z);
}

float3 VoronoiCell3D(float3 p) {
    return frac(sin(float3(dot(p, float3(127.1, 311.7, 74.7)), dot(p, float3(269.5, 183.3, 246.1)), dot(p, float3(113.5, 271.9, 124.6)))) * 43758.5453);
}

float Voronoi3D(float3 p) {
    float3 cell = floor(p);
    float3 local = frac(p);
    float minDist = 1.0;

    for (int z = -1; z <= 1; z++) {
        for (int y = -1; y <= 1; y++) {
            for (int x = -1; x <= 1; x++) {
                float3 neighbor = float3(x, y, z);
                float3 featurePoint = VoronoiCell3D(cell + neighbor) + neighbor;
                float3 diff = featurePoint - local;
                float dist = length(diff);
                minDist = min(minDist, dist);
            }
        }
    }

    return minDist;
}

void TriplanarVoronoiNoise_float(float3 WorldPos, float3 WorldNormal, float Scale, out float Out) {
    Out = Voronoi3D(WorldPos * Scale);
}