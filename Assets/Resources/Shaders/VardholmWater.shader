// The sea.
//
// The island was not an island. The ground mesh ended at a hard square edge with nothing beyond
// it, so the player was standing on a tile, not on land surrounded by water -- and no amount of
// shading the tile would have fixed that.
//
// Gerstner-lite: a sum of directional sine waves displacing the vertices, with the surface normal
// derived analytically from the same expression rather than recalculated on the mesh. That is what
// buys real moving highlights instead of a scrolling texture, and it costs nothing per frame on
// the CPU: the whole animation is _Time in the vertex shader.
Shader "Vardholm/Water"
{
    Properties
    {
        _DeepColor    ("Deep", Color)                     = (0.06, 0.13, 0.17, 1)
        _ShallowColor ("Shallow", Color)                  = (0.16, 0.30, 0.33, 1)
        _SkyColor     ("Sky reflection", Color)           = (0.58, 0.66, 0.72, 1)
        _FoamColor    ("Crest foam", Color)               = (0.82, 0.86, 0.87, 1)
        _WaveAmplitude("Wave height", Range(0, 1.5))      = 0.22
        _WaveLength   ("Wave length", Range(1, 40))       = 11
        _WaveSpeed    ("Wave speed", Range(0, 3))         = 0.75
        _RippleScale  ("Ripple scale", Range(0.05, 4))    = 0.9
        _RippleSpeed  ("Ripple speed", Range(0, 2))       = 0.35
        _FresnelPower ("Fresnel power", Range(0.5, 8))    = 3.5
        _Reflectivity ("Reflectivity", Range(0, 1))       = 0.55
        _FoamAmount   ("Foam on crests", Range(0, 1))     = 0.35
        _Opacity      ("Base opacity", Range(0, 1))       = 0.82
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        LOD 200

        CGPROGRAM
        // alpha:fade rather than premultiplied: the water is a flat sheet with nothing drawn
        // underneath it except the seabed slope of the island, so a straight blend is correct and
        // is one less thing to get wrong.
        #pragma surface surf Lambert vertex:vert alpha:fade
        #pragma target 3.0

        fixed4 _DeepColor;
        fixed4 _ShallowColor;
        fixed4 _SkyColor;
        fixed4 _FoamColor;
        float _WaveAmplitude;
        float _WaveLength;
        float _WaveSpeed;
        float _RippleScale;
        float _RippleSpeed;
        float _FresnelPower;
        float _Reflectivity;
        float _FoamAmount;
        float _Opacity;

        struct Input
        {
            float3 worldPos;
            float crest;
        };

        // Three waves crossing at angles that do not divide evenly into each other, so the pattern
        // does not visibly repeat within the distance the fog lets the player see.
        // #define rather than `static const`: this compiles through every backend Unity targets,
        // including the GLES ones where file-scope statics in a surface shader are a gamble.
        #define WaveDirA float2(0.92, 0.39)
        #define WaveDirB float2(-0.45, 0.89)
        #define WaveDirC float2(0.66, -0.75)

        float WaveHeight(float2 p, float k, float t, out float2 slope)
        {
            float kA = k;
            float kB = k * 1.71;
            float kC = k * 2.63;

            float pA = dot(p, WaveDirA) * kA + t * 1.00;
            float pB = dot(p, WaveDirB) * kB + t * 1.43;
            float pC = dot(p, WaveDirC) * kC + t * 0.77;

            float h = sin(pA) * 0.55 + sin(pB) * 0.30 + sin(pC) * 0.15;

            // The derivative of the same sum: the analytic surface normal, which is why the
            // highlights track the waves instead of sliding across a flat sheet.
            slope = WaveDirA * (cos(pA) * 0.55 * kA)
                  + WaveDirB * (cos(pB) * 0.30 * kB)
                  + WaveDirC * (cos(pC) * 0.15 * kC);

            return h;
        }

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);

            // THE SEA FOLLOWS THE CAMERA. The mesh is a disc of rings centred on its own origin,
            // and the zone's origin is the middle of the island: from the shore, 60-80 m out, the
            // near rim of a 240 m disc is only 160 m away and the fog has not hidden it. Shifting
            // every vertex by the camera's XZ re-centres the disc on the viewer each frame, so the
            // rim is always the full radius out and the dense inner rings are always underfoot.
            // The wave function is evaluated at WORLD position, so the surface itself does not
            // slide -- only the sampling grid does, which on a smooth surface is invisible.
            //
            // Only valid because ZoneBuilder places the Sea object with identity rotation and
            // scale: an object-space XZ offset is then a world-space one. VERIFY if that changes.
            v.vertex.xz += _WorldSpaceCameraPos.xz;

            float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
            float k = 6.2831853 / max(_WaveLength, 0.001);

            float2 slope;
            float h = WaveHeight(wp.xz, k, _Time.y * _WaveSpeed, slope);

            v.vertex.y += h * _WaveAmplitude;
            o.crest = saturate(h * 0.5 + 0.5);
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            float k = 6.2831853 / max(_WaveLength, 0.001);

            float2 slope;
            WaveHeight(IN.worldPos.xz, k, _Time.y * _WaveSpeed, slope);

            // Fine ripples on top of the swell, at a scale too small to be worth displacing
            // geometry for but exactly the scale that makes water look wet.
            float2 rp = IN.worldPos.xz * _RippleScale;
            float rt = _Time.y * _RippleSpeed;
            float2 rippleSlope = float2(
                cos(rp.x * 1.7 + rt * 2.1) * 0.5 + cos(rp.y * 2.3 - rt * 1.3) * 0.3,
                cos(rp.y * 1.9 - rt * 1.7) * 0.5 + cos(rp.x * 2.7 + rt * 1.1) * 0.3);

            float2 total = slope * _WaveAmplitude + rippleSlope * 0.045;

            // The water plane is generated flat and horizontal with tangent (1,0,0) and normal
            // (0,1,0), so tangent space here is (X, Z, Y) of world space. That correspondence is a
            // property of the mesh ZoneMeshes.BuildWater builds, not a general truth.
            o.Normal = normalize(float3(-total.x, -total.y, 1.0));

            float3 viewDir = normalize(_WorldSpaceCameraPos - IN.worldPos);
            float facing = saturate(dot(float3(0, 1, 0), viewDir));
            float fresnel = pow(1.0 - facing, _FresnelPower);

            float3 body = lerp(_DeepColor.rgb, _ShallowColor.rgb, IN.crest);
            float3 col = lerp(body, _SkyColor.rgb, saturate(fresnel * _Reflectivity));

            // Foam on the tops of the waves only. Sea foam at the shoreline needs the depth buffer
            // to find where the seabed meets the surface, and a depth pass is not a cost this game
            // pays on a phone for one effect.
            float foam = smoothstep(0.82 - _FoamAmount * 0.3, 0.98, IN.crest);
            col = lerp(col, _FoamColor.rgb, foam * _FoamAmount);

            o.Albedo = col;
            o.Alpha = saturate(_Opacity + fresnel * (1.0 - _Opacity));
        }
        ENDCG
    }

    Fallback "Diffuse"
}
