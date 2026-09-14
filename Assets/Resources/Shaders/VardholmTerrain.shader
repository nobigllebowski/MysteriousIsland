// The ground.
//
// WHY THIS EXISTS AT ALL: ZoneMeshes writes a colour into every ground vertex, and Unity's
// Standard shader ignores mesh vertex colours entirely. The whole height gradient the mesh
// generator computes was being thrown away, and the terrain rendered as one flat tint -- which is
// exactly what "looks like a mockup" means.
//
// On top of the vertex colour this adds the three things that make ground read as ground: rock
// showing through where the slope is too steep to hold soil, sand where the land meets the water,
// and two octaves of world-space noise so no two square metres are the same shade. All procedural,
// no textures, one material, one draw call per zone.
Shader "Vardholm/Terrain"
{
    Properties
    {
        _Color          ("Tint", Color)                    = (1, 1, 1, 1)
        _RockColor      ("Rock", Color)                    = (0.42, 0.41, 0.38, 1)
        _SandColor      ("Sand", Color)                    = (0.62, 0.57, 0.46, 1)
        _WetColor       ("Wet sand", Color)                = (0.34, 0.31, 0.26, 1)
        _SeaLevel       ("Sea level", Float)               = 0.0
        _SandHeight     ("Sand band height", Float)        = 1.6
        _SandBlend      ("Sand band blend", Float)         = 1.4
        _SlopeStart     ("Rock starts at slope", Range(0, 1))   = 0.34
        _SlopeBlend     ("Rock blend", Range(0.01, 0.6))        = 0.22
        _MacroScale     ("Macro noise scale", Float)       = 0.021
        _MacroStrength  ("Macro noise strength", Range(0, 0.6)) = 0.22
        _DetailScale    ("Detail noise scale", Float)      = 0.34
        _DetailStrength ("Detail noise strength", Range(0, 0.5)) = 0.13
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        // Lambert rather than Standard: this island is overcast and matte, there is no specular
        // story to tell, and diffuse-only halves the fragment cost on the largest mesh in the game.
        #pragma surface surf Lambert fullforwardshadows
        #pragma target 3.0

        fixed4 _Color;
        fixed4 _RockColor;
        fixed4 _SandColor;
        fixed4 _WetColor;
        float _SeaLevel;
        float _SandHeight;
        float _SandBlend;
        float _SlopeStart;
        float _SlopeBlend;
        float _MacroScale;
        float _MacroStrength;
        float _DetailScale;
        float _DetailStrength;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
            float4 color : COLOR;
        };

        float hash21(float2 p)
        {
            p = frac(p * float2(127.1, 311.7));
            p += dot(p, p + 34.56);
            return frac(p.x * p.y);
        }

        float vnoise(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            float a = hash21(i);
            float b = hash21(i + float2(1.0, 0.0));
            float c = hash21(i + float2(0.0, 1.0));
            float d = hash21(i + float2(1.0, 1.0));
            return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
        }

        float fbm2(float2 p)
        {
            return vnoise(p) * 0.64 + vnoise(p * 2.13 + 11.7) * 0.36;
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            float3 wp = IN.worldPos;

            float macro = fbm2(wp.xz * _MacroScale);
            float detail = fbm2(wp.xz * _DetailScale);

            // The vertex colour is the mesh's own height gradient. It is the base, and everything
            // below modulates it rather than replacing it.
            float3 col = IN.color.rgb * _Color.rgb;
            col *= lerp(1.0 - _MacroStrength, 1.0 + _MacroStrength, macro);

            // Rock on the steep faces. The noise term pushed into the slope value breaks the
            // contour line that a pure slope threshold draws across a hillside -- that clean
            // altitude ring is the single most recognisable tell of procedural terrain.
            float slope = 1.0 - saturate(IN.worldNormal.y);
            float rock = smoothstep(_SlopeStart, _SlopeStart + _SlopeBlend, slope + (detail - 0.5) * 0.22);
            col = lerp(col, _RockColor.rgb * (0.78 + 0.44 * detail), rock);

            // Sand where the land comes down to the water, darkening to wet sand at the waterline.
            float aboveSea = wp.y - _SeaLevel;
            float sand = 1.0 - smoothstep(_SandHeight, _SandHeight + _SandBlend, aboveSea);
            sand *= 1.0 - rock * 0.75;
            float3 beach = lerp(_WetColor.rgb, _SandColor.rgb, saturate(aboveSea / max(_SandHeight, 0.001)));
            col = lerp(col, beach * (0.9 + 0.2 * detail), sand);

            col *= lerp(1.0 - _DetailStrength, 1.0 + _DetailStrength, detail);

            o.Albedo = col;
            o.Alpha = 1.0;
        }
        ENDCG
    }

    Fallback "Diffuse"
}
