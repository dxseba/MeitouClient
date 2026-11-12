namespace Meitou.Brutality.Effects;

// Liquid and electrical shape functions are retained from the pinned particle shader. GPU transport is mod-owned.
internal static class GoreEffectShaders
{
    public const string Vertex = """
        #version 330 core
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec2 aUv;
        layout(location = 2) in vec4 aColour;
        layout(location = 3) in vec3 aLiquidEnds;
        layout(location = 4) in vec3 aNormal;
        uniform mat4 uViewProjection;
        uniform vec3 uEye;
        uniform vec3 uCentre;
        uniform int uTissueSkinned;
        uniform mat4 uTissuePose[9];
        out vec2 vUv;
        out vec4 vColour;
        out vec3 vWorld;
        out vec3 vNormal;
        flat out vec3 vLiquidEnds;
        void main() {
            vec3 position = aPosition;
            vec3 normal = aNormal;
            if (uTissueSkinned != 0) {
                // Tissue does not sample UVs. They carry the first two weights, with the third and node IDs in the auxiliary channel.
                int packedNodes = int(aLiquidEnds.y);
                ivec4 nodes = (ivec4(packedNodes) >> ivec4(0, 4, 8, 12)) & ivec4(15);
                vec3 w = vec3(aUv, aLiquidEnds.x);
                mat4 skin = uTissuePose[nodes.x] * w.x + uTissuePose[nodes.y] * w.y
                          + uTissuePose[nodes.z] * w.z + uTissuePose[nodes.w] * (1.0 - w.x - w.y - w.z);
                position = (skin * vec4(aPosition, 1.0)).xyz;
                vec3 posedNormal = mat3(skin) * aNormal;
                if (dot(posedNormal, posedNormal) > 1e-12) normal = normalize(posedNormal);
            }
            vec3 p = position + uCentre;
            gl_Position = uViewProjection * vec4(p, 1.0);
            vUv = aUv; vColour = aColour; vWorld = p + uEye; vLiquidEnds = aLiquidEnds; vNormal = normal;
        }
        """;

    public const string Fragment = """
        #version 330 core
        in vec2 vUv;
        in vec4 vColour;
        in vec3 vWorld;
        in vec3 vNormal;
        flat in vec3 vLiquidEnds;
        uniform sampler2D uTexture;
        uniform int uDischarge;
        uniform int uCoverage;
        uniform vec3 uSunDirection;
        uniform vec3 uBowelSun;
        uniform vec3 uBowelSky;
        uniform vec3 uBowelGround;
        uniform vec4 uAtmoParams;
        uniform vec4 uAtmoLight;
        uniform vec3 uAtmoSunLight;
        uniform vec4 uAtmoMaps;
        uniform samplerCube uAtmoIrradiance;
        uniform sampler2D uAtmoAmbientMap;
        uniform vec3 uEye;
        out vec4 fragColour;
        float sootHash(vec2 p) { return fract(sin(dot(p, vec2(17.17, 91.37))) * 143758.67); }
        float sootNoise(vec2 p)
        {
            vec2 cell = floor(p), f = fract(p);
            f = f * f * (3.0 - 2.0 * f);
            return mix(mix(sootHash(cell), sootHash(cell + vec2(1.0, 0.0)), f.x),
                mix(sootHash(cell + vec2(0.0, 1.0)), sootHash(cell + vec2(1.0)), f.x), f.y);
        }
        vec4 soot(vec2 uv)
        {
            vec2 p = uv * 2.0 - 1.0;
            float cloud = 0.6 * sootNoise(p * 3.4 + 3.0) + 0.3 * sootNoise(p * 7.1 + 8.0) + 0.1 * sootNoise(p * 14.2);
            float edge = length(p) + (0.5 - cloud) * 0.36;
            return vec4(vec3(1.0), (1.0 - smoothstep(0.25, 1.0, edge)) * (0.65 + 0.35 * cloud));
        }
        vec4 liquidDrop(vec2 uv)
        {
            vec2 p = uv * 2.0 - 1.0;
            float taper = mix(0.72, 1.0, smoothstep(-1.0, 0.6, p.y));
            float r = length(vec2(p.x / taper, p.y));
            float alpha = 1.0 - smoothstep(0.68, 1.0, r);
            float rim = smoothstep(0.15, 0.95, r);
            return vec4(vec3(1.2 - rim * 0.55), alpha);
        }
        vec4 liquidStream(vec2 uv)
        {
            float across = abs(uv.x * 2.0 - 1.0);
            float cap = 1.0;
            if (vLiquidEnds.x > 0.5) cap = min(cap, sqrt(max(0.0, 1.0 - pow(max(0.0, 1.0 - uv.y * vLiquidEnds.z * 2.0), 2.0))));
            if (vLiquidEnds.y > 0.5) cap = min(cap, sqrt(max(0.0, 1.0 - pow(max(0.0, 1.0 - (1.0 - uv.y) * vLiquidEnds.z * 2.0), 2.0))));
            // World-space variation crosses internal joins without restarting an end fade or a bright highlight.
            float variation = sootNoise(vec2(dot(vWorld, vec3(1.7, 0.8, 0.3)), dot(vWorld, vec3(-0.4, 1.1, 1.5))));
            float radius = cap * (0.9 + variation * 0.1);
            float feather = max(fwidth(across), 0.035);
            float alpha = 1.0 - smoothstep(radius - feather, radius, across);
            float normalZ = sqrt(max(0.0, 1.0 - across * across));
            float shade = 0.45 + 0.62 * normalZ;
            return vec4(vec3(shade), alpha);
        }
        float robotFlashSegment(vec2 p, vec2 a, vec2 b)
        {
            vec2 along = b - a;
            float t = clamp(dot(p - a, along) / dot(along, along), 0.0, 1.0);
            return length(p - a - along * t);
        }
        vec4 robotBreakFlash(vec2 uv)
        {
            vec2 p = uv * 2.0 - 1.0;
            vec2 root = vec2(-0.04, 0.02);
            // Unequal crooked paths and an offshoot keep the contact flash from becoming a radial shell or star.
            float arc = robotFlashSegment(p, root, vec2(-0.22, 0.10));
            arc = min(arc, robotFlashSegment(p, vec2(-0.22, 0.10), vec2(-0.40, 0.03)));
            arc = min(arc, robotFlashSegment(p, vec2(-0.40, 0.03), vec2(-0.71, 0.34)));
            arc = min(arc, robotFlashSegment(p, root, vec2(0.19, 0.20)));
            arc = min(arc, robotFlashSegment(p, vec2(0.19, 0.20), vec2(0.24, 0.08)));
            arc = min(arc, robotFlashSegment(p, vec2(0.24, 0.08), vec2(0.49, 0.40)));
            arc = min(arc, robotFlashSegment(p, root, vec2(0.12, -0.11)));
            arc = min(arc, robotFlashSegment(p, vec2(0.12, -0.11), vec2(0.40, -0.15)));
            arc = min(arc, robotFlashSegment(p, vec2(0.40, -0.15), vec2(0.60, -0.08)));
            arc = min(arc, robotFlashSegment(p, vec2(-0.22, 0.10), vec2(-0.18, 0.31)) + 0.004);
            arc = min(arc, robotFlashSegment(p, vec2(-0.18, 0.31), vec2(-0.36, 0.47)) + 0.004);
            float feather = max(fwidth(arc), 0.003);
            float strand = 1.0 - smoothstep(0.009, 0.009 + feather, arc);
            float glow = exp(-arc * 48.0) * 0.18;
            vec2 hot = (p - root) * vec2(8.5, 11.0);
            vec2 lobe = (p - vec2(0.075, -0.045)) * vec2(16.0, 12.0);
            float core = max(exp(-dot(hot, hot)), exp(-dot(lobe, lobe)) * 0.7);
            core *= 0.45 + 0.55 * sootNoise(p * 18.0 + 6.1);
            vec3 colour = mix(vec3(0.60, 0.85, 1.0), vec3(2.2, 1.35, 1.0), core);
            return vec4(colour, clamp(core + strand * 0.85 + glow, 0.0, 1.0));
        }

        // The integrated bowel's diffuse and dielectric response. The mod pass reads the scene's
        // light and textures without pulling in the world shader's shadow, GI or lamp buffers.
        vec3 bowelLight(vec3 albedo, vec3 n, vec3 view)
        {
            if (uAtmoParams.x <= 0.5) {
                vec3 ambient = mix(uBowelGround, uBowelSky, 0.5 + 0.5 * n.y);
                vec3 direction = normalize(vec3(uSunDirection.x, max(uSunDirection.y, 0.02), uSunDirection.z));
                float diffuse = max(dot(n, direction), 0.0);
                vec3 sunlight = diffuse * uBowelSun;
                vec3 light = ambient + sunlight;
                if (vLiquidEnds.z > 0.5) {
                    // Match the daylight range of the world HDR target, whose exposure is applied after this pass.
                    float daylight = smoothstep(-0.025, 0.36, uSunDirection.y);
                    float skyFacing = 0.5 + 0.5 * n.y;
                    vec3 bounce = vec3(mix(1.09, 1.28, skyFacing));
                    light = mix(light, bounce + 0.48 * sunlight, daylight);
                }
                float wet = pow(max(dot(reflect(-direction, n), view), 0.0), 32.0);
                return albedo * light + (0.04 * diffuse * wet) * uBowelSun;
            }

            vec4 map = vec4(1.0, 1.0, 1.0, 0.5);
            if (uAtmoMaps.z > 0.5) {
                map = texture(uAtmoAmbientMap, (vWorld.xz + uAtmoMaps.w) / (2.0 * uAtmoMaps.w));
                float luminance = dot(map.rgb, vec3(0.2126, 0.7152, 0.0722));
                map.rgb *= max(1.0, (uAtmoMaps.z - 1.0) / max(luminance, 1e-3));
            }
            vec3 irradiance = vec3(1.09, 1.20, 1.51);
            if (uAtmoMaps.x > 0.5) {
                vec4 sky = textureLod(uAtmoIrradiance, n, 3.0);
                irradiance = sky.rgb * sky.a * 4.0;
            }

            const float pi = 3.14159265359;
            const float gloss = 0.8;
            const float roughness = 1.0 - gloss * 0.99;
            const float alpha = roughness * roughness;
            const float alphaSquared = alpha * alpha;
            const float dielectric = 0.04;
            vec3 direction = uAtmoLight.xyz;
            vec3 sun = uAtmoSunLight * (map.a * 2.0);
            float nl = max(dot(n, direction), 0.0);
            vec3 halfVector = normalize(view + direction);
            float nh = max(dot(n, halfVector), 0.0);
            float lh = max(dot(direction, halfVector), 0.0);
            float denom = max(nh * nh * (alphaSquared - 1.0) + 1.0, 1e-6);
            float distribution = alphaSquared / (pi * denom * denom);
            float fresnel = dielectric + (1.0 - dielectric) * exp2((-5.55473 * lh - 6.98316) * lh);
            float k = alpha * 0.5;
            float visibility = 1.0 / (lh * lh * (1.0 - k * k) + k * k);
            vec3 diffuse = sun * (pi * nl) + irradiance * map.rgb * uAtmoLight.w;
            return albedo * diffuse * (1.0 - dielectric) + sun * (nl * distribution * fresnel * visibility / pi);
        }

        void main() {
            if (uDischarge == 8) {
                if (uCoverage != 0) {
                    fragColour = vec4(uCoverage == 2 ? 2.0 : 1.0);
                    return;
                }
                vec3 n = normalize(vNormal);
                if (!gl_FrontFacing) n = -n;
                fragColour = vec4(bowelLight(vColour.rgb, n, normalize(uEye - vWorld)), 1.0);
                return;
            }
            vec2 uv = uDischarge == 1 ? vUv.yx : vUv;
            if (uDischarge == 4) uv.x = 0.34 + uv.x * 0.32;
            vec4 t = uDischarge == 7 ? robotBreakFlash(vUv) : uDischarge == 6 ? liquidStream(vUv) : uDischarge == 5 ? liquidDrop(vUv) : uDischarge == 3 ? soot(vUv) : texture(uTexture, uv);
            // Negative vertex RGB is the pinned particle ABI's recolour marker, not negative emitted light.
            if (min(vColour.r, min(vColour.g, vColour.b)) < 0.0) t.rgb = vec3(max(t.r, max(t.g, t.b)));
            vec4 c = t * abs(vColour);
            if (uDischarge == 9) {
                vec3 n = normalize(vNormal);
                float diffuse = max(dot(n, uSunDirection), 0.0);
                vec3 halfVector = normalize(uSunDirection + normalize(uEye - vWorld));
                float wet = pow(max(dot(n, halfVector), 0.0), 32.0) * 0.12;
                c = vec4(vColour.rgb * (0.28 + 0.72 * diffuse) + vec3(wet), 1.0);
            }
            if (uDischarge == 5) {
                vec2 glintAt = (vUv - vec2(0.36, 0.30)) * vec2(12.0, 9.0);
                float glint = exp(-dot(glintAt, glintAt));
                // The first auxiliary channel marks stock droplets with the darker organic pigment.
                if (vLiquidEnds.x > 0.5) c.rgb = mix(c.rgb, vec3(0.49, 0.39, 0.35), glint * 0.17);
                else c.rgb = mix(c.rgb, vec3(0.7), glint * 0.32);
            }
            bool additive = uDischarge == 1 || uDischarge == 2 || uDischarge == 4 || uDischarge == 7;
            if (additive) c.rgb *= c.a;
            if (uCoverage != 0) {
                float amount = additive ? max(c.r, max(c.g, c.b)) * c.a : c.a;
                c = vec4(clamp(amount * (uDischarge == 3 ? 1.0 : 30.0), 0.0, uCoverage == 2 ? 2.0 : 1.0));
            }
            fragColour = c;
        }
        """;
}
