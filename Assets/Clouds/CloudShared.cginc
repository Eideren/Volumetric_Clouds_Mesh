#if !defined(CLOUD_SHARED)
#define CLOUD_SHARED

#pragma require randomwrite
#include "UnityCG.cginc"

struct ListNode
{
    float depth;
    uint normals;
    uint nextNodeAddress;
};

struct UnpackedData
{
    float depth;
    float3 normals;
};

uniform RWStructuredBuffer<ListNode> linkedListNodes : register(u4);
uniform RWByteAddressBuffer addressOfHeadForPixel : register(u5);

// (pow(2, 24) - 1)
static const uint _2Pow24Min1 = 16777216 - 1; 

inline ListNode Pack(const UnpackedData unpackedData, const uint nextNodeAddress)
{
    ListNode o;
    o.depth = Linear01Depth(unpackedData.depth);
    
    const uint4 u = (uint4)(saturate(float4(unpackedData.normals.xyz, 0)) * 255 + 0.5);
    o.normals = (u.w << 24UL) | (u.z << 16UL) | (u.y << 8UL) | u.x;
    o.nextNodeAddress = nextNodeAddress;
    return o;
}

inline UnpackedData Unpack(const ListNode packedData)
{
    UnpackedData o;
    o.depth = packedData.depth * _ProjectionParams.z;
    
    const uint4 p = uint4(
        (packedData.normals & 0xFFUL),
        (packedData.normals >> 8UL) & 0xFFUL,
        (packedData.normals >> 16UL) & 0xFFUL,
        (packedData.normals >> 24UL));
    o.normals.xyz = (((float4)p) / 255).xyz;
    return o;
}

inline uint VertToLinkAddress(const float2 vPos)
{
    return (_ScreenParams.x * (vPos.y - 0.5) + (vPos.x - 0.5))*4;
}

inline void SendToBeResolved(const float3 normals, const float3 pos)
{
    const uint newLinkHeadAddress = linkedListNodes.IncrementCounter();

    // Test if we reached maximum amount of overlap 
    uint maxStructs, stride;
    linkedListNodes.GetDimensions(maxStructs, stride);
    if(newLinkHeadAddress >= maxStructs)
        return;

    // Set new head in the list
    uint oldHeadAddress;
    addressOfHeadForPixel.InterlockedExchange(VertToLinkAddress(pos), newLinkHeadAddress, oldHeadAddress);
    
    UnpackedData unpackedData;
    unpackedData.normals = normals;
    unpackedData.depth = pos.z;
    
    linkedListNodes[newLinkHeadAddress] = Pack(unpackedData, oldHeadAddress);
}



// Inigo Quilez' stuff

float3 hash( float3 p ) // replace this by something better. really. do // Eideren: No perf issue right now, will do once there is
{
	p = float3( dot(p,float3(127.1,311.7, 74.7)),
			  dot(p,float3(269.5,183.3,246.1)),
			  dot(p,float3(113.5,271.9,124.6)));

	return -1.0 + 2.0*frac(sin(p)*43758.5453123);
}

// return value noise (in x) and its derivatives (in yzw)
float noise( in float3 p )
{
    float3 i = floor( p );
    float3 f = frac( p );
	
	float3 u = f*f*(3.0-2.0*f);

    return lerp( lerp( lerp( dot( hash( i + float3(0.0,0.0,0.0) ), f - float3(0.0,0.0,0.0) ), 
                          dot( hash( i + float3(1.0,0.0,0.0) ), f - float3(1.0,0.0,0.0) ), u.x),
                     lerp( dot( hash( i + float3(0.0,1.0,0.0) ), f - float3(0.0,1.0,0.0) ), 
                          dot( hash( i + float3(1.0,1.0,0.0) ), f - float3(1.0,1.0,0.0) ), u.x), u.y),
                lerp( lerp( dot( hash( i + float3(0.0,0.0,1.0) ), f - float3(0.0,0.0,1.0) ), 
                          dot( hash( i + float3(1.0,0.0,1.0) ), f - float3(1.0,0.0,1.0) ), u.x),
                     lerp( dot( hash( i + float3(0.0,1.0,1.0) ), f - float3(0.0,1.0,1.0) ), 
                          dot( hash( i + float3(1.0,1.0,1.0) ), f - float3(1.0,1.0,1.0) ), u.x), u.y), u.z );
}

float fbm( float3 x, const int octaves, const float3 time )
{
	static const float3x3 m3  = float3x3( 0.00,  0.80,  0.60,
	                      -0.80,  0.36, -0.48,
	                      -0.60, -0.48,  0.64 );
	static const float3x3 m3i = float3x3( 0.00, -0.80, -0.60,
	                       0.80,  0.36, -0.48,
	                       0.60, -0.48,  0.64 );
    float f = 1.98;  // could be 2.0
    float s = 0.49;  // could be 0.5
    float a = 0.0;
    float b = 0.5;
    float3x3 m = float3x3(
	    1.0,0.0,0.0,
	    0.0,1.0,0.0,
	    0.0,0.0,1.0);
    for( int i=0; i < octaves; i++ )
    {
        float n = noise(x + time);
        a += b*n.x;          // accumulate values
        b *= s;
        x = f*mul(m3,x);
        m = f*mul(m3i,m);
    }
    return a;
}


// return value noise (in x) and its derivatives (in yzw)
float4 noised( in float3 x )
{
    // grid
    float3 i = floor(x);
    float3 w = frac(x);
    
    #if 1
    // quintic interpolant
    float3 u = w*w*w*(w*(w*6.0-15.0)+10.0);
    float3 du = 30.0*w*w*(w*(w-2.0)+1.0);
    #else
    // cubic interpolant
    float3 u = w*w*(3.0-2.0*w);
    float3 du = 6.0*w*(1.0-w);
    #endif    
    
    // gradients
    float3 ga = hash( i+float3(0.0,0.0,0.0) );
    float3 gb = hash( i+float3(1.0,0.0,0.0) );
    float3 gc = hash( i+float3(0.0,1.0,0.0) );
    float3 gd = hash( i+float3(1.0,1.0,0.0) );
    float3 ge = hash( i+float3(0.0,0.0,1.0) );
	float3 gf = hash( i+float3(1.0,0.0,1.0) );
    float3 gg = hash( i+float3(0.0,1.0,1.0) );
    float3 gh = hash( i+float3(1.0,1.0,1.0) );
    
    // projections
    float va = dot( ga, w-float3(0.0,0.0,0.0) );
    float vb = dot( gb, w-float3(1.0,0.0,0.0) );
    float vc = dot( gc, w-float3(0.0,1.0,0.0) );
    float vd = dot( gd, w-float3(1.0,1.0,0.0) );
    float ve = dot( ge, w-float3(0.0,0.0,1.0) );
    float vf = dot( gf, w-float3(1.0,0.0,1.0) );
    float vg = dot( gg, w-float3(0.0,1.0,1.0) );
    float vh = dot( gh, w-float3(1.0,1.0,1.0) );
	
    // interpolations
    return float4( va + u.x*(vb-va) + u.y*(vc-va) + u.z*(ve-va) + u.x*u.y*(va-vb-vc+vd) + u.y*u.z*(va-vc-ve+vg) + u.z*u.x*(va-vb-ve+vf) + (-va+vb+vc-vd+ve-vf-vg+vh)*u.x*u.y*u.z,    // value
                 ga + u.x*(gb-ga) + u.y*(gc-ga) + u.z*(ge-ga) + u.x*u.y*(ga-gb-gc+gd) + u.y*u.z*(ga-gc-ge+gg) + u.z*u.x*(ga-gb-ge+gf) + (-ga+gb+gc-gd+ge-gf-gg+gh)*u.x*u.y*u.z +   // derivatives
                 du * (float3(vb,vc,ve) - va + u.yzx*float3(va-vb-vc+vd,va-vc-ve+vg,va-vb-ve+vf) + u.zxy*float3(va-vb-ve+vf,va-vb-vc+vd,va-vc-ve+vg) + u.yzx*u.zxy*(-va+vb+vc-vd+ve-vf-vg+vh) ));
}

float4 fbmd( float3 x, const int octaves, const float3 time )
{
	static const float3x3 m3  = float3x3( 0.00,  0.80,  0.60,
	                      -0.80,  0.36, -0.48,
	                      -0.60, -0.48,  0.64 );
	static const float3x3 m3i = float3x3( 0.00, -0.80, -0.60,
	                       0.80,  0.36, -0.48,
	                       0.60, -0.48,  0.64 );
    float f = 1.98;  // could be 2.0
    float s = 0.49;  // could be 0.5
    float a = 0.0;
    float b = 0.5;
    float3 d = 0.0;
    float3x3 m = float3x3(
	    1.0,0.0,0.0,
	    0.0,1.0,0.0,
	    0.0,0.0,1.0);
    for( int i=0; i < octaves; i++ )
    {
        float4 n = noised(x + time);
        a += b*n.x;          // accumulate values
        d += b*mul(m,n.yzw);      // accumulate derivatives
        b *= s;
        x = f*mul(m3,x);
        m = f*mul(m3i,m);
    }
    return float4( a, d );
}



#endif // !CLOUD_SHARED