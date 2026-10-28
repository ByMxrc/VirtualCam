using System.IO;
using UnityEngine;

/// <summary>
/// Mesa de Minecraft (Blockbench) para VTuberCam.
/// Se carga desde StreamingAssets/Blockbench/table/table.gltf y se pega al avatar
/// para que herede el zoom, el paneo y la rotacion del panel.
/// El modelo ya viene en bloques (1 unidad = 1 bloque de Minecraft).
///
/// Colocacion simple: valores locales relativos al avatar.
/// X = 0 la centra; Z negativo la deja delante del personaje (entre el personaje y la camara).
/// La altura se maneja en pixeles de Blockbench sobre las manos: 0 px = manos, +5 px = efecto sentado.
/// </summary>
[DisallowMultipleComponent]
public class MinecraftTable : MonoBehaviour
{
    /// <summary>Ancho del modelo en bloques (32 px).</summary>
    public const float ModelWidthBlocks = 2f;
    /// <summary>Alto del modelo en bloques (12 px de pata + 4 px de tablero = 16 px).</summary>
    public const float ModelHeightBlocks = 1f;
    /// <summary>Profundidad del modelo en bloques (16 px).</summary>
    public const float ModelDepthBlocks = 1f;
    /// <summary>Pixeles de Blockbench por bloque.</summary>
    public const float PixelsPerBlock = 16f;

    [Header("Modelo (dentro de StreamingAssets)")]
    [Tooltip("Ruta relativa del .gltf exportado de Blockbench.")]
    public string modelPath = "Blockbench/table/table.gltf";
    [Tooltip("Carpeta con las texturas .png del modelo.")]
    public string textureFolder = "Blockbench/table";

    [Header("Colocacion por defecto (local al avatar)")]
    [Tooltip("X = 0 centra la mesa. Z negativo la deja delante del personaje.")]
    public Vector3 defaultLocalPosition = new Vector3(0f, -0.65f, 0f);
    [Tooltip("Ancho por defecto en bloques (1 = tamano real del modelo).")]
    public float defaultScale = 1f;
    [Tooltip("Altura de la superficie en pixeles de Blockbench sobre las manos. 5-6 px = efecto sentado.")]
    public float defaultTopPixels = 5.6f;

    [Header("Estado actual")]
    public bool visible = true;
    public float Scale { get; private set; }
    public Vector3 LocalPosition { get; private set; }

    /// <summary>Altura de la superficie de la mesa, en pixeles de Blockbench sobre las manos.</summary>
    public float TopPixels { get { return (LocalPosition.y + ModelHeightBlocks * Scale) * PixelsPerBlock; } }

    /// <summary>Ancho real de la mesa en bloques.</summary>
    public float WidthBlocks { get { return ModelWidthBlocks * Scale; } }

    GameObject _model;
    Transform _avatarRoot;
    bool _built;

    // ── Preferencias ─────────────────────────────────────────────────────────
    const string PrefOn      = "vtb_tableOn";
    const string PrefScale   = "vtb_tableScale";
    const string PrefX       = "vtb_tableX";
    const string PrefY       = "vtb_tableY";
    const string PrefZ       = "vtb_tableZ";
    const string PrefWorldX  = "vtb_tableWorldX";
    const string PrefWorldY  = "vtb_tableWorldY";
    const string PrefWorldZ  = "vtb_tableWorldZ";
    const string PrefIsParented = "vtb_tableIsParented";
    const string PrefVersion = "vtb_tableVersion";
    const int    CurrentVersion = 3; // nueva versión por simplificación de colocación
    bool _hadSavedPlacement;
    bool _isParented = true;

    void Awake()
    {
        if (PlayerPrefs.GetInt(PrefVersion, 1) < CurrentVersion)
        {
            PlayerPrefs.DeleteKey(PrefScale);
            PlayerPrefs.DeleteKey(PrefX);
            PlayerPrefs.DeleteKey(PrefY);
            PlayerPrefs.DeleteKey(PrefZ);
            PlayerPrefs.SetInt(PrefVersion, CurrentVersion);
            PlayerPrefs.Save();
        }

        _hadSavedPlacement = PlayerPrefs.HasKey(PrefX);
        _isParented = PlayerPrefs.GetInt(PrefIsParented, 1) == 1;
        visible = PlayerPrefs.GetInt(PrefOn, visible ? 1 : 0) == 1;
        Scale = Mathf.Clamp(PlayerPrefs.GetFloat(PrefScale, defaultScale), 0.1f, 5f);
        LocalPosition = new Vector3(
            PlayerPrefs.GetFloat(PrefX, defaultLocalPosition.x),
            PlayerPrefs.GetFloat(PrefY, TopPixelsToY(defaultTopPixels, Scale)),
            PlayerPrefs.GetFloat(PrefZ, defaultLocalPosition.z));
    }

    void Start()
    {
        EnsureBuilt();
    }

    // ── API publica ──────────────────────────────────────────────────────────
    public void SetVisible(bool value)
    {
        EnsureBuilt();
        visible = value;
        if (_model != null) _model.SetActive(value);
        PlayerPrefs.SetInt(PrefOn, value ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void Toggle()
    {
        SetVisible(!visible);
    }

    /// <summary>Cambia el ancho sin mover la superficie: la mesa se agranda hacia abajo.</summary>
    public void SetScale(float value)
    {
        EnsureBuilt();
        float topY = LocalPosition.y + ModelHeightBlocks * Scale;
        Scale = Mathf.Clamp(value, 0.1f, 5f);
        LocalPosition = new Vector3(LocalPosition.x, topY - ModelHeightBlocks * Scale, LocalPosition.z);
        ApplyTransform();
        SavePlacement();
    }

    public void SetLocalPosition(Vector3 position)
    {
        EnsureBuilt();
        LocalPosition = position;
        ApplyTransform();
        SavePlacement();
    }

    /// <summary>Altura de la superficie en pixeles de Blockbench (0 = manos, 5-6 = sentado).</summary>
    public void SetTopPixels(float pixels)
    {
        EnsureBuilt();
        LocalPosition = new Vector3(LocalPosition.x, TopPixelsToY(pixels, Scale), LocalPosition.z);
        ApplyTransform();
        SavePlacement();
    }

    /// <summary>Indica si la mesa esta parentada al avatar o suelta en la escena.</summary>
    public bool IsParented { get { return _isParented; } }

    /// <summary>Desvincula la mesa del avatar para moverla libremente en la escena.</summary>
    public void Desparentar()
    {
        EnsureBuilt();
        if (_model != null && _model.transform.parent != null)
        {
            _model.transform.SetParent(null, true); // worldPositionStays = true
            _isParented = false;
            var root = _avatarRoot != null ? _avatarRoot : transform;
            LocalPosition = root.InverseTransformPoint(_model.transform.position);
            PlayerPrefs.SetInt(PrefIsParented, 0);
            PlayerPrefs.Save();
        }
    }

    /// <summary>Vuelve a vincular la mesa al avatar usando su posicion mundial actual como offset local.</summary>
    public void Reparentar()
    {
        EnsureBuilt();
        if (_model == null) return;

        // Convertir la posicion mundial actual a local respecto al avatar para que no salte.
        if (_avatarRoot != null)
        {
            LocalPosition = _avatarRoot.InverseTransformPoint(_model.transform.position);
        }
        _model.transform.SetParent(_avatarRoot, false);
        _isParented = true;
        ApplyTransform();

        PlayerPrefs.SetInt(PrefIsParented, 1);
        PlayerPrefs.Save();
    }

    /// <summary>Posicion mundial actual de la mesa.</summary>
    public Vector3 GetWorldPosition()
    {
        EnsureBuilt();
        return _model != null ? _model.transform.position : Vector3.zero;
    }

    /// <summary>Establece la posicion mundial de la mesa (cuando esta desvinculada).</summary>
    public void SetWorldPosition(Vector3 pos)
    {
        EnsureBuilt();
        if (_model != null)
        {
            _model.transform.position = pos;
            if (_isParented && _avatarRoot != null)
            {
                LocalPosition = _avatarRoot.InverseTransformPoint(pos);
            }
            SavePlacement();
        }
    }

    /// <summary>Devuelve la mesa a su colocacion por defecto (centrada en X, Z delante del avatar).</summary>
    public void ResetPlacement()
    {
        EnsureBuilt();
        Scale = defaultScale;
        LocalPosition = new Vector3(
            defaultLocalPosition.x,
            TopPixelsToY(defaultTopPixels, Scale),
            defaultLocalPosition.z);
        if (_isParented && _avatarRoot != null)
        {
            _model.transform.SetParent(_avatarRoot, false);
            ApplyTransform();
        }
        SavePlacement();
    }

    static float TopPixelsToY(float pixels, float scale)
    {
        return pixels / PixelsPerBlock - ModelHeightBlocks * scale;
    }

    void SavePlacement()
    {
        PlayerPrefs.SetFloat(PrefScale, Scale);
        PlayerPrefs.SetFloat(PrefX, LocalPosition.x);
        PlayerPrefs.SetFloat(PrefY, LocalPosition.y);
        PlayerPrefs.SetFloat(PrefZ, LocalPosition.z);

        if (!_isParented && _model != null)
        {
            PlayerPrefs.SetFloat(PrefWorldX, _model.transform.position.x);
            PlayerPrefs.SetFloat(PrefWorldY, _model.transform.position.y);
            PlayerPrefs.SetFloat(PrefWorldZ, _model.transform.position.z);
        }

        PlayerPrefs.Save();
    }

    // ── Construccion / adjunto ───────────────────────────────────────────────
    void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        string jsonPath = Path.Combine(Application.streamingAssetsPath, modelPath);
        string texturesPath = Path.Combine(Application.streamingAssetsPath, textureFolder);

        _model = GltfModelLoader.Load(jsonPath, texturesPath, "TableModel");
        if (_model == null)
        {
            Debug.LogError("[MinecraftTable] No se pudo cargar la mesa. Ruta: " + jsonPath);
            return;
        }

        _avatarRoot = FindAvatarRoot();

        // Capa del avatar: se lee ANTES de colgar la mesa (si no, la mesa entra en la busqueda).
        int layer = _avatarRoot != null ? AvatarLayer(_avatarRoot) : gameObject.layer;

        // Si no esta parentada y hay posicion mundial guardada, aplicarla primero
        if (!_isParented)
        {
            float wx = PlayerPrefs.GetFloat(PrefWorldX, _model.transform.position.x);
            float wy = PlayerPrefs.GetFloat(PrefWorldY, _model.transform.position.y);
            float wz = PlayerPrefs.GetFloat(PrefWorldZ, _model.transform.position.z);
            _model.transform.position = new Vector3(wx, wy, wz);
            // Sincronizar LocalPosition para consistencia
            var root = _avatarRoot != null ? _avatarRoot : transform;
            LocalPosition = root.InverseTransformPoint(_model.transform.position);
        }

        // Parentar o desparentar segun el estado guardado
        if (_isParented && _avatarRoot != null)
        {
            _model.transform.SetParent(_avatarRoot, false);
            ApplyTransform();
        }
        else
        {
            _model.transform.SetParent(null, true); // worldPositionStays = true
        }

        // La mesa tiene que estar en la misma capa que el avatar o no saldra en el stream.
        SetLayerRecursively(_model, layer);

        Debug.Log("[MinecraftTable] Mesa cargada. parented=" + _isParented + " pos=" + _model.transform.position + " scale=" + Scale);
        _model.SetActive(visible);
    }

    void ApplyTransform()
    {
        if (_model == null) return;
        _model.transform.localPosition = LocalPosition;
        _model.transform.localRotation = Quaternion.identity;
        _model.transform.localScale = Vector3.one * Scale;
    }

    static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    /// <summary>Capa real del avatar (la de su malla, no la del transform raiz).</summary>
    int AvatarLayer(Transform root)
    {
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;
            if (_model != null && r.transform.IsChildOf(_model.transform)) continue;
            return r.gameObject.layer;
        }
        return root.gameObject.layer;
    }

    static Transform FindAvatarRoot()
    {
        var driver = Object.FindFirstObjectByType<PoseAvatarDriver>();
        if (driver != null) return driver.transform;

        var model = GameObject.Find("model");
        return model != null ? model.transform : null;
    }

    void OnDestroy()
    {
        if (_model != null) Destroy(_model);
    }
}
