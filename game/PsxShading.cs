using System.Collections.Generic;
using Godot;

namespace TPWGodot
{
    /// <summary>The PlayStation GPU's textured drawing, shared by everything that draws the game's textures.</summary>
    public static class PsxShading
    {
        /// <summary>The GPU's texture blend, as a shader. ⚠ Three rules, each the hardware's and not a style:
        /// palette colour 0 is TRANSPARENT (the atlas stores it as alpha 0, so discard; alpha 0.5 marks the GPU's blend
        /// bit, which an opaque primitive ignores); the texel is MODULATED by
        /// the vertex colour with 128 as neutral, so × 2 (the models' vertex colours run 48..255 around 128,
        /// darkening and brightening as baked light); and sampling is NEAREST, no filtering. The product is a
        /// display-space colour, so it is converted to linear on the way out, or Godot's output encode would
        /// brighten it a second time.</summary>
        public static Shader Shader(bool cull)
        {
            string key = cull ? "back" : "disabled";
            if (_shaders.TryGetValue(key, out var sh)) return sh;
            sh = new Shader
            {
                Code = $@"shader_type spatial;
render_mode unshaded, cull_{key};
uniform sampler2D atlas : filter_nearest;
vec3 to_linear(vec3 c) {{
    return mix(pow((c + 0.055) / 1.055, vec3(2.4)), c / 12.92, lessThan(c, vec3(0.04045)));
}}
void fragment() {{
    vec4 t = texture(atlas, UV);
    if (t.a < 0.25) discard;
    ALBEDO = to_linear(clamp(t.rgb * COLOR.rgb * 2.0, 0.0, 1.0));
}}
",
            };
            _shaders[key] = sh;
            return sh;
        }
        static readonly Dictionary<string, Shader> _shaders = new();

        /// <summary>A semi-transparent primitive (MeshFace.SemiTransparent), as the GPU draws it, in two passes:
        /// the <paramref name="blended"/> = false pass draws the texels WITHOUT the blend bit solid (the GPU does not
        /// blend those even in a semi-transparent primitive), the true pass draws only the texels WITH it, combined
        /// with what is behind by the page's mode: 0 (B + F) / 2, 1 B + F, 2 B - F, 3 B + F / 4.
        /// ⚠ The GPU blends in display space and Godot in linear, so mode 0 and 3 come out a touch different in
        /// brightness; the choice of which texels blend, and how, is the GPU's.</summary>
        public static Shader SemiTransparentShader(int mode, bool blended, bool cull)
        {
            string key = $"semi{mode}{(blended ? "b" : "s")}{(cull ? "c" : "")}";
            if (_shaders.TryGetValue(key, out var sh)) return sh;
            string blend = !blended ? "blend_mix" : mode switch { 1 => "blend_add", 2 => "blend_sub", 3 => "blend_add", _ => "blend_mix" };
            string alpha = mode switch { 0 => "0.5", 3 => "0.25", _ => "1.0" };
            string pick = blended ? "if (t.a < 0.25 || t.a > 0.75) discard;" : "if (t.a < 0.75) discard;";
            string extra = blended ? $"ALPHA = {alpha};" : "";
            string modes = blended ? $"{blend}, depth_draw_never, " : "";
            sh = new Shader
            {
                Code = $@"shader_type spatial;
render_mode unshaded, {modes}cull_{(cull ? "back" : "disabled")};
uniform sampler2D atlas : filter_nearest;
vec3 to_linear(vec3 c) {{
    return mix(pow((c + 0.055) / 1.055, vec3(2.4)), c / 12.92, lessThan(c, vec3(0.04045)));
}}
void fragment() {{
    vec4 t = texture(atlas, UV);
    {pick}
    ALBEDO = to_linear(clamp(t.rgb * COLOR.rgb * 2.0, 0.0, 1.0));
    {extra}
}}
",
            };
            _shaders[key] = sh;
            return sh;
        }

        /// <summary>The same blend, plus the game's scrolling textures (TextureSheet.ScrollRects): a polygon whose
        /// texels lie in a scrolling rectangle carries that rectangle in CUSTOM0 (x, y, width, height in atlas
        /// pixels; width 0 for everything else), and the shader fetches row (r - scroll_rows) mod height where the
        /// GPU would fetch row r. That is exactly what the game's rewrite of those texels in VRAM makes the GPU
        /// see (0x80034194), without rewriting a texture every frame. <paramref name="cull"/> is Godot's cull mode:
        /// "disabled" draws from both sides; the game's single-sided polygons use ParkView.SingleSidedCull.</summary>
        public static Shader ScrollingShader(string cull = "disabled")
        {
            string key = "scroll_" + cull;
            if (_shaders.TryGetValue(key, out var sh)) return sh;
            sh = new Shader
            {
                Code = @"shader_type spatial;
render_mode unshaded, cull_" + cull + @";
uniform sampler2D atlas : filter_nearest;
uniform float scroll_rows = 0.0;
varying flat vec4 rect;
vec3 to_linear(vec3 c) {
    return mix(pow((c + 0.055) / 1.055, vec3(2.4)), c / 12.92, lessThan(c, vec3(0.04045)));
}
void vertex() {
    rect = CUSTOM0;
}
void fragment() {
    vec2 uv = UV;
    if (rect.z > 0.0) {
        vec2 size = vec2(textureSize(atlas, 0));
        vec2 px = uv * size - rect.xy;
        px.y = mod(px.y - scroll_rows, rect.w);
        uv = (rect.xy + px) / size;
    }
    vec4 t = texture(atlas, uv);
    if (t.a < 0.25) discard;
    ALBEDO = to_linear(clamp(t.rgb * COLOR.rgb * 2.0, 0.0, 1.0));
}
",
            };
            _shaders[key] = sh;
            return sh;
        }
    }
}
