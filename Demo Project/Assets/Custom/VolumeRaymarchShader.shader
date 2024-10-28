Shader "Custom/VolumeRaymarchShader"
{
    Properties
    {
        _MainTex ("Texture", 3D) = "" {}
        _MinVal("Min val", Range(0.0, 1.0)) = 0.0
        _MaxVal("Max val", Range(0.0, 1.0)) = 1.0
        [HideInInspector] _TextureSize("Dataset dimensions", Vector) = (1, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        LOD 100
        Cull Front
        ZTest LEqual
        ZWrite On
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct vert_in
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct frag_in
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 vertexLocal : TEXCOORD1;
            };

            struct frag_out
            {
                float4 colour : SV_TARGET;
                float depth : SV_DEPTH;
            };

            sampler3D _MainTex;
            float _MinVal;
            float _MaxVal;
            float3 _TextureSize;

            // Gets the color at the specified position
            float4 getColor(float3 pos)
            {
#if defined(SHADER_API_D3D11) || defined(SHADER_API_METAL) || defined(SHADER_API_VULKAN) || defined(SHADER_API_GLCORE) || defined(SHADER_API_GLES3)
                return tex3Dlod(_MainTex, float4(pos.x, pos.y, pos.z, 0.0f));
#else
                return tex3D(_MainTex, pos);
#endif
            }

            struct RayInfo
            {
                float3 startPos;
                float3 endPos;
                float3 direction;
                float2 aabbInters;
            };

            struct RaymarchInfo
            {
                RayInfo ray;
                int numSteps;
                float numStepsRecip;
                float stepSize;
            };

            float3 getViewRayDir(float3 vertexLocal)
            {
                if(unity_OrthoParams.w == 0)
                {
                    // Perspective
                    return normalize(ObjSpaceViewDir(float4(vertexLocal, 0.0f)));
                }
                else
                {
                    // Orthographic
                    float3 camfwd = mul((float3x3)unity_CameraToWorld, float3(0,0,-1));
                    float4 camfwdobjspace = mul(unity_WorldToObject, camfwd);
                    return normalize(camfwdobjspace);
                }
            }

            float2 intersectAABB(float3 rayOrigin, float3 rayDir, float3 boxMin, float3 boxMax)
            {
                float3 tMin = (boxMin - rayOrigin) / rayDir;
                float3 tMax = (boxMax - rayOrigin) / rayDir;
                float3 t1 = min(tMin, tMax);
                float3 t2 = max(tMin, tMax);
                float tNear = max(max(t1.x, t1.y), t1.z);
                float tFar = min(min(t2.x, t2.y), t2.z);
                return float2(tNear, tFar);
            };

            RayInfo getRayBack2Front(float3 vertexLocal)
            {
                RayInfo ray;
                ray.direction = getViewRayDir(vertexLocal);
                ray.startPos = vertexLocal + float3(0.5f, 0.5f, 0.5f);
                ray.aabbInters = intersectAABB(ray.startPos, ray.direction, float3(0.0, 0.0, 0.0), float3(1.0f, 1.0f, 1.0));
                ray.endPos = ray.startPos + ray.direction * ray.aabbInters.y;
                return ray;
            }

            RaymarchInfo initRaymarch(RayInfo ray, int maxNumSteps)
            {
                RaymarchInfo raymarchInfo;
                raymarchInfo.stepSize = 1.732f / maxNumSteps;
                raymarchInfo.numSteps = (int)clamp(abs(ray.aabbInters.x - ray.aabbInters.y) / raymarchInfo.stepSize, 1, maxNumSteps);
                raymarchInfo.numStepsRecip = 1.0 / raymarchInfo.numSteps;
                return raymarchInfo;
            }

            float localToDepth(float3 localPos)
            {
                float4 clipPos = UnityObjectToClipPos(float4(localPos, 1.0f));
                return (clipPos.z / clipPos.w) * 0.5 + 0.5;
            }

            // Maximum Intensity Projection mode
            frag_out frag_mip(frag_in i)
            {
                #define MAX_NUM_STEPS 512

                RayInfo ray = getRayBack2Front(i.vertexLocal);
                RaymarchInfo raymarchInfo = initRaymarch(ray, MAX_NUM_STEPS);

                float maxDensity = 0.0f;
                float3 maxDensityPos = ray.startPos;
                float4 maxColor = float4(0, 0, 0, 0);
                for (int iStep = 0; iStep < raymarchInfo.numSteps; iStep++)
                {
                    const float t = iStep * raymarchInfo.numStepsRecip;
                    const float3 currPos = lerp(ray.startPos, ray.endPos, t);
                    
                    const float4 color = getColor(currPos);
                    if (color.a > maxDensity && color.a > _MinVal && color.a < _MaxVal)
                    {
                        maxDensity = color.a;
                        maxDensityPos = currPos;
                        maxColor = color;
                    }
                }

                // Write fragment output
                frag_out output;
                output.colour = float4(maxColor.rgb, maxDensity); // maximum intensity with color
                output.depth = localToDepth(maxDensityPos - float3(0.5f, 0.5f, 0.5f));
                return output;
            }

            frag_in vert(vert_in v)
            {
                frag_in o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.vertexLocal = v.vertex;
                return o;
            }

            frag_out frag(frag_in i)
            {
                return frag_mip(i);
            }

            ENDCG
        }
    }
}
