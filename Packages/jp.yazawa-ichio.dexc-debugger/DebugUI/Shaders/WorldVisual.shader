Shader "Hidden/Dexc/WorldVisual"
{
	SubShader
	{
		Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
		Pass
		{
			ZWrite Off
			ZTest LEqual
			Cull Off
			Blend SrcAlpha OneMinusSrcAlpha
			HLSLPROGRAM
			#pragma vertex DexcVertex
			#pragma fragment DexcFragment
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			struct Attributes { float3 positionOS : POSITION; float4 color : COLOR; };
			struct Varyings { float4 positionHCS : SV_POSITION; float4 color : COLOR; };

			Varyings DexcVertex(Attributes input)
			{
				Varyings output;
				output.positionHCS = TransformObjectToHClip(input.positionOS);
				output.color = input.color;
				return output;
			}

			float4 DexcFragment(Varyings input) : SV_Target { return input.color; }
			ENDHLSL
		}
	}
}
