Shader "Clouds/CloudShadowsHack"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags
    	{ 
        	"RenderType"="Transparent" 
        	"Queue" = "Transparent"
        }
        LOD 100

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On ZTest LEqual

            CGPROGRAM
            #pragma target 3.0

            // -------------------------------------
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing

            #pragma vertex vert_shadow
            #pragma fragment frag_shadow

            #include "UnityStandardShadow.cginc"
			sampler3D _DitherMaskLOD;

            struct v2f_shadow
		    {
		        V2F_SHADOW_CASTER;
		        UNITY_VERTEX_OUTPUT_STEREO
				half3 normal  : NORMAL;
		    };
            
            v2f_shadow vert_shadow(appdata_base v)
		    {
		        v2f_shadow o;
		        UNITY_SETUP_INSTANCE_ID(v);
		        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
		        TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
				o.normal = UnityObjectToWorldNormal(v.normal);
		        return o;
		    }
		 
		    float4 frag_shadow(v2f_shadow i) : SV_Target
		    {
		    	/*if(distance(_WorldSpaceCameraPos, _LightPositionRange.xyz) < 1)
		    		discard;*/
		    	//_WorldSpaceLightPos0
		    	#ifdef UNITY_PASS_SHADOWCASTER
		    	float alpha = abs(dot(normalize(i.normal), _WorldSpaceLightPos0.xyz));
                half alphaRef = tex3D(_DitherMaskLOD, float3(i.pos.xy*0.25,alpha*0.9375)).a;
                clip (alphaRef - 0.01);
		        SHADOW_CASTER_FRAGMENT(i)
		    	#else
		    	return float4(0, 0, 0, 0);
		    	#endif
		    }

            ENDCG
        }
    }
}