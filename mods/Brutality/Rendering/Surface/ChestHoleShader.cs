namespace Meitou.Brutality.Rendering.Surface;

/// <summary>The punch-through hole's edge, shared by the colour, shadow and motion passes so all three open the same hole.</summary>
internal static class ChestHoleShader
{
    /// <summary>
    /// Signed bind-space distance from the hole's ragged edge (below 0 inside), measured on its tilted ellipse (<see cref="ChestHoleFrame"/>):
    /// shape is (aspect, tilt, tear, flare). The three sine amplitudes sum to <see cref="ChestHoleFrame.Ragged"/> before the tear scales
    /// them, which keeps the lining outside every edge; the flare widens the edge towards the exit as <c>ChestHoleFrame.Flared</c> does.
    /// </summary>
    public const string Field = """
        float chestHoleEdge(vec3 p, vec4 centre, vec4 axis, vec4 shape)
        {
            vec3 d = p - centre.xyz;
            float along = dot(d, axis.xyz);
            vec3 across = d - axis.xyz * along;
            vec3 side = normalize(cross(axis.xyz, vec3(0.0, 1.0, 0.0)));
            vec3 up = cross(side, axis.xyz);
            float c = cos(shape.y), s = sin(shape.y);
            float u = dot(across, side * c + up * s) / shape.x, v = dot(across, up * c - side * s) * shape.x;
            float turn = atan(v, u);
            float seed = axis.w;
            float rag = (0.11 * sin(turn * 5.0 + seed) + 0.07 * sin(turn * 11.0 + along * 2.3 + seed * 1.7)
                + 0.04 * sin(turn * 23.0 - along * 4.1 + seed * 2.9)) * shape.z;
            float span = 1.3 * centre.w;
            float flare = 1.0 + (shape.w - 1.0) * smoothstep(-span, span, along);
            return length(vec2(u, v)) - centre.w * (1.0 + rag) * flare;
        }

        // Cloth tears wider than the skin it covers and plate wider still, so the wound's own edge shows inside them.
        float chestHoleWidening(uint flags, uint anatomy, float plated)
        {
            if (anatomy != 0u) return 0.0;
            if (plated > 0.0) return 0.2;
            return (flags & 16384u) != 0u ? 0.12 : 0.0;
        }

        """;
}
