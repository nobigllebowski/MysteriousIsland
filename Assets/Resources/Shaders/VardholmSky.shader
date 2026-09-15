// A sky, drawn rather than imported.
//
// The world had no skybox at all: the camera cleared to the fog colour, so every zone was a flat
// field of one grey behind the geometry. That single fact is most of what made the island read as a
// mockup -- there was no horizon, and without a horizon there is no sense of standing outdoors.
//
// Procedural because this project must run from a clone with no asset downloads, and because a
// gradient plus a sun costs one full-screen pass of arithmetic and no texture memory.
Shader "Vardholm/Sky"
{
    Properties
    {
        _ZenithColor  ("Zenith", Color)           = (0.33, 0.47, 0.62, 1)
        _HorizonColor ("Horizon", Color)          = (0.72, 0.76, 0.78, 1)
        _GroundColor  ("Below horizon", Color)    = (0.22, 0.23, 0.22, 1)
        _SunColor     ("Sun", Color)              = (1.00, 0.96, 0.88, 1)
        _SunDirection ("Sun direction", Vector)   = (0.4, 0.5, 0.75, 0)
        _SunAngle     ("Sun angular radius (deg)", Range(0.1, 6)) = 0.8
        _SunGlowWidth ("Sun glow width (deg)", Range(0.5, 40))   = 9
        _SunHaloWidth ("Sun halo width (deg)", Range(5, 120))    = 55
        _HorizonPower ("Horizon tightness", Range(0.2, 6)) = 1.4
        _CloudColor   ("Cloud", Color)            = (0.86, 0.87, 0.88, 1)
        _CloudCover   ("Cloud cover", Range(0, 1))         = 0.45
        _CloudSharp   ("Cloud edge", Range(0.02, 0.6))     = 0.22
        _CloudScale   ("Cloud scale", Range(0.5, 12))      = 3.5
    }

    SubShader
    {
        // Background queue, no depth write, no culling: the standard contract for a skybox, which
        // is drawn as a hull around the camera before anything else.
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            fixed4 _ZenithColor;
            fixed4 _HorizonColor;
            fixed4 _GroundColor;
            fixed4 _SunColor;
            float4 _SunDirection;
            float  _SunAngle;
            float  _SunGlowWidth;
            float  _SunHaloWidth;
            float  _HorizonPower;
            fixed4 _CloudColor;
            float  _CloudCover;
            float  _CloudSharp;
            float  _CloudScale;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            // The skybox hull's object-space vertex position IS the view direction; no matrix
            // needed, and it interpolates correctly across the face.
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

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

            float fbm(float2 p)
            {
                return vnoise(p) * 0.53 + vnoise(p * 2.07 + 19.3) * 0.27
                     + vnoise(p * 4.31 + 41.7) * 0.14 + vnoise(p * 8.13 + 7.1) * 0.06;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float up = d.y;

                // Two gradients meeting at the horizon. Above it the sky deepens toward the zenith;
                // below it the dome darkens toward the ground colour, which is what the player sees
                // past the edge of the island before the water plane takes over.
                float3 above = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(saturate(up), _HorizonPower));
                float3 below = lerp(_HorizonColor.rgb, _GroundColor.rgb, pow(saturate(-up), 0.6));
                float3 col = up >= 0.0 ? above : below;

                // Clouds, on a plane projected through the dome. Dividing by the vertical component
                // stretches the pattern toward the horizon exactly the way a real cloud deck
                // foreshortens, and the saturate() keeps the divide away from zero at eye level.
                float horizonFade = saturate(up * 6.0);
                float2 plane = d.xz / max(abs(up) + 0.18, 0.18) * _CloudScale;
                float clouds = fbm(plane);
                clouds = smoothstep(1.0 - _CloudCover, 1.0 - _CloudCover + _CloudSharp, clouds);
                col = lerp(col, _CloudColor.rgb, clouds * horizonFade * 0.85);

                // The sun: a small hard disc, a glow around it, and a broad halo over the sky.
                // The glow is what ties the sky to the directional light -- without it the light
                // has no visible source and the scene looks lit from nowhere.
                //
                // MEASURED AS A CHORD, NOT AS A DOT PRODUCT, and the previous version being a dot
                // product is why the first screenshot had a sun 43 times too wide. An angular size
                // written as `1 - _SunSize` has to be inverted through acos to mean anything:
                // _SunSize = 0.02 reads as a plausible "2%" and is an 11.5 degree disc, against the
                // real sun's 0.27. Worse, the honest value is unusable that way -- cos(0.27 deg) is
                // 0.99998896, and comparing numbers that close to 1 falls apart in fp32 and is
                // hopeless in the half precision a phone GPU may use here.
                //
                // The chord |d - sunDir| is 2*sin(theta/2), which for a small angle IS the angle in
                // radians, stays far from 1, and loses no precision. So the sun is specified in
                // degrees and compared in chord space.
                float3 sunDir = normalize(_SunDirection.xyz);
                float chord = length(d - sunDir);

                float discEdge = radians(_SunAngle);
                float disc = 1.0 - smoothstep(discEdge * 0.72, discEdge, chord);
                float glow = exp(-chord / max(radians(_SunGlowWidth), 1e-4));
                float halo = exp(-chord / max(radians(_SunHaloWidth), 1e-4)) * 0.16;

                // Nothing shines from under the world. The sun sets with the light rather than
                // burning through the sea, and the glow fades with it rather than snapping off.
                float aboveHorizon = smoothstep(-0.06, 0.04, sunDir.y);
                col += _SunColor.rgb * (disc * 2.0 + glow * 0.55 + halo) * aboveHorizon;

                // A pinch of dither. A smooth gradient across a whole screen is the one case where
                // 8-bit output bands visibly, and banding is a tell that reads as cheap.
                //
                // Keyed off the view direction rather than the pixel position: reading SV_POSITION
                // in a fragment shader needs the VPOS semantic and UNITY_VPOS_TYPE to be portable,
                // and this needs a cheap per-pixel hash, not screen coordinates specifically.
                float dither = (hash21(d.xz * 2048.0 + d.y * 977.0) - 0.5) * (1.0 / 255.0);
                return fixed4(col + dither, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
