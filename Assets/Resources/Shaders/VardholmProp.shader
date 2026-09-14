// Rocks, bone, dressed stone, and anything else solid that is not the ground.
//
// The same problem the terrain had, in a smaller form: every rock in a zone shares one material,
// so every rock was precisely the same shade of grey, and twenty-six identical greys read as
// twenty-six copies of one prop rather than as a field of stone.
//
// The fix is world-space noise -- mottling that depends on where the object stands rather than on
// its UVs, which these procedurally generated hulls do not meaningfully have. Two rocks side by
// side get different patterns for free, and the pattern stays put when the player walks past.
Shader "Vardholm/Prop"
{
    Properties
    {
        _Color         ("Colour", Color)                 = (0.5, 0.5, 0.47, 1)
        _DarkColor     ("Crevice", Color)                = (0.22, 0.22, 0.20, 1)
        _MottleScale   ("Mottle scale", Float)           = 0.9
        _MottleStrength("Mottle strength", Range(0, 1))  = 0.42
        _GrainScale    ("Grain scale", Float)            = 7.0
        _GrainStrength ("Grain strength", Range(0, 0.4)) = 0.10
        _UpBleach      ("Top-face bleach", Range(0, 0.6))= 0.16
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Lambert fullforwardshadows
        #pragma target 3.0

        fixed4 _Color;
        fixed4 _DarkColor;
        float _MottleScale;
        float _MottleStrength;
        float _GrainScale;
        float _GrainStrength;
        float _UpBleach;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
        };

        float hash31(float3 p)
        {
            p = frac(p * 0.3183099 + float3(0.71, 0.113, 0.419));
            p += dot(p, p.yzx + 19.19);
            return frac((p.x + p.y) * p.z);
        }

        float vnoise3(float3 p)
        {
            float3 i = floor(p);
            float3 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);

            float n000 = hash31(i + float3(0, 0, 0));
            float n100 = hash31(i + float3(1, 0, 0));
            float n010 = hash31(i + float3(0, 1, 0));
            float n110 = hash31(i + float3(1, 1, 0));
            float n001 = hash31(i + float3(0, 0, 1));
            float n101 = hash31(i + float3(1, 0, 1));
            float n011 = hash31(i + float3(0, 1, 1));
            float n111 = hash31(i + float3(1, 1, 1));

            float x00 = lerp(n000, n100, f.x);
            float x10 = lerp(n010, n110, f.x);
            float x01 = lerp(n001, n101, f.x);
            float x11 = lerp(n011, n111, f.x);

            return lerp(lerp(x00, x10, f.y), lerp(x01, x11, f.y), f.z);
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            float3 wp = IN.worldPos;

            float mottle = vnoise3(wp * _MottleScale) * 0.65 + vnoise3(wp * _MottleScale * 2.4 + 5.1) * 0.35;
            float grain = vnoise3(wp * _GrainScale);

            float3 col = lerp(_DarkColor.rgb, _Color.rgb, saturate(mottle * _MottleStrength + (1.0 - _MottleStrength)));
            col *= lerp(1.0 - _GrainStrength, 1.0 + _GrainStrength, grain);

            // Upward faces catch the weather: salt, dust and bird lime all land on the top of a
            // rock and nowhere else, and that one cue does more for "this stone has been outside
            // for a century" than any amount of extra geometry.
            float upness = saturate(IN.worldNormal.y);
            col *= 1.0 + _UpBleach * upness * upness * mottle;

            o.Albedo = col;
            o.Alpha = 1.0;
        }
        ENDCG
    }

    Fallback "Diffuse"
}
