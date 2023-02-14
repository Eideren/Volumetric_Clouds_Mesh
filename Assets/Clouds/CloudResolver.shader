Shader "Clouds/CloudResolver"
{
	Properties
	{
		_Density("Density", float) = 0.005
		_NearCloudBlendDist("Near Cloud Blend Distance", float) = 0.1
		
		_SilverLiningStrength("Silver Lining Strength", float) = 38.1
		_SilverLiningAngle("Silver Lining Angle", float) = 22.3
		_SilverLiningDensity("Silver Lining Density", float) = 2
		
		_Color("Color", Color) = (1,1,1,1)
		_InsideColor("Inside color", Color) = (0.75,0.75,0.75,0.5)
		
		_DiffusePow("Diffuse Pow", float) = 2.0
		_NoiseMult("Noise Mult", float) = 1.0
	}
    SubShader
    {
		ZTest Always
		ZWrite Off
        Cull Back
        Tags
        {
            "RenderType"="Background"
            "Queue"="AlphaTest+110"
        	"PreviewType"="None"
        }
		GrabPass { }
        Pass
        {
            Name "FORWARD"
	        Tags
    		{
    			"LightMode" = "ForwardBase"
	        }
        	
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
			#pragma multi_compile_fwdbase
            #pragma multi_compile_fog
			#define FORWARD_BASE_PASS

			#include "UnityCG.cginc"
            #include "CloudShared.cginc"
			#include "UnityPBSLighting.cginc"

            float4 _InsideColor, _Color;
			float _Density, _NearCloudBlendDist, _SilverLiningStrength, _SilverLiningAngle, _SilverLiningDensity;
			sampler2D _GrabTexture;
            sampler2D _CameraDepthTexture;
			float _DiffusePow, _NoiseMult;

            

            struct VertInput
            {
                float4 vertex : POSITION;
            };

            struct FragInput
            {
                float4 vertex : SV_POSITION;
            };

            
            
            FragInput vert (VertInput v)
            {
                FragInput o;
            	// Given a quad whose lower left and upper right are {-0.5, -0.5}, {0.5, 0.5}
            	// Stretch it to fit over the whole screen on top of the near plane
                o.vertex.y = -v.vertex.y*2;
                o.vertex.x = v.vertex.x*2;
                o.vertex.z = 1.0;
                o.vertex.w = 1.0;
                return o;
            }

            
            
            inline float3 CamUVToWorldVector(const float2 uv)
			{
            	const float camAspect =	unity_CameraProjection._m11 / unity_CameraProjection._m00;
				const float tanFov = tan(atan(1.0 / unity_CameraProjection._m11));
            	
				const float3 v = float3(float2(camAspect * tanFov, tanFov) * (uv * 2 - 1), 1);
				
				return mul((float3x3)unity_CameraToWorld, v);
			}

            
			

            

            // Saturate density and ease in-out for smoother-looking blending
            inline float DensityClamp(const float f)
            {
	            return -1.0/2.0 * (cos(UNITY_PI*saturate(f)) - 1);
            }

            inline float3 Shade(const float3 normal, const float depth, const float3 viewDir)
            {
				UnityLight light;
				light.color = _LightColor0.rgb;
				light.dir = _WorldSpaceLightPos0.xyz;
				light.ndotl = saturate(dot(normal, light.dir) * 0.5+0.5);
				light.ndotl = 1-pow(1-light.ndotl, _DiffusePow);
				float3 indirectDiffuse = 0;
				indirectDiffuse += max(0, ShadeSH9(float4(normal, 1)));

				const float fresnelTerm = abs(dot(normal, viewDir));
				const float3 diffuseContrib = lerp(0, light.color, light.ndotl);
				
				float3 lightContrib = diffuseContrib*0.6 + indirectDiffuse*0.7;
				lightContrib *= lerp(0.9, 1, fresnelTerm);
				lightContrib = lerp(lightContrib, (lightContrib.x+lightContrib.y+lightContrib.z)/3, 0.3);
				
				float3 finalCol = lightContrib * _Color;
            	UNITY_CALC_FOG_FACTOR_RAW(depth);
				UNITY_FOG_LERP_COLOR(finalCol, unity_FogColor, unityFogFactor);
            	return finalCol;
            }

            

            float4 frag (FragInput input) : SV_Target
            {
				static const float SKYBOX_DIST = 3.402823466e+38F;
				// The maximum amount of fragments we can store is screen.x * screen.y * MaxSortedPixels,
	            // but a single pixel on screen can store more than 'MaxSortedPixels' fragments if other pixels don't store as many
				static const int MAX_SORTED_FRAGMENTS = 20;
            	
            	
            	const float2 uv = input.vertex.xy/_ScreenParams.xy;
            	const float3 depthToWorldVector = CamUVToWorldVector(uv);
            	const float3 viewDir = normalize(depthToWorldVector);
				const float3 lightDir = _WorldSpaceLightPos0.xyz;
				const float3 lightColor = _LightColor0.rgb;
            	const float3 worldColor = tex2D(_GrabTexture, uv);
            	const float depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv);
            	const float worldDepth = depth > 0 ? LinearEyeDepth(depth) : SKYBOX_DIST;
            	
				const uint headPos = VertToLinkAddress(input.vertex.xy);
				uint addressOfHead = addressOfHeadForPixel.Load(headPos);
            	addressOfHeadForPixel.Store(headPos, 0);

            	
				// Loop through linked list of fragments stored for this pixel and store them in the array
            	UnpackedData fragments[MAX_SORTED_FRAGMENTS+1];// '+1' as padding to avoid additional bounds check when sorting
				int fragmentCount = 0;
            	// Using a maximum of 'MAX_SORTED_FRAGMENTS*4' to avoid driver crash when
            	// the buffers have not been properly cleared
            	UNITY_LOOP [allow_uav_condition] for (int i = 0; i < MAX_SORTED_FRAGMENTS*4 && addressOfHead != 0; i++)
				{
					// Retrieve next node in link
					const ListNode listNode = linkedListNodes[addressOfHead];

            		// FOR SOME REASON THIS IS REQUIRED HERE OTHERWISE STUFF FLASHES ON AND OFF
            		// NOT CLEARED SOMEWHERE ELSE IN BULK, JUST RIGHT HERE
            		linkedListNodes[addressOfHead] = (ListNode)0;
            		
            		const UnpackedData unpackedData = Unpack(listNode);
            		
					int j = fragmentCount - 1;
					for (;j >= 0 && fragments[j].depth > unpackedData.depth; j--)
					{
						fragments[j+1] = fragments[j];
					}

					fragments[j+1] = unpackedData;

            		// Set true to go through all fragments registered for this pixel, obviously really slow
            		#ifdef false
						fragmentCount = min(fragmentCount+1, MAX_SORTED_FRAGMENTS);
						addressOfHead = listNode.nextNodeAddress;
            		#else
            			fragmentCount++;
						addressOfHead = (fragmentCount == MAX_SORTED_FRAGMENTS) ? 0 : listNode.nextNodeAddress;
            		#endif
				}
            	
            	// Should be ambient/light intensity
				const float3 cloudInsideColor = lerp(_LightColor0, (_LightColor0.x+_LightColor0.y+_LightColor0.z)/3, _InsideColor.a) * _InsideColor.rgb;
            	const float densityConst = _Density;
            	const float3 silverLiningConst = _SilverLiningStrength * saturate(pow(dot(viewDir, lightDir)*0.5+0.5, _SilverLiningAngle));
            	const float nearCloudBlendDist = _NearCloudBlendDist;

            	float silverLiningOcclusion = 0;
            	
            	float4 cloudAggregate = 0;
            	if (fragmentCount & 1)
            	{
            		// Amount of fragments is not even, we're likely inside of a cloud seeing its back face.
            		const UnpackedData backFace = fragments[0];
            		const float density = DensityClamp(min(worldDepth, backFace.depth) * densityConst);
            		
            		silverLiningOcclusion = lerp(silverLiningOcclusion, 1, density);

            		const float silverLiningScalar = pow(1-saturate(silverLiningOcclusion), _SilverLiningDensity);
            		
            		float3 col = lerp(cloudInsideColor.rgb, lightColor, saturate(silverLiningConst * silverLiningScalar));

            		cloudAggregate += (1.0 - cloudAggregate.a) * float4(col, 1) * density;
            		if(worldDepth < backFace.depth)
            		{
            			// World geometry between camera and closest fragment,
            			// we're inside of a cloud looking at geometry also inside of that cloud,
            			// further fragments along this pixel would also be occluded by this geometry so set alpha to 1
            			cloudAggregate += (1.0 - cloudAggregate.a) * float4(worldColor.rgb, 1);
            		}
            	}

            	// Process fragments in pairs as pairs are very likely to be the front and back faces of a shape
            	// for a pair, the distance between the front and back face defines how dense the cloud is at this pixel.
            	// Process from closest to furthest to exit early if cloud opacity is fully opaque
            	for (int i = fragmentCount & 1; i < fragmentCount && cloudAggregate.a <= 0.999; i += 2)
				{
            		const UnpackedData backFace = fragments[i+1];
            		UnpackedData frontFace = fragments[i];
            		
            		const float3 worldPos = _WorldSpaceCameraPos + depthToWorldVector * frontFace.depth;
            		float fbmSample = fbm(worldPos*0.02, 4, _Time.x * 10 * float3(1, 0, 1));
            		frontFace.depth += fbmSample.x*_NoiseMult*80;

            		const float3 backFaceColor = Shade(backFace.normals, backFace.depth, viewDir);
            		const float3 frontFaceColor = Shade(frontFace.normals, frontFace.depth, viewDir);

            		// Is world geometry drawn in front of backface ?
            		if(backFace.depth > worldDepth)
            		{
            			// Is front face drawn above world geometry
            			if(frontFace.depth < worldDepth)
            			{
            				// world geometry between front and back face
            				// Add front face blend that fades out near world geo
            				const float density = DensityClamp((worldDepth - frontFace.depth) * densityConst);
            				const float3 shapeCol = lerp(cloudInsideColor, frontFaceColor, saturate(frontFace.depth*nearCloudBlendDist));
            				cloudAggregate += (1.0 - cloudAggregate.a) * float4(shapeCol, 1) * density;
            			}
            			// else: world geometry in front of both front and back face,
            			// this fragment is occluded, ignore it and exit
            			break;
            		}

            		float density = backFace.depth - frontFace.depth;
            		density *= densityConst;
            		density = DensityClamp(density);
            		
            		silverLiningOcclusion = lerp(silverLiningOcclusion, 1, density);

            		const float silverLiningScalar = pow(1-saturate(silverLiningOcclusion), _SilverLiningDensity);
            		
            		float3 scatteredColor = lerp(backFaceColor, frontFaceColor, density);
            		scatteredColor = lerp(scatteredColor, lightColor, saturate(silverLiningConst * silverLiningScalar));
            		
            		// Blend color slightly towards 'cloudInsideColor' when close to front face
            		// to soften the transition from near to inside cloud
            		scatteredColor = lerp(cloudInsideColor, scatteredColor, saturate(frontFace.depth*nearCloudBlendDist-0.5));


            		
            		cloudAggregate += (1.0 - cloudAggregate.a) * float4(scatteredColor.rgb, 1) * density;
				}

            	// Not 100% sure this is right for the final blending
            	// Blend cloud color & alpha with worldColor
            	cloudAggregate += (1.0 - cloudAggregate.a) * float4(worldColor.rgb, 1);
            	float4 finalColor = float4(cloudAggregate.rgb, 1);
				return finalColor;
            }
            ENDCG
        }
    }
}