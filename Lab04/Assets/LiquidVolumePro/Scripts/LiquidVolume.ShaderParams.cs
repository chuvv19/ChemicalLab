using UnityEngine;

namespace LiquidVolumeFX {

    public partial class LiquidVolume {

        static class ShaderParams {

            public static int BubblesData = Shader.PropertyToID("_BubblesData");
            public static int PointLightInsideAtten = Shader.PropertyToID("_PointLightInsideAtten");
            public static int PointLightColorArray = Shader.PropertyToID("_PointLightColor");
            public static int PointLightPositionArray = Shader.PropertyToID("_PointLightPosition");
            public static int PointLightCount = Shader.PropertyToID("_PointLightCount");
            public static int GlossinessInt = Shader.PropertyToID("_GlossinessInternal");
            public static int DoubleSidedBias = Shader.PropertyToID("_DoubleSidedBias");
            public static int BackDepthBias = Shader.PropertyToID("_BackDepthBias");
            public static int Muddy = Shader.PropertyToID("_Muddy");
            public static int Alpha = Shader.PropertyToID("_Alpha");
            public static int AlphaCombined = Shader.PropertyToID("_AlphaCombined");
            public static int SparklingIntensity = Shader.PropertyToID("_SparklingIntensity");
            public static int SparklingThreshold = Shader.PropertyToID("_SparklingThreshold");
            public static int DepthAtten = Shader.PropertyToID("_DeepAtten");
            public static int SmokeColor = Shader.PropertyToID("_SmokeColor");
            public static int SmokeAtten = Shader.PropertyToID("_SmokeAtten");
            public static int SmokeSpeed = Shader.PropertyToID("_SmokeSpeed");
            public static int SmokeHeightAtten = Shader.PropertyToID("_SmokeHeightAtten");
            public static int SmokeRaySteps = Shader.PropertyToID("_SmokeRaySteps");
            public static int LiquidRaySteps = Shader.PropertyToID("_LiquidRaySteps");
            public static int FlaskBlurIntensity = Shader.PropertyToID("_FlaskBlurIntensity");
            public static int FoamColor = Shader.PropertyToID("_FoamColor");
            public static int FoamRaySteps = Shader.PropertyToID("_FoamRaySteps");
            public static int FoamDensity = Shader.PropertyToID("_FoamDensity");
            public static int FoamWeight = Shader.PropertyToID("_FoamWeight");
            public static int FoamBottom = Shader.PropertyToID("_FoamBottom");
            public static int FoamTurbulence = Shader.PropertyToID("_FoamTurbulence");
            public static int RefractTex = Shader.PropertyToID("_RefractTex");
            public static int FlaskThickness = Shader.PropertyToID("_FlaskThickness");
            public static int Size = Shader.PropertyToID("_Size");
            public static int Scale = Shader.PropertyToID("_Scale");
            public static int Center = Shader.PropertyToID("_Center");
            public static int SizeWorld = Shader.PropertyToID("_SizeWorld");
            public static int DepthAwareOffset = Shader.PropertyToID("_DepthAwareOffset");
            public static int Turbulence = Shader.PropertyToID("_Turbulence");
            public static int LayersColorArray = Shader.PropertyToID("_LayersColors");
            public static int LayersColor2Array = Shader.PropertyToID("_LayersColors2");
            public static int LayersProperties = Shader.PropertyToID("_LayersProperties");
            public static int LayersPropertiesTex = Shader.PropertyToID("_LayersPropertiesTex");
            public static int LayersColorsTex = Shader.PropertyToID("_LayersColorsTex");
            public static int LayersColors2Tex = Shader.PropertyToID("_LayersColors2Tex");
            public static int TurbulenceSpeed = Shader.PropertyToID("_TurbulenceSpeed");
            public static int MurkinessSpeed = Shader.PropertyToID("_MurkinessSpeed");
            public static int DitherStrength = Shader.PropertyToID("_DitherStrength");
            public static int Color1 = Shader.PropertyToID("_Color1");
            public static int Color2 = Shader.PropertyToID("_Color2");
            public static int RimColor = Shader.PropertyToID("_RimColor");
            public static int RimPower = Shader.PropertyToID("_RimPower");
            public static int EmissionColor = Shader.PropertyToID("_EmissionColor");
            public static int LightColor = Shader.PropertyToID("_LightColor");
            public static int LightDir = Shader.PropertyToID("_LightDir");
            public static int LevelPos = Shader.PropertyToID("_LevelPos");
            public static int UpperLimit = Shader.PropertyToID("_UpperLimit");
            public static int LowerLimit = Shader.PropertyToID("_LowerLimit");
            public static int FoamMaxPos = Shader.PropertyToID("_FoamMaxPos");
            public static int CullMode = Shader.PropertyToID("_CullMode");
            public static int ZTestMode = Shader.PropertyToID("_ZTestMode");
            public static int NoiseTex = Shader.PropertyToID("_NoiseTex");
            public static int NoiseTexUnwrapped = Shader.PropertyToID("_NoiseTexUnwrapped");
            public static int FoamNoiseTex = Shader.PropertyToID("_FoamNoiseTex");
            public static int GlobalRefractionTexture = Shader.PropertyToID("_VLGrabBlurTexture");
            public static int RotationMatrix = Shader.PropertyToID("_Rot");
            public static int FinalAlphaMultiplier = Shader.PropertyToID("_FinalAlphaMultiplier");
            public static int QueueOffset = Shader.PropertyToID("_QueueOffset");
            public static int PreserveSpecular = Shader.PropertyToID("_BlendModePreserveSpecular");

            public static int StencilRef = Shader.PropertyToID("_StencilRef");
            public static int StencilComp = Shader.PropertyToID("_StencilComp");
            public static int StencilPass = Shader.PropertyToID("_StencilPass");
            public static int StencilFail = Shader.PropertyToID("_StencilFail");
            public static int StencilZFail = Shader.PropertyToID("_StencilZFail");

            public const string SHADER_KEYWORD_DEPTH_AWARE = "LIQUID_VOLUME_DEPTH_AWARE";
            public const string SHADER_KEYWORD_DEPTH_AWARE_CUSTOM_PASS = "LIQUID_VOLUME_DEPTH_AWARE_PASS";
            public const string SHADER_KEYWORD_NON_AABB = "LIQUID_VOLUME_NON_AABB";
            public const string SHADER_KEYWORD_IGNORE_GRAVITY = "LIQUID_VOLUME_IGNORE_GRAVITY";
            public const string SHADER_KEYWORD_SPHERE = "LIQUID_VOLUME_SPHERE";
            public const string SHADER_KEYWORD_CUBE = "LIQUID_VOLUME_CUBE";
            public const string SHADER_KEYWORD_CYLINDER = "LIQUID_VOLUME_CYLINDER";
            public const string SHADER_KEYWORD_USE_REFRACTION = "LIQUID_VOLUME_USE_REFRACTION";

        }
    }

}