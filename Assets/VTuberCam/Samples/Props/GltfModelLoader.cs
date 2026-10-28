using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Carga un modelo .gltf (exportado de Blockbench) en runtime y construye la malla.
/// Soporta buffers embebidos en base64 y texturas desde archivos .png de la carpeta.
/// Las coordenadas del modelo ya vienen en bloques (1 unidad = 1 bloque de Minecraft).
/// </summary>
public static class GltfModelLoader
{
    static readonly string[] VecComponentNames = { "x", "y", "z", "w" };

    public static GameObject Load(string gltfPath, string textureFolder, string objectName = "GltfModel")
    {
        if (string.IsNullOrEmpty(gltfPath) || !File.Exists(gltfPath))
        {
            Debug.LogError("[Gltf] No se encontro el modelo: " + gltfPath);
            return null;
        }

        string json = File.ReadAllText(gltfPath);
        Dictionary<string, object> root;
        try
        {
            root = BlockbenchModelLoader.MiniJson.Parse(json) as Dictionary<string, object>;
        }
        catch (Exception e)
        {
            Debug.LogError("[Gltf] JSON invalido (" + gltfPath + "): " + e.Message);
            return null;
        }

        if (root == null)
        {
            Debug.LogError("[Gltf] El GLTF no contiene un objeto raiz: " + gltfPath);
            return null;
        }

        var buffers = GetList(root, "buffers");
        var bufferViews = GetList(root, "bufferViews");
        var accessors = GetList(root, "accessors");
        var meshes = GetList(root, "meshes");
        var nodes = GetList(root, "nodes");
        var materials = GetList(root, "materiales");
        if (materials == null) materials = GetList(root, "materials");
        var textures = GetList(root, "textures");
        var images = GetList(root, "images");

        if (buffers == null || bufferViews == null || accessors == null || meshes == null || nodes == null)
        {
            Debug.LogError("[Gltf] El modelo no tiene los campos requeridos (buffers, bufferViews, accessors, meshes, nodes).");
            return null;
        }

        // ── Leer buffer(es) embebidos en base64 ──────────────────────────────
        var bufferData = new List<byte[]>();
        foreach (var b in buffers)
        {
            var dict = b as Dictionary<string, object>;
            string uri = GetString(dict, "uri");
            byte[] data = null;
            if (!string.IsNullOrEmpty(uri) && uri.StartsWith("data:application/octet-stream;base64,"))
            {
                string b64 = uri.Substring("data:application/octet-stream;base64,".Length);
                data = Convert.FromBase64String(b64);
            }
            if (data == null)
            {
                Debug.LogError("[Gltf] No se pudo leer el buffer: " + uri);
                return null;
            }
            bufferData.Add(data);
        }

        // ── Cargar texturas ─────────────────────────────────────────────────
        var textureMaterials = new List<Material>();
        if (textures != null && images != null)
        {
            // Cargar archivos .png de la carpeta (orden alfabetico) como respaldo
            var pngFiles = new List<string>();
            if (!string.IsNullOrEmpty(textureFolder) && Directory.Exists(textureFolder))
            {
                foreach (var f in Directory.GetFiles(textureFolder, "*.png"))
                    pngFiles.Add(f);
                pngFiles.Sort();
            }

            foreach (var t in textures)
            {
                var td = t as Dictionary<string, object>;
                int sourceIdx = GetInt(td, "source");
                Shader shader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Unlit/Texture");
                Material mat = new Material(shader);

                if (images != null && sourceIdx >= 0 && sourceIdx < images.Count)
                {
                    var img = images[sourceIdx] as Dictionary<string, object>;
                    string uri = GetString(img, "uri");
                    if (!string.IsNullOrEmpty(uri) && uri.StartsWith("data:image/png;base64,"))
                    {
                        string b64 = uri.Substring("data:image/png;base64,".Length);
                        try
                        {
                            var tex = LoadTextureFromBase64(b64, GetString(img, "mimeType"));
                            if (tex != null) mat.mainTexture = tex;
                        }
                        catch { }
                    }
                }

                // Si no se cargo desde base64, intentar desde archivo .png por indice
                if (mat.mainTexture == null && pngFiles.Count > 0)
                {
                    int fileIdx = Mathf.Min(textureMaterials.Count, pngFiles.Count - 1);
                    string pngPath = pngFiles[fileIdx];
                    var tex = LoadTextureFromFile(pngPath);
                    if (tex != null) mat.mainTexture = tex;
                    else Debug.LogWarning("[Gltf] Falta textura para indice " + sourceIdx + " (no hay .png disponible).");
                }

                ConfigureMaterial(mat);
                textureMaterials.Add(mat);
            }
        }

        // ── Construir meshes ────────────────────────────────────────────────
        var go = new GameObject(objectName);
        var meshCache = new Dictionary<int, Mesh>(); // meshIndex -> Mesh

        foreach (var n in nodes)
        {
            var nd = n as Dictionary<string, object>;
            int meshIdx = GetInt(nd, "mesh");
            if (meshIdx < 0) continue;

            string nodeName = GetString(nd, "name");
            if (string.IsNullOrEmpty(nodeName)) nodeName = "node";

            var child = new GameObject(nodeName);
            child.transform.SetParent(go.transform, false);

            if (!meshCache.TryGetValue(meshIdx, out Mesh mesh))
            {
                mesh = BuildMesh(meshes[meshIdx] as Dictionary<string, object>, accessors, bufferViews, bufferData);
                meshCache[meshIdx] = mesh;
            }

            if (mesh == null) continue;

            child.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = child.AddComponent<MeshRenderer>();
            var md = meshes[meshIdx] as Dictionary<string, object>;
            var primitives = GetList(md, "primitives");
            var mats = new List<Material>();
            if (primitives != null)
            {
                foreach (var p in primitives)
                {
                    var pd = p as Dictionary<string, object>;
                    int matIdx = GetInt(pd, "material");
                    Material m = (matIdx >= 0 && matIdx < textureMaterials.Count) ? textureMaterials[matIdx] : null;
                    mats.Add(m ?? new Material(Shader.Find("Standard")));
                }
            }
            renderer.sharedMaterials = mats.ToArray();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        return go;
    }

    static Mesh BuildMesh(Dictionary<string, object> meshDef,
                          List<object> accessors,
                          List<object> bufferViews,
                          List<byte[]> bufferData)
    {
        var primitives = GetList(meshDef, "primitives");
        if (primitives == null || primitives.Count == 0) return null;

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var subMeshIndices = new List<List<int>>();
        var subMeshMaterials = new List<int>();

        int vertexOffset = 0;

        foreach (var p in primitives)
        {
            var pd = p as Dictionary<string, object>;
            var attributes = GetDict(pd, "attributes");
            int posAcc = GetInt(attributes, "POSITION");
            int norAcc = attributes.ContainsKey("NORMAL") ? GetInt(attributes, "NORMAL") : -1;
            int uvAcc = attributes.ContainsKey("TEXCOORD_0") ? GetInt(attributes, "TEXCOORD_0") : -1;
            int idxAcc = GetInt(pd, "indices");

            var pos = ReadAccessor(accessors, bufferViews, bufferData, posAcc);
            var nor = norAcc >= 0 ? ReadAccessor(accessors, bufferViews, bufferData, norAcc) : null;
            var uv = uvAcc >= 0 ? ReadAccessorUV(accessors, bufferViews, bufferData, uvAcc) : null;
            var idx = ReadAccessorIndices(accessors, bufferViews, bufferData, idxAcc);

            if (pos == null || idx == null) continue;

            int start = vertices.Count;
            vertices.AddRange(pos);
            if (nor != null) normals.AddRange(nor); else { for (int i = 0; i < pos.Count; i++) normals.Add(Vector3.up); }
            if (uv != null) uvs.AddRange(uv); else { for (int i = 0; i < pos.Count; i++) uvs.Add(Vector2.zero); }

            subMeshIndices.Add(new List<int>());
            foreach (var tri in idx)
                subMeshIndices[^1].Add(start + tri);
            subMeshMaterials.Add(GetInt(pd, "material"));

            vertexOffset += pos.Count;
        }

        var mesh = new Mesh { name = "GltfMesh" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);

        // Submeshes por material
        mesh.subMeshCount = subMeshIndices.Count;
        for (int i = 0; i < subMeshIndices.Count; i++)
            mesh.SetIndices(subMeshIndices[i].ToArray(), MeshTopology.Triangles, i, true);

        mesh.RecalculateBounds();
        return mesh;
    }

    // ── Lectura de accessors ───────────────────────────────────────────────

    static List<Vector3> ReadAccessor(List<object> accessors, List<object> bufferViews, List<byte[]> bufferData, int accIdx)
    {
        var ad = accessors[accIdx] as Dictionary<string, object>;
        int bvIdx = GetInt(ad, "bufferView");
        var bvd = bufferViews[bvIdx] as Dictionary<string, object>;
        int bufferIdx = GetInt(bvd, "buffer");
        int accByteOffset = ad.ContainsKey("byteOffset") ? GetInt(ad, "byteOffset") : 0;
        int bvByteOffset = GetInt(bvd, "byteOffset");
        int stride = bvd.ContainsKey("byteStride") ? GetInt(bvd, "byteStride") : 0;
        int count = GetInt(ad, "count");
        int componentType = GetInt(ad, "componentType");
        string type = GetString(ad, "type");

        int compSize = ComponentSize(componentType);
        int comps = TypeComponents(type);
        int elementBytes = comps * compSize;
        if (stride == 0) stride = elementBytes;

        byte[] buf = bufferData[bufferIdx];
        int start = bvByteOffset + accByteOffset;

        var result = new List<Vector3>(count);
        for (int i = 0; i < count; i++)
        {
            int off = start + i * stride;
            float x = ReadComponent(buf, off, componentType);
            float y = comps >= 2 ? ReadComponent(buf, off + compSize, componentType) : 0f;
            float z = comps >= 3 ? ReadComponent(buf, off + 2 * compSize, componentType) : 0f;
            result.Add(new Vector3(x, y, z));
        }
        return result;
    }

    static List<Vector2> ReadAccessorUV(List<object> accessors, List<object> bufferViews, List<byte[]> bufferData, int accIdx)
    {
        var ad = accessors[accIdx] as Dictionary<string, object>;
        int bvIdx = GetInt(ad, "bufferView");
        var bvd = bufferViews[bvIdx] as Dictionary<string, object>;
        int bufferIdx = GetInt(bvd, "buffer");
        int accByteOffset = ad.ContainsKey("byteOffset") ? GetInt(ad, "byteOffset") : 0;
        int bvByteOffset = GetInt(bvd, "byteOffset");
        int stride = bvd.ContainsKey("byteStride") ? GetInt(bvd, "byteStride") : 0;
        int count = GetInt(ad, "count");
        int componentType = GetInt(ad, "componentType");

        int compSize = ComponentSize(componentType);
        int elementBytes = 2 * compSize;
        if (stride == 0) stride = elementBytes;

        byte[] buf = bufferData[bufferIdx];
        int start = bvByteOffset + accByteOffset;

        var result = new List<Vector2>(count);
        for (int i = 0; i < count; i++)
        {
            int off = start + i * stride;
            float x = ReadComponent(buf, off, componentType);
            float y = ReadComponent(buf, off + compSize, componentType);
            result.Add(new Vector2(x, y));
        }
        return result;
    }

    static List<int> ReadAccessorIndices(List<object> accessors, List<object> bufferViews, List<byte[]> bufferData, int accIdx)
    {
        var ad = accessors[accIdx] as Dictionary<string, object>;
        int bvIdx = GetInt(ad, "bufferView");
        var bvd = bufferViews[bvIdx] as Dictionary<string, object>;
        int bufferIdx = GetInt(bvd, "buffer");
        int accByteOffset = ad.ContainsKey("byteOffset") ? GetInt(ad, "byteOffset") : 0;
        int bvByteOffset = GetInt(bvd, "byteOffset");
        int count = GetInt(ad, "count");
        int componentType = GetInt(ad, "componentType");

        int compSize = ComponentSize(componentType);
        byte[] buf = bufferData[bufferIdx];
        int start = bvByteOffset + accByteOffset;

        var result = new List<int>(count);
        for (int i = 0; i < count; i++)
        {
            int off = start + i * compSize;
            result.Add(componentType == 5123 ? BitConverter.ToUInt16(buf, off) : // 5123 = unsigned short
                       componentType == 5125 ? BitConverter.ToInt32(buf, off) :  // 5125 = unsigned int
                       componentType == 5121 ? buf[off] : 0);                  // 5121 = unsigned byte
        }
        return result;
    }

    static float ReadComponent(byte[] buf, int offset, int componentType)
    {
        switch (componentType)
        {
            case 5126: return BitConverter.ToSingle(buf, offset); // float
            case 5123: return BitConverter.ToUInt16(buf, offset); // unsigned short
            case 5125: return BitConverter.ToInt32(buf, offset);  // unsigned int (leído como int)
            case 5121: return buf[offset];                        // unsigned byte
            case 5122: return BitConverter.ToInt16(buf, offset);  // short
            case 5120: return (sbyte)buf[offset];                 // byte
            default: return 0f;
        }
    }

    static int ComponentSize(int componentType)
    {
        switch (componentType)
        {
            case 5120: case 5121: return 1;
            case 5122: case 5123: return 2;
            case 5125: return 4;
            case 5126: return 4;
            default: return 4;
        }
    }

    static int TypeComponents(string type)
    {
        switch (type)
        {
            case "SCALAR": return 1;
            case "VEC2": return 2;
            case "VEC3": return 3;
            case "VEC4": return 4;
            default: return 3;
        }
    }

    // ── Texturas ───────────────────────────────────────────────────────────

    static Texture2D LoadTextureFromBase64(string b64, string mimeType)
    {
        byte[] data = Convert.FromBase64String(b64);
        var tex = new Texture2D(2, 2);
        if (tex.LoadImage(data))
        {
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Repeat;
            return tex;
        }
        return null;
    }

    static Texture2D LoadTextureFromFile(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        var tex = new Texture2D(2, 2);
        if (tex.LoadImage(data))
        {
            tex.name = Path.GetFileNameWithoutExtension(path);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Repeat;
            return tex;
        }
        return null;
    }

    static void ConfigureMaterial(Material mat)
    {
        Shader shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Unlit/Texture");
        mat.shader = shader;

        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0f);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0f);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
        mat.color = Color.white;

        // Pequeno refuerzo de emision para que no se vea oscura con la luz de arriba
        if (mat.HasProperty("_EmissionMap") && mat.HasProperty("_EmissionColor") && mat.mainTexture != null)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetTexture("_EmissionMap", mat.mainTexture);
            mat.SetColor("_EmissionColor", new Color(0.22f, 0.22f, 0.22f, 1f));
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
    }

    // ── Helpers MiniJson ───────────────────────────────────────────────────

    static Dictionary<string, object> GetDict(Dictionary<string, object> obj, string key)
    {
        if (obj != null && obj.TryGetValue(key, out object value)) return value as Dictionary<string, object>;
        return null;
    }

    static List<object> GetList(Dictionary<string, object> obj, string key)
    {
        if (obj != null && obj.TryGetValue(key, out object value)) return value as List<object>;
        return null;
    }

    static string GetString(Dictionary<string, object> obj, string key)
    {
        if (obj != null && obj.TryGetValue(key, out object value)) return value as string;
        return null;
    }

    static int GetInt(Dictionary<string, object> obj, string key)
    {
        if (obj == null || !obj.TryGetValue(key, out object value)) return -1;
        if (value is double d) return (int)d;
        if (value is int i) return i;
        if (value is long l) return (int)l;
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    static bool ContainsKey(Dictionary<string, object> obj, string key)
    {
        return obj != null && obj.ContainsKey(key);
    }
}
