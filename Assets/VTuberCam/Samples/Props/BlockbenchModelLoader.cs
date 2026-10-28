using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Carga un modelo de Blockbench (.json de bloque/item) en runtime y construye la malla.
/// Escala: 16 "pixeles" de Blockbench = 1 bloque = 1 unidad de Unity.
/// El pivote se centra en X/Z (coordenada 8 de Blockbench) y el suelo queda en Y = 0.
/// </summary>
public static class BlockbenchModelLoader
{
    public const float PixelsPerBlock = 16f;

    static readonly string[] FaceOrder = { "north", "east", "south", "west", "up", "down" };

    /// <summary>
    /// Crea un GameObject con la geometria del modelo.
    /// </summary>
    /// <param name="jsonPath">Ruta absoluta al .json exportado de Blockbench.</param>
    /// <param name="textureFolder">Carpeta que contiene los .png de las texturas.</param>
    public static GameObject Load(string jsonPath, string textureFolder, string objectName = "BlockbenchModel")
    {
        if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath))
        {
            Debug.LogError("[Blockbench] No se encontro el modelo: " + jsonPath);
            return null;
        }

        Dictionary<string, object> root;
        try
        {
            root = MiniJson.Parse(File.ReadAllText(jsonPath)) as Dictionary<string, object>;
        }
        catch (Exception e)
        {
            Debug.LogError("[Blockbench] JSON invalido (" + jsonPath + "): " + e.Message);
            return null;
        }

        if (root == null)
        {
            Debug.LogError("[Blockbench] El JSON no contiene un objeto raiz: " + jsonPath);
            return null;
        }

        var textures = GetDict(root, "textures");
        var elements = GetList(root, "elements");
        if (elements == null || elements.Count == 0)
        {
            Debug.LogError("[Blockbench] El modelo no tiene 'elements': " + jsonPath);
            return null;
        }

        var go = new GameObject(objectName);
        var materialCache = new Dictionary<string, Material>();
        Material fallback = CreateMaterial(null);

        for (int i = 0; i < elements.Count; i++)
        {
            var element = elements[i] as Dictionary<string, object>;
            if (element == null) continue;

            Vector3? from = GetVector3(element, "from");
            Vector3? to = GetVector3(element, "to");
            var faces = GetDict(element, "faces");
            if (from == null || to == null || faces == null) continue;

            // Blockbench: X/Z centrados en 8, Y con el suelo en 0.
            Vector3 min = new Vector3(
                (Mathf.Min(from.Value.x, to.Value.x) - 8f) / PixelsPerBlock,
                Mathf.Min(from.Value.y, to.Value.y) / PixelsPerBlock,
                (Mathf.Min(from.Value.z, to.Value.z) - 8f) / PixelsPerBlock);

            Vector3 max = new Vector3(
                (Mathf.Max(from.Value.x, to.Value.x) - 8f) / PixelsPerBlock,
                Mathf.Max(from.Value.y, to.Value.y) / PixelsPerBlock,
                (Mathf.Max(from.Value.z, to.Value.z) - 8f) / PixelsPerBlock);

            Mesh mesh = BuildElementMesh(faces, textures, textureFolder, materialCache, fallback, min, max,
                out Material[] elementMaterials);
            if (mesh == null) continue;

            string elementName = GetString(element, "name");
            if (string.IsNullOrEmpty(elementName)) elementName = "element_" + i;

            var child = new GameObject(elementName);
            child.transform.SetParent(go.transform, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = elementMaterials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        return go;
    }

    // ─────────────────────────────────────────────────────────────────────────
    static Mesh BuildElementMesh(
        Dictionary<string, object> faces,
        Dictionary<string, object> textures,
        string textureFolder,
        Dictionary<string, Material> materialCache,
        Material fallback,
        Vector3 min,
        Vector3 max,
        out Material[] elementMaterials)
    {
        elementMaterials = null;
        var vertices = new List<Vector3>(24);
        var uvs = new List<Vector2>(24);
        var normals = new List<Vector3>(24);
        var triangles = new List<int[]>(6);
        var materials = new List<Material>(6);

        Vector3 center = (min + max) * 0.5f;
        Vector3 half = (max - min) * 0.5f;

        foreach (string faceName in FaceOrder)
        {
            if (!faces.TryGetValue(faceName, out object faceObj)) continue;

            var face = faceObj as Dictionary<string, object>;
            if (face == null) continue;

            GetFaceAxes(faceName, out Vector3 normal, out Vector3 right, out Vector3 up);

            float extentRight = Mathf.Abs(right.x) * half.x + Mathf.Abs(right.y) * half.y + Mathf.Abs(right.z) * half.z;
            float extentUp = Mathf.Abs(up.x) * half.x + Mathf.Abs(up.y) * half.y + Mathf.Abs(up.z) * half.z;

            // Esquinas: abajo-izquierda, abajo-derecha, arriba-derecha, arriba-izquierda.
            Vector3 c0 = center - right * extentRight - up * extentUp;
            Vector3 c1 = center + right * extentRight - up * extentUp;
            Vector3 c2 = center + right * extentRight + up * extentUp;
            Vector3 c3 = center - right * extentRight + up * extentUp;

            float u1 = 0f, v1 = 0f, u2 = PixelsPerBlock, v2 = PixelsPerBlock;
            if (face.TryGetValue("uv", out object uvObj) && uvObj is List<object> uv && uv.Count >= 4)
            {
                u1 = ToFloat(uv[0]);
                v1 = ToFloat(uv[1]);
                u2 = ToFloat(uv[2]);
                v2 = ToFloat(uv[3]);
            }

            // Blockbench usa V desde arriba; Unity desde abajo.
            Vector2 uv0 = new Vector2(u1 / PixelsPerBlock, 1f - v2 / PixelsPerBlock);
            Vector2 uv1 = new Vector2(u2 / PixelsPerBlock, 1f - v2 / PixelsPerBlock);
            Vector2 uv2 = new Vector2(u2 / PixelsPerBlock, 1f - v1 / PixelsPerBlock);
            Vector2 uv3 = new Vector2(u1 / PixelsPerBlock, 1f - v1 / PixelsPerBlock);

            int baseIndex = vertices.Count;
            vertices.Add(c0); vertices.Add(c1); vertices.Add(c2); vertices.Add(c3);
            normals.Add(normal); normals.Add(normal); normals.Add(normal); normals.Add(normal);
            uvs.Add(uv0); uvs.Add(uv1); uvs.Add(uv2); uvs.Add(uv3);
            triangles.Add(new[] { baseIndex, baseIndex + 2, baseIndex + 1, baseIndex, baseIndex + 3, baseIndex + 2 });

            string texturePath = ResolveTexturePath(face, textures, textureFolder);
            Material material = fallback;
            if (!string.IsNullOrEmpty(texturePath))
            {
                if (!materialCache.TryGetValue(texturePath, out material) || material == null)
                {
                    material = CreateMaterial(texturePath);
                    materialCache[texturePath] = material;
                }
            }
            materials.Add(material);
        }

        if (triangles.Count == 0) return null;

        var mesh = new Mesh { name = "BlockbenchElement" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetNormals(normals);
        mesh.subMeshCount = triangles.Count;
        for (int i = 0; i < triangles.Count; i++)
            mesh.SetTriangles(triangles[i], i, true);
        mesh.RecalculateBounds();

        elementMaterials = materials.ToArray();
        return mesh;
    }

    static void GetFaceAxes(string face, out Vector3 normal, out Vector3 right, out Vector3 up)
    {
        switch (face)
        {
            case "north": normal = Vector3.back;    up = Vector3.up;      break;
            case "south": normal = Vector3.forward; up = Vector3.up;      break;
            case "east":  normal = Vector3.right;   up = Vector3.up;      break;
            case "west":  normal = Vector3.left;    up = Vector3.up;      break;
            case "up":    normal = Vector3.up;      up = Vector3.forward; break;
            case "down":  normal = Vector3.down;    up = Vector3.forward; break;
            default:      normal = Vector3.forward; up = Vector3.up;      break;
        }
        right = Vector3.Cross(normal, up);
    }

    static string ResolveTexturePath(
        Dictionary<string, object> face,
        Dictionary<string, object> textures,
        string textureFolder)
    {
        if (!face.TryGetValue("texture", out object textureObj)) return null;

        string textureRef = textureObj as string;
        if (string.IsNullOrEmpty(textureRef)) return null;

        if (textureRef.StartsWith("#", StringComparison.Ordinal) && textures != null)
        {
            string key = textureRef.Substring(1);
            if (textures.TryGetValue(key, out object value))
                textureRef = value as string;
        }

        if (string.IsNullOrEmpty(textureRef)) return null;

        // "block/oak_planks" o "minecraft:block/oak_planks" -> "oak_planks"
        textureRef = textureRef.Replace('\\', '/');
        int colon = textureRef.LastIndexOf(':');
        if (colon >= 0) textureRef = textureRef.Substring(colon + 1);
        int slash = textureRef.LastIndexOf('/');
        if (slash >= 0) textureRef = textureRef.Substring(slash + 1);

        string path = Path.Combine(textureFolder, textureRef + ".png");
        if (!File.Exists(path))
        {
            Debug.LogWarning("[Blockbench] Falta la textura: " + path);
            return null;
        }
        return path;
    }

    static Material CreateMaterial(string texturePath)
    {
        Shader shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Unlit/Texture");

        var material = new Material(shader);

        if (!string.IsNullOrEmpty(texturePath) && File.Exists(texturePath))
        {
            try
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (texture.LoadImage(File.ReadAllBytes(texturePath)))
                {
                    texture.name = Path.GetFileNameWithoutExtension(texturePath);
                    texture.filterMode = FilterMode.Point;
                    texture.wrapMode = TextureWrapMode.Repeat;
                    material.mainTexture = texture;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Blockbench] No se pudo cargar la textura " + texturePath + ": " + e.Message);
            }
        }

        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0f);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
        material.color = Color.white;

        // La luz de la escena viene de arriba y deja las caras frontales casi negras.
        // Un poco de emision con la misma textura mantiene el look Minecraft sin que
        // la mesa se vea como una franja oscura.
        if (material.HasProperty("_EmissionMap") && material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetTexture("_EmissionMap", material.mainTexture);
            material.SetColor("_EmissionColor", new Color(0.22f, 0.22f, 0.22f, 1f));
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        return material;
    }

    // ── Helpers de lectura ───────────────────────────────────────────────────
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

    static Vector3? GetVector3(Dictionary<string, object> obj, string key)
    {
        if (obj == null || !obj.TryGetValue(key, out object value)) return null;
        if (!(value is List<object> list) || list.Count < 3) return null;
        return new Vector3(ToFloat(list[0]), ToFloat(list[1]), ToFloat(list[2]));
    }

    static float ToFloat(object value)
    {
        if (value is double d) return (float)d;
        if (value is float f) return f;
        if (value is long l) return l;
        return Convert.ToSingle(value, CultureInfo.InvariantCulture);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Parser JSON minimo (objetos, arrays, strings, numeros, bool, null)
    // ═════════════════════════════════════════════════════════════════════════
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            int index = 0;
            object value = ParseValue(json, ref index);
            SkipWhitespace(json, ref index);
            if (index < json.Length) throw new FormatException("Texto extra al final del JSON.");
            return value;
        }

        static object ParseValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON inesperado.");

            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't': Expect(s, ref i, "true");  return true;
                case 'f': Expect(s, ref i, "false"); return false;
                case 'n': Expect(s, ref i, "null");  return null;
                default:  return ParseNumber(s, ref i);
            }
        }

        static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var result = new Dictionary<string, object>();
            i++; // {
            while (true)
            {
                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new FormatException("Objeto JSON sin cerrar.");
                if (s[i] == '}') { i++; break; }

                string key = ParseString(s, ref i);
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("Falta ':' en el JSON.");
                i++;

                result[key] = ParseValue(s, ref i);

                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new FormatException("Objeto JSON sin cerrar.");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; break; }
                throw new FormatException("Se esperaba ',' o '}' en el JSON.");
            }
            return result;
        }

        static List<object> ParseArray(string s, ref int i)
        {
            var result = new List<object>();
            i++; // [
            while (true)
            {
                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new FormatException("Array JSON sin cerrar.");
                if (s[i] == ']') { i++; break; }

                result.Add(ParseValue(s, ref i));

                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new FormatException("Array JSON sin cerrar.");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; break; }
                throw new FormatException("Se esperaba ',' o ']' en el JSON.");
            }
            return result;
        }

        static string ParseString(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length || s[i] != '"') throw new FormatException("Se esperaba una cadena JSON.");
            i++;

            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();

                if (c != '\\') { sb.Append(c); continue; }

                if (i >= s.Length) break;
                char escape = s[i++];
                switch (escape)
                {
                    case '"':  sb.Append('"');  break;
                    case '\\': sb.Append('\\'); break;
                    case '/':  sb.Append('/');  break;
                    case 'b':  sb.Append('\b'); break;
                    case 'f':  sb.Append('\f'); break;
                    case 'n':  sb.Append('\n'); break;
                    case 'r':  sb.Append('\r'); break;
                    case 't':  sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("Escape \\u incompleto.");
                        string hex = s.Substring(i, 4);
                        i += 4;
                        sb.Append((char)Convert.ToInt32(hex, 16));
                        break;
                    default:
                        sb.Append(escape);
                        break;
                }
            }
            throw new FormatException("Cadena JSON sin cerrar.");
        }

        static double ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            while (i < s.Length && ((s[i] >= '0' && s[i] <= '9') || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '-' || s[i] == '+'))
                i++;

            string token = s.Substring(start, i - start);
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                throw new FormatException("Numero JSON invalido: " + token);
            return value;
        }

        static void Expect(string s, ref int i, string expected)
        {
            if (i + expected.Length > s.Length || string.CompareOrdinal(s, i, expected, 0, expected.Length) != 0)
                throw new FormatException("Se esperaba '" + expected + "' en el JSON.");
            i += expected.Length;
        }

        static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n'))
                i++;
        }
    }
}
