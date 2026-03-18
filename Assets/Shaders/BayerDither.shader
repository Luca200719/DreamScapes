//TESTING PULLING

Shader "FullScreen/BayerDither"
{
    Properties
    {
        _Strength("Strength", Range(0, 0.2)) = 0.03
        _Levels("Levels", Range(2, 64)) = 16
        _NoiseAmount("Noise Amount", Range(0, 1)) = 0.5
    }

    HLSLINCLUDE

    #pragma vertex Vert
    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch

    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

    float _Strength;
    float _Levels;
    float _NoiseAmount;

    float Bayer8x8(uint2 pos)
    {
        static const float bayer[8][8] =
        {
            {  0.0/64.0, 32.0/64.0,  8.0/64.0, 40.0/64.0,  2.0/64.0, 34.0/64.0, 10.0/64.0, 42.0/64.0 },
            { 48.0/64.0, 16.0/64.0, 56.0/64.0, 24.0/64.0, 50.0/64.0, 18.0/64.0, 58.0/64.0, 26.0/64.0 },
            { 12.0/64.0, 44.0/64.0,  4.0/64.0, 36.0/64.0, 14.0/64.0, 46.0/64.0,  6.0/64.0, 38.0/64.0 },
            { 60.0/64.0, 28.0/64.0, 52.0/64.0, 20.0/64.0, 62.0/64.0, 30.0/64.0, 54.0/64.0, 22.0/64.0 },
            {  3.0/64.0, 35.0/64.0, 11.0/64.0, 43.0/64.0,  1.0/64.0, 33.0/64.0,  9.0/64.0, 41.0/64.0 },
            { 51.0/64.0, 19.0/64.0, 59.0/64.0, 27.0/64.0, 49.0/64.0, 17.0/64.0, 57.0/64.0, 25.0/64.0 },
            { 15.0/64.0, 47.0/64.0,  7.0/64.0, 39.0/64.0, 13.0/64.0, 45.0/64.0,  5.0/64.0, 37.0/64.0 },
            { 63.0/64.0, 31.0/64.0, 55.0/64.0, 23.0/64.0, 61.0/64.0, 29.0/64.0, 53.0/64.0, 21.0/64.0 }
        };
        return bayer[pos.x % 8][pos.y % 8];
    }

    float4 FullScreenPass(Varyings varyings) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);

        float depth = LoadCameraDepth(varyings.positionCS.xy);
        PositionInputs posInput = GetPositionInput(varyings.positionCS.xy, _ScreenSize.zw, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);

        float3 color = CustomPassSampleCameraColor(posInput.positionNDC.xy, 0);

        uint2 pixelPos  = uint2(floor(varyings.positionCS.xy));
        float bayer     = Bayer8x8(pixelPos);

        uint2 noisePos  = uint2(floor(varyings.positionCS.xy * (1080.0 / _ScreenSize.y)));
        uint hx = noisePos.x ^ (noisePos.y * 2749u) ^ asuint(_Time.y * 0.3);
        uint hy = noisePos.y ^ (noisePos.x * 1847u) ^ asuint(_Time.y * 0.7);
        hx = hx * 747796405u + 2891336453u; hx = ((hx >> 13) ^ hx) * 1274126177u;
        hy = hy * 747796405u + 2891336453u; hy = ((hy >> 13) ^ hy) * 1274126177u;
        float noise = float(hx ^ hy) / 4294967295.0;

        float threshold = lerp(bayer, noise, _NoiseAmount);

        // Scale strength by inverse luminance so bright areas get same perceptual impact
        float luma = dot(color, float3(0.2126, 0.7152, 0.0722));
        float perceptualStrength = _Strength * (1.0 + (3 * luma));

        float3 dithered  = color + (threshold - 0.5) * perceptualStrength;
        float3 quantized = floor(dithered * _Levels + 0.5) / _Levels;
        float3 result    = lerp(color, quantized, perceptualStrength);

        return float4(result, 1);
    }

    ENDHLSL

    SubShader
    {
        Tags{ "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Name "Custom Pass 0"
            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off

            HLSLPROGRAM
                #pragma fragment FullScreenPass
            ENDHLSL
        }
    }
    Fallback Off
}
