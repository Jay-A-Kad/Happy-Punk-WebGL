Shader "Custom/PunkCartoon"
{
    // Comic-book cel shading post-process for URP.
    // Applied via PunkCartoonRendererFeature on the URP Renderer asset.
    //
    // Technique:
    //   1. Posterize — quantises colours into flat bands (cel shading).
    //   2. Sobel edge detection — draws clean black ink outlines.
    //   3. Color grading — lifts saturation and contrast for a printed-ink feel.
    //   4. Halftone dots — subtle dot-screen pattern evokes comic-book printing.
    //   5. Vignette — mild darkening toward the frame edge for depth.
    //
    // Film grain, scanlines, and chromatic aberration are intentionally
    // removed — they add photographic noise, not cartoon clarity.
    //
    // WebGL note: Uses the Blitter API (_BlitTexture + sampler_LinearClamp).
    // Vertex shader uses the Blit.hlsl Vert entry point which is compatible
    // with both RenderGraph and Compatibility Mode paths.

    Properties
    {
        // _BlitTexture is set automatically by the Blitter API - do not rename
        _BlitTexture ("Source", 2D) = "white" {}

        [Header(Cel Shading)]
        _PosterizeLevels ("Posterize Levels", Range(2, 12)) = 5

        [Header(Outline)]
        _OutlineThickness  ("Outline Thickness",  Range(0.5, 3.0)) = 1.0
        _OutlineThreshold  ("Outline Threshold",  Range(0.01, 0.5)) = 0.08
        _OutlineColor      ("Outline Color",      Color) = (0.04, 0.02, 0.06, 1)

        [Header(Color Grading)]
        _Saturation  ("Saturation",  Range(0.5, 2.0)) = 1.25
        _Contrast    ("Contrast",    Range(0.5, 2.0)) = 1.20
        _Brightness  ("Brightness",  Range(0.6, 1.3)) = 1.00

        [Header(Halftone)]
        _HalftoneScale    ("Halftone Scale",    Range(0, 200)) = 80
        _HalftoneStrength ("Halftone Strength", Range(0, 0.25)) = 0.07

        [Header(Vignette)]
        _VignetteStrength ("Vignette Strength", Range(0, 1.2)) = 0.30
        _VignetteFalloff  ("Vignette Falloff",  Range(1, 6))   = 2.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "PunkCartoon"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            // Required for WebGL ES3.0 compatibility — avoids derivative ops in unlit passes
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // ── Uniforms ─────────────────────────────────────────────────────────

            float  _PosterizeLevels;

            float  _OutlineThickness;
            float  _OutlineThreshold;
            float4 _OutlineColor;

            float  _Saturation;
            float  _Contrast;
            float  _Brightness;

            float  _HalftoneScale;
            float  _HalftoneStrength;

            float  _VignetteStrength;
            float  _VignetteFalloff;

            // ── Helpers ──────────────────────────────────────────────────────────

            // Renamed to avoid collision with Luminance() already defined in Core.hlsl.
            float ComicLum(float3 c)
            {
                return dot(c, float3(0.2126, 0.7152, 0.0722));
            }

            // Quantise to flat colour bands — the core of cel shading.
            // Uses round() so bands are symmetric and don't bias to darks.
            float3 Posterize(float3 c, float levels)
            {
                return round(c * (levels - 1.0)) / (levels - 1.0);
            }

            // Signed-distance circle for the halftone dot.
            // Returns 1 inside the dot, 0 outside.
            float HalftoneDot(float2 uv, float scale, float brightness)
            {
                float2 cell  = frac(uv * scale) - 0.5;
                // Radius proportional to the inverse of local brightness:
                // dark areas → big dots, bright areas → small dots (classic print).
                float radius = sqrt(1.0 - saturate(brightness)) * 0.45;
                return 1.0 - smoothstep(radius - 0.04, radius + 0.04, length(cell));
            }

            float3 SampleScene(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
            }

            // ── Fragment ──────────────────────────────────────────────────────────

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv    = input.texcoord;
                float2 texel = _BlitTexture_TexelSize.xy;

                // ── 1. Sample ────────────────────────────────────────────────────
                float3 col = SampleScene(uv);

                // ── 2. Sobel Edge Detection (luminance) ──────────────────────────
                float2 off = texel * _OutlineThickness;

                float l00 = ComicLum(SampleScene(uv + float2(-off.x, -off.y)));
                float l10 = ComicLum(SampleScene(uv + float2(   0.0, -off.y)));
                float l20 = ComicLum(SampleScene(uv + float2( off.x, -off.y)));
                float l01 = ComicLum(SampleScene(uv + float2(-off.x,    0.0)));
                float l21 = ComicLum(SampleScene(uv + float2( off.x,    0.0)));
                float l02 = ComicLum(SampleScene(uv + float2(-off.x,  off.y)));
                float l12 = ComicLum(SampleScene(uv + float2(   0.0,  off.y)));
                float l22 = ComicLum(SampleScene(uv + float2( off.x,  off.y)));

                float gx = -l00 - 2.0*l01 - l02 + l20 + 2.0*l21 + l22;
                float gy = -l00 - 2.0*l10 - l20 + l02 + 2.0*l12 + l22;
                float edge      = sqrt(gx*gx + gy*gy);
                float outlineMask = smoothstep(_OutlineThreshold, _OutlineThreshold * 1.8, edge);

                // ── 3. Posterize (cel shading) ───────────────────────────────────
                col = Posterize(col, _PosterizeLevels);

                // ── 4. Color Grading ─────────────────────────────────────────────
                col *= _Brightness;
                col  = (col - 0.5) * _Contrast + 0.5;

                float lum = ComicLum(col);
                col = lerp(float3(lum, lum, lum), col, _Saturation);
                col = saturate(col);

                // ── 5. Ink Outline ───────────────────────────────────────────────
                // Smoothstep blend so the outline has clean, anti-aliased edges.
                col = lerp(col, _OutlineColor.rgb, outlineMask);

                // ── 6. Halftone dot-screen overlay ───────────────────────────────
                // Only visible in mid-to-dark regions; barely perceptible in brights.
                // Branch replaced with lerp to avoid dynamic branching on WebGL.
                float halftoneActive = step(0.001, _HalftoneStrength);
                float cellLum = ComicLum(col);
                float dot_    = HalftoneDot(uv, _HalftoneScale, cellLum);
                col -= dot_ * _HalftoneStrength * (1.0 - cellLum) * halftoneActive;
                col  = saturate(col);

                // ── 7. Vignette ──────────────────────────────────────────────────
                // Branch replaced with lerp to avoid dynamic branching on WebGL.
                float vigActive = step(0.001, _VignetteStrength);
                float2 vigUV  = uv - 0.5;
                float  vd     = dot(vigUV, vigUV);
                float  vig    = 1.0 - saturate(
                    pow(vd * 2.0, _VignetteFalloff * 0.5) * _VignetteStrength);
                col *= lerp(1.0, vig, vigActive);

                return float4(saturate(col), 1.0);
            }

            ENDHLSL
        }
    }

    FallBack Off
}
