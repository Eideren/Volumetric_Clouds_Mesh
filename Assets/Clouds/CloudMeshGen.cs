using System;
using UnityEngine;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using static UnityEngine.Vector3;
using Debug = UnityEngine.Debug;
using static System.Math;
namespace Clouds{


[System.Serializable]
public struct NoiseConfig
{
	public int Octaves;
	public float DetailIntensity;
	public float DetailScale;
}


[System.Serializable]
public struct Layer
{
	public string Name;
	public bool Disable;
	public float start, stop;
	public float Intensity;
	public float Curve;
}



public class CloudMeshGen : MonoBehaviour
{
	public float NoiseScale = 1f;
	[Range(0f, 1f)]
	public float WorleyToPerlinRatio = 0.5f;
	public NoiseConfig WorleyConfig = new NoiseConfig{ Octaves = 3, DetailIntensity = 0.4f, DetailScale = 2f };
	public NoiseConfig PerlinConfig = new NoiseConfig{ Octaves = 4, DetailIntensity = 0.5f, DetailScale = 2.2f };
	public float WorleyPow = 1f;
	public Layer[] Layers =
	{
		new Layer
		{
			Name = "Stratus",
			start = 0.05f,
			stop = 0.1f,
			Intensity = 0.8f,
			Curve = 1f,
		},
		new Layer
		{
			Name = "Cumulus",
			start = 0.2f,
			stop = 0.5f,
			Intensity = 0.8f,
			Curve = 1f,
		},
		new Layer
		{
			Name = "Cumulonimbus",
			start = 0.2f,
			stop = 0.9f,
			Intensity =2f,
			Curve = 40f,
		},
	};
	
	public int ResolutionXZ = 32;
	public int ResolutionY = 32;
	public bool CloseVolume = true;
	public float Coverage = 0.5f;
	public bool SmoothNormals = true;
	public Material Material;
	public Material PlaneMaterial;
	public bool GenerateMesh;

	public bool PreviewAsTex;
	public Texture2D Tex;
	
	float[] _field;
	MeshFilter _filter;
	
	static GraphicsBuffer _linkedListNodes, _addressOfHeadForPixel;
	static bool _setupClear;
	static MeshRenderer _resolvePlane;
	static Material _planeMat;
	static PermutationTable Perm = new PermutationTable(1024, 255, 1010);



	void Update()
	{
		ValidateData(PlaneMaterial);
	}



	void ValidateData(Material planeMat)
	{
		_planeMat = planeMat;
		
		if(_setupClear)
			return;
		
		_setupClear = true;
		//Camera.onPreCull += OnPreRenderCallback;
		Camera.onPreRender += OnPreRenderCallback;
		Camera.onPostRender += OnPostRenderCallback;

		static void OnPostRenderCallback( Camera cam )
		{
			Graphics.ClearRandomWriteTargets();
		}

		static void OnPreRenderCallback(Camera cam)
		{
			if( cam.name == "Preview Scene Camera" )
				return;
			
			if( _resolvePlane == null )
			{
				_resolvePlane = NewResolvePlane();
			}
			_resolvePlane.sharedMaterial = _planeMat;
			
			const int MaxFragmentsPerPixel = 8;
			int screenWidth = Screen.width > 0 ? Screen.width : throw new Exception();
			int screenHeight = Screen.height > 0 ? Screen.height : throw new Exception();

			int widthByHeight = screenWidth * screenHeight;
			if( _addressOfHeadForPixel == null || _addressOfHeadForPixel.count != widthByHeight )
			{
				_addressOfHeadForPixel?.Release();
				_linkedListNodes?.Release();

				int nodesCount = screenWidth * screenHeight * MaxFragmentsPerPixel;
				_linkedListNodes = new GraphicsBuffer(GraphicsBuffer.Target.Counter, nodesCount, sizeof(uint) * 3);
				_addressOfHeadForPixel = new GraphicsBuffer(GraphicsBuffer.Target.Raw, widthByHeight, sizeof(uint));

				int usage = (widthByHeight + nodesCount * 3) * 4;
				Debug.Log($"Reset to {screenWidth}/{screenHeight} for {cam.name}, using at least {(usage / (1000d*1000d)):0.00} MB");
			}
			
			Graphics.SetRandomWriteTarget(4, _linkedListNodes, false);
			Graphics.SetRandomWriteTarget(5, _addressOfHeadForPixel, false);
		}

		static MeshRenderer NewResolvePlane()
		{
			var name = $"{nameof(CloudMeshGen)} {nameof(_resolvePlane)}";
			var go = SceneManager.GetActiveScene().GetRootGameObjects();
			MeshRenderer outputPlane = null;
			foreach( var gameObject in go )
			{
				if( gameObject.name == name )
					outputPlane = gameObject.GetComponent<MeshRenderer>();
			}

			if( outputPlane == null )
			{
				outputPlane ??= GameObject.CreatePrimitive(PrimitiveType.Quad).GetComponent<MeshRenderer>();
				var mesh = Instantiate(outputPlane.GetComponent<MeshFilter>().sharedMesh);
				mesh.bounds = new Bounds( new Vector3( 0f, 0f, 0f ), new Vector3( 1E+36f, 1E+36f, 1E+36f ) );
				outputPlane.GetComponent<MeshFilter>().sharedMesh = mesh;
			}
				
			outputPlane.gameObject.name = name;
			//outputPlane.gameObject.hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor;//HideFlags.HideAndDontSave & ~HideFlags.DontUnloadUnusedAsset & ~HideFlags.HideInHierarchy;
			if( outputPlane.gameObject.GetComponent<MeshCollider>() is MeshCollider col )
			{
				col.enabled = false;
				col.sharedMesh = null;
			}
			outputPlane.shadowCastingMode = ShadowCastingMode.Off;
			outputPlane.receiveShadows = false;
			outputPlane.allowOcclusionWhenDynamic = false;
			outputPlane.lightProbeUsage = LightProbeUsage.Off;
			outputPlane.reflectionProbeUsage = ReflectionProbeUsage.Off;
			outputPlane.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
			return outputPlane;
		}
	}


	void OnDrawGizmos()
	{
		ValidateData(PlaneMaterial);
		if( GenerateMesh )
		{
			GenerateMesh = false;
			RebuildMeshes();
			return;
		}

		if( PreviewAsTex )
		{
			PreviewAsTex = false;
			UpdatePreviewTex();
			return;
		}
	}



	void UpdateScalarField()
	{
		int depth = ResolutionXZ;
		int width = ResolutionXZ;
		int height = ResolutionY;
		
		if(_field == null || _field.Length != width * height * depth)
			_field = new float[width * height * depth];
		
		Array.Clear(_field, 0, _field.Length);
		
		Parallel.For( CloseVolume ? 1 : 0, CloseVolume ? depth-1 : depth, z =>
		{
			int widthMax = CloseVolume ? width-1 : width;
			int heightMax = CloseVolume ? height-1 : height;
			int widthStart = CloseVolume ? 1 : 0;
			int heightStart = CloseVolume ? 1 : 0;

			for (int y = heightStart; y < heightMax; y++)
			{
				for (int x = widthStart; x < widthMax; x++)
				{
					float fx = (float)x / width;
					float fy = (float)y / height;
					float fz = (float)z / depth;
			        
					_field[ x + y * width + z * width * height ] = Sample( new Vector3(fx, fy, fz) );
				}
			}
		} );
	}



	Vector3[] GenerateNormals(List<Vector3> verts)
	{
		int depth = ResolutionXZ;
		int width = ResolutionXZ;
		int height = ResolutionY;
		
		Vector3[] fieldNormals = new Vector3[ width * height * depth ];
		Parallel.For( 0, depth, z =>
        {
	        for (int y = 0; y < height; y++)
	        {
		        for (int x = 0; x < width; x++)
		        {
			        var centerIdx = x + y * width + z * width * height;
			        ref var nrml = ref fieldNormals[ centerIdx ];
			        // Can just add/remove int to get proper index, no need to recompute the whole thing
			        nrml.x = _field[Max(x-1, 0) + y * width + z * width * height] - _field[Min(x+1, width-1) + y * width + z * width * height];
			        nrml.y = _field[x + Max(y-1, 0) * width + z * width * height] - _field[x + Min(y+1, height-1) * width + z * width * height];
			        nrml.z = _field[x + y * width + Max(z-1, 0) * width * height] - _field[x + y * width + Min(z+1, depth-1) * width * height];
			        nrml = nrml.normalized;
		        }
	        }
        } );

		if( SmoothNormals )
		{
			Vector3[] fieldNormalAvg = new Vector3[ width * height * depth ];
			Parallel.For( 0, depth, z =>
			{
				for (int y = 0; y < height; y++)
				{
					for (int x = 0; x < width; x++)
					{
						int count = 0;
						const int kernelSize = 1;
						Vector3 val = default;
						for( int i = -kernelSize; i <= kernelSize; i++ )
						{
							for( int j = -kernelSize; j <= kernelSize; j++ )
							{
								for( int k = -kernelSize; k <= kernelSize; k++ )
								{
									int x1 = (x + i);
									int y1 = (y + j);
									int z1 = (z + k);

									if( x1 < 0 || x1 > width - 1 )
										continue;
									if( y1 < 0 || y1 > height - 1 )
										continue;
									if( z1 < 0 || z1 > depth - 1 )
										continue;
									
									count++;
									val += fieldNormals[ x1 + y1 * width + z1 * width * height ];
								}
							}
						}
						fieldNormalAvg[x + y * width + z * width * height] = val/count;
					}
				}
			} );
			fieldNormals = fieldNormalAvg;
		}

        Vector3[] geoNormals = new Vector3[ verts.Count ];
        Parallel.For( 0, verts.Count, i =>
        {
	        var vert = verts[ i ];
	        int x0 = Mathf.FloorToInt( vert.x );
	        int y0 = Mathf.FloorToInt( vert.y );
	        int z0 = Mathf.FloorToInt( vert.z );
	        int x1 = Min(x0 + 1, width-1);
	        int y1 = Min(y0 + 1, height-1);
	        int z1 = Min(z0 + 1, depth-1);
	        
	        float tX = vert.x % 1f;
	        float tY = vert.y % 1f;
	        float tZ = vert.z % 1f;

	        ref var x0y0z0 = ref fieldNormals[x0 + y0 * width + z0 * width * height];
	        ref var x1y0z0 = ref fieldNormals[x1 + y0 * width + z0 * width * height];
	        ref var x0y1z0 = ref fieldNormals[x0 + y1 * width + z0 * width * height];
	        ref var x1y1z0 = ref fieldNormals[x1 + y1 * width + z0 * width * height];
	        ref var x0y0z1 = ref fieldNormals[x0 + y0 * width + z1 * width * height];
	        ref var x1y0z1 = ref fieldNormals[x1 + y0 * width + z1 * width * height];
	        ref var x0y1z1 = ref fieldNormals[x0 + y1 * width + z1 * width * height];
	        ref var x1y1z1 = ref fieldNormals[x1 + y1 * width + z1 * width * height];
	        geoNormals[i] = 
		        Slerp( 
			        Slerp( 
				        Slerp(x0y0z0, x1y0z0, tX), 
				        Slerp(x0y1z0, x1y1z0, tX), tY), 
			        Slerp( 
				        Slerp(x0y0z1, x1y0z1, tX),
				        Slerp(x0y1z1, x1y1z1, tX), tY), 
			        tZ).normalized;
        } );
        return geoNormals;
	}



	void RebuildMeshes()
	{
		var stringOutputTime = "";
		var sw = Stopwatch.StartNew();

		int depth = ResolutionXZ;
        int width = ResolutionXZ;
        int height = ResolutionY;
        
        stringOutputTime += $"Prepare:{sw.Elapsed.TotalMilliseconds}";
        sw.Restart();

        UpdateScalarField();
        
        stringOutputTime += $"\nScalar Field:{sw.Elapsed.TotalMilliseconds}";
        sw.Restart();

        var indices = new List<int>();
        var verts = new List<Vector3>();
        MarchingCubes.Generate(Coverage, _field, width, height, depth, verts, indices);
        
        stringOutputTime += $"\nMarching Cube:{sw.Elapsed.TotalMilliseconds}";
        sw.Restart();

		var geoNormals = GenerateNormals(verts);
        
        stringOutputTime += $"\nMesh normals:{sw.Elapsed.TotalMilliseconds}";
        sw.Restart();

        MeshesFromMarchedData(verts, indices, geoNormals);

        stringOutputTime += $"\nMesh:{sw.Elapsed.TotalMilliseconds}";
        stringOutputTime = $"Timings:\n{stringOutputTime}";
        Debug.Log( stringOutputTime );
	}
	
	
	void MeshesFromMarchedData(List<Vector3> verts, List<int> indices, Vector3[] geoNormals)
	{
		int depth = ResolutionXZ;
		int width = ResolutionXZ;
		int height = ResolutionY;

		if( _filter == null )
		{
			MeshRenderer renderer = gameObject.GetComponent<MeshRenderer>();
			if( renderer == null )
				renderer = gameObject.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = Material;
			
			_filter = gameObject.GetComponent<MeshFilter>();
			if( _filter == null )
				_filter = gameObject.AddComponent<MeshFilter>();
			_filter.sharedMesh ??= new Mesh
			{
				name = "CloudMesh",
				indexFormat = IndexFormat.UInt32
			};
		}

		var scale = new Vector3( width, height, depth );

		//transform.localScale = new Vector3( 1f / width, 1f / height, 1f / depth );
		
		var mesh = _filter.sharedMesh;
		mesh.SetTriangles(indices, 0);
		mesh.SetVertices(verts);
		mesh.SetNormals(geoNormals);
		mesh.bounds = new Bounds(scale * 0.5f, scale);
	}



	float Sample(Vector3 p, bool sampleLayers = true)
	{
		var unit = p;
		p *= NoiseScale;
		float contrib = 0f;
		if(WorleyToPerlinRatio < 1f)
			contrib += (1f-WorleyToPerlinRatio) * fbmWorley(p, WorleyConfig, WorleyPow);
		if(WorleyToPerlinRatio > 0f)
			contrib += WorleyToPerlinRatio * fbmSimplex(p, PerlinConfig);

		if( sampleLayers )
		{
			int valid = 0;
			float weight = 0f;
			foreach( Layer layer in Layers )
			{
				if( layer.Disable )
					continue;
				
				var midPoint = (layer.stop + layer.start) * 0.5f;
				var length = layer.stop - midPoint;
				weight += (1f - Mathf.Clamp01(Mathf.Pow(Mathf.Abs( midPoint - unit.y ) / length, 1f * layer.Curve)))*layer.Intensity;
				valid++;
			}

			contrib = valid > 0 ? contrib * weight : contrib;
		}

		return contrib;
	}



	static float fbmWorley( Vector3 p, in NoiseConfig config, float worleyPow )
	{
		float value = 0f;
		float contrib = 1f;
		float maxVal = 0f;
		
		for (int i = 0; i < config.Octaves; i++)
		{
			maxVal += contrib;
			value += contrib * SampleWorley(p, Perm, worleyPow);
			p *= config.DetailScale;
			contrib *= config.DetailIntensity;
		}
		return value / maxVal; // Scale back from 0->whatever to 0->1
	}



	static float fbmSimplex( Vector3 p, in NoiseConfig config )
	{
		float value = 0f;
		float contrib = 1f;
		float maxVal = 0f;
		
		for (int i = 0; i < config.Octaves; i++)
		{
			maxVal += contrib;
			value += contrib * SampleSimplex(p, Perm);
			p *= config.DetailScale;
			contrib *= config.DetailIntensity;
		}
		return value / maxVal; // Scale back from 0->whatever to 0->1
	}
	
	
	
    static readonly float[] OFFSET_F = { -0.5f, 0.5f, 1.5f };

    static unsafe float SampleWorley(in Vector3 p, PermutationTable Perm, float worleyPow)
    {
	    const float K = 1.0f / 7.0f;
	    const float Ko = 3.0f / 7.0f;
	    
	    var x = p.x; var y = p.y; var z = p.z;
	    
        int Pi0 = Mathf.FloorToInt(x);
        int Pi1 = Mathf.FloorToInt(y);
        int Pi2 = Mathf.FloorToInt(z);

        float Pf0 = Frac(x);
        float Pf1 = Frac(y);
        float Pf2 = Frac(z);

        int* pX = stackalloc int[ 3 ]
        {
            Perm[Pi0 - 1],
            Perm[Pi0],
            Perm[Pi0 + 1]
        };

        int* pY = stackalloc int[ 3 ]
        {
            Perm[Pi1 - 1],
            Perm[Pi1],
            Perm[Pi1 + 1]
        };

        float d0, d1, d2;
        float F0 = 1e6f;
        float F1 = 1e6f;
        float F2 = 1e6f;

        int px, py, pz;
        float oxx, oxy, oxz;
        float oyx, oyy, oyz;
        float ozx, ozy, ozz;

        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
	            var ij = Perm.Table[(pY[j] + Perm.Table[pX[i] & Perm.Wrap]) & Perm.Wrap] + Pi2;
	            px = Perm.Table[(ij-1) & Perm.Wrap] & Perm.Max;
	            py = Perm.Table[(ij) & Perm.Wrap] & Perm.Max;
	            pz = Perm.Table[(ij+1) & Perm.Wrap] & Perm.Max;

                oxx = Frac(px * K) - Ko;
                oxy = Frac(py * K) - Ko;
                oxz = Frac(pz * K) - Ko;

                oyx = (Mathf.Floor(px * K) % 7.0f) * K - Ko;
                oyy = (Mathf.Floor(py * K) % 7.0f) * K - Ko;
                oyz = (Mathf.Floor(pz * K) % 7.0f) * K - Ko;

                px = Perm[px];
                py = Perm[py];
                pz = Perm[pz];

                ozx = Frac(px * K) - Ko;
                ozy = Frac(py * K) - Ko;
                ozz = Frac(pz * K) - Ko;

                var OFFSET_F_i = OFFSET_F[i];
                var OFFSET_F_j = OFFSET_F[j];
                d0 = Distance3(Pf0, Pf1, Pf2, OFFSET_F_i + oxx, OFFSET_F_j + oyx, -0.5f + ozx);
                d1 = Distance3(Pf0, Pf1, Pf2, OFFSET_F_i + oxy, OFFSET_F_j + oyy, 0.5f + ozy);
                d2 = Distance3(Pf0, Pf1, Pf2, OFFSET_F_i + oxz, OFFSET_F_j + oyz, 1.5f + ozz);

                if (d0 < F0) { F2 = F1; F1 = F0; F0 = d0; }
                else if (d0 < F1) { F2 = F1; F1 = d0; }
                else if (d0 < F2) { F2 = d0; }

                if (d1 < F0) { F2 = F1; F1 = F0; F0 = d1; }
                else if (d1 < F1) { F2 = F1; F1 = d1; }
                else if (d1 < F2) { F2 = d1; }

                if (d2 < F0) { F2 = F1; F1 = F0; F0 = d2; }
                else if (d2 < F1) { F2 = F1; F1 = d2; }
                else if (d2 < F2) { F2 = d2; }
            }
        }
        
        
        return Mathf.Clamp01(Mathf.Pow(1f - Mathf.Clamp01(F0), worleyPow));
    }
    
    static float SampleSimplex(in Vector3 p, PermutationTable Perm)
    {
	    var x = p.x; var y = p.y; var z = p.z;
        //The 0.5 is to make the scale simliar to the other noise algorithms
        x *= 0.5f;
        y *= 0.5f;
        z *= 0.5f;

        // Simple skewing factors for the 3D case
        const float F3 = 0.333333333f;
        const float G3 = 0.166666667f;

        float n0, n1, n2, n3; // Noise contributions from the four corners

        // Skew the input space to determine which simplex cell we're in
        float s = (x+y+z)*F3; // Very nice and simple skew factor for 3D
        float xs = x+s;
        float ys = y+s;
        float zs = z+s;
        int i = Mathf.FloorToInt(xs);
        int j = Mathf.FloorToInt(ys);
        int k = Mathf.FloorToInt(zs);

        float t = (i+j+k)*G3; 
        float X0 = i-t; // Unskew the cell origin back to (x,y,z) space
        float Y0 = j-t;
        float Z0 = k-t;
        float x0 = x-X0; // The x,y,z distances from the cell origin
        float y0 = y-Y0;
        float z0 = z-Z0;

        // For the 3D case, the simplex shape is a slightly irregular tetrahedron.
        // Determine which simplex we are in.
        int i1, j1, k1; // Offsets for second corner of simplex in (i,j,k) coords
        int i2, j2, k2; // Offsets for third corner of simplex in (i,j,k) coords

        /* This code would benefit from a backport from the GLSL version! */
        if(x0>=y0)
        {
            if(y0>=z0) { i1=1; j1=0; k1=0; i2=1; j2=1; k2=0; } // X Y Z order
            else if(x0>=z0) { i1=1; j1=0; k1=0; i2=1; j2=0; k2=1; } // X Z Y order
            else { i1=0; j1=0; k1=1; i2=1; j2=0; k2=1; } // Z X Y order
        }
        else
        { // x0<y0
            if(y0<z0) { i1=0; j1=0; k1=1; i2=0; j2=1; k2=1; } // Z Y X order
            else if(x0<z0) { i1=0; j1=1; k1=0; i2=0; j2=1; k2=1; } // Y Z X order
            else { i1=0; j1=1; k1=0; i2=1; j2=1; k2=0; } // Y X Z order
        }

        // A step of (1,0,0) in (i,j,k) means a step of (1-c,-c,-c) in (x,y,z),
        // a step of (0,1,0) in (i,j,k) means a step of (-c,1-c,-c) in (x,y,z), and
        // a step of (0,0,1) in (i,j,k) means a step of (-c,-c,1-c) in (x,y,z), where
        // c = 1/6.

        float x1 = x0 - i1 + G3; // Offsets for second corner in (x,y,z) coords
        float y1 = y0 - j1 + G3;
        float z1 = z0 - k1 + G3;
        float x2 = x0 - i2 + 2.0f*G3; // Offsets for third corner in (x,y,z) coords
        float y2 = y0 - j2 + 2.0f*G3;
        float z2 = z0 - k2 + 2.0f*G3;
        float x3 = x0 - 1.0f + 3.0f*G3; // Offsets for last corner in (x,y,z) coords
        float y3 = y0 - 1.0f + 3.0f*G3;
        float z3 = z0 - 1.0f + 3.0f*G3;

        // Calculate the contribution from the four corners
        float t0 = 0.6f - x0*x0 - y0*y0 - z0*z0;
        if(t0 < 0.0) 
	        n0 = 0.0f;
        else 
        {
            t0 *= t0;
			n0 = t0 * t0 * Grad(Perm[i, j, k], x0, y0, z0);
        }

        float t1 = 0.6f - x1*x1 - y1*y1 - z1*z1;
        if(t1 < 0.0) 
	        n1 = 0.0f;
        else 
        {
            t1 *= t1;
			n1 = t1 * t1 * Grad(Perm[i+i1, j+j1, k+k1], x1, y1, z1);
        }

        float t2 = 0.6f - x2*x2 - y2*y2 - z2*z2;
        if(t2 < 0.0) 
	        n2 = 0.0f;
        else 
        {
            t2 *= t2;
			n2 = t2 * t2 * Grad(Perm[i+i2, j+j2, k+k2], x2, y2, z2);
        }

        float t3 = 0.6f - x3*x3 - y3*y3 - z3*z3;
        if(t3<0.0) 
	        n3 = 0.0f;
        else 
        {
            t3 *= t3;
			n3 = t3 * t3 * Grad(Perm[i+1, j+1, k+1], x3, y3, z3);
        }

        // Add contributions from each corner to get the final noise value.
        // The result is scaled to stay just inside [0,1]
        return (32.0f * (n0 + n1 + n2 + n3)) * 0.5f + 0.5f;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static float Grad(int hash, float x, float y, float z)
    {
        int h = hash & 15;      // Convert low 4 bits of hash code into 12 simple
        float u = h < 8 ? x : y; // gradient directions, and compute dot product.
        float v = h < 4 ? y : h == 12 || h == 14 ? x : z; // Fix repeats at h = 12 to 15
        return ((h & 1) != 0 ? -u : u) + ((h & 2) != 0 ? -v : v);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static float Frac(float v) => v - Mathf.Floor(v);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static float Distance3(float p1x, float p1y, float p1z, float p2x, float p2y, float p2z)
    {
	    return (p1x - p2x) * (p1x - p2x) + (p1y - p2y) * (p1y - p2y) + (p1z - p2z) * (p1z - p2z);
    }



    void UpdatePreviewTex()
    {
	    int Size = ResolutionXZ;
		
	    if(Tex == null || Tex.width != Size)
		    Tex = new Texture2D( Size, Size );
	    Color[] c = new Color[ Size * Size ];
	    Parallel.For( 0, Size, i =>
	    {
		    for( int j = 0; j < Size; j++ )
		    {
			    var uv = new Vector3( i / (float) Size, j / (float) Size, 0 );
			    var v = Sample( uv, false );
			    c[ i * Size + j ] = new Color( v, v, v );
		    }
	    } );
	    string str = "";
	    foreach( Color color in c )
	    {
		    if(color.r < 0f)
			    str += $"{color.r}\n";
	    }
	    if(str.Length > 0)
		    Debug.LogWarning( str );
	    Tex.SetPixels( c );
	    Tex.Apply();
    }
}
}