using System.Runtime.InteropServices;
using System.Text.Json;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AzerothMegabonk.ElwynnBiome;

/// <summary>Locally converted original Elwynn pixels and static M2 prototypes.</summary>
public sealed class BiomeAssets : IDisposable
{
    public Dictionary<string, Texture2D> GroundTextures { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, GameObject> Props { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Bounds> PropBounds { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> PropSources { get; } = new(StringComparer.Ordinal);
    public string SourceFolder { get; private set; } = "";
    public int Version { get; private set; }
    public int SourceVertices { get; private set; }
    public int SourceTriangles { get; private set; }

    readonly Dictionary<string, Texture2D> textures = new(StringComparer.Ordinal);
    readonly Dictionary<string, Material> materials = new(StringComparer.Ordinal);
    readonly List<Mesh> meshes = new();
    readonly List<string> shaderWarnings = new();
    GameObject prototypes;
    bool released;

    static T Retain<T>(T asset) where T : Object
    {
        asset.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return asset;
    }

    public static BiomeAssets Load(string folder)
    {
        var pack = new BiomeAssets { SourceFolder = Path.GetFullPath(folder) };
        try { pack.LoadPack(); return pack; }
        catch { pack.Release(); throw; }
    }

    string PathInPack(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(SourceFolder, relative));
        var root = SourceFolder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Biome asset path escapes its pack");
        return full;
    }

    void LoadPack()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(PathInPack("manifest.json")));
        var manifest = doc.RootElement;
        if (manifest.GetProperty("format").GetString() != "azeroth-elwynn-biome-v1")
            throw new InvalidDataException("Unsupported Elwynn pack format");
        Version = manifest.GetProperty("version").GetInt32();
        var groundIds = manifest.GetProperty("groundTextures").EnumerateObject()
            .Select(x => x.Value.GetString()).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in manifest.GetProperty("textures").EnumerateObject())
        {
            var j = entry.Value;
            int width = j.GetProperty("width").GetInt32(), height = j.GetProperty("height").GetInt32();
            var raw = File.ReadAllBytes(PathInPack(j.GetProperty("file").GetString()));
            if (width <= 0 || height <= 0 || raw.Length != checked(width * height * 4))
                throw new InvalidDataException("Elwynn RGBA dimensions do not match: " + entry.Name);
            bool ground = groundIds.Contains(entry.Name);
            var texture = Retain(new Texture2D(width, height, TextureFormat.RGBA32, ground)
                { name = "AzerothElwynn_Texture_" + entry.Name });
            textures.Add(entry.Name, texture);
            // LoadRawTextureData expects space for the complete native mip chain.
            // Preserve the source level0 bytes, then let Apply generate all lower
            // levels instead of uploading an undersized base-only buffer.
            var upload = raw;
            if (ground)
            {
                int bytes = 0, mipWidth = width, mipHeight = height;
                while (true)
                {
                    bytes = checked(bytes + mipWidth * mipHeight * 4);
                    if (mipWidth == 1 && mipHeight == 1) break;
                    mipWidth = Math.Max(1, mipWidth / 2); mipHeight = Math.Max(1, mipHeight / 2);
                }
                upload = new byte[bytes]; Buffer.BlockCopy(raw, 0, upload, 0, raw.Length);
            }
            var pin = GCHandle.Alloc(upload, GCHandleType.Pinned);
            try { texture.LoadRawTextureData(pin.AddrOfPinnedObject(), upload.Length); texture.Apply(ground, true); }
            finally { pin.Free(); }
            texture.filterMode = ground ? FilterMode.Trilinear : FilterMode.Bilinear;
            texture.anisoLevel = 4;
            texture.wrapModeU = j.GetProperty("wrapU").GetBoolean() ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            texture.wrapModeV = j.GetProperty("wrapV").GetBoolean() ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        }
        foreach (var entry in manifest.GetProperty("groundTextures").EnumerateObject())
            GroundTextures.Add(entry.Name, textures[entry.Value.GetString()]);

        prototypes = Retain(new GameObject("AzerothElwynn_PrototypeLibrary"));
        prototypes.SetActive(false);
        Object.DontDestroyOnLoad(prototypes);
        foreach (var entry in manifest.GetProperty("props").EnumerateObject())
            LoadProp(entry.Name, entry.Value.GetProperty("file").GetString());
        if (GroundTextures.Count != 6 || Props.Count != 15)
            throw new InvalidDataException("The original Elwynn source pack is incomplete");
    }

    static float[] Floats(JsonElement j, string key) => j.GetProperty(key).EnumerateArray().Select(x => x.GetSingle()).ToArray();
    static Vector3 Vector(JsonElement j) { var p = j.EnumerateArray().Select(x => x.GetSingle()).ToArray(); return new(p[0], p[1], p[2]); }

    void LoadProp(string id, string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(PathInPack(file)));
        var j = doc.RootElement;
        if (j.GetProperty("format").GetString() != "azeroth-elwynn-prop-v1" || j.GetProperty("id").GetString() != id)
            throw new InvalidDataException("Elwynn model identity mismatch: " + id);
        int count = j.GetProperty("vertexCount").GetInt32();
        var p = Floats(j, "positions"); var n = Floats(j, "normals"); var uv = Floats(j, "uv");
        if (p.Length != count * 3 || n.Length != count * 3 || uv.Length != count * 2)
            throw new InvalidDataException("Elwynn model vertex arrays mismatch: " + id);
        var vertices = new Vector3[count]; var normals = new Vector3[count]; var uvs = new Vector2[count];
        for (int i = 0; i < count; i++)
        {
            vertices[i] = new(p[i * 3], p[i * 3 + 1], p[i * 3 + 2]);
            normals[i] = new(n[i * 3], n[i * 3 + 1], n[i * 3 + 2]);
            uvs[i] = new(uv[i * 2], uv[i * 2 + 1]);
        }
        var min = Vector(j.GetProperty("boundsMin")); var max = Vector(j.GetProperty("boundsMax"));
        PropBounds.Add(id, new Bounds((min + max) * .5f, max - min));
        PropSources.Add(id, j.GetProperty("source").GetProperty("path").GetString());
        var prefab = Retain(new GameObject("AzerothElwynn_" + id));
        prefab.SetActive(false);
        prefab.transform.SetParent(prototypes.transform, false);
        Props.Add(id, prefab);
        SourceVertices += count;
        int passIndex = 0;
        foreach (var pass in j.GetProperty("passes").EnumerateArray())
        {
            int flags = pass.GetProperty("renderFlags").GetInt32(), blend = pass.GetProperty("blendMode").GetInt32();
            var textureKey = pass.GetProperty("texture").GetString();
            string materialKey = textureKey + ":" + flags + ":" + blend;
            if (!materials.TryGetValue(materialKey, out var material))
            {
                material = CreateMaterial(materialKey, textures[textureKey], flags, blend);
                materials.Add(materialKey, material);
            }
            var triangles = pass.GetProperty("triangles").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            if (triangles.Length == 0 || triangles.Length % 3 != 0 || triangles.Any(x => x < 0 || x >= count))
                throw new InvalidDataException("Elwynn model triangle indices mismatch: " + id);
            SourceTriangles += triangles.Length / 3;
            // Some source grass/pine cards explicitly disable back-face culling.
            // Duplicating their reverse winding preserves that behavior even if
            // a stripped fallback shader does not expose a _Cull property.
            if ((flags & 4) != 0)
            {
                int size = triangles.Length;
                Array.Resize(ref triangles, size * 2);
                for (int i = 0; i < size; i += 3)
                { triangles[size + i] = triangles[i]; triangles[size + i + 1] = triangles[i + 2]; triangles[size + i + 2] = triangles[i + 1]; }
            }
            var mesh = Retain(new Mesh { name = "AzerothElwynn_" + id + "_Pass" + passIndex });
            meshes.Add(mesh); mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uvs; mesh.triangles = triangles;
            mesh.RecalculateBounds();
            var child = Retain(new GameObject("AzerothElwynn_OriginalPass_" + passIndex++));
            child.transform.SetParent(prefab.transform, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.receiveShadows = true;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
    }

    Material CreateMaterial(string key, Texture2D texture, int flags, int blend)
    {
        bool cutout = blend == 1;
        if (blend != 0 && !cutout)
            throw new InvalidDataException("Unexpected Elwynn static material blend mode " + blend);
        string[] names = cutout
            ? new[] { "Unlit/Transparent Cutout", "Legacy Shaders/Transparent/Cutout/Diffuse", "Standard", "Sprites/Default" }
            : new[] { "Standard", "Legacy Shaders/Diffuse", "Unlit/Texture", "Sprites/Default" };
        Shader shader = null;
        foreach (var name in names) { shader = Shader.Find(name); if (shader != null) break; }
        if (shader == null) throw new InvalidOperationException("No compatible Elwynn texture shader is loaded");
        var material = Retain(new Material(shader) { name = "AzerothElwynn_Material_" + key, mainTexture = texture, color = Color.white });
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", .05f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0);
        if (cutout)
        {
            if (material.HasProperty("_Cutoff")) material.SetFloat("_Cutoff", .5f);
            if (material.HasProperty("_Mode")) material.SetFloat("_Mode", 1);
            if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 1);
            if (material.HasProperty("_SrcBlend")) material.SetInt("_SrcBlend", 1);
            if (material.HasProperty("_DstBlend")) material.SetInt("_DstBlend", 0);
            if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 1);
            material.EnableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = 2450;
            if (shader.name == "Sprites/Default") shaderWarnings.Add("AlphaKey fallback uses sprite alpha blending: " + key);
        }
        if ((flags & 4) != 0 && material.HasProperty("_Cull")) material.SetInt("_Cull", 0);
        return material;
    }

    public bool AllAssetsValid => !released && prototypes != null && GroundTextures.Count == 6 && Props.Count == 15
        && textures.Values.All(x => x != null) && materials.Values.All(x => x != null)
        && meshes.All(x => x != null) && Props.Values.All(x => x != null);

    public string DiagnosticsJson() => JsonSerializer.Serialize(new
    {
        allValid = AllAssetsValid, packVersion = Version, groundCount = GroundTextures.Count, propCount = Props.Count,
        textureCount = textures.Count, meshCount = meshes.Count, materialCount = materials.Count,
        sourceVertices = SourceVertices, sourceTriangles = SourceTriangles,
        invalidTextures = textures.Values.Count(x => x == null), invalidMeshes = meshes.Count(x => x == null),
        invalidMaterials = materials.Values.Count(x => x == null),
        retainedTextures = textures.Values.Count(x => x != null && (x.hideFlags & HideFlags.DontUnloadUnusedAsset) != 0),
        retainedMeshes = meshes.Count(x => x != null && (x.hideFlags & HideFlags.DontUnloadUnusedAsset) != 0),
        groundFiltering = GroundTextures.Select(x => new
            { id = x.Key, mipCount = x.Value != null ? x.Value.mipmapCount : 0, filter = x.Value != null ? x.Value.filterMode.ToString() : "missing" }).ToArray(),
        shaders = materials.Values.Where(x => x != null).Select(x => x.shader.name).Distinct().ToArray(),
        alphaWarnings = shaderWarnings.ToArray()
    });

    public void Release()
    {
        if (released) return;
        released = true;
        if (prototypes != null) { prototypes.SetActive(false); Object.Destroy(prototypes); }
        foreach (var mesh in meshes) if (mesh != null) Object.Destroy(mesh);
        foreach (var material in materials.Values) if (material != null) Object.Destroy(material);
        foreach (var texture in textures.Values) if (texture != null) Object.Destroy(texture);
        meshes.Clear(); materials.Clear(); textures.Clear(); GroundTextures.Clear(); Props.Clear(); PropBounds.Clear(); PropSources.Clear();
    }

    public void Dispose() => Release();
}
