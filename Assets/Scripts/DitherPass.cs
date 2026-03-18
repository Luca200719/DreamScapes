using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering;

public class DitherPass : CustomPass {
    public Shader ditherShader;

    [Range(0f, 1f)]
    public float strength = 0.1f;

    [Range(2, 64)]
    public int levels = 16;

    [Range(0f, 1f)]
    public float noiseAmount = 0.5f;

    Material _material;

    protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd) {
        if (ditherShader == null)
            ditherShader = Shader.Find("FullScreen/BayerDither");
        _material = new Material(ditherShader);
    }

    protected override void Execute(CustomPassContext ctx) {
        if (_material == null) return;

        _material.SetFloat("_Strength", strength);
        _material.SetFloat("_Levels", levels);
        _material.SetFloat("_NoiseAmount", noiseAmount);

        CoreUtils.DrawFullScreen(ctx.cmd, _material, ctx.cameraColorBuffer, shaderPassId: 0);
    }

    protected override void Cleanup() {
        CoreUtils.Destroy(_material);
    }
}