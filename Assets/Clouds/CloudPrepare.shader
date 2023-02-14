Shader "Clouds/CloudPrepare"
{
	Properties
	{
		_AnimatedSize ("Animated Size", float) = 20.0
		_AnimatedSizeTilling ("- Tilling", float) = 0.1
		_AnimatedSizeSpeed ("- Speed", float) = 2.0
		
		_WindPushStrength ("Wind Push Strength", float) = 2.0
		_WindPushTilling ("- Tilling", float) = 0.2
		_WindPushSpeed ("- Speed", float) = 2.0
	}
    CGINCLUDE
	float _AnimatedSize, _AnimatedSizeTilling;
	float _WindPushStrength, _WindPushTilling;
	float _AnimatedSizeSpeed, _WindPushSpeed;
    #include "CloudShared.cginc"
	struct VertInput
	{
		float4 vertex : POSITION;
		half3 normal  : NORMAL;
	};

	struct FragInput
	{
		float4 vertex : SV_POSITION;
		half3 normal  : NORMAL;
		float3 worldPos : TEXCOORD0;
	};
	FragInput vert(VertInput v)
	{
		FragInput o;
		
		const float3 samplePos = v.vertex;
		
		v.vertex.xyz += v.normal.xyz * (fbm(samplePos * _AnimatedSizeTilling, 2, _Time.x * _AnimatedSizeSpeed * float3(1, 0, 1))) * _AnimatedSize * (v.vertex.y/500);

		v.vertex.xyz += fbm(samplePos * _WindPushTilling, 4, _Time.x * _WindPushSpeed * float3(1, 0, 1)) * _WindPushStrength * float3(1, 0, 1);
		
		o.worldPos = mul(unity_ObjectToWorld, v.vertex);
		o.vertex = UnityWorldToClipPos(o.worldPos);
		o.normal = UnityObjectToWorldNormal(v.normal);
		return o;
	}

	[earlydepthstencil] // Useful when ZTest != 'Always'
	float4 frag(FragInput i) : SV_Target
	{
		i.normal = normalize(i.normal);
		SendToBeResolved(i.normal.xyz, i.vertex.xyz);
		return float4(0, 0, 0, 0);
	}
	ENDCG
    SubShader
    {
        Tags
    	{ 
        	"RenderType" = "TransparentCutout"
    		"Queue" = "AlphaTest+100"
        	"ForceNoShadowCasting" = "True"
        }
        Pass 
    	{
    		// Need cloud backfaces to find how dense clouds are
			Cull Off
    		// We could do an LEqual here if we didn't have to render when inside of a cloud as well;
    		// we figure out that we're inside when the closest face is a backface, 
    		// with LEqual the backface won't be drawn when other geometry sits in front of that backface
			ZTest Always
    		// This is a transparent shader, let other objects be drawn behind it
			ZWrite Off
    		// Do not output color to framebuffer now, 
    		// register them in the OIT structures to be 
    		// read and shown on screen in a latter pass
			ColorMask 0

			CGPROGRAM

			#pragma vertex vert
			#pragma fragment frag
			ENDCG
		}
    }
}