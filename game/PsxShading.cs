using System.Collections.Generic;
using Godot;

namespace TPWGodot
{
    /// <summary>The PlayStation GPU's textured drawing, shared by everything that draws the game's textures.</summary>
    public static class PsxShading
    {
        /// <summary>The GPU's texture blend, as a shader. ⚠ Three rules, each the hardware's and not a style:
        /// palette colour 0 is TRANSPARENT (the atlas stores it as alpha 0, so discard); the texel is MODULATED by
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
    if (t.a < 0.5) discard;
    ALBEDO = to_linear(clamp(t.rgb * COLOR.rgb * 2.0, 0.0, 1.0));
}}
",
            };
            _shaders[key] = sh;
            return sh;
        }
        static readonly Dictionary<string, Shader> _shaders = new();
    }
}
