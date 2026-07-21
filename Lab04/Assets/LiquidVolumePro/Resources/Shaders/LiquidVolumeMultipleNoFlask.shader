Shader "LiquidVolume/MultipleNoFlask" {
	Properties {
		[HideInInspector] _DitherStrength ("Dither Strength", Float) = 0.5
		[HideInInspector] _FoamColor ("Foam Color", Color) = (1,1,1,0.9)
		[HideInInspector] _FlaskThickness ("Flask Thickness", Vector) = (0.05,0.05,0.05)
		[HideInInspector] _GlossinessInternal ("Internal Smoothness", Vector) = (0.5, 180, 0.3)
		[HideInInspector] _Muddy ("Muddy", Range(0,1)) = 1.0
		[HideInInspector] _Turbulence ("Turbulence", Vector) = (1.0,1.0,1.0,0)
		[HideInInspector] _TurbulenceSpeed("Turbulence Speed", Float) = 1
        [HideInInspector] _MurkinessSpeed("Murkiness Speed", Float) = 1
		[HideInInspector] _SparklingIntensity ("Sparkling Intensity", Range(0,1)) = 1.0
		[HideInInspector] _SparklingThreshold ("Sparkling Threshold", Range(0,1)) = 0.85

		[HideInInspector] _RimColor ("Rim Color", Color) = (1,0,0,0.1)
		[HideInInspector] _RimPower ("Rim Power", Float) = 1
		[HideInInspector] _LightDir ("Light Direction", Vector) = (0,1,0)
		[HideInInspector] _LightColor ("Light Color", Color) = (1,1,1)
		[HideInInspector] _EmissionColor ("Emission Color", Color) = (0,0,0)

		[HideInInspector] _DeepAtten("Deep Atten", Range(0,10)) = 2.0
		[HideInInspector] _LiquidRaySteps ("Liquid Ray Steps", Int) = 10
		[HideInInspector] _SmokeColor ("Smoke Color", Color) = (0.7,0.7,0.7,0.1)
		[HideInInspector] _SmokeAtten("Smoke Atten", Range(0,10)) = 2.0
		[HideInInspector] _SmokeRaySteps ("Smoke Ray Steps", Int) = 10
		[HideInInspector] _SmokeSpeed ("Smoke Speed", Range(0,20)) = 5.0
		[HideInInspector] _SmokeHeightAtten ("Smoke Height Atten", Range(0,1)) = 0.0
		[HideInInspector] _Noise2Tex ("Noise Tex 2D3D", 2D) = "white"
		[HideInInspector] _NoiseTex2D ("Noise Tex 2D", 2D) = "white"
		[HideInInspector] _FlaskBlurIntensity ("Flask Blur Intensity", Float) = 1.0
		[HideInInspector] _FoamRaySteps ("Foam Ray Steps", Int) = 15
		[HideInInspector] _FoamWeight ("Foam Weight", Float) = 10.0
		[HideInInspector] _FoamBottom ("Foam Visible From Bottom", Float) = 1.0
		[HideInInspector] _FoamTurbulence ("Foam Turbulence", Float) = 1.0
		[HideInInspector] _Scale ("Scale", Vector) = (0.25, 0.2, 1, 5.0)
		
		[HideInInspector] _CullMode ("Cull Mode", Int) = 2
		[HideInInspector] _ZTestMode ("ZTest Mode", Int) = 4

		[HideInInspector] _FoamDensity ("Foam Density", Float) = 1
		[HideInInspector] _AlphaCombined ("Alpha Combined", Float) = 1.0
		[HideInInspector] _FoamMaxPos("Foam Max Pos", Float) = 0
		[HideInInspector] _LevelPos ("Level Pos", Float) = 0
		[HideInInspector] _UpperLimit ("Upper Limit", Float) = 1
		[HideInInspector] _LowerLimit ("Lower Limit", Float) = -1
		_NoiseTex ("Noise Tex", 3D) = "white"
		_FoamNoiseTex ("Foam Noise Tex", 3D) = "white"
		[HideInInspector] _Center ("Center", Vector) = (1,1,1)
		[HideInInspector] _Size ("Size", Vector) = (1,1,1,0.5)
		[HideInInspector] _DoubleSidedBias ("Double Sided Bias", Float) = 0
        [HideInInspector] _BackDepthBias ("Back Depth Bias", Float) = 0
        [HideInInspector] _SizeWorld ("Vertical Size World Units", Float) = 1

        [HideInInspector] _LayersPropertiesTex("", 2D) = "" {}
        [HideInInspector] _LayersColorsTex("", 2D) = "" {}
        [HideInInspector] _LayersColors2Tex("", 2D) = "" {}

        [HideInInspector] _BubblesData ("Bubbles Data", Vector) = (16.0,0,1.0)

        [HideInInspector] _NoiseTexUnwrapped("Noise Unwrapped", 2D) = "gray"

		[HideInInspector] _FinalAlphaMultiplier("Final Alpha Multiplier", Float) = 1

        // Stencil properties
        [HideInInspector] _StencilRef ("Stencil Reference", Int) = 0
        [HideInInspector] _StencilComp ("Stencil Comparison", Int) = 8 // Always
        [HideInInspector] _StencilPass ("Stencil Pass", Int) = 0 // Keep
        [HideInInspector] _StencilFail ("Stencil Fail", Int) = 0 // Keep
        [HideInInspector] _StencilZFail ("Stencil ZFail", Int) = 0 // Keep
	}
	SubShader {
	Tags { "Queue" = "Transparent+1" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector"="True" "RenderType"="Transparent" "DisableBatching"="True" }
	Stencil {
		Ref [_StencilRef]
		Comp [_StencilComp]
		Pass [_StencilPass]
		Fail [_StencilFail]
		ZFail [_StencilZFail]
	}

	Pass {
		// Shadow ==========================================================================================================================================================
        Name "ShadowCaster"
		Cull Front
		Tags { "LightMode" = "ShadowCaster"  }

		HLSLPROGRAM
		#pragma vertex vert
		#pragma fragment frag
        #pragma multi_compile_shadowcaster
        #pragma fragmentoption ARB_precision_hint_fastest
		#include "LVShadowPass.cginc"
		ENDHLSL
	} 

	Pass {
		// PBS Liquid ====================================================================================================================================
        Name "ForwardLit"
        Tags { "LightMode" = "UniversalForward" }
		ZWrite Off 
		Cull [_CullMode]
		ZTest [_ZTestMode]
		Blend SrcAlpha OneMinusSrcAlpha

		HLSLPROGRAM
		#pragma vertex vert
		#pragma fragment frag
		#define LIGHTING LightingWrappedSpecular
		#pragma fragmentoption ARB_precision_hint_fastest
		#pragma target 3.0
//		#pragma multi_compile _ _MAIN_LIGHT_SHADOWS
//		#pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
		#pragma multi_compile_local _ LIQUID_VOLUME_USE_REFRACTION
		#pragma multi_compile_local _ LIQUID_VOLUME_SPHERE LIQUID_VOLUME_CUBE LIQUID_VOLUME_CYLINDER
		#pragma multi_compile_local _ LIQUID_VOLUME_NON_AABB LIQUID_VOLUME_IGNORE_GRAVITY
		#pragma multi_compile_local _ LIQUID_VOLUME_DEPTH_AWARE
		#pragma multi_compile_local _ LIQUID_VOLUME_DEPTH_AWARE_PASS
		#define USES_RIM
		#include "LVLiquidPass3DMultiple.cginc"
		ENDHLSL
    }

	}
	
	Fallback "Transparent/VertexLit"
}
