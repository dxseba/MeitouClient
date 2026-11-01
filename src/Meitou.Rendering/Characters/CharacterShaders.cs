using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meitou.Rendering.Characters;

/// <summary>
/// The character programs, native GLSL 450 on the native model's sets (docs/renderer-native.md 3.3; docs/character-renderer.md): set 0's
/// two extra storage buffers hold the frame's bone palettes (binding 6) and the materials of the parts in view (binding 7), and the
/// instances carry the model matrix, the palette's first bone and the material's index as per-instance vertex inputs, so one instanced draw
/// covers every character that shows the same mesh part whatever its textures, skin or hair colour.
/// </summary>
static class CharacterShaders
{
    /// <summary>The first per-instance input location (the mesh's own inputs, <c>Vertex</c>, are at 0 to 6): four matrix rows, then <see cref="DataLocation"/>.</summary>
    public const int InstanceLocation = 7, DataLocation = 11, PreviousLocation = 12;
    /// <summary>The storage buffers of set 0 after the frame's own (the order of the extra list given to <c>NativeFrame.Prepare</c>): the bone palettes, the materials, the face morph offsets, last frame's palettes (motion pass).</summary>
    /// <summary>A wounded body's palette starts one entry early: that entry's first float holds the bits of its wound mask's bindless index (<see cref="WoundMasks"/>).</summary>
    public const int WoundHeaderBones = 33;
    /// <summary>Instance flag of the anatomy part: it shows only within the wound spheres in entries 1 and 2 of the header (bind position, radius).</summary>
    public const uint AnatomyFlag = 1u << 30;
    /// <summary>Bind height of a human face's eyes, a tuning value.</summary>
    const string EyeHeight = "17.9";
    public const uint WoundFlag = 1u << 31, TearFlag = 1u << 2, PlateFlag = 1u << 3;
    public const uint BonesBinding = 6, MaterialsBinding = 7, MorphsBinding = 8, PreviousBonesBinding = 9;
    /// <summary>The texture slots of a material (<c>uint tex[]</c> in the shader, <see cref="TextureSlots"/>): <see cref="Slots"/> used, the rest hold the stand-in.</summary>
    public const int TextureSlotCount = 20;

    // Material flags (CharacterMaterialRecord.Flags).
    public const uint HasDiffuse = 1 << 0, HasNormal = 1 << 1, NormalSwizzled = 1 << 2, HasHead = 1 << 3, HasHeadNormal = 1 << 4, HasHeadMask = 1 << 5,
        HasBodyMask = 1 << 6, HasHair = 1 << 7, HasBeard = 1 << 8, ClipNormalAlpha = 1 << 9, Dyed = 1 << 10, Vest0Coloured = 1 << 11,
        // 1 << 12 and 1 << 13 are the second and third vest layers' Vest0Coloured.
        Worn = 1 << 14,
        // Textures a part has but that are not resident yet (they stream in): the one giving its cut-out (the part is not drawn until it is), the head's
        // diffuse and normal map (the head's region shows the skin stand-in and a flat normal, not the body texture's transparent border).
        AlphaPending = 1 << 15, HeadPending = 1 << 16, HeadNormalPending = 1 << 17,
        // Skin that scatters light (the Meitou skin switch, docs/character-renderer.md "Skin"): a person's body, not a robot's.
        Skin = 1 << 18,
        // An icon whose mesh has vertex colours (the game's RTTIcons_Coloured): they multiply the albedo.
        VertexColoured = 1 << 19,
        // Blood on the surface (character.hlsl "Bloodyness"): the part is covered at full amount (a severed limb's stump), docs/game/blood-decals.md "On the body".
        Bloody = 1 << 20,
        // With Bloody: the whole mesh at the full amount (a stump) instead of its parts' damage (the instance carries them).
        BloodFull = 1 << 21,
        // A headless body's neck cap (NeckCap, Meitou decapitation): the wound; with Mechanical, a robot race's metal socket.
        Wound = 1 << 22, Mechanical = 1 << 23;
    // Instance flag (CharInstance.Flags; bit 0 interior, 12-15 hidden limbs, 16-17 the boots' side, 18-27 hidden parts): a headless body (Meitou decapitation): its head
    // vertices (CharacterContent.HeadBit) are hidden.
    public const uint Headless = 1u << 28;
    // A cut-off head (CharacterAppearance.HeadOnly): only the head vertices are drawn.
    public const uint HeadOnly = 1u << 29;
    // Shading modes.
    public const uint ShadeItem = 0, ShadeBody = 1, ShadeHair = 2;
    // Texture slots (CharacterMaterialRecord.Tex).
    public const int SlotDiffuse = 0, SlotNormal = 1, SlotColourMap = 2, SlotHeadDiffuse = 3, SlotHeadNormal = 4, SlotHeadMask = 5, SlotBodyMask = 6,
        SlotHairOverlay = 7, SlotBeardOverlay = 8, SlotVest = 9, SlotBlendNormal = 18, SlotBlood = 19, Slots = 20;

    const string PushMembers = """
            vec3 flatColour;
            uint depthIndex;
            vec4 fill;          // rgb; a unused
            uint skinLut;       // the skin scattering table's bindless index (SkinScattering), 0: the switch is off
        """;

    static readonly string Structs = $$"""
        struct CharMaterial
        {
            uint shading;        // 0 item, 1 body, 2 hair
            uint flags;
            uint vestCount;
            float alphaThreshold;
            uint tex[{{TextureSlotCount}}];
            vec4 shirtColour;    // w: an item's gloss multiplier (an icon's material `specular mult`; 1 otherwise)
            vec4 skinTone;
            vec4 hairColour;
            vec4 hairAlpha;
            vec4 hairMult;
            vec4 beardAlpha;
            vec4 diffuseChannel;
            vec4 alphaChannel;
            vec4 colour1;
            vec4 colour2;
        };
        layout(std430, set = 0, binding = {{MaterialsBinding}}) readonly buffer Materials { CharMaterial items[]; } materials;
        """;

    static string Head(string stage) => "#version 450\n" + NativeShaders.Prelude(PushMembers) + Structs + stage;

    public static string Vertex() => VertexSource(false);
    /// <summary>The vertex program for the motion vectors: also skins with last frame's palette (binding 9) and model matrix (locations 12 to 15).</summary>
    public static string MotionVertex() => VertexSource(true);

    static string VertexSource(bool motion) => ("#version 450\n" + NativeShaders.Prelude(PushMembers) + $$"""
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec2 aUv;
        layout(location = 3) in vec4 aTangent;
        layout(location = 4) in vec4 aColour;           // x: the vertex's morph slot as raw bits (0: none), y: the body vertex's hide bits (bit k: limb slot k, bit 4 + p: part-map part p), z: curvature (1 / radius)
        layout(location = 5) in uvec4 aBones;
        layout(location = 6) in vec4 aWeights;
        layout(location = 7) in vec4 aInstance0;
        layout(location = 8) in vec4 aInstance1;
        layout(location = 9) in vec4 aInstance2;
        layout(location = 10) in vec4 aInstance3;
        layout(location = 11) in uvec4 aInstanceData;   // x first bone of the palette, y material, z flags
        //MOTION_IN
        layout(std430, set = 0, binding = {{BonesBinding}}) readonly buffer Skin { mat4 bones[]; } skin;
        layout(std430, set = 0, binding = {{MorphsBinding}}) readonly buffer Morphs { vec4 deltas[]; } morphs;
        //MOTION_BUF

        out vec3 vWorld;
        out float vCurvature;   // the skin's curvature, baked per vertex (CharacterContent.MarkCurvature)
        out vec3 vNormal;
        out vec4 vTangent;
        out vec2 vUv;
        flat out uint vMaterial;
        flat out uint vInterior;
        out float vHidden;
        //MOTION_OUT
        out vec4 vColour;
        out vec4 vBlood;        // the blood mapping of character.hlsl (cylinder projection of the part in its own space), before the race scale
        flat out uint vCuts;
        out vec3 vBindPos;
        out vec3 vSkinPos;
        flat out uint vHeadOpen;  // 1 the scalp is gone at the brow, 2 the skull broken too
        out float vJawCut;      // the lost jaw's vertices at 1: its edge falls between them, per pixel
        out float vCutBand;     // a cut limb: distance past the cut, below 0 gone
        flat out uint vWounds;  // the wound mask, 0 without wounds
        flat out float vClipLayer;
        flat out float vPlated;
        flat out uint vAnatomy; // 1 on the anatomy part, whose UVs are its nearest skin
        out float vClip;        // anatomy: above 0 outside every wound
        out float vBloodAmount; // the blood amount of the vertex's body part (the instance carries the parts': two 8-bit amounts in the w of each matrix row)

        // The lower jaw: any real weight on its bone (lips and chin are shared with the head), or the face below the mouth's line. The throat under the chin stays with the neck.
        bool JawVertex(mat4 header)
        {
            if (aPosition.y < 16.8) return false;
            for (int k = 0; k < 4; k++)
            {
                uint b = aBones[k];
                if (aWeights[k] > 0.12 && ((floatBitsToUint(header[1 + b / 32u][b / 8u % 4u]) >> (b % 8u * 4u)) & 15u) == 6u) return true;
            }
            return aPosition.y > 16.95 && aPosition.y < 17.66 && aPosition.z > 0.75;
        }

        void main()
        {
            mat4 model = mat4(vec4(aInstance0.xyz, 0.0), vec4(aInstance1.xyz, 0.0), vec4(aInstance2.xyz, 0.0), vec4(aInstance3.xyz, 1.0));
            mat4 sk = mat4(1.0);
            if (dot(aWeights, vec4(1.0)) > 0.0)
            {
                uint b = aInstanceData.x;
                sk = skin.bones[b + aBones.x] * aWeights.x + skin.bones[b + aBones.y] * aWeights.y
                   + skin.bones[b + aBones.z] * aWeights.z + skin.bones[b + aBones.w] * aWeights.w;
            }
            mat4 world = model * sk;
            vec3 local = aPosition;
            uint slot = floatBitsToUint(aColour.x);
            if (aInstanceData.w != 0u && slot != 0u && slot < 0x1000000u) local += morphs.deltas[aInstanceData.w + slot].xyz;
            if ((aInstanceData.z & 0xC0000000u) == 0x80000000u)
            {
                // Flesh gone: skin near a heavy wound of its own body group sinks towards the bone.
                uint bone = aWeights.x >= aWeights.y ? aBones.x : aBones.y;
                mat4 words = skin.bones[aInstanceData.x - {{WoundHeaderBones}}u];
                uint own = (floatBitsToUint(words[1 + bone / 32u][bone / 8u % 4u]) >> (bone % 8u * 4u)) & 15u;
                float dent = 0.0;
                for (uint i = 0u; i < 64u; i++)
                {
                    vec4 c = skin.bones[aInstanceData.x - 32u + i / 4u][i % 4u];
                    if (c.w <= 0.0 || abs(floor(c.w / 10.0) - float(own)) > 0.5) continue;
                    float radius = mod(c.w, 10.0), near = 1.0 - clamp(distance(local, c.xyz) / radius, 0.0, 1.0);
                    dent = max(dent, near * near * (3.0 - 2.0 * near) * max(radius - 1.0, 0.0) * 0.28);
                }
                if (aPosition.y > 16.0) dent = 0.0;   // the head counts as torso by group: a chest wound must not sink the face
                local -= aNormal * min(dent, own == 0u ? 0.55 : own < 3u ? 0.25 : 0.4);   // never past the bone: arms are thinnest
            }
            vec4 p = world * vec4(local, 1.0);
            vCurvature = aColour.z;
            vWorld = p.xyz + view.eye;   // the instances are relative to the eye (CharacterRenderer.DrawView)
            mat3 n = mat3(world);
            vNormal = n * aNormal;
            vTangent = vec4(n * aTangent.xyz, aTangent.w);
            vUv = aUv;
            vColour = aColour;   // an icon mesh's own vertex colours (a body's carry the morph slot and limb bits: only icons read them)
            // character.hlsl: x = height along the mesh's Y, y = angle round it (atan2(normal.x, normal.z) / 2pi), z = the angle shifted by half a turn (the wrap), w = normal.z.
            float turn = atan(aNormal.x, aNormal.z) / 3.14159265359 * 0.5;
            vBlood = vec4(aPosition.y, turn, turn > 0.0 ? turn - 0.5 : turn + 0.5, aNormal.z);
            bool wounded = (aInstanceData.z & 0x80000000u) != 0u, anatomy = (aInstanceData.z & 0x40000000u) != 0u;
            vAnatomy = anatomy ? 1u : 0u;
            vPlated = (aInstanceData.z & 8u) != 0u ? 1.0 : 0.0;
            vCuts = aInstanceData.x - 16u;
            vBindPos = aTangent.xyz;
            vSkinPos = aPosition;
            vClipLayer = aTangent.w;
            vWounds = wounded ? floatBitsToUint(skin.bones[aInstanceData.x - {{WoundHeaderBones}}u][0][0]) : 0u;
            vClip = -1.0;
            vCutBand = 1000.0;
            vJawCut = 0.0;
            vHeadOpen = wounded ? floatBitsToUint(skin.bones[aInstanceData.x - {{WoundHeaderBones}}u][0][1]) >> 7u & 3u : 0u;
            if ((aInstanceData.z & 12u) != 0u)
            {
                // Worn on the body: torn where a wound of its own body group lies under it (the wound spheres are in the same bind space; in it a hand hangs by the hip).
                uint primary = aWeights.x >= aWeights.y ? aBones.x : aBones.y;
                mat4 first = skin.bones[aInstanceData.x - {{WoundHeaderBones}}u];
                uint group = (floatBitsToUint(first[1 + primary / 32u][primary / 8u % 4u]) >> (primary % 8u * 4u)) & 15u;
                for (uint i = 0u; i < 64u; i++)
                {
                    vec4 c = skin.bones[aInstanceData.x - 32u + i / 4u][i % 4u];
                    if (c.w > 0.0 && abs(floor(c.w / 10.0) - float(group)) < 0.5) vClip = max(vClip, min(mod(c.w, 10.0), 1.1) * 0.75 - distance(aPosition, c.xyz));
                }
            }
            if (anatomy && aTangent.w > 5.5 && aTangent.w < 6.5) vAnatomy = vHeadOpen >= 2u ? 2u : 3u;   // the brain: only behind a broken skull
            else if (anatomy && aTangent.w > 4.5 && aTangent.w < 5.5) vAnatomy = (floatBitsToUint(skin.bones[aInstanceData.x - {{WoundHeaderBones}}u][0][1]) >> 6u & 1u) != 0u ? 2u : 3u;   // the mouth: only with the jaw gone
            else if (anatomy && aTangent.w > 6.5) vAnatomy = 2u;   // the back wall needs no mask: only a hole in the skin reveals it
            else if (anatomy && (floatBitsToUint(skin.bones[aInstanceData.x - {{WoundHeaderBones}}u][0][1]) >> uint(aColour.a + 0.5) & 1u) != 0u && fract(aColour.a) > 0.1) vAnatomy = 2u;   // a stump: shown whatever the mask
            else if (anatomy)
            {
                vClip = 1000.0;
                if ((floatBitsToUint(skin.bones[aInstanceData.x - {{WoundHeaderBones}}u][0][1]) >> 6u & 1u) != 0u && JawVertex(skin.bones[aInstanceData.x - {{WoundHeaderBones}}u])) vAnatomy = 3u;
                uint lost = floatBitsToUint(skin.bones[aInstanceData.x - {{WoundHeaderBones}}u][0][1]);
                for (uint i = 0u; i < 64u; i++)
                {
                    vec4 c = skin.bones[aInstanceData.x - 32u + i / 4u][i % 4u];
                    if (c.w > 0.0 && (lost >> uint(aColour.a + 0.5) & 1u) == 0u && abs(floor(c.w / 10.0) - (aColour.a > 4.5 ? 0.0 : floor(aColour.a + 0.5))) < 0.5) vClip = min(vClip, distance(aPosition, c.xyz) - mod(c.w, 10.0));
                }
            }
            vMaterial = aInstanceData.y;
            vInterior = aInstanceData.z & 1u;
            // Hidden limbs and parts (docs/characters.md "Hidden body parts"): the body's vertices of a limb whose bit 12 + k the instance carries, or of a part-map part whose
            // bit 18 + p it carries (the vertex's limb bits 0-3, part bits from 4: CharacterContent.MarkBody); the fragments discard where it is above 0.
            uint marks = uint(aColour.y + 0.5);
            vHidden = ((marks & (aInstanceData.z >> 12) & 15u) | ((marks >> 4) & (aInstanceData.z >> 18) & 1023u) | ((marks >> 14) & (aInstanceData.z >> 28) & 1u)) != 0u ? 1.0 : 0.0;
            if ((aInstanceData.z & 0x8000000Cu) != 0u && !anatomy)
            {
                // A limb cut at its own height: everything of that limb below it goes.
                mat4 cutHeader = skin.bones[aInstanceData.x - {{WoundHeaderBones}}u];
                uint cutBone = aWeights.x >= aWeights.y ? aBones.x : aBones.y;
                uint cutGroup = (floatBitsToUint(cutHeader[1 + cutBone / 32u][cutBone / 8u % 4u]) >> (cutBone % 8u * 4u)) & 15u;
                uint only = floatBitsToUint(cutHeader[0][3]);
                if (only != 0u) { vHidden = (only >> 8) == 6u ? (JawVertex(cutHeader) ? 0.0 : 1.0) : cutGroup == (only >> 8) ? 0.0 : 1.0; vCutBand = float(only & 255u) * 0.1 - aPosition.y; }
                else if ((floatBitsToUint(cutHeader[0][1]) >> 6u & 1u) != 0u && JawVertex(cutHeader)) vJawCut = 1.0;
                else if (cutGroup >= 1u && cutGroup <= 4u)
                {
                    uint height = (floatBitsToUint(cutHeader[0][2]) >> (8u * (cutGroup - 1u))) & 255u;
                    if (height != 0u) vCutBand = aPosition.y - float(height) * 0.1;
                }
            }
            if ((aInstanceData.z & {{HeadOnly}}u) != 0u) vHidden = (marks & {{CharacterContent.HeadBit}}u) == 0u ? 1.0 : 0.0;   // a cut-off head: its vertices only
            // The part of the vertex (bits 16-19: its index + 1, CharacterContent.MarkBody) and its blood amount (the instance: part k in byte k & 1 of the w of row k / 2).
            uint bloodPart = (marks >> 16) & 15u;
            vBloodAmount = 0.0;
            if (bloodPart != 0u)
            {
                uint k = bloodPart - 1u;
                float pair = k < 2u ? aInstance0.w : k < 4u ? aInstance1.w : k < 6u ? aInstance2.w : aInstance3.w;
                vBloodAmount = float((uint(pair + 0.5) >> (8u * (k & 1u))) & 255u) / 255.0;
            }
            gl_Position = view.viewProjection * p;
            if ((aInstanceData.z & 0x40000000u) != 0u)
            {
                // Anatomy loses every depth test to the skin in front of it, however it pokes through: only a hole shows it.
                vec4 back = view.viewProjection * vec4(p.xyz + normalize(p.xyz) * 1.0, 1.0);
                gl_Position.z = back.z / back.w * gl_Position.w;
            }
            // Boots over one prosthetic leg (COLLAPSE_SIDE): flags bits 16-17, 1 collapses the x >= 0 half, 2 the x < 0 half.
            uint hideSide = (aInstanceData.z >> 16) & 3u;
            if ((hideSide == 1u && aPosition.x >= 0.0) || (hideSide == 2u && aPosition.x < 0.0)) gl_Position *= 0.0;
            //MOTION_BODY
        }
        """).Replace("//MOTION_IN", motion ? MotionIn : "").Replace("//MOTION_BUF", motion ? MotionBuf : "")
          .Replace("//MOTION_OUT", motion ? "out vec4 vNow;\nout vec4 vPrevious;" : "").Replace("//MOTION_BODY", motion ? MotionBody : "");

    const string MotionIn = """
        layout(location = 12) in vec4 aPrev0;
        layout(location = 13) in vec4 aPrev1;
        layout(location = 14) in vec4 aPrev2;
        layout(location = 15) in vec4 aPrev3;
        """;
    static readonly string MotionBuf = $$"""
        layout(std430, set = 0, binding = {{PreviousBonesBinding}}) readonly buffer PreviousSkin { mat4 bones[]; } previousSkin;
        """;
    const string MotionBody = """
            mat4 previousSk = mat4(1.0);
            if (dot(aWeights, vec4(1.0)) > 0.0)
            {
                uint b = aInstanceData.x;
                previousSk = previousSkin.bones[b + aBones.x] * aWeights.x + previousSkin.bones[b + aBones.y] * aWeights.y
                   + previousSkin.bones[b + aBones.z] * aWeights.z + previousSkin.bones[b + aBones.w] * aWeights.w;
            }
            mat4 previousModel = mat4(vec4(aPrev0.xyz, 0.0), vec4(aPrev1.xyz, 0.0), vec4(aPrev2.xyz, 0.0), vec4(aPrev3.xyz, 1.0));
            vNow = gl_Position;
            vPrevious = view.previousViewProjection * (previousModel * previousSk * vec4(local, 1.0));
        """;

    // The texture lookups are non-uniform across a draw (the instances differ in material) but constant over a primitive, so the
    // derivatives of the quad are fine. The cut-outs discard after every lookup (no derivatives after an OpKill).
    const string Lookup = """
        vec4 tex(uint i, vec2 uv) { return texture(textures2D[nonuniformEXT(i)], uv); }
        """;

    /// <summary>
    /// How many times more curved the skin is taken to be than it is (a look, not physics): at true size the profile's millimetres barely touch a face's
    /// 5 to 8 cm radius (the terminator's red at N.L 0 goes from 0.016 to 0.019); 30 puts a face near r = 2 mm, red about 0.16 there, a tail past N.L -0.2.
    /// </summary>
    public const float SkinScatterScale = 30;

    /// <summary>
    /// The skin's curvature (1 / radius in world units) to the table's axis (<see cref="SkinScattering.MaxInverseRadiusMm"/> at 1): 1 unit = 100 mm
    /// (docs/formats/terrain.md, Observed: decimetres fit the bodies' sizes), times <see cref="SkinScatterScale"/>.
    /// </summary>
    const float SkinCurvatureScale = SkinScatterScale / (100f * SkinScattering.MaxInverseRadiusMm);

    static string F(float v) => v.ToString("0.0########", System.Globalization.CultureInfo.InvariantCulture);

    public static string Fragment() => "#version 450\n" + NativeShaders.Prelude(PushMembers) + "#define LAMP_SKIN\n" + NativeShaders.AtmosphereFunctions + Structs + Lookup + $$"""
        in vec3 vWorld;
        in float vCurvature;
        in vec3 vNormal;
        in vec4 vTangent;
        in vec2 vUv;
        flat in uint vMaterial;
        flat in uint vInterior;
        in float vHidden;
        in vec4 vColour;
        in vec4 vBlood;
        flat in uint vHeadOpen;
        in float vJawCut;
        in float vCutBand;
        flat in uint vWounds;
        flat in uint vCuts;
        in vec3 vBindPos;
        in vec3 vSkinPos;
        flat in float vClipLayer;
        flat in float vPlated;
        flat in uint vAnatomy;
        in float vClip;
        in float vBloodAmount;
        layout(std430, set = 0, binding = {{BonesBinding}}) readonly buffer CutMats { mat4 m[]; } cutMats;

        // The cuts as blade shapes in UV, smooth at any distance: x core, y open area, z ring, w depth.
        vec4 woundField(vec2 uv)
        {
            vec4 best = vec4(0.0);
            float aa = max(fwidth(uv.x) + fwidth(uv.y), 1e-5);
            for (uint i = 0u; i < 64u; i++)
            {
                vec4 c = cutMats.m[vCuts + i / 4u][i % 4u];
                if (c.w <= 0.0) continue;
                float len = floor(c.w) / 10000.0, depth = fract(c.w);
                if (len <= 0.0) continue;
                if (uv.y < 0.0)
                {
                    // The face's texture is mirrored: only the side really struck, and never over the eyes.
                    vec4 hitAt = cutMats.m[vCuts - 16u + i / 4u][i % 4u];
                    if (hitAt.x * vSkinPos.x < 0.0 && abs(vSkinPos.x) > 0.08) continue;
                    if (abs(vSkinPos.y - {{EyeHeight}}) < 0.45 && vSkinPos.z > 0.5) continue;
                }
                vec2 d = uv - c.xy, axis = vec2(cos(c.z), sin(c.z));
                float x = dot(d, axis) / len, y = abs(dot(d, vec2(-axis.y, axis.x)));
                if (abs(x) >= 1.0) continue;
                float wobble = sin(x * 9.0 + c.x * 80.0) * 0.06 + sin(x * 23.0 + c.y * 90.0) * 0.03;
                float half_ = min(len, 0.06) * (0.1 + 0.2 * depth) * (1.0 + len * 12.0) * pow(1.0 - x * x, 0.8) * (1.0 + wobble);
                float sd = y - half_;
                vec4 here = vec4(1.0 - smoothstep(-half_ * 0.55 - aa, -half_ * 0.55 + aa, sd), 1.0 - smoothstep(-aa, aa, sd), 1.0 - smoothstep(0.0, len * 0.06 + aa, sd), depth);
                here.w *= here.y;
                // Crossing cuts join: a smooth maximum, so no sharp cross where they meet.
                best = max(best, here) + here * best * vec4(0.0, 0.35, 0.35, 0.0) * (1.0 - best);
            }
            return best;
        }

        // Open flesh over the albedo, from the character's wound mask.
        void woundSurface(inout vec3 albedo, inout float gloss, CharMaterial m, inout vec3 bend)
        {
            vec4 field = woundField(vUv);
            // The lips: the ring's slope tips the normal, so light finds a raised, parted edge.
            float rise = field.b - field.g;
            vec3 dx = dFdx(vWorld), dy = dFdy(vWorld), across = cross(dy, normalize(vNormal)), down = cross(normalize(vNormal), dx);
            bend = (across * dFdx(rise) + down * dFdy(rise)) / max(abs(dot(dx, across)), 1e-5) * 0.09;
            vec3 cut = field.rgb;
            if (field.a > (vUv.y < 0.0 ? 0.85 : 0.5) && (vUv.y >= 0.0 || field.r > 0.5)) discard;   // the face opens only at a deep cut's heart: eyes and teeth sit behind it   // only the deepest cuts open onto the anatomy
            // A painted cut, rim to core: bruise, thin fat, wet meat with fine grain, dark bottom.
            // Wet runs from the cuts, thick and glossy where they pool down the middle.
            float run = smoothstep(0.1, 0.55, tex(vWounds, vec2(vUv.x, (vUv.y + 1.0) * 0.5)).g);
            albedo = mix(albedo, vec3(0.24, 0.012, 0.014), run * 0.92);
            gloss = mix(gloss, 0.85, run);
            float grain = 0.82 + 0.18 * sin((vUv.x + vUv.y) * 900.0 + sin(vUv.x * 310.0) * 2.0);
            albedo = mix(albedo, albedo * vec3(0.8, 0.5, 0.48), cut.b);
            float lump = 0.5 + 0.5 * sin(vUv.x * 410.0 + sin(vUv.y * 260.0) * 2.5) * sin(vUv.y * 370.0 + sin(vUv.x * 210.0) * 2.5);
            vec3 meat = mix(m.colour1.rgb * 0.75, m.colour1.rgb * 0.3, lump) * grain;
            albedo = mix(albedo, meat, smoothstep(0.1, 0.45, cut.g));
            // Only a broken thread of fat at the lip, not a band; the heart stays dark red, never black.
            float lip = smoothstep(0.1, 0.3, cut.g) * (1.0 - smoothstep(0.3, 0.5, cut.g)) * smoothstep(0.45, 0.7, lump);
            albedo = mix(albedo, vec3(0.6, 0.47, 0.3), lip * 0.7);
            albedo = mix(albedo, m.colour1.rgb * 0.22, smoothstep(0.5, 1.0, cut.r));
            gloss = mix(gloss, 0.75, cut.g);
        }
        layout(location = 0) out vec4 fragColour;

        bool flag(uint f, uint bit) { return (f & bit) != 0u; }

        // A clothing "vest" layer drawn on the body (docs/characters.md): the coverage is the vest normal map's alpha (skin: what it leaves bare).
        void vest(uint d, uint nm, uint c, bool coloured, vec3 shirt, vec2 uv, inout vec3 albedo, inout vec4 normalMap, inout float gloss, inout float skin)
        {
            vec4 v = tex(d, uv);
            vec4 vn = tex(nm, uv);
            if (coloured) v.rgb = mix(v.rgb, shirt * dot(v.rgb, vec3(1.0 / 3.0)), tex(c, uv).r);
            albedo = mix(albedo, v.rgb, vn.a);
            normalMap.wy = mix(normalMap.wy, vn.xy, vn.a);
            gloss = mix(gloss, v.a, vn.a);
            skin *= 1.0 - vn.a;
        }

        // The Meitou skin's sun term in place of the clamped N.L (docs/character-renderer.md "Skin"; SkinScattering): the pre-integrated table at the surface's
        // curvature, read per channel with its own normal, red nearest the smooth geometric one (light that travels farther under the skin sees less of the bumps).
        // curvature: 1 / radius in world units; share: how much of the fragment is bare skin. The light the table adds over the plain term takes the skin's own hue,
        // saturated (albedo over its brightest channel, squared): red-orange on human skin as the table has it, amber or green on hive chitin, any modded race its own.
        vec3 skinDiffuse(vec3 n, vec3 geometric, vec3 l, float curvature, float share, vec3 albedo)
        {
            float lambert = clamp(dot(n, l), 0.0, 1.0);
            if (share <= 0.0) return vec3(lambert);
            vec3 nl = vec3(dot(normalize(mix(n, geometric, 0.6)), l), dot(normalize(mix(n, geometric, 0.3)), l), dot(n, l));
            vec3 u = (nl * 0.5 + 0.5) * {{F((SkinScattering.Width - 1f) / SkinScattering.Width)}} + {{F(0.5f / SkinScattering.Width)}};
            float v = sqrt(clamp(curvature * {{F(SkinCurvatureScale)}}, 0.0, 1.0)) * {{F((SkinScattering.Height - 1f) / SkinScattering.Height)}} + {{F(0.5f / SkinScattering.Height)}};
            uint t = pc.skinLut;
            vec3 s = vec3(textureLod(textures2D[t], vec2(u.r, v), 0.0).r, textureLod(textures2D[t], vec2(u.g, v), 0.0).g, textureLod(textures2D[t], vec2(u.b, v), 0.0).b);
            // Only what it adds over the plain term: the table also takes red from the lit side (energy it moves into the shadow), which turned sunlit skin cyan-grey;
            // only the warm band past the terminator is wanted.
            float extra = max(max(s.r, max(s.g, s.b)) - lambert, 0.0);
            vec3 hue = albedo / max(max(albedo.r, max(albedo.g, albedo.b)), 1e-3);
            return vec3(lambert) + extra * hue * hue * share;
        }

        // The lamps' diffuse term (LampShaders, LAMP_SKIN): the same skin as the sun's, from what main sets before lighting (no skin: plain N.L).
        vec3 skinGeometric = vec3(0.0, 1.0, 0.0), skinAlbedo = vec3(1.0);
        float skinCurvature = 0.0, skinShare = 0.0;
        vec3 lampSkinDiffuse(vec3 n, vec3 l) { return skinDiffuse(n, skinGeometric, l, skinCurvature, skinShare, skinAlbedo); }

        void main()
        {
            vec3 geometric = normalize(vNormal);
            float curvature = vCurvature;
            if (vHidden > 0.0) discard;
            if (pc.depthIndex != 0u) { fragColour = vec4(pc.flatColour, 1.0); return; }
            CharMaterial m = materials.items[vMaterial];
            uint f = m.flags;
            if (flag(f, 32768u)) discard;   // its cut-out is still streaming in: a solid card would show
            vec3 n = geometric;
            if (!gl_FrontFacing) n = -n;
            bool icon = view.fogDistance < 0.0 && view.fogDistance > -1.5;   // an inventory icon (CharacterRenderer.DrawIcon)
            vec2 uv = vUv;
            vec3 albedo = vec3(0.72);
            float gloss = 0.2;
            float metal = 0.0;                        // a worn item's metalness: its colour map's blue (skin.hlsl)
            vec4 nm = vec4(0.5, 0.5, 1.0, 0.5);
            bool swizzled = flag(f, 4u);
            bool cut = false;
            float skin = 0.0;                         // how much of the fragment is bare skin (the Meitou skin switch)

            if (m.shading == 2u)
            {
                vec4 t = flag(f, 1u) ? tex(m.tex[0], uv) : vec4(1.0);
                cut = flag(f, 1u) && dot(t, m.alphaChannel) < m.alphaThreshold;
                float brightness = dot(t, m.diffuseChannel);
                albedo = brightness * m.hairColour.rgb;
                gloss = brightness * 0.3;
            }
            else if (m.shading == 1u)
            {
                vec4 body = flag(f, 1u) ? tex(m.tex[0], uv) : vec4(0.7, 0.6, 0.5, 0.2);
                skin = flag(f, 262144u) && pc.skinLut != 0u ? 1.0 : 0.0;
                if (flag(f, 2u)) nm = tex(m.tex[1], uv);
                // The original's muscleBlend: towards the strong or the skinny body normal map (skinTone.w, 0 while that map is not resident).
                if (flag(f, 2u) && m.skinTone.w > 0.0) nm = mix(nm, tex(m.tex[18], uv), m.skinTone.w);
                float tone = flag(f, 64u) ? tex(m.tex[6], uv).r : 0.0;
                vec2 huv = uv + vec2(0.0, 1.0);          // head UVs sit in v -1..0
                if (flag(f, 8u)) body += tex(m.tex[3], huv);
                else if (flag(f, 65536u) && uv.y < 0.0) body = vec4(0.7, 0.6, 0.5, 0.2);   // the head still streaming in: skin, not the border's black
                if (flag(f, 16u) && flag(f, 2u)) nm += tex(m.tex[4], huv);
                else if (flag(f, 131072u) && uv.y < 0.0) nm = vec4(0.5, 0.5, 1.0, 0.5);
                if (flag(f, 32u)) tone += tex(m.tex[5], huv).r;
                body.rgb *= 1.0 - m.skinTone.rgb * tone;
                if (flag(f, 128u))
                {
                    vec4 h = tex(m.tex[7], huv);
                    float cover = clamp(dot(h, m.hairAlpha), 0.0, 1.0);
                    body.rgb = mix(body.rgb, m.hairColour.rgb * clamp(dot(h, m.hairMult), 0.0, 1.0), cover);
                    skin *= 1.0 - cover;
                }
                if (flag(f, 256u))
                {
                    float b = clamp(dot(tex(m.tex[8], huv), m.beardAlpha), 0.0, 1.0);
                    body.rgb = mix(body.rgb, m.hairColour.rgb * b, b);
                    skin *= 1.0 - b;
                }
                albedo = body.rgb;
                gloss = body.a;
                // vestCount: a bit per worn layer whose textures are resident (one still streaming in is left out, not drawn black).
                if ((m.vestCount & 1u) != 0u) vest(m.tex[9], m.tex[10], m.tex[11], flag(f, 2048u), m.shirtColour.rgb, uv, albedo, nm, gloss, skin);
                if ((m.vestCount & 2u) != 0u) vest(m.tex[12], m.tex[13], m.tex[14], flag(f, 4096u), m.shirtColour.rgb, uv, albedo, nm, gloss, skin);
                if ((m.vestCount & 4u) != 0u) vest(m.tex[15], m.tex[16], m.tex[17], flag(f, 8192u), m.shirtColour.rgb, uv, albedo, nm, gloss, skin);
                swizzled = true;                          // character normals: X in alpha, Y in green
            }
            else
            {
                // An icon's missing texture map is white (the game binds a white texture); the world's items show grey.
                vec4 d = flag(f, 1u) ? tex(m.tex[0], uv) : icon ? vec4(1.0) : vec4(0.72, 0.72, 0.72, 0.2);
                if (flag(f, 524288u)) d.rgb *= vColour.rgb;
                bool clip = flag(f, 512u);
                if (flag(f, 2u) || clip) nm = tex(m.tex[1], uv);
                vec3 cm = flag(f, 1024u) ? tex(m.tex[2], uv).rgb : vec3(0.0);
                // Inventory RTT materials reject at 0.5; worn clothing retains its separate 0.6 threshold.
                cut = clip && nm.a < (view.fogDistance < 0.0 ? 0.5 : 0.6);
                if (flag(f, 1024u))
                {
                    float intensity = dot(d.rgb, vec3(1.0 / 3.0));
                    d.rgb = mix(d.rgb, m.colour1.rgb * mix(intensity, 1.0, m.colour1.a), cm.r);
                    d.rgb = mix(d.rgb, m.colour2.rgb * mix(intensity, 1.0, m.colour2.a), cm.g);
                }
                albedo = d.rgb;
                // A worn item's material (the Skinned template, skin.hlsl) has glossMult 0: nothing sets it, so worn clothing is matte; an attached item with
                // its own mesh (the StaticObject template) keeps 1.
                gloss = flag(f, 16384u) ? 0.0 : d.a * m.shirtColour.w;
                if (flag(f, 16384u) && flag(f, 1024u)) metal = cm.b;
            }
            if (flag(f, 1048576u))
            {
                // character.hlsl "Bloodyness": the splatter map (bloody.png, wrapped) tinted with the race's blood colour, laid over the
                // albedo by its alpha. colour1.rgb: the blood colour; colour2.xy: the race's `blood horizontal` and `blood vertical`.
                vec2 bc = vec2(vBlood.x * m.colour2.y, (vBlood.w < 0.0 ? vBlood.z : vBlood.y) * m.colour2.x);
                vec4 blood = tex(m.tex[19], bc) * vec4(m.colour1.rgb, 1.0);
                float amount = flag(f, 2097152u) ? 1.0 : vBloodAmount;
                if (vWounds != 0u) amount *= 0.45;   // a wounded body: the cuts carry the gore, not an even coat
                albedo = mix(albedo, blood.rgb, clamp(blood.a - 1.0 + clamp(amount, 0.0, 1.0), 0.0, 1.0));
            }
            // A headless body's neck cap (NeckCap): uv is (0, 0) at its centre and 1 at the rim; colour1 the race's blood colour, the diffuse slot the splatter map.
            // Flesh: the fluid darkening into the cut, a bone at the centre. Mechanical (a robot race): a dark metal socket, its core darker, the fluid over it.
            if (flag(f, {{Wound}}u))
            {
                float r = length(uv);
                float splat = flag(f, 1u) ? tex(m.tex[0], uv * 0.5 + 0.5).a : 0.5;
                vec3 fluid = m.colour1.rgb;
                if (flag(f, {{Mechanical}}u))
                {
                    albedo = mix(vec3(0.17, 0.16, 0.15), vec3(0.05), 1.0 - smoothstep(0.25, 0.45, r));
                    albedo = mix(albedo, fluid, splat * 0.6 * smoothstep(0.3, 0.9, r));
                    gloss = 0.6;
                    metal = 0.85;
                }
                else
                {
                    albedo = mix(fluid * 0.35, fluid, splat * smoothstep(0.0, 1.0, r));
                    albedo = mix(vec3(0.75, 0.70, 0.60), albedo, smoothstep(0.12, 0.18, r));
                    gloss = 0.7;
                }
                skin = 0.0;
                nm = vec4(0.5, 0.5, 1.0, 0.5);
            }
            // Metal plate does not tear: it takes the wound's blood.
            vec3 woundBend = vec3(0.0);
            // A cut limb's end: a clean edge, then a ragged band of flesh with bone at its heart.
            if (vHeadOpen != 0u)
            {
                // Bone is painted on the skin itself: the fitted skull does not close the forehead. A broken skull opens at its middle onto the brain.
                float open = distance(vSkinPos, vec3(0.3, 18.85, 0.65)) + 0.03 * sin(vSkinPos.x * 31.0 + vSkinPos.y * 13.0) + 0.02 * sin(vSkinPos.y * 47.0);
                if (vAnatomy == 0u)
                {
                    if (vHeadOpen >= 2u && open < 0.2) discard;
                    float wet = 1.0 - smoothstep(0.32, 0.42, open), bone = 1.0 - smoothstep(0.29, 0.33, open);
                    albedo = mix(mix(albedo, vec3(0.3, 0.02, 0.025), wet), vec3(0.68, 0.6, 0.47), bone);
                    gloss = mix(gloss, 0.75, wet);
                }
            }
            if (vJawCut > 0.0)
            {
                float tornLip = vJawCut + 0.035 * sin(vBlood.x * 70.0) + 0.02 * sin(vBlood.y * 35.0);   // a soft edge: the face's long triangles draw any stronger tear as spikes
                if (vSkinPos.y < 17.0) tornLip = (vSkinPos.y - 16.72) * 7.5 + 0.05 * sin(vBlood.y * 35.0);   // under the chin the triangles reach down the neck: the edge is a height there
                if (tornLip > 0.6) discard;
                float wet = smoothstep(0.15, 0.5, tornLip);
                albedo = mix(albedo, m.colour1.rgb * 0.55, wet);
                gloss = mix(gloss, 0.75, wet);
            }
            if (vCutBand < 1000.0)
            {
                float edge = vCutBand + 0.05 * sin(vBlood.y * 40.0) + 0.03 * sin(vBlood.y * 97.0);
                if (edge < 0.0) discard;
                float band = 1.0 - smoothstep(0.1, 0.32, edge);
                albedo = mix(albedo, !gl_FrontFacing && edge < 0.2 ? vec3(0.7, 0.64, 0.5) : m.colour1.rgb * 0.55, band);
                gloss = mix(gloss, 0.7, band);
            }
            if (vPlated > 0.0 && vClip > -0.5) { float smear = smoothstep(-0.5, 0.2, vClip + 0.25 * sin(vBlood.y * 23.0 + vBlood.x * 5.0)); albedo = mix(albedo, vec3(0.16, 0.01, 0.012), smear * 0.9); gloss = mix(gloss, 0.7, smear); }
            else if (vClip > 0.0 && (vAnatomy != 0u || vClip + 0.18 * (sin(vBlood.y * 19.0 + vBlood.x * 3.1) + sin(vBlood.x * 7.3 - vBlood.y * 31.0)) > 0.0)) discard;   // a tear's edge is ragged
            if (vAnatomy == 3u) discard;   // the lower teeth left with the jaw
            if (vAnatomy != 0u)
            {
                // Only under a hole: a coarse skin mesh would let the fitted anatomy show elsewhere. Wet grain, as Anatomy.hlsl.
                if (vWounds == 0u) discard;   // the skin's own holes cut it, per pixel (the depth push in the vertex stage)
                // By depth, as Gore Lab's RemoveLayer: only a very deep cut loses its muscle and shows the bone and organs behind; they stay whole.
                // In bind space, per pixel: the anatomy's own UVs are one skin vertex's and would cut it in blocks.
                if (vAnatomy == 1u && vClipLayer > 0.5 && vClipLayer < 3.5)
                    for (uint i = 0u; i < 64u; i++)
                    {
                        vec4 c = cutMats.m[vCuts - 16u + i / 4u][i % 4u];
                        float reach = max(mod(c.w, 10.0) - 1.6, 0.0) * 1.0;
                        if (c.w > 0.0 && reach > 0.0 && distance(vBindPos, c.xyz) + 0.12 * sin(vBindPos.x * 9.0 + vBindPos.y * 7.0) * sin(vBindPos.z * 8.0) < reach) discard;
                    }
                vec3 grain = fract(vec3(floor(vBlood.xyx * vec3(90.0, 400.0, 90.0))) * 0.1031);
                grain += dot(grain, grain.yzx + 33.33);
                albedo *= 0.9 + 0.1 * fract((grain.x + grain.y) * grain.z);
                if (vClipLayer > 3.5 && vClipLayer < 4.5) { albedo = vColour.rgb * (0.9 + 0.1 * sin(vBlood.y * 30.0)); gloss = 0.75; }
                if (vAnatomy == 2u) albedo *= 0.6;   // a stump's cut flesh, wet and dark
                gloss = 0.5;
            }
            else if (m.shading == 1u && !gl_FrontFacing && vWounds != 0u && dot(vNormal, view.eye - vWorld) < 0.0)   // truly the inside: a flipped outer triangle still has its normal towards the eye
            {
                // The body's inside, drawn only through a hole: the cavity behind the anatomy. An unwounded body keeps its plain colour: a reduced level's flipped triangles must not vanish.
                // Wet cavity wall, not a black void.
                albedo = m.colour1.rgb * 0.5 * (0.7 + 0.3 * sin(vBlood.x * 11.0 + sin(vBlood.y * 40.0) * 2.0));   // meat, not a void
                gloss = 0.3;
            }
            else if (vWounds != 0u) woundSurface(albedo, gloss, m, woundBend);
            if (cut) discard;
            // The game writes character albedo darkened: 0.7 for bodies, heads and hair (character.hlsl DARKEN), 0.8 for worn items (skin.hlsl); not its icons (rtticons.hlsl,
            // a fog distance of -1).
            // An item attached with its own mesh (StaticObject, not Worn) is not darkened.
            if (view.fogDistance > -0.5 || view.fogDistance < -1.5) albedo *= m.shading != 0u ? 0.7 : flag(f, 16384u) ? 0.8 : 1.0;

            n = normalize(n - woundBend);
            vec3 smoothNormal = n;
            if (flag(f, 2u) && m.shading != 2u && dot(vTangent.xyz, vTangent.xyz) > 1e-8)
            {
                vec3 t = normalize(vTangent.xyz - n * dot(n, vTangent.xyz));
                // cross(n, t) * w is the game's binormal (the mesh's BINORMAL times its w, Model.Build gameBinormals); worn items flip their maps' green (skin.hlsl), so
                // their binormal is the opposite; an icon's is cross(t, n) times the tangent's own w (rtticons, Model.Build tangentSign).
                vec3 b = (flag(f, 16384u) || icon ? cross(t, n) : cross(n, t)) * vTangent.w;
                vec3 tn;
                if (swizzled) { vec2 xy = vec2(nm.a, nm.g) * 2.0 - 1.0; tn = vec3(xy, sqrt(max(0.0, 1.0 - dot(xy, xy)))); }
                else tn = nm.xyz * 2.0 - 1.0;
                n = normalize(t * tn.x + b * tn.y + n * tn.z);
            }

            // Lit as the world's other meshes are (Shaders.MeshFragment): the game's deferred lighting with the game's sky, else the simple model.
            vec3 v = normalize(view.eye - vWorld);
            gloss = clamp(gloss, 0.0, 1.0);
            vec3 colour;
            giInterior = vInterior != 0u;
            skinGeometric = smoothNormal; skinCurvature = curvature; skinShare = skin; skinAlbedo = albedo;
            atmoMetalness = metal;
            if (view.fogDistance > 0.0 && uAtmoParams.x > 0.5) colour = kenshiLight(albedo, n, v, gloss, vWorld, skinDiffuse(n, smoothNormal, uAtmoLight.xyz, curvature, skin, albedo));
            // The editor's stage (CharacterRenderer.DrawStage, a fog distance of -2): the game's rtt.hlsl (AtmosphereShaders.stageLight); -3 the same unclamped (Meitou's stage and
            // portraits, which also take the Meitou skin; the game's stage keeps its plain N.L).
            else if (view.fogDistance < -1.5)
            {
                vec3 l = normalize(view.lightDir);
                colour = stageLight(albedo, n, v, l, gloss, view.fogDistance < -2.5, skinDiffuse(n, smoothNormal, l, curvature, view.fogDistance < -2.5 ? skin : 0.0, albedo));
            }
            else if (view.fogDistance < 0.0)
            {
                // An inventory icon (CharacterRenderer.DrawIcon, a negative fog distance): the game's rtticons.hlsl, one white punctual light along the view axis and no ambient
                // (CalcPunctualLight: diffuse = pi * N.L * (1 - mean specular colour), GGX specular for roughness 1 - 0.99 * gloss, specular colour 0.4). The game stores that linear
                // result as it is in an 8-bit target, so the icon target is UNORM, not sRGB (the lit values match the saved pictures: backpack mean 133 102 77 computed, 142 114 91 saved).
                // The game's REFLECTION term lerps the albedo towards its cube (the reflectionMap alias) by gloss^2; which cube the game binds is unknown, and a constant mid
                // grey fits its saved pictures (docs/game/ui-screens.md "Item icons"). rtticons.hlsl negates the
                // normal, the light and -normalize(world position) together, so with the normal unnegated here the view vector is +normalize of the world position:
                // from the item's origin, not to the camera.
                const float PI = 3.14159265;
                albedo = mix(albedo, vec3(0.5), gloss * gloss);
                vec3 l = normalize(view.lightDir);
                vec3 hv = normalize(normalize(vWorld) + l);
                float nl = clamp(dot(n, l), 0.0, 1.0), lh = clamp(dot(l, hv), 0.0, 1.0), nh = clamp(dot(n, hv), 0.0, 1.0);
                float rough = 1.0 - gloss * 0.99, alpha = rough * rough, alphaSqr = alpha * alpha;
                float denom = max(1e-6, nh * nh * (alphaSqr - 1.0) + 1.0);
                float D = alphaSqr / (PI * denom * denom);
                float fresnel = exp2((-5.55473 * lh - 6.98316) * lh);
                float k = alpha * 0.5, k2 = k * k, vis = 1.0 / (lh * lh * (1.0 - k2) + k2);
                float fv = (0.4 + (1.0 - 0.4) * fresnel) * vis;
                float specular = nl * D * fv / PI;
                colour = clamp(albedo * PI * nl * 0.6 + specular, 0.0, 1.0);
            }
            else
            {
                vec3 l = normalize(view.lightDir);
                float diff = max(dot(n, l), 0.0);
                // The portraits' plain model takes the Meitou skin too (the switch, per race).
                vec3 diffSkin = skinDiffuse(n, smoothNormal, l, curvature, skin, albedo);
                vec3 ambient = mix(vec3(0.22, 0.20, 0.18), vec3(0.42, 0.45, 0.50), 0.5 + 0.5 * n.y);
                vec3 h = normalize(l + v);
                float spec = pow(max(dot(n, h), 0.0), 8.0 + 56.0 * gloss) * gloss * 0.5;
                vec3 sunLight = vec3(1.0, 0.97, 0.92);
                colour = albedo * (ambient + diffSkin * sunLight) + spec * diff * sunLight;
            }
            // A presentation fill (CharacterRenderer.Fill, 0 in play): ambient light, stronger towards the silhouette as a back light rims it.
            if (view.fogDistance > 0.0) colour += albedo * pc.fill.rgb * (1.0 + 2.0 * pow(1.0 - max(dot(n, v), 0.0), 3.0));
            if (view.fogDistance > 0.0) colour = atmoApply(colour, view.eye, vWorld);
            fragColour = vec4(colour, 1.0);
        }
        """;

    /// <summary>The sun's shadow caster: the cut-outs of <see cref="Fragment"/> (hair cards, worn cloth) and the game's caster bias.</summary>
    public static string DepthFragment()
    {
        string stock = NativeShaders.DepthFragment;
        const string stockMain = "void main() { shadowWriteDepth(); }";
        if (!stock.Contains(stockMain)) throw new InvalidOperationException("CharacterShaders: the stock depth fragment changed");
        return stock.Replace(stockMain, "") + Structs + Lookup + """
        in vec2 vUv;
        flat in uint vMaterial;
        in float vHidden;
        void main()
        {
            if (vHidden > 0.0) discard;
            CharMaterial m = materials.items[vMaterial];
            uint f = m.flags;
            if ((f & 32768u) != 0u) discard;
            bool cut = false;
            if (m.shading == 2u && (f & 1u) != 0u)
                cut = dot(tex(m.tex[0], vUv), m.alphaChannel) < m.alphaThreshold;
            else if (m.shading == 0u && (f & 512u) != 0u)
                cut = tex(m.tex[1], vUv).a < 0.6;
            if (cut) discard;
            shadowWriteDepth();
        }
        """;
    }

    public static string DepthVertex() => Vertex();

    /// <summary>
    /// The motion pass (<c>IWorldPostChain.ObjectMotion</c>): the character's own motion, as the grass writes its sway (UV units, this frame minus last, jitter removed), where
    /// the character is what the near depth shows (<c>pc.depthIndex</c> carries the depth texture's bindless index in this pass).
    /// </summary>
    public static string MotionFragment() => "#version 450\n" + NativeShaders.Prelude(PushMembers) + Structs + Lookup + """
        in vec2 vUv;
        in vec4 vNow;
        in vec4 vPrevious;
        flat in uint vMaterial;
        in float vHidden;
        layout(location = 0) out vec4 fragColour;
        float viewZ(float d) { float zd = 2.0 * d - 1.0; return view.nearPlanes.x * view.nearPlanes.y / (view.nearPlanes.y - zd * (view.nearPlanes.y - view.nearPlanes.x)); }
        void main()
        {
            if (vHidden > 0.0) discard;
            CharMaterial m = materials.items[vMaterial];
            uint f = m.flags;
            if ((f & 32768u) != 0u) discard;
            if (m.shading == 2u && (f & 1u) != 0u) { if (dot(tex(m.tex[0], vUv), m.alphaChannel) < m.alphaThreshold) discard; }
            else if (m.shading == 0u && (f & 512u) != 0u) { if (tex(m.tex[1], vUv).a < 0.6) discard; }
            float stored = texelFetch(textures2D[nonuniformEXT(pc.depthIndex)], ivec2(gl_FragCoord.xy), 0).r;
            if (stored >= 1.0) discard;
            float zs = viewZ(stored), zf = viewZ(gl_FragCoord.z);
            if (abs(zs - zf) > 0.002 * zs + 0.05) discard;
            vec2 now = vNow.xy / vNow.w - view.jitterNdc, previous = vPrevious.xy / vPrevious.w;
            fragColour = vec4((now - previous) * 0.5, 0.0, 0.0);
        }
        """;
}

/// <summary>The C# side of <c>CharMaterial</c> (std430, 256 bytes): the part's textures by bindless index and its colours.</summary>
[StructLayout(LayoutKind.Sequential)]
struct CharacterMaterialRecord
{
    /// <summary><see cref="VestCount"/> is a mask: bit i, vest layer i's textures are resident.</summary>
    public uint Shading, Flags, VestCount;
    public float AlphaThreshold;
    public TextureSlots Tex;
    public Vector4 ShirtColour, SkinTone, HairColour, HairAlpha, HairMult, BeardAlpha, DiffuseChannel, AlphaChannel, Colour1, Colour2;

    public const int Size = 256;
}

[InlineArray(CharacterShaders.TextureSlotCount)]
struct TextureSlots { uint first; }

/// <summary>The C# side of the push block (<c>flatColour</c>, <c>depthIndex</c>, <c>fill</c>, <c>skinLut</c>): 36 bytes. <c>DepthIndex</c> is the near depth texture's bindless index in the motion pass and 0 otherwise.</summary>
[StructLayout(LayoutKind.Sequential)]
struct CharacterPush
{
    public Vector3 FlatColour;
    public uint DepthIndex;
    /// <summary>The colour pass's fill light (<see cref="CharacterRenderer.Fill"/>; w unused).</summary>
    public Vector4 Fill;
    /// <summary>The skin scattering table's bindless index (<see cref="SkinScattering"/>), 0 while the <c>skin</c> switch is Faithful.</summary>
    public uint SkinLut;
}

/// <summary>One instance (144 bytes): the model matrix whose rows the vertex program reads, the palette's first bone, the material's index, the morph base and last frame's matrix.</summary>
[StructLayout(LayoutKind.Sequential)]
struct CharInstance
{
    public Matrix4x4 Model;
    public uint BoneBase, Material, Flags, MorphBase;
    /// <summary>Last frame's model matrix (the motion vectors' pass).</summary>
    public Matrix4x4 PreviousModel;

    public const int Size = 144;
}
