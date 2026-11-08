// Portal surface: shows what the linked portal sees (a render texture sampled in screen
// space, so it lines up exactly with the world behind it), with a rippling energy rim.
// With no view available it shows an animated energy swirl instead.
Shader "GravityPortals/PortalView"
{
    Properties
    {
        _MainTex ("View", 2D) = "black" {}
        _Color ("Portal Color", Color) = (0, 0.85, 1, 1)
        _HasView ("Has View", Float) = 0
        _Thick ("Thick (passing through)", Float) = 0
        _FlipY ("Flip View Y", Float) = 0
        _Clarity ("Clarity (1 = clean opening, no effects)", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Z Test (Always while your eye is right at the opening)", Float) = 4
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }

        Pass
        {
            Name "PortalView"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On
            ZTest [_ZTest]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _HasView;
                float _Thick;
                float _FlipY;
                float _Clarity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            // Screen position in Unity's texture convention (handles platform Y flips).
            float4 ScreenPosFromClip(float4 positionCS)
            {
                float4 o = positionCS * 0.5f;
                o.xy = float2(o.x, o.y * _ProjectionParams.x) + o.w;
                o.zw = positionCS.zw;
                return o;
            }

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.screenPos = ScreenPosFromClip(o.positionCS);
                o.uv = v.uv;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float2 suv = i.screenPos.xy / i.screenPos.w;
                if (_FlipY > 0.5) suv.y = 1.0 - suv.y;

                float t = _Time.y;
                float2 c = i.uv - 0.5;
                float r = length(c);
                float ang = atan2(c.y, c.x);
                float swirl = 0.5 + 0.5 * sin(t * 2.5 - r * 30.0 + ang * 3.0);

                // Clarity fades out every effect, leaving a clean window onto the other side
                // (doorways always, any portal as you get close to it).
                float effects = 1.0 - saturate(_Clarity);

                // A faint ripple across the view sells it as an energy membrane.
                float2 ripple = float2(sin(t * 3.0 + i.uv.y * 40.0), cos(t * 2.6 + i.uv.x * 40.0)) * 0.0015 * effects;
                half3 view = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, suv + ripple).rgb;

                half3 energy = _Color.rgb * (0.25 + 0.5 * swirl);
                half3 col = lerp(energy, view + _Color.rgb * 0.05 * swirl * effects, saturate(_HasView));

                // Glowing rim at the edge of the opening (off while the camera is inside it).
                float edge = saturate(max(abs(c.x), abs(c.y)) * 2.0);
                float rim = pow(edge, 10.0) * (1.0 - saturate(_Thick)) * lerp(0.15, 1.0, effects);
                col = lerp(col, _Color.rgb * 2.0, rim * 0.85);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
