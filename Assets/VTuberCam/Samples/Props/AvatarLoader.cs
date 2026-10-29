using System.IO;
using UnityEngine;

/// <summary>
/// Carga un avatar .gltf desde StreamingAssets y lo configura para PoseAvatarDriver.
/// Busca automáticamente los huesos por nombre (Torso/Body, Head, Right Arm, Left Arm).
/// Aplica la textura de skin correcta (steve_skin.png o alex_skin.png) con shader cutout.
///
/// Ciclo de carga correcto (Fase 3 del plan):
///   1. Unload() — destruye avatar anterior y libera recursos de GPU
///   2. Carga el GLTF con flipV=true y buildHierarchy=true
///   3. Aplica la skin (shader Unlit/Transparent Cutout, wrapMode Clamp)
///   4. Busca los huesos en la jerarquía reconstruida
///   5. Crea el driver si no existe; siempre llama a driver.Bind(...)
///   6. Habilita el driver
/// </summary>
public class AvatarLoader : MonoBehaviour
{
    [Header("Modelo GLTF")]
    [Tooltip("Ruta relativa dentro de StreamingAssets (ej: Avatars/steve.gltf).")]
    public string gltfPath = "Avatars/steve.gltf";
    [Tooltip("Carpeta con las texturas .png (ej: Avatars).")]
    public string textureFolder = "Avatars";
    [Tooltip("true = Steve (Wide), false = Alex (Slim).")]
    public bool isWide = true;

    [Header("Resultado")]
    [Tooltip("El PoseAvatarDriver se asigna automáticamente si está en la escena.")]
    public PoseAvatarDriver driver;

    [Header("Skin personalizada (opcional)")]
    [Tooltip("Ruta a un archivo .png de skin personalizada. Si se deja vacío, se usa la skin por defecto del modelo.")]
    public string customSkinPath = "";

    [Header("Estado")]
    public bool loaded = false;
    public GameObject avatarInstance;

    // Recursos de GPU creados en AssignSkinTexture — se destruyen en Unload().
    Texture2D _skinTex;
    Material  _skinMat;

    // Shader de skin compartido (se resuelve una sola vez).
    static Shader _skinShader;

    void Awake()
    {
        if (driver == null)
            driver = FindFirstObjectByType<PoseAvatarDriver>();
    }

    /// <summary>Carga el avatar seleccionado y configura el driver.</summary>
    public bool Load()
    {
        // ── 1. Destruir el avatar previo y liberar recursos ──
        Unload();

        string fullPath = Path.Combine(Application.streamingAssetsPath, gltfPath);
        if (!File.Exists(fullPath))
        {
            Debug.LogError("[AvatarLoader] No se encontró el modelo: " + fullPath);
            return false;
        }

        string texFolder = Path.Combine(Application.streamingAssetsPath, textureFolder);

        // ── 2. Cargar GLTF con UVs correctas y jerarquía de nodos ──
        var model = GltfModelLoader.Load(fullPath, texFolder, "AvatarModel",
                                         flipV: true, buildHierarchy: true);
        if (model == null)
        {
            Debug.LogError("[AvatarLoader] No se pudo cargar el GLTF: " + fullPath);
            return false;
        }

        avatarInstance = model;
        model.transform.SetParent(transform, false);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;

        // Asignar capa 6 (Avatar) a todo el modelo para que la cámara virtual lo renderice
        SetLayerRecursively(model, 6);

        // ── 3. Aplicar la skin (Cutout) ──
        AssignSkinTexture(model);

        // ── 4. Buscar huesos en la jerarquía reconstruida ──
        // Con buildHierarchy=true el pivote del nodo es el padre de la malla,
        // y FindChildByName lo encuentra antes que a los hijos de malla.
        var torso    = FindBone(model.transform, "Body");
        var head     = FindBone(model.transform, "Head");
        var rightArm = FindBone(model.transform, "Right Arm");
        var leftArm  = FindBone(model.transform, "Left Arm");

        // Fallback por si el modelo usa nombres diferentes
        if (torso    == null) torso    = FindBone(model.transform, "torso");
        if (rightArm == null) rightArm = FindBone(model.transform, "Right");
        if (leftArm  == null) leftArm  = FindBone(model.transform, "Left");

        // ── 5. Crear driver si no existe en la escena ──
        if (driver == null)
        {
            driver = model.GetComponent<PoseAvatarDriver>();
            if (driver == null)
                driver = model.AddComponent<PoseAvatarDriver>();
        }

        // ── 6. Ligar huesos al driver y habilitarlo ──
        try
        {
            driver.Bind(torso, head, rightArm, leftArm);
            driver.enabled = true;
        }
        catch (System.Exception e)
        {
            Debug.LogError("[AvatarLoader] Error al ligar el driver: " + e);
        }

        loaded = true;
        Debug.Log($"[AvatarLoader] Avatar cargado: {gltfPath} | Wide={isWide}" +
                  $" | Torso={torso != null} | Head={head != null}" +
                  $" | RArm={rightArm != null} | LArm={leftArm != null}");
        return true;
    }

    void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    /// <summary>
    /// Busca un Transform por nombre exacto (sin distinción de mayúsculas ni espacios).
    /// Con la jerarquía reconstruida, el nodo pivote (sin malla) aparece antes que
    /// sus hijos de malla, así que la búsqueda en profundidad lo devuelve siempre primero.
    /// </summary>
    Transform FindBone(Transform parent, string boneName)
    {
        // Normalizar: quitar espacios y pasar a minúsculas para comparar
        string boneNorm = boneName.Replace(" ", "").ToLowerInvariant();

        foreach (Transform child in parent)
        {
            string childNorm = child.name.Replace(" ", "").ToLowerInvariant();
            if (childNorm == boneNorm)
                return child;

            var found = FindBone(child, boneName);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// Busca un Transform cuyo nombre contenga la cadena dada (case-insensitive).
    /// Solo se usa como fallback.
    /// </summary>
    Transform FindChildByName(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name.Contains(name, System.StringComparison.OrdinalIgnoreCase))
                return child;
            var found = FindChildByName(child, name);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// Asigna la textura de skin correcta (personalizada o por defecto) a los
    /// materiales del modelo con un shader Unlit + Alpha Cutout: se ve igual con
    /// o sin luces en la escena y recorta las capas exteriores transparentes.
    ///
    /// No debe lanzar excepciones: se puede llamar desde OnGUI (botón "Buscar skin"),
    /// y una excepción a medias rompería el estado de GUILayout para siempre.
    /// </summary>
    void AssignSkinTexture(GameObject model)
    {
        if (model == null) return;

        string skinPath = ResolveSkinPath();
        if (string.IsNullOrEmpty(skinPath)) return;

        Texture2D newTex = null;
        Material  newMat = null;

        try
        {
            byte[] data = File.ReadAllBytes(skinPath);

            // RGBA32 + Point + Clamp: pixel art nítido y sin sangrado entre regiones UV.
            newTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!newTex.LoadImage(data))
            {
                Debug.LogWarning("[AvatarLoader] No se pudo decodificar la imagen de skin: " + skinPath);
                Object.Destroy(newTex);
                return;
            }
            newTex.name = Path.GetFileNameWithoutExtension(skinPath);
            newTex.filterMode = FilterMode.Point;
            newTex.wrapMode = TextureWrapMode.Clamp;
            newTex.anisoLevel = 0;

            Shader shader = ResolveSkinShader();
            if (shader == null)
            {
                Debug.LogError("[AvatarLoader] No hay ningún shader válido para la skin. " +
                               "Falta Assets/Resources/Materials/SkinCutout.shader.");
                Object.Destroy(newTex);
                return;
            }

            newMat = new Material(shader) { name = "Skin_" + newTex.name };
            ApplyTextureToMaterial(newMat, newTex);

            // Aplicar el mismo material a todos los renderers del modelo
            var renderers = model.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var r in renderers)
            {
                if (r == null) continue;

                // Un material por submesh (los avatares tienen 1, pero así no se rompe
                // si algún modelo trae varios submeshes en la misma malla).
                int subMeshes = Mathf.Max(1, r.sharedMaterials.Length);
                var mats = new Material[subMeshes];
                for (int i = 0; i < mats.Length; i++) mats[i] = newMat;
                r.sharedMaterials = mats;

                r.enabled = true;   // por si el driver anterior los había ocultado
            }

            // ── Commit: los renderers ya usan los recursos nuevos ──
            Texture2D oldTex = _skinTex;
            Material  oldMat = _skinMat;
            _skinTex = newTex;
            _skinMat = newMat;

            // El driver guarda instancias propias de los materiales (parpadeo).
            // Si no se vuelven a capturar, seguirían pintando la skin anterior.
            if (driver != null && loaded)
                driver.RebindSkinMaterials();

            // Recién ahora es seguro liberar la skin anterior
            if (oldMat != null) Object.Destroy(oldMat);
            if (oldTex != null) Object.Destroy(oldTex);

            Debug.Log($"[AvatarLoader] Skin aplicada: {skinPath} → {renderers.Length} renderer(es)" +
                      $" (shader {shader.name})");
        }
        catch (System.Exception e)
        {
            Debug.LogError("[AvatarLoader] Error al aplicar la skin: " + e);
            if (newMat != null) Object.Destroy(newMat);
            if (newTex != null) Object.Destroy(newTex);
        }
    }

    /// <summary>Ruta de la skin a usar (personalizada si existe, si no la del modelo).</summary>
    string ResolveSkinPath()
    {
        if (!string.IsNullOrEmpty(customSkinPath) && File.Exists(customSkinPath))
            return customSkinPath;

        string skinFileName = isWide ? "steve_skin.png" : "alex_skin.png";
        string path = Path.Combine(Application.streamingAssetsPath, textureFolder, skinFileName);

        if (File.Exists(path)) return path;

        Debug.LogWarning("[AvatarLoader] No se encontró la textura de skin: " + path);
        return null;
    }

    /// <summary>
    /// Shader de skin. Se carga como asset desde Resources (así Unity garantiza que
    /// entra en los builds) y se comprueba que sea compatible; si no, se usan los
    /// shaders integrados como respaldo. Así una skin nunca se queda sin material.
    /// </summary>
    static Shader ResolveSkinShader()
    {
        if (_skinShader != null) return _skinShader;

        Shader[] candidates =
        {
            Resources.Load<Shader>("Materials/SkinCutout"),
            Shader.Find("VTuberCam/SkinCutout"),
            Shader.Find("Unlit/Transparent Cutout"),
            Shader.Find("Legacy Shaders/Transparent/Cutout/Diffuse"),
            Shader.Find("Sprites/Default"),
            Shader.Find("Unlit/Texture")
        };

        foreach (var s in candidates)
        {
            if (s != null && s.isSupported)
            {
                _skinShader = s;
                break;
            }
        }

        return _skinShader;
    }

    /// <summary>Asigna la textura al material con el nombre de propiedad que use el shader.</summary>
    static void ApplyTextureToMaterial(Material mat, Texture2D tex)
    {
        if (mat.HasProperty("_MainTex"))   mat.SetTexture("_MainTex", tex);
        if (mat.HasProperty("_BaseMap"))   mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color", Color.white);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
        if (mat.HasProperty("_Cutoff"))    mat.SetFloat("_Cutoff", 0.5f);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0f);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0f);
        if (mat.HasProperty("_Metallic"))  mat.SetFloat("_Metallic", 0f);
    }

    /// <summary>
    /// Establece la skin personalizada desde un .png. Con una ruta vacía vuelve a
    /// la skin por defecto del modelo. Si el avatar ya está cargado, se reaplica al instante.
    /// </summary>
    public void SetCustomSkin(string path)
    {
        // Quitar skin personalizada → volver a la skin por defecto
        if (string.IsNullOrEmpty(path))
        {
            customSkinPath = "";
            if (loaded && avatarInstance != null)
                AssignSkinTexture(avatarInstance);
            return;
        }

        if (!File.Exists(path))
        {
            Debug.LogWarning("[AvatarLoader] Ruta de skin inválida: " + path);
            return;
        }

        customSkinPath = path;
        Debug.Log($"[AvatarLoader] Skin personalizada configurada: {customSkinPath}");

        // Si el avatar ya está cargado, reaplicar la skin (AssignSkinTexture
        // libera los recursos anteriores y avisa al driver).
        if (loaded && avatarInstance != null)
            AssignSkinTexture(avatarInstance);
    }

    /// <summary>
    /// Destruye el avatar actual y libera todos los recursos de GPU.
    /// Se llama automáticamente al inicio de Load() para evitar solapamientos.
    /// </summary>
    public void Unload()
    {
        // Anular referencias del driver antes del Destroy para evitar el bug
        // del driver-zombi (el avatar nuevo se asociaría a un driver que muere
        // al final del frame por el Destroy diferido).
        if (driver != null)
        {
            bool driverIsOnAvatar = (avatarInstance != null)
                                    && (driver.gameObject == avatarInstance
                                        || avatarInstance.GetComponentInChildren<PoseAvatarDriver>() == driver);
            if (driverIsOnAvatar)
                driver = null;
            else
            {
                driver.enabled = false;
            }
        }

        // Liberar mallas, texturas y materiales creados en tiempo de ejecución
        if (avatarInstance != null)
        {
            foreach (var mf in avatarInstance.GetComponentsInChildren<MeshFilter>())
            {
                if (mf && mf.sharedMesh) Object.Destroy(mf.sharedMesh);
            }
        }

        if (_skinTex != null) { Object.Destroy(_skinTex); _skinTex = null; }
        if (_skinMat != null) { Object.Destroy(_skinMat); _skinMat = null; }

        if (avatarInstance != null)
        {
            Object.Destroy(avatarInstance);
            avatarInstance = null;
        }

        loaded = false;
    }
}
