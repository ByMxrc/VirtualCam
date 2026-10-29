// Skin de avatar Minecraft: Unlit + Alpha Cutout.
// - Unlit: no depende de luces (antes el modelo salía negro porque usaba un
//   material Legacy/Diffuse en una escena sin luces).
// - Cutout: recorta los píxeles transparentes de las capas exteriores
//   (gorro, chaqueta, brazos) sin ordenar transparencias.
// Vive en Assets/Resources para que Unity lo incluya SIEMPRE en los builds.
Shader "VTuberCam/SkinCutout"
{
    Properties
    {
        _MainTex ("Skin", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "Queue" = "AlphaTest"
            "RenderType" = "TransparentCutout"
            "IgnoreProjector" = "True"
        }

        LOD 100
        Lighting Off
        ZWrite On
        Cull Back

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Cutoff;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * _Color;
                clip(c.a - _Cutoff);
                return c;
            }
            ENDCG
        }
    }

    Fallback Off
}
