using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_2023_3_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif

namespace LiquidVolumeFX {

    public class LiquidVolumeDepthPrePassRenderFeature : ScriptableRendererFeature {

        static class ShaderParams {
            public const string RTBackBufferName = "_VLBackBufferTexture";
            public static int RTBackBuffer = Shader.PropertyToID(RTBackBufferName);
            public const string RTFrontBufferName = "_VLFrontBufferTexture";
            public static int RTFrontBuffer = Shader.PropertyToID(RTFrontBufferName);
            public static int FlaskThickness = Shader.PropertyToID("_FlaskThickness");
            public static int ForcedInvisible = Shader.PropertyToID("_LVForcedInvisible");
            public const string SKW_FP_RENDER_TEXTURE = "LIQUID_VOLUME_FP_RENDER_TEXTURES";

            public static int[] idSHA = {
                Shader.PropertyToID("unity_SHAr"),
                Shader.PropertyToID("unity_SHAg"),
                Shader.PropertyToID("unity_SHAb")
            };

            public static int[] idSHB = {
                Shader.PropertyToID("unity_SHBr"),
                Shader.PropertyToID("unity_SHBg"),
                Shader.PropertyToID("unity_SHBb")
            };

            public static int idSHC = Shader.PropertyToID("unity_SHC");
        }

        enum Pass {
            BackBuffer = 0,
            FrontBuffer = 1
        }

        public readonly static List<LiquidVolume> lvBackRenderers = new List<LiquidVolume>();
        public readonly static List<LiquidVolume> lvFrontRenderers = new List<LiquidVolume>();

        public static void AddLiquidToBackRenderers (LiquidVolume lv) {
            if (lv == null || lv.topology != TOPOLOGY.Irregular || lvBackRenderers.Contains(lv)) return;
            lvBackRenderers.Add(lv);
        }

        public static void RemoveLiquidFromBackRenderers (LiquidVolume lv) {
            if (lv == null || !lvBackRenderers.Contains(lv)) return;
            lvBackRenderers.Remove(lv);
        }

        public static void AddLiquidToFrontRenderers (LiquidVolume lv) {
            if (lv == null || lv.topology != TOPOLOGY.Irregular || lvFrontRenderers.Contains(lv)) return;
            lvFrontRenderers.Add(lv);
        }

        public static void RemoveLiquidFromFrontRenderers (LiquidVolume lv) {
            if (lv == null || !lvFrontRenderers.Contains(lv)) return;
            lvFrontRenderers.Remove(lv);
        }

        class DepthPass : ScriptableRenderPass {

            const string profilerTag = "LiquidVolumeDepthPrePass";

            Material mat;
            int targetNameId;
            RTHandle targetRT;
            int passId;
            List<LiquidVolume> lvRenderers;
            public ScriptableRenderer renderer;
            public bool interleavedRendering;

            class PassData {
                public Camera cam;
                public CommandBuffer cmd;
                public DepthPass depthPass;
                public Material mat;
#if UNITY_2022_1_OR_NEWER
                public RTHandle source, depth;
#else
                public RenderTargetIdentifier source, depth;
#endif
                public RenderTextureDescriptor cameraTargetDescriptor;
#if UNITY_2023_3_OR_NEWER
                public TextureHandle colorTexture, depthTexture;
#endif
            }

            class LiquidVolumeDistanceComparer : IComparer<LiquidVolume> {

                public Vector3 currentCameraPosition;

                public int Compare (LiquidVolume lv1, LiquidVolume lv2) {
                    bool isNull1 = lv1 == null;
                    bool isNull2 = lv2 == null;
                    if (isNull1 && isNull2) return 0;
                    if (isNull2) return 1;
                    if (isNull1) return -1;
                    float dist1 = Vector3.Distance(lv1.transform.position, currentCameraPosition);
                    float dist2 = Vector3.Distance(lv2.transform.position, currentCameraPosition);
                    if (dist1 < dist2) return 1;
                    if (dist1 > dist2) return -1;
                    return 0;
                }
            }

            readonly PassData passData = new PassData();
            static readonly LiquidVolumeDistanceComparer distanceComparer = new LiquidVolumeDistanceComparer();

            public DepthPass (Material mat, Pass pass, RenderPassEvent renderPassEvent) {
                this.renderPassEvent = renderPassEvent;
                this.mat = mat;
                passData.depthPass = this;
                switch (pass) {
                    case Pass.BackBuffer: {
                            targetNameId = ShaderParams.RTBackBuffer;
                            RenderTargetIdentifier rt = new RenderTargetIdentifier(targetNameId, 0, CubemapFace.Unknown, -1);
                            targetRT = RTHandles.Alloc(rt, name: ShaderParams.RTBackBufferName);
                            passId = (int)Pass.BackBuffer;
                            lvRenderers = lvBackRenderers;
                            break;
                        }
                    case Pass.FrontBuffer: {
                            targetNameId = ShaderParams.RTFrontBuffer;
                            RenderTargetIdentifier rt = new RenderTargetIdentifier(targetNameId, 0, CubemapFace.Unknown, -1);
                            targetRT = RTHandles.Alloc(rt, name: ShaderParams.RTFrontBufferName);
                            passId = (int)Pass.FrontBuffer;
                            lvRenderers = lvFrontRenderers;
                            break;
                        }
                }
            }

            public void Setup (LiquidVolumeDepthPrePassRenderFeature feature, ScriptableRenderer renderer) {
                this.renderer = renderer;
                this.interleavedRendering = feature.interleavedRendering;
            }


#if !UNITY_6000_4_OR_NEWER
#if UNITY_2023_3_OR_NEWER
            [Obsolete]
#endif
            public override void Configure (CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor) {
                cameraTextureDescriptor.colorFormat = LiquidVolume.useFPRenderTextures ? RenderTextureFormat.RHalf : RenderTextureFormat.ARGB32;
                cameraTextureDescriptor.sRGB = false;
                cameraTextureDescriptor.depthBufferBits = 16;
                cameraTextureDescriptor.msaaSamples = 1;
                cmd.GetTemporaryRT(targetNameId, cameraTextureDescriptor);
                if (!interleavedRendering) {
                    ConfigureTarget(targetRT);
                }
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }
#endif

#if !UNITY_6000_4_OR_NEWER
#if UNITY_2023_3_OR_NEWER
            [Obsolete]
#endif
            public override void Execute (ScriptableRenderContext context, ref RenderingData renderingData) {

                if (lvRenderers == null) return;

                CommandBuffer cmd = CommandBufferPool.Get(profilerTag);
                cmd.Clear();

                passData.cam = renderingData.cameraData.camera;
                passData.cmd = cmd;
                passData.mat = mat;
                passData.cameraTargetDescriptor = renderingData.cameraData.cameraTargetDescriptor;
#if UNITY_2022_1_OR_NEWER
                passData.source = renderer.cameraColorTargetHandle;
                passData.depth = renderer.cameraDepthTargetHandle;
#else
                passData.source = renderer.cameraColorTarget;
                passData.depth = renderer.cameraDepthTarget;
#endif
                if (passData.cam.cameraType != CameraType.Preview) {
                    ExecutePass(passData);
                }
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
#endif


#if UNITY_2023_3_OR_NEWER
            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {

                if (lvRenderers == null) return;

                using (var builder = renderGraph.AddUnsafePass<PassData>(profilerTag, out var passData)) {
                    builder.AllowPassCulling(false);

                    UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                    UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                    passData.cam = cameraData.camera;
                    passData.mat = mat;
                    passData.depthPass = this;
                    passData.colorTexture = resourceData.activeColorTexture;
                    passData.depthTexture = resourceData.activeDepthTexture;
                    passData.cameraTargetDescriptor = cameraData.cameraTargetDescriptor;
                    if (interleavedRendering) {
                        builder.UseTexture(resourceData.activeColorTexture, AccessFlags.ReadWrite);
                        builder.UseTexture(resourceData.activeDepthTexture, AccessFlags.Read);
                    }

                    builder.SetRenderFunc(static (PassData passData, UnsafeGraphContext context) => {
                        CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        passData.cmd = cmd;
                        passData.source = passData.colorTexture;
                        passData.depth = passData.depthTexture;
                        ExecutePass(passData);
                    });
                }
            }
#endif
            static void SetSHCoefficients (Vector3 position, Renderer renderer, CommandBuffer cmd) {
                SphericalHarmonicsL2 sh;
                LightProbes.GetInterpolatedProbe(position, null, out sh);

                // Constant + Linear
                for (var i = 0; i < 3; i++) {
                    cmd.SetGlobalVector(ShaderParams.idSHA[i], new Vector4(sh[i, 3], sh[i, 1], sh[i, 2], sh[i, 0] - sh[i, 6]));
                }

                // Quadratic polynomials
                for (var i = 0; i < 3; i++) {
                    cmd.SetGlobalVector(ShaderParams.idSHB[i], new Vector4(sh[i, 4], sh[i, 6], sh[i, 5] * 3, sh[i, 7]));
                }

                // Final quadratic polynomial
                cmd.SetGlobalVector(ShaderParams.idSHC, new Vector4(sh[0, 8], sh[2, 8], sh[1, 8], 1));
            }



            static void ExecutePass (PassData passData) {

                CommandBuffer cmd = passData.cmd;

                cmd.SetGlobalFloat(ShaderParams.ForcedInvisible, 0);

                Camera cam = passData.cam;
                DepthPass depthPass = passData.depthPass;

                RenderTextureDescriptor desc = passData.cameraTargetDescriptor;
                desc.colorFormat = LiquidVolume.useFPRenderTextures ? RenderTextureFormat.RHalf : RenderTextureFormat.ARGB32;
                desc.sRGB = false;
                desc.depthBufferBits = 16;
                desc.msaaSamples = 1;
                cmd.GetTemporaryRT(depthPass.targetNameId, desc);

                int lvRenderersCount = depthPass.lvRenderers.Count;

                if (depthPass.interleavedRendering) {

                    distanceComparer.currentCameraPosition = cam.transform.position;
                    depthPass.lvRenderers.Sort(distanceComparer);

                    RenderTargetIdentifier rti = new RenderTargetIdentifier(depthPass.targetNameId, 0, CubemapFace.Unknown, -1);
                    cmd.SetGlobalTexture(depthPass.targetNameId, rti);

                    for (int k = 0; k < lvRenderersCount; k++) {
                        LiquidVolume lv = depthPass.lvRenderers[k];
                        if (lv != null && lv.gameObject.activeInHierarchy) {
                            if (lv.topology == TOPOLOGY.Irregular) {
                                cmd.SetRenderTarget(rti, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                                if (LiquidVolume.useFPRenderTextures) {
                                    cmd.ClearRenderTarget(true, true, new Color(cam.farClipPlane, 0, 0, 0), 1f);
                                    cmd.EnableShaderKeyword(ShaderParams.SKW_FP_RENDER_TEXTURE);
                                }
                                else {
                                    cmd.ClearRenderTarget(true, true, new Color(0.9882353f, 0.4470558f, 0.75f, 0f), 1f);
                                    cmd.DisableShaderKeyword(ShaderParams.SKW_FP_RENDER_TEXTURE);
                                }
                                cmd.SetGlobalFloat(ShaderParams.FlaskThickness, 1.0f - lv.flaskThickness);
                                // draw back face
                                cmd.DrawRenderer(lv.mr, passData.mat, lv.subMeshIndex >= 0 ? lv.subMeshIndex : 0, depthPass.passId);
                            }
                            // draw liquid
                            RenderTargetIdentifier rtiColor = new RenderTargetIdentifier(passData.source, 0, CubemapFace.Unknown, -1);
                            RenderTargetIdentifier rtiDepth = new RenderTargetIdentifier(passData.depth, 0, CubemapFace.Unknown, -1);
                            cmd.SetRenderTarget(rtiColor, rtiDepth);
                            SetSHCoefficients(lv.transform.position, lv.mr, cmd);
                            cmd.DrawRenderer(lv.mr, lv.liqMat, lv.subMeshIndex >= 0 ? lv.subMeshIndex : 0, shaderPass: 1);
                        }
                    }
                    cmd.SetGlobalFloat(ShaderParams.ForcedInvisible, 1);
                }
                else {
                    RenderTargetIdentifier rti = new RenderTargetIdentifier(depthPass.targetNameId, 0, CubemapFace.Unknown, -1);
                    cmd.SetRenderTarget(rti);
                    cmd.SetGlobalTexture(depthPass.targetNameId, rti);

                    // accumulate back face depths into custom rt
                    if (LiquidVolume.useFPRenderTextures) {
                        cmd.ClearRenderTarget(true, true, new Color(cam.farClipPlane, 0, 0, 0), 1f);
                        cmd.EnableShaderKeyword(ShaderParams.SKW_FP_RENDER_TEXTURE);
                    }
                    else {
                        cmd.ClearRenderTarget(true, true, new Color(0.9882353f, 0.4470558f, 0.75f, 0f), 1f);
                        cmd.DisableShaderKeyword(ShaderParams.SKW_FP_RENDER_TEXTURE);
                    }

                    for (int k = 0; k < lvRenderersCount; k++) {
                        LiquidVolume lv = depthPass.lvRenderers[k];
                        if (lv != null && lv.gameObject.activeInHierarchy) {
                            cmd.SetGlobalFloat(ShaderParams.FlaskThickness, 1.0f - lv.flaskThickness);
                            cmd.DrawRenderer(lv.mr, passData.mat, lv.subMeshIndex >= 0 ? lv.subMeshIndex : 0, depthPass.passId);
                        }
                    }
                }
            }

            public void CleanUp () {
                RTHandles.Release(targetRT);
            }
        }


        [SerializeField, HideInInspector]
        Shader shader;

        public static bool installed;
        Material mat;
        DepthPass backPass, frontPass;

        [Tooltip("Renders each irregular liquid volume completely before rendering the next one.")]
        public bool interleavedRendering;

        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;

        private void OnDestroy () {
            Shader.SetGlobalFloat(ShaderParams.ForcedInvisible, 0);
            CoreUtils.Destroy(mat);
            if (backPass != null) {
                backPass.CleanUp();
            }
            if (frontPass != null) {
                frontPass.CleanUp();
            }
        }

        public override void Create () {
            name = "Liquid Volume Depth PrePass";
            shader = Shader.Find("LiquidVolume/DepthPrePass");
            if (shader == null) {
                return;
            }
            mat = CoreUtils.CreateEngineMaterial(shader);
            backPass = new DepthPass(mat, Pass.BackBuffer, renderPassEvent);
            frontPass = new DepthPass(mat, Pass.FrontBuffer, renderPassEvent);
        }

        // This method is called when setting up the renderer once per-camera.
        public override void AddRenderPasses (ScriptableRenderer renderer, ref RenderingData renderingData) {
            installed = true;
            if (backPass != null && lvBackRenderers.Count > 0) {
                backPass.Setup(this, renderer);
                renderer.EnqueuePass(backPass);
            }
            if (frontPass != null && lvFrontRenderers.Count > 0) {
                frontPass.Setup(this, renderer);
                frontPass.renderer = renderer;
                renderer.EnqueuePass(frontPass);
            }
        }
    }
}
