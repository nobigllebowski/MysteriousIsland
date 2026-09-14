// Grass, fern and scrub.
//
// Flora in this world is two crossed quads per plant. Drawn with an opaque shader that is exactly
// what it looks like: a pair of solid cardboard rectangles standing in a field. This shader cuts
// the plant shape out of the quad procedurally, so the silhouette is blades and fronds, and moves
// it in the wind -- and a still world is the other half of why a screenshot reads as a mockup.
//
// The wind costs no CPU and no Update(). It is _Time in the vertex shader, which is the one place
// in this project where something is allowed to move without going through the Ticker, because
// nothing on the C# side ever learns that it moved.
Shader "Vardholm/Foliage"
{
    Properties
    {
        _BaseColor  ("Base", Color)                    = (0.10, 0.17, 0.10, 1)
        _TipColor   ("Tip", Color)                     = (0.28, 0.40, 0.20, 1)
        _Blades     ("Blades", Range(1, 5))            = 3
        _Width      ("Blade width", Range(0.02, 0.9))  = 0.42
        _Taper      ("Taper", Range(0, 1))             = 0.75
        _Bend       ("Bend", Range(0, 0.5))            = 0.22
        _Broadleaf  ("Broadleaf", Range(0, 1))         = 0
        _WindStrength ("Wind strength", Range(0, 0.6)) = 0.11
        _WindSpeed    ("Wind speed", Range(0, 4))      = 1.1
        _Cutoff     ("Alpha cutoff", Range(0, 1))      = 0.5
    }

    SubShader
    {
        // TransparentCutout, not Transparent: cutout sorts by depth like any opaque object, which
        // is the only way a field of overlapping plants can be drawn in any order and still be
        // correct. Alpha blending here would need a per-plant sort and would still be wrong.
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "IgnoreProjector" = "True" }
        LOD 200
        Cull Off

        CGPROGRAM
        #pragma surface surf LeafWrap vertex:vert alphatest:_Cutoff addshadow
        #pragma target 3.0

        fixed4 _BaseColor;
        fixed4 _TipColor;
        float _Blades;
        float _Width;
        float _Taper;
        float _Bend;
        float _Broadleaf;
        float _WindStrength;
        float _WindSpeed;

        struct Input
        {
            float2 leafUv;
        };

        // Wrap lighting instead of Lambert.
        //
        // These quads are two-sided (Cull Off) and every vertex normal points one way, so half the
        // blades in any plant face away from the sun and would render black under a straight N·L.
        // Wrapping the term around the terminator lights both faces, and is also simply how thin
        // translucent leaves behave: light comes through them as well as off them.
        half4 LightingLeafWrap(SurfaceOutput s, half3 lightDir, half atten)
        {
            half ndl = dot(s.Normal, lightDir);
            half wrap = saturate(ndl * 0.5 + 0.5);
            wrap = wrap * wrap * 0.75 + wrap * 0.25;

            half4 c;
            c.rgb = s.Albedo * _LightColor0.rgb * (wrap * atten * 2.0);
            c.a = s.Alpha;
            return c;
        }

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.leafUv = v.texcoord.xy;

            // Sway scaled by the square of height up the blade: the root does not move, the tip
            // moves most. The world position goes into the phase so neighbouring plants are out of
            // step with each other -- a field swaying in unison looks like an animation, not wind.
            float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
            float phase = wp.x * 0.7 + wp.z * 0.55;
            float gust = sin(_Time.y * _WindSpeed + phase) * 0.7
                       + sin(_Time.y * _WindSpeed * 2.3 + phase * 1.7) * 0.3;

            float height = v.texcoord.y * v.texcoord.y;
            v.vertex.x += gust * height * _WindStrength;
            v.vertex.z += gust * height * _WindStrength * 0.4;
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            float2 uv = IN.leafUv;
            float t = saturate(uv.y);

            // The plant shape, cut out of the quad. Each blade is a vertical band that narrows
            // toward the tip and leans over as it rises; a broadleaf is the same band widened into
            // an ellipse. One expression, two plant families, no textures.
            float blades = floor(_Blades + 0.5);
            float slot = floor(uv.x * blades);
            float local = frac(uv.x * blades) - 0.5;

            // Everything below is in slot-local units, where the slot spans [-0.5, 0.5]. Mixing
            // slot-local and quad-wide units here is the easy way to get a shape that silently
            // changes with the blade count.
            float jitter = frac(sin(slot * 91.7 + 3.1) * 43758.5453);

            // Blades lean apart rather than all one way: the slot index decides the direction, so
            // a tuft opens outward from its centre the way a real one grows.
            float side = sign(slot - (blades - 1.0) * 0.5 + 0.001);
            float lean = _Bend * t * t * side * (0.6 + jitter * 0.8);

            float halfWidth = _Width * 0.5 * (1.0 - _Taper * t);
            halfWidth *= lerp(1.0, sqrt(saturate(4.0 * t * (1.0 - t))) * 1.8, _Broadleaf);

            float alpha = step(abs(local - lean), halfWidth);

            // Blades of different lengths. Without this every tuft is a flat-topped hedge.
            float lengthLimit = 0.58 + jitter * 0.42;
            alpha *= step(t, lengthLimit);

            float3 col = lerp(_BaseColor.rgb, _TipColor.rgb, t);

            // The near edge of a blade catches more light than its middle; cheap, and it stops the
            // cutout silhouette from looking like a sticker.
            col *= 0.85 + 0.3 * saturate(1.0 - abs(local - lean) / max(halfWidth, 0.001));

            o.Albedo = col;
            o.Alpha = alpha;
        }
        ENDCG
    }

    Fallback "Transparent/Cutout/Diffuse"
}
