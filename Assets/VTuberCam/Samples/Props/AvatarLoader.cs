using System.IO;
using UnityEngine;

/// <summary>
/// Carga un avatar .gltf desde StreamingAssets y lo configura para PoseAvatarDriver.
/// Busca automáticamente los huesos por nombre (Torso, Head, Right Arm, Left Arm).
/// Aplica la textura de skin correcta (steve_skin.png o alex_skin.png).
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

    void Awake()
    {
        if (driver == null)
            driver = FindFirstObjectByType<PoseAvatarDriver>();
    }

    /// <summary>Carga el avatar seleccionado y configura el driver.</summary>
    public bool Load()
    {
        string fullPath = Path.Combine(Application.streamingAssetsPath, gltfPath);
        if (!File.Exists(fullPath))
        {
            Debug.LogError("[AvatarLoader] No se encontró el modelo: " + fullPath);
            return false;
        }

        // Cargar modelo GLTF
        var model = GltfModelLoader.Load(fullPath, textureFolder, "AvatarModel");
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

        // Buscar huesos automáticamente
        var torso = FindChildByName(model.transform, "torso");
        var head = FindChildByName(model.transform, "head");
        var rightArm = FindChildByName(model.transform, "rightarm");
        var leftArm = FindChildByName(model.transform, "leftarm");

        // Si no los encuentra por nombre exacto, buscar por palabras clave
        if (torso == null) torso = FindChildByName(model.transform, "body");
        if (head == null) head = FindChildByName(model.transform, "head");
        if (rightArm == null) rightArm = FindChildByName(model.transform, "right");
        if (leftArm == null) leftArm = FindChildByName(model.transform, "left");

        // Asegurar que existe un PoseAvatarDriver en el modelo cargado
        if (driver == null)
        {
            driver = model.GetComponent<PoseAvatarDriver>();
            if (driver == null)
                driver = model.AddComponent<PoseAvatarDriver>();
        }

        // Configurar driver
        driver.torso = torso;
        driver.head = head;
        driver.rightArm = rightArm;
        driver.leftArm = leftArm;
        driver.enabled = true;

        // Asignar la textura de skin correcta (steve_skin.png o alex_skin.png)
        AssignSkinTexture(model);

        loaded = true;
        Debug.Log($"[AvatarLoader] Avatar cargado: {gltfPath} | Wide={isWide} | Torso={torso != null} | Head={head != null} | RArm={rightArm != null} | LArm={leftArm != null}");
        return true;
    }

    void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    /// <summary>Asigna la textura de skin correcta (personalizada o por defecto) a los materiales del modelo.</summary>
    void AssignSkinTexture(GameObject model)
    {
        string skinPath = "";

        // Si hay skin personalizada guardada, usarla
        if (!string.IsNullOrEmpty(customSkinPath) && File.Exists(customSkinPath))
        {
            skinPath = customSkinPath;
            Debug.Log($"[AvatarLoader] Usando skin personalizada: {skinPath}");
        }
        else
        {
            // Usar skin por defecto del modelo
            string skinFileName = isWide ? "steve_skin.png" : "alex_skin.png";
            skinPath = Path.Combine(Application.streamingAssetsPath, textureFolder, skinFileName);

            if (!File.Exists(skinPath))
            {
                Debug.LogWarning("[AvatarLoader] No se encontró la textura de skin por defecto: " + skinPath);
                return;
            }
        }

        byte[] data = File.ReadAllBytes(skinPath);
        var tex = new Texture2D(2, 2);
        if (!tex.LoadImage(data))
        {
            Debug.LogWarning("[AvatarLoader] No se pudo cargar la imagen de skin: " + skinPath);
            return;
        }

        tex.name = Path.GetFileNameWithoutExtension(skinPath);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Repeat;

        // Crear un material con shader Unlit/Texture para que muestre la textura tal cual
        var skinMat = new Material(Shader.Find("Unlit/Texture"));
        skinMat.name = "Skin_" + tex.name;
        skinMat.mainTexture = tex;
        skinMat.color = Color.white;

        // Aplicar el mismo material con textura a todos los renderers del modelo
        var renderers = model.GetComponentsInChildren<MeshRenderer>();
        int count = 0;
        foreach (var r in renderers)
        {
            r.sharedMaterials = new Material[] { skinMat };
            count++;
        }

        Debug.Log($"[AvatarLoader] Skin aplicada: {skinPath} a {renderers.Length} renderer(es), {count} material(es)");
    }

    /// <summary>Establece una skin personalizada desde una ruta de archivo .png.</summary>
    public void SetCustomSkin(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            Debug.LogWarning("[AvatarLoader] Ruta de skin inválida: " + path);
            customSkinPath = "";
            return;
        }

        customSkinPath = path;
        Debug.Log($"[AvatarLoader] Skin personalizada configurada: {customSkinPath}");

        // Si el avatar ya está cargado, reaplicar la skin
        if (loaded && avatarInstance != null)
        {
            AssignSkinTexture(avatarInstance);
        }
    }

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

    /// <summary>Destruye el avatar actual.</summary>
    public void Unload()
    {
        if (avatarInstance != null)
        {
            Destroy(avatarInstance);
            avatarInstance = null;
        }
        loaded = false;
        if (driver != null)
        {
            driver.torso = null;
            driver.head = null;
            driver.rightArm = null;
            driver.leftArm = null;
            driver.enabled = false;
        }
    }
}
