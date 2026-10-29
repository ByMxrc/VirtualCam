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
///
/// Parámetros opcionales:
///   flipV          — invierte la coordenada V de las UVs (glTF origen arriba-izq, Unity abajo-izq).
///                    Activarlo en avatares; dejarlo en false para la mesa (MinecraftTable).
///   buildHierarchy — reconstruye el árbol de nodos del glTF en GameObjects emparentados.
///                    Activarlo en avatares para que Hat/Jacket sigan a la cabeza/brazos.
/// </summary>
public static class GltfModelLoader
{
    static readonly string[] VecComponentNames = { "x", "y", "z", "w" };

    public static GameObject Load(string gltfPath, string textureFolder, string objectName = "GltfModel",
                                  bool flipV = false, bool buildHierarchy = false)
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

        if (buildHierarchy)
        {
            // Reconstruir el árbol completo de nodos del glTF.
            // Primero creamos todos los GameObjects, luego los emparentamos según children[].
            int nodeCount = nodes.Count;
            var nodeObjects = new GameObject[nodeCount];

            for (int ni = 0; ni < nodeCount; ni++)
            {
                var nd = nodes[ni] as Dictionary<string, object>;
                string nodeName = GetString(nd, "name");
                if (string.IsNullOrEmpty(nodeName)) nodeName = "node_" + ni;

                var nodeGo = new GameObject(nodeName);
                nodeObjects[ni] = nodeGo;

                // Aplicar translation / rotation / scale del nodo si existen.
                ApplyNodeTransform(nd, nodeGo.transform);

                // Adjuntar malla si el nodo tiene una.
                int meshIdx = GetInt(nd, "mesh");
                if (meshIdx >= 0 && meshIdx < meshes.Count)
                {
                    if (!meshCache.TryGetValue(meshIdx, out Mesh mesh))
                    {
                        mesh = BuildMesh(meshes[meshIdx] as Dictionary<string, object>,
                                         accessors, bufferViews, bufferData, flipV);
                        meshCache[meshIdx] = mesh;
                    }

                    if (mesh != null)
                    {
                        nodeGo.AddComponent<MeshFilter>().sharedMesh = mesh;

                        var renderer = nodeGo.AddComponent<MeshRenderer>();
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
                }
            }

            // Segunda pasada: emparentar según children[] del glTF.
            // Los nodos sin padre se cuelgan del objeto raíz go.
            bool[] hasParent = new bool[nodeCount];
            for (int ni = 0; ni < nodeCount; ni++)
            {
                var nd = nodes[ni] as Dictionary<string, object>;
                var children = GetList(nd, "children");
                if (children == null) continue;
                foreach (var c in children)
                {
                    int childIdx = -1;
                    if (c is double d) childIdx = (int)d;
                    else if (c is int i) childIdx = i;
                    else if (c is long l) childIdx = (int)l;
                    else { try { childIdx = Convert.ToInt32(c, CultureInfo.InvariantCulture); } catch { } }

                    if (childIdx >= 0 && childIdx < nodeCount)
                    {
                        nodeObjects[childIdx].transform.SetParent(nodeObjects[ni].transform, false);
                        hasParent[childIdx] = true;
                    }
                }
            }

            // Colgar los nodos raíz (sin padre) del objeto go.
            for (int ni = 0; ni < nodeCount; ni++)
            {
                if (!hasParent[ni])
                    nodeObjects[ni].transform.SetParent(go.transform, false);
            }
        }
        else
        {
            // Comportamiento plano original: solo nodos con malla, todos hijos de go.
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
                    mesh = BuildMesh(meshes[meshIdx] as Dictionary<string, object>,
                                     accessors, bufferViews, bufferData, flipV);
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
        }

        return go;
    }

    // ── Aplicar transformación de nodo glTF ──────────────────────────────────

    static void ApplyNodeTransform(Dictionary<string, object> nd, Transform t)
    {
        // translation
        var transList = GetList(nd, "translation");
        if (transList != null && transList.Count >= 3)
        {
            float tx = ToFloat(transList[0]);
            float ty = ToFloat(transList[1]);
            float tz = ToFloat(transList[2]);
            t.localPosition = new Vector3(tx, ty, tz);
        }

        // rotation (quaternion XYZW en glTF)
        var rotList = GetList(nd, "rotation");
        if (rotList != null && rotList.Count >= 4)
        {
            float rx = ToFloat(rotList[0]);
            float ry = ToFloat(rotList[1]);
            float rz = ToFloat(rotList[2]);
            float rw = ToFloat(rotList[3]);
            t.localRotation = new Quaternion(rx, ry, rz, rw);
        }

        // scale
        var scaleList = GetList(nd, "scale");
        if (scaleList != null && scaleList.Count >= 3)
        {
            float sx = ToFloat(scaleList[0]);
            float sy = ToFloat(scaleList[1]);
            float sz = ToFloat(scaleList[2]);
            t.localScale = new Vector3(sx, sy, sz);
        }
    }

    static float ToFloat(object o)
    {
        if (o is double d) return (float)d;
        if (o is float f) return f;
        if (o is int i) return i;
        try { return Convert.ToSingle(o, CultureInfo.InvariantCulture); } catch { return 0f; }
    }

    // ── Construcción de malla ────────────────────────────────────────────────

    static Mesh BuildMesh(Dictionary<string, object> meshDef,
                          List<object> accessors,
                          List<object> bufferViews,
                          List<byte[]> bufferData,
                          bool flipV)
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
            var uv = uvAcc >= 0 ? ReadAccessorUV(accessors, bufferViews, bufferData, uvAcc, flipV) : null;
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

    /// <summary>
    /// Lee coordenadas UV. Si flipV es true, invierte la coordenada V para compatibilizar
    /// el sistema glTF (origen arriba-izquierda) con Unity (origen abajo-izquierda).
    /// </summary>
    static List<Vector2> ReadAccessorUV(List<object> accessors, List<object> bufferViews, List<byte[]> bufferData, int accIdx, bool flipV)
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
            // glTF: V=0 es borde SUPERIOR. Unity: V=0 es borde INFERIOR. Invertir si flipV=true.
            result.Add(new Vector2(x, flipV ? 1f - y : y));
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
