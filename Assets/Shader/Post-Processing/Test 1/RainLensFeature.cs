using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class RainLensFeature : ScriptableRendererFeature
{
	class RainPass : ScriptableRenderPass
	{
		public Material material;

		RTHandle source;
		RTHandle tempTexture;

		public void SetTarget(RTHandle colorHandle)
		{
			source = colorHandle;
		}

		public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
		{
			RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
			desc.depthBufferBits = 0;

			RenderingUtils.ReAllocateIfNeeded(ref tempTexture, desc);
		}

		public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
		{
			if (material == null) return;

			CommandBuffer cmd = CommandBufferPool.Get("RainLensPass");

			Blitter.BlitCameraTexture(cmd, source, tempTexture);
			Blitter.BlitCameraTexture(cmd, tempTexture, source, material, 0);

			context.ExecuteCommandBuffer(cmd);
			CommandBufferPool.Release(cmd);
		}
	}

	RainPass pass;
	public Material material;

	public override void Create()
	{
		pass = new RainPass();
		pass.renderPassEvent = RenderPassEvent.AfterRendering;
	}

	// IMPORTANT: NEW METHOD FOR URP 14+
	public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
	{
		if (material == null) return;

		pass.material = material;
		pass.SetTarget(renderer.cameraColorTargetHandle);
	}

	public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
	{
		if (material == null) return;

		renderer.EnqueuePass(pass);
	}
}