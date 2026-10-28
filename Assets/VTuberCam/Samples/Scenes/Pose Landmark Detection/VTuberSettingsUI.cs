using UnityEngine;

/// <summary>
/// VTuberCam — Panel de ajustes minimalista y limpio.
/// Ventana modal centrada con fondo translúcido y controles redondeados.
/// </summary>
public class VTuberSettingsUI : MonoBehaviour
{
    // ─── Referencias ─────────────────────────────────────────────────────────
    [Header("Referencias (se detectan solas si no las asignas)")]
    public PoseAvatarDriver avatar;
    public Camera mainCamera;
    public UnityCapture virtualCam;
    [Tooltip("Objeto raíz del modelo 3D.")]
    public Transform avatarRoot;

    [Header("Modelos Wide / Slim (opcional)")]
    public GameObject wideModel;
    public GameObject slimModel;

    [Header("Escritorio de Minecraft (opcional)")]
    [Tooltip("Se busca/crea solo si lo dejas vacío.")]
    public MinecraftTable table;

    [Header("Avatar Loader (opcional)")]
    [Tooltip("Se busca/crea solo si lo dejas vacío.")]
    public AvatarLoader avatarLoader;

    [Header("Teclas rápidas")]
    public KeyCode toggleMenuKey = KeyCode.Tab;
    public KeyCode cleanModeKey  = KeyCode.F1;
    public KeyCode recalibKeyUI  = KeyCode.R;

    // ─── Estado UI ────────────────────────────────────────────────────────────
    bool  _showPanel        = false;
    bool  _cleanMode        = false;
    bool  _virtualCamActive = false; // inicia apagado, se enciende tras elegir avatar
    bool  _showStartScreen  = true;
    bool  _avatarSelected   = false;
    string _customSkinPath  = "";  // Ruta a skin personalizada (.png)
    float _startTime;
    float _pulseT    = 0f;

    // ─── Categoría activa ────────────────────────────────────────────────────
    // 0 = General, 1 = Expresiones, 2 = Extras
    int _activeCategory = 0;

    // ─── Ajustes del avatar ──────────────────────────────────────────────────
    bool  _skinWide      = true;
    bool  _blink         = true;

    // ─── Fondo gris oscuro por defecto (sin chroma key) ──────────────────────
    static readonly Color DefaultBgColor = new Color(0.15f, 0.15f, 0.20f, 1f);

    // ─── Transformación del avatar ────────────────────────────────────────────
    Vector3    _avatarInitPos;
    Quaternion _avatarInitRot;
    float      _avatarInitScale;

    float   _modelScale = 2.2f;
    float   _rotationY  = 0f;
    Vector2 _panOffset  = Vector2.zero;
    Vector3 _lastMouse;
    bool    _draggingPan = false;
    bool    _draggingRot = false;

    const float ROT_Y_MAX = 180f;
    const float SCALE_MIN = 0.3f;
    const float SCALE_MAX = 5.0f;

    [Header("Escala Inicial por Defecto")]
    public float defaultScale = 2.2f;

    // ─── Colores minimalistas ────────────────────────────────────────────────
    static readonly Color ColBg      = new Color(0.12f, 0.14f, 0.18f, 0.92f);   // fondo translúcido
    static readonly Color ColBgLight = new Color(0.18f, 0.20f, 0.25f, 0.95f);   // tarjetas
    static readonly Color ColAccent  = new Color(0.40f, 0.60f, 0.95f, 1.00f);   // azul suave
    static readonly Color ColAccentD = new Color(0.25f, 0.45f, 0.80f, 1.00f);   // azul oscuro
    static readonly Color ColText    = new Color(0.92f, 0.94f, 0.98f, 1.00f);
    static readonly Color ColTextDim = new Color(0.62f, 0.66f, 0.72f, 1.00f);
    static readonly Color ColSep     = new Color(0.30f, 0.33f, 0.40f, 0.60f);
    static readonly Color ColDanger  = new Color(0.95f, 0.30f, 0.30f, 1.00f);

    // ─── Texturas ─────────────────────────────────────────────────────────────
    Texture2D _texWhite, _texBg, _texCard, _texBtn, _texBtnHov,
              _texBtnAcc, _texBtnAccHov, _texBtnDanger, _texBtnDangerHov,
              _texBtnTab, _texBtnTabSel, _texCheckOff, _texCheckOn, _texCheckMark;

    // ─── Estilos ──────────────────────────────────────────────────────────────
    GUIStyle _stTitle, _stSection, _stBody, _stHint,
             _stBtnNorm, _stBtnSel, _stBtnAcc, _stBtnAccHov,
             _stBtnDanger, _stBtnTab, _stBtnTabSel, _stBigBtn, _stCard,
             _stCheck;
    bool     _stylesReady;
    float    _panelW = 560f;
    float    _panelH = 640f;

    // ─────────────────────────────────────────────────────────────────────────
    void Awake()
    {
        _startTime = Time.realtimeSinceStartup;

        if (!avatar)     avatar     = FindFirstObjectByType<PoseAvatarDriver>();
        if (avatar != null) avatar.showRecalibButton = false;
        if (!mainCamera) mainCamera = Camera.main ?? FindFirstObjectByType<Camera>();
        if (!avatarRoot)
        {
            if (avatar) avatarRoot = avatar.transform;
            else { var m = GameObject.Find("model"); if (m) avatarRoot = m.transform; }
        }

        if (!avatarLoader)
            avatarLoader = FindFirstObjectByType<AvatarLoader>();

        if (avatarRoot)
        {
            _avatarInitPos   = avatarRoot.position;
            _avatarInitRot   = avatarRoot.rotation;
            _avatarInitScale = avatarRoot.localScale.x > 0 ? avatarRoot.localScale.x : 1f;
        }

        _skinWide   = PlayerPrefs.GetInt("vtb_skinWide", 1) == 1;
        _blink      = PlayerPrefs.GetInt("vtb_blink", 1) == 1;
        _customSkinPath = PlayerPrefs.GetString("vtb_customSkinPath", "");

        // Borrar preferencias de posición/tamaño para reiniciar cada vez que se abre
        PlayerPrefs.DeleteKey("vtb_scale");
        PlayerPrefs.DeleteKey("vtb_panX");
        PlayerPrefs.DeleteKey("vtb_panY");
        PlayerPrefs.DeleteKey("vtb_rotY");
        PlayerPrefs.Save();

        _modelScale = defaultScale * 1.05f;
        _panOffset.x = 0f;
        _panOffset.y = 0f;
        _rotationY  = 0f;

        // Iniciar SIN cámara y SIN avatar visible hasta que el usuario elija uno
        _avatarSelected = false;
        if (virtualCam) virtualCam.enabled = false;
        _virtualCamActive = false;

        if (mainCamera)
        {
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = DefaultBgColor;
        }

        EnsureTable();
        if (table != null) table.SetVisible(false);
        // No aplicar skin/blink/transform hasta que haya avatar cargado
    }

    void EnsureTable()
    {
        if (table != null) return;
        table = FindFirstObjectByType<MinecraftTable>();
        if (table == null)
        {
            var go = new GameObject("MinecraftTable");
            table = go.AddComponent<MinecraftTable>();
        }
    }

    void Update()
    {
        _pulseT += Time.deltaTime;

        if (!_showStartScreen)
        {
            if (Input.GetKeyDown(toggleMenuKey)) _showPanel = !_showPanel;
            if (Input.GetKeyDown(cleanModeKey))  _cleanMode = !_cleanMode;
            if (Input.GetKeyDown(recalibKeyUI) && avatar) avatar.RecalibrateNow();
            if (!_cleanMode) HandleMouseControls();
        }
    }

    bool IsMouseOverUI()
    {
        Vector2 mp = Input.mousePosition;
        if (!_showPanel && mp.x >= (Screen.width - 150f) && mp.y >= (Screen.height - 50f))
            return true;
        return false;
    }

    void HandleMouseControls()
    {
        if (!avatarRoot) return;
        bool overUI = IsMouseOverUI();

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (!overUI && Mathf.Abs(scroll) > 0.001f)
        {
            _modelScale = Mathf.Clamp(_modelScale + scroll * 0.4f, SCALE_MIN, SCALE_MAX);
            ApplyAvatarTransform();
            SaveTransformPrefs();
        }

        if (Input.GetMouseButtonDown(1) && !overUI)
        {
            _lastMouse = Input.mousePosition;
            _draggingPan = true;
        }
        else if (Input.GetMouseButton(1) && _draggingPan)
        {
            Vector3 d = Input.mousePosition - _lastMouse;
            _panOffset += new Vector2(d.x, d.y) * 0.004f;
            _lastMouse = Input.mousePosition;
            ApplyAvatarTransform();
            SaveTransformPrefs();
        }
        else if (!Input.GetMouseButton(1))
        {
            _draggingPan = false;
        }

        bool rotBtn  = Input.GetMouseButton(2) || (Input.GetKey(KeyCode.LeftShift) && Input.GetMouseButton(0));
        bool rotDown = Input.GetMouseButtonDown(2) || (Input.GetKey(KeyCode.LeftShift) && Input.GetMouseButtonDown(0));

        if (rotDown && !overUI)
        {
            _lastMouse = Input.mousePosition;
            _draggingRot = true;
        }
        else if (rotBtn && _draggingRot)
        {
            float dx = (Input.mousePosition.x - _lastMouse.x) * 0.5f;
            _rotationY = Mathf.Clamp(_rotationY + dx, -ROT_Y_MAX, ROT_Y_MAX);
            _lastMouse = Input.mousePosition;
            ApplyAvatarTransform();
            SaveTransformPrefs();
        }
        else if (!rotBtn)
        {
            _draggingRot = false;
        }
    }

    void ApplyAvatarTransform()
    {
        if (!avatarRoot) return;
        float s = _avatarInitScale * _modelScale;
        avatarRoot.localScale = Vector3.one * s;
        avatarRoot.position   = _avatarInitPos + new Vector3(_panOffset.x, _panOffset.y, 0f);
        avatarRoot.rotation   = _avatarInitRot * Quaternion.Euler(0f, _rotationY, 0f);
    }
    void ResetAvatarTransform()
    { _modelScale = defaultScale; _panOffset = Vector2.zero; _rotationY = 0f; ApplyAvatarTransform(); SaveTransformPrefs(); }
    void SaveTransformPrefs()
    { PlayerPrefs.SetFloat("vtb_scale", _modelScale); PlayerPrefs.SetFloat("vtb_panX", _panOffset.x); PlayerPrefs.SetFloat("vtb_panY", _panOffset.y); PlayerPrefs.SetFloat("vtb_rotY", _rotationY); PlayerPrefs.Save(); }

    // ─── ONGUI ────────────────────────────────────────────────────────────────
    void OnGUI()
    {
        if (!_stylesReady) BuildStyles();
        if (_showStartScreen) { DrawStartScreen(); return; }
        if (_cleanMode) return;

        if (!_showPanel)
        {
            float ty = Screen.height - 72f;
            DrawRect(new Rect(10, ty - 6, 430, 68), new Color(0.05f, 0.06f, 0.09f, 0.80f));
            GUI.color = new Color(1f, 1f, 1f, 0.60f);
            GUI.Label(new Rect(16, ty,      420, 18), "Rueda: Zoom  |  Der+Drag: Mover  |  Cent+Drag: Rotar", _stHint);
            GUI.Label(new Rect(16, ty + 20, 350, 18), "[Tab] Ajustes   [F1] Modo Limpio   [R] Recalibrar",    _stHint);
            GUI.color = Color.white;

            DrawRect(new Rect(Screen.width - 140, 10, 130, 36), ColBgLight);
            DrawRect(new Rect(Screen.width - 140, 10, 2, 36), ColAccent);
            if (GUI.Button(new Rect(Screen.width - 138, 10, 128, 36), "  Ajustes", _stBtnNorm))
                _showPanel = true;
            return;
        }

        DrawModal();
    }

    void DrawModal()
    {
        float sw = Screen.width;
        float sh = Screen.height;
        float mx = (sw - _panelW) * 0.5f;
        float my = (sh - _panelH) * 0.5f;

        // Fondo translúcido detrás del modal (oscurece la escena)
        DrawRect(new Rect(0, 0, sw, sh), new Color(0f, 0f, 0f, 0.35f));

        // Fondo del modal translúcido
        DrawRect(new Rect(mx, my, _panelW, _panelH), ColBg);
        // Borde superior sutil
        DrawRect(new Rect(mx, my, _panelW, 1), ColAccent);

        // Título
        GUI.color = ColText;
        GUI.Label(new Rect(mx + 20, my + 16, 400, 26), "Configuración", new GUIStyle(_stTitle) { fontSize = 18, fontStyle = FontStyle.Bold });

        // Botón X cerrar
        Rect closeBtn = new Rect(mx + _panelW - 34, my + 14, 26, 26);
        if (GUI.Button(closeBtn, "✕", _stBtnDanger))
            _showPanel = false;

        // Contenido (scroll)
        float contentY = my + 56;
        float contentH = _panelH - 56 - 44;
        Rect contentRect = new Rect(mx + 20, contentY, _panelW - 40, contentH);

        GUILayout.BeginArea(contentRect);
        GUILayout.Space(4);

        // Pestañas
        GUILayout.BeginHorizontal();
        string[] cats = { "Configuración General", "Expresiones", "Extras" };
        for (int i = 0; i < cats.Length; i++)
        {
            bool sel = (_activeCategory == i);
            GUIStyle st = sel ? _stBtnTabSel : _stBtnTab;
            if (GUILayout.Button(cats[i], st, GUILayout.Height(30)))
                _activeCategory = i;
            GUILayout.Space(6);
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(12);

        switch (_activeCategory)
        {
            case 0: DrawCategoryGeneral(); break;
            case 1: DrawCategoryExpresiones(); break;
            case 2: DrawCategoryExtras(); break;
        }

        GUILayout.EndArea();

        // Botones inferiores
        float btnY = my + _panelH - 38;
        float btnW = (_panelW - 60) / 2f;
        GUILayout.BeginArea(new Rect(mx + 20, btnY, _panelW - 40, 32));
        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Restaurar valores originales", _stBtnDanger, GUILayout.Height(28)))
        {
            _modelScale = defaultScale; _panOffset = Vector2.zero; _rotationY = 0f;
            ApplyAvatarTransform(); SaveTransformPrefs();
            if (table != null) table.ResetPlacement();
        }

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Aplicar cambios", _stBtnAcc, GUILayout.Height(28)))
        {
            // Los cambios se aplican en tiempo real
        }

        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    // ══ CATEGORÍA GENERAL ══
    void DrawCategoryGeneral()
    {
        DrawCardBegin();

        // Cámara Virtual
        DrawSection("Cámara Virtual");
        bool vcam = _virtualCamActive && virtualCam != null && virtualCam.enabled;
        bool newVcam = Checkbox(vcam, "Habilitar cámara virtual");
        if (newVcam != vcam)
        {
            _virtualCamActive = newVcam;
            if (virtualCam) virtualCam.enabled = newVcam;
            PlayerPrefs.SetInt("vtb_vcam", newVcam ? 1 : 0); PlayerPrefs.Save();
        }
        DrawHint("OBS/Discord/Zoom: añade 'Cámara Web' → dispositivo 'Unity Video Capture'.");
        GUILayout.Space(4);
        BtnAcc("Abrir Instalador del Driver", () =>
        {
            try
            {
                string p = System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(Application.dataPath, "..", "VirtualCameraDriver"));
                if (System.IO.Directory.Exists(p))
                    System.Diagnostics.Process.Start("explorer.exe", p);
            }
            catch (System.Exception ex) { Debug.LogError("Error: " + ex.Message); }
        });

        DrawCardEnd();

        GUILayout.Space(12);

        // Fondo
        DrawCardBegin();
        DrawSection("Fondo");
        GUILayout.BeginHorizontal(); GUILayout.Space(10);
        GUILayout.Label("Color por defecto: Gris Oscuro", _stBody);
        GUILayout.EndHorizontal();
        DrawHint("El fondo de la cámara virtual es gris oscuro. En OBS usa filtro 'Clave de Color' si necesitas chroma.");
        DrawCardEnd();

        GUILayout.Space(12);

        // Debug
        DrawCardBegin();
        DrawSection("Datos de Debug (HUD arriba)");
        bool showDebug = avatar != null && avatar.showDebug;
        bool newDebug = Checkbox(showDebug, "Mostrar datos de debug");
        if (newDebug != showDebug)
        {
            if (avatar) avatar.showDebug = newDebug;
        }
        DrawHint("Oculta el texto de debug que aparece arriba a la izquierda.");
        DrawCardEnd();

        GUILayout.Space(12);

        // Reset vista
        DrawCardBegin();
        BtnAcc("Centrar y Resetear Vista", ResetAvatarTransform);
        DrawHint("Rueda=Zoom  |  Der=Mover  |  Central=Rotar");
        DrawCardEnd();
    }

    // ══ CATEGORÍA EXPRESIONES ══
    void DrawCategoryExpresiones()
    {
        DrawCardBegin();
        DrawSection("Expresiones");

        bool newBlink = Checkbox(_blink, "Parpadeo automático");
        if (newBlink != _blink) { _blink = newBlink; ApplyBlink(); }

        GUILayout.Space(10);

        if (avatar != null)
        {
            float hs = avatar.headSensitivity;
            SliderFull("Sensibilidad cabeza", $"{hs:0.0}x", ref hs, 0.8f, 2.5f,
                v => { avatar.headSensitivity = v; PlayerPrefs.SetFloat("vtb_headSens", v); PlayerPrefs.Save(); }, 0.05f);
            avatar.headSensitivity = hs;
        }

        DrawHint("Controla el pestañeo y la sensibilidad de giro de cabeza.");
        DrawCardEnd();
    }

    // ══ CATEGORÍA EXTRAS ══
    void DrawCategoryExtras()
    {
        DrawCardBegin();
        DrawSection("Extras");

        EnsureTable();
        if (table != null)
        {
            bool newVisible = Checkbox(table.visible, "Mostrar mesa");
            if (newVisible != table.visible) table.SetVisible(newVisible);

            GUILayout.Space(6);

            // Toggle vinculado / desvinculado
            bool newParented = Checkbox(table.IsParented, "Vinculado al avatar");
            if (newParented != table.IsParented)
            {
                if (newParented) table.Reparentar();
                else table.Desparentar();
            }

            GUILayout.Space(6);

            if (table.IsParented)
            {
                // Vinculado: sliders locales (offset respecto al avatar)
                float tableWidth = table.WidthBlocks;
                SliderFull("Ancho mesa", $"{tableWidth:0.00} bl", ref tableWidth, 1f, 4f,
                    v => table.SetScale(v / 2f), 0.01f);

                float tableTopPx = table.TopPixels;
                SliderFull("Altura mesa", $"{tableTopPx:+0.0;-0.0;0} px", ref tableTopPx, -16f, 14f,
                    v => table.SetTopPixels(v), 0.05f);

                Vector3 localPos = table.LocalPosition;
                float lx = localPos.x;
                float ly = localPos.y;
                float lz = localPos.z;

                SliderFull("Pos X (local)", $"{lx:+0.00;-0.00;0}", ref lx, -2f, 2f,
                    v => { Vector3 p = table.LocalPosition; p.x = v; table.SetLocalPosition(p); }, 0.005f);
                SliderFull("Pos Y (local)", $"{ly:+0.00;-0.00;0}", ref ly, -2f, 2f,
                    v => { Vector3 p = table.LocalPosition; p.y = v; table.SetLocalPosition(p); }, 0.005f);
                SliderFull("Pos Z (local)", $"{lz:+0.00;-0.00;0}", ref lz, -2f, 2f,
                    v => { Vector3 p = table.LocalPosition; p.z = v; table.SetLocalPosition(p); }, 0.005f);
            }
            else
            {
                // Desvinculado: sliders mundo X / Y / Z
                Vector3 wpos = table.GetWorldPosition();
                float wx = wpos.x;
                float wy = wpos.y;
                float wz = wpos.z;

                SliderFull("Pos X (mundo)", $"{wx:+0.00;-0.00;0}", ref wx, -5f, 5f,
                    v => { Vector3 p = table.GetWorldPosition(); p.x = v; table.SetWorldPosition(p); }, 0.005f);
                SliderFull("Pos Y (mundo)", $"{wy:+0.00;-0.00;0}", ref wy, -5f, 5f,
                    v => { Vector3 p = table.GetWorldPosition(); p.y = v; table.SetWorldPosition(p); }, 0.005f);
                SliderFull("Pos Z (mundo)", $"{wz:+0.00;-0.00;0}", ref wz, -5f, 5f,
                    v => { Vector3 p = table.GetWorldPosition(); p.z = v; table.SetWorldPosition(p); }, 0.005f);

                DrawHint("La posición se guarda automáticamente al mover los sliders.");
            }

            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(table.IsParented ? "Desvincular mesa" : "Vincular mesa",
                _stBtnAcc, GUILayout.Height(28)))
            {
                if (table.IsParented) table.Desparentar();
                else table.Reparentar();
            }
            GUILayout.EndHorizontal();

            BtnAcc("Resetear mesa", () => table.ResetPlacement());
            DrawHint("Vinculado: la mesa se mueve con el avatar. Desvinculado: la mesa queda fija en escena.");
        }
        else
        {
            DrawHint("No se pudo cargar la mesa. Revisa StreamingAssets/Blockbench/table/table.json");
        }

        GUILayout.Space(8);

        // ── Skin personalizada ──
        DrawCardBegin();
        DrawSection("Skin personalizada");

        GUILayout.BeginHorizontal(); GUILayout.Space(10);
        GUILayout.Label("Skin (.png):", _stBody, GUILayout.Width(70));
        _customSkinPath = GUILayout.TextField(_customSkinPath, _stBody, GUILayout.Height(24));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal(); GUILayout.Space(10);
#if UNITY_EDITOR
        if (GUILayout.Button("Buscar skin (.png)", _stBtnAcc, GUILayout.Height(26)))
        {
            var path = UnityEditor.EditorUtility.OpenFilePanel("Seleccionar skin", "", "png");
            if (!string.IsNullOrEmpty(path))
            {
                _customSkinPath = path;
                if (avatarLoader != null)
                {
                    avatarLoader.SetCustomSkin(_customSkinPath);
                    PlayerPrefs.SetString("vtb_customSkinPath", _customSkinPath);
                    PlayerPrefs.Save();
                }
            }
        }
#else
        if (GUILayout.Button("Buscar skin (.png) (solo en Editor)", _stBtnAcc, GUILayout.Height(26)))
        {
            // En builds no se puede abrir el explorador de archivos
            Debug.LogWarning("Seleccionar archivo solo disponible en el Editor de Unity");
        }
#endif
        GUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(_customSkinPath))
        {
            GUILayout.BeginHorizontal(); GUILayout.Space(10);
            GUILayout.Label($"Archivo: {_customSkinPath}", _stHint);
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Quitar skin personalizada", _stBtnDanger, GUILayout.Height(24)))
            {
                _customSkinPath = "";
                PlayerPrefs.SetString("vtb_customSkinPath", "");
                PlayerPrefs.Save();
                if (avatarLoader != null)
                {
                    avatarLoader.SetCustomSkin("");
                }
            }
        }

        DrawHint("Selecciona un archivo .png para usarlo como skin del avatar. Se guarda para la próxima vez.");
        DrawCardEnd();
    }

    // ─── PANTALLA DE INICIO ───────────────────────────────────────────────────
    void DrawStartScreen()
    {
        float elapsed = Time.realtimeSinceStartup - _startTime;
        float alpha   = Mathf.Clamp01(elapsed * 1.5f);
        float sw = Screen.width;
        float sh = Screen.height;

        DrawRect(new Rect(0, 0, sw, sh), new Color(0.08f, 0.10f, 0.14f, alpha));

        GUI.color = new Color(ColAccent.r, ColAccent.g, ColAccent.b, alpha * 0.10f);
        for (float x = 0; x < sw; x += 80) GUI.DrawTexture(new Rect(x, 0, 1, sh), _texWhite);
        for (float y = 0; y < sh; y += 80) GUI.DrawTexture(new Rect(0, y, sw, 1), _texWhite);
        GUI.color = Color.white;

        float cardW = Mathf.Min(sw * 0.90f, 700f);
        float cardH = 480f;
        float cx = (sw - cardW) * 0.5f;
        float cy = (sh - cardH) * 0.5f;

        DrawRect(new Rect(cx, cy, cardW, cardH), ColBg);
        DrawRect(new Rect(cx, cy, cardW, 2), ColAccent);

        float lx = cx + 30f;
        float ly = cy + 30f;

        GUI.color = ColText;
        GUI.Label(new Rect(lx, ly, 70, 70), "VTC",
            new GUIStyle(_stTitle) { fontSize = 28, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = ColAccent } });

        GUI.Label(new Rect(lx + 80, ly + 4, 350, 34), "VTuberCam",
            new GUIStyle(_stTitle) { fontSize = 24, normal = { textColor = ColText } });
        GUI.Label(new Rect(lx + 82, ly + 36, 300, 20), "Selecciona tu avatar para iniciar",
            new GUIStyle(_stHint) { fontSize = 11,
                normal = { textColor = ColTextDim } });

        float sepY = cy + 110f;
        DrawRect(new Rect(cx + 20, sepY, cardW - 40, 1), ColSep);

        float cxCenter = cx + cardW * 0.5f;
        float btnW = 200f;
        float btnH = 70f;
        float gap = 20f;
        float totalW = btnW * 2 + gap;
        float startX = cxCenter - totalW * 0.5f;

        // Botón Steve (Wide)
        Rect steveBtn = new Rect(startX, sepY + 30f, btnW, btnH);
        GUI.color = _skinWide ? ColAccent : Color.white;
        if (GUI.Button(steveBtn, "Steve\n(Amplio / Wide)", new GUIStyle(_stBtnAcc) { fontSize = 14, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.black } }))
        {
            _skinWide = true;
            if (avatarLoader != null)
            {
                avatarLoader.gltfPath = "Avatars/steve.gltf";
                avatarLoader.textureFolder = "Avatars";
                avatarLoader.isWide = true;
                if (avatarLoader.Load())
                {
                    _avatarSelected = true;
                    avatar = avatarLoader.driver;
                    if (avatar != null) avatarRoot = avatar.transform;
                    ActivateCameraAndStart();
                }
            }
        }

        // Botón Alex (Slim)
        Rect alexBtn = new Rect(startX + btnW + gap, sepY + 30f, btnW, btnH);
        GUI.color = !_skinWide ? ColAccent : Color.white;
        if (GUI.Button(alexBtn, "Alex\n(Delgado / Slim)", new GUIStyle(_stBtnAcc) { fontSize = 14, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.black } }))
        {
            _skinWide = false;
            if (avatarLoader != null)
            {
                avatarLoader.gltfPath = "Avatars/alex.gltf";
                avatarLoader.textureFolder = "Avatars";
                avatarLoader.isWide = false;
                if (avatarLoader.Load())
                {
                    _avatarSelected = true;
                    avatar = avatarLoader.driver;
                    if (avatar != null) avatarRoot = avatar.transform;
                    ActivateCameraAndStart();
                }
            }
        }
        GUI.color = Color.white;

        // Mensaje si no hay AvatarLoader
        if (avatarLoader == null)
        {
            GUI.Label(new Rect(cxCenter - 150, sepY + 120f, 300, 20),
                "Asigna un AvatarLoader en el inspector", _stHint);
        }

        // Botón INICIAR (solo si hay avatar seleccionado)
        if (_avatarSelected)
        {
            float btnBY = sepY + 140f;
            Rect startBtn = new Rect(cxCenter - btnW * 0.5f, btnBY, btnW, btnH * 0.7f);
            GUI.color = ColText;
            GUIStyle stStart = new GUIStyle(_stBigBtn)
            {
                fontSize = 14, fontStyle = FontStyle.Bold,
                normal = new GUIStyleState { background = _texBtnAcc, textColor = Color.white },
                hover  = new GUIStyleState { background = _texBtnAccHov, textColor = Color.white }
            };

            if (GUI.Button(startBtn, "  INICIAR VTUBERCAM", stStart))
                _showStartScreen = false;
            GUI.color = Color.white;
        }

        GUI.Label(new Rect(cx, cy + cardH - 20f, cardW, 16),
            "VTuberCam  |  Open Source  |  Unity + AI Pose Landmarker",
            new GUIStyle(_stHint)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = ColTextDim }
            });
    }

    void ActivateCameraAndStart()
    {
        _virtualCamActive = true;
        if (!virtualCam && mainCamera)
        {
            virtualCam = mainCamera.GetComponent<UnityCapture>();
            if (!virtualCam) virtualCam = mainCamera.gameObject.AddComponent<UnityCapture>();
        }
        if (virtualCam)
        {
            virtualCam.HideWarnings = true;
            virtualCam.ResizeMode = UnityCapture.EResizeMode.LinearResize;
            virtualCam.enabled = true;
        }
        PlayerPrefs.SetInt("vtb_vcam", 1); PlayerPrefs.Save();

        if (mainCamera)
        {
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = DefaultBgColor;
        }

        // Posicionar el avatar frente a la cámara para que se vea en el Game view
        if (avatarRoot != null && mainCamera != null)
        {
            float dist = 2.5f; // distancia frente a la cámara
            avatarRoot.position = mainCamera.transform.position + mainCamera.transform.forward * dist;
            avatarRoot.rotation = Quaternion.LookRotation(-mainCamera.transform.forward, Vector3.up);
            _avatarInitPos   = avatarRoot.position;
            _avatarInitRot   = avatarRoot.rotation;
            _avatarInitScale = avatarRoot.localScale.x > 0 ? avatarRoot.localScale.x : 1f;
        }

        // Aplicar skin personalizada si existe
        if (avatarLoader != null && !string.IsNullOrEmpty(_customSkinPath) && System.IO.File.Exists(_customSkinPath))
        {
            avatarLoader.SetCustomSkin(_customSkinPath);
        }

        EnsureTable();
        if (table != null) table.SetVisible(true);

        ApplySkin(); ApplyBlink(); ApplyAvatarTransform();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    void DrawCardBegin()
    {
        GUILayout.BeginVertical(_stCard);
        GUILayout.Space(2);
    }

    void DrawCardEnd()
    {
        GUILayout.Space(2);
        GUILayout.EndVertical();
    }

    void DrawSection(string t)
    {
        GUILayout.Space(4);
        GUILayout.Label(t, _stSection);
        GUILayout.Space(4);
    }

    void DrawHint(string t)
    {
        GUILayout.Space(2);
        GUILayout.Label(t, _stHint);
        GUILayout.Space(2);
    }

    void DrawStartLabel(float x, float y, string label, float alpha)
    {
        GUI.Label(new Rect(x, y + 4, 100, 20), label,
            new GUIStyle(_stBody) { normal = { textColor = ColTextDim }, fontSize = 10 });
    }

    bool DrawStartBtn(float x, float y, float bw, float bh, string label, bool selected, float alpha)
    {
        GUIStyle st = selected ? _stBtnAcc : _stBtnNorm;
        bool r = GUI.Button(new Rect(x, y, bw, bh), label, st);
        return r;
    }

    void DrawShortcutRow(float x, float y, string key, string desc, float alpha)
    {
        GUI.Label(new Rect(x, y, 110, 18), key,
            new GUIStyle(_stBody) { fontStyle = FontStyle.Bold, normal = { textColor = ColAccent }, fontSize = 9 });
        GUI.Label(new Rect(x + 114, y, 150, 18), desc,
            new GUIStyle(_stBody) { normal = { textColor = ColText }, fontSize = 9 });
    }

    void DrawRect(Rect r, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(r, _texWhite);
        GUI.color = Color.white;
    }

    void BtnAcc(string label, System.Action onClick)
    {
        GUILayout.BeginHorizontal(); GUILayout.Space(10);
        if (GUILayout.Button(label, _stBtnAcc, GUILayout.Height(28)))
            onClick();
        GUILayout.EndHorizontal();
        GUILayout.Space(4);
    }

    void SliderFull(string label, string valStr, ref float val, float min, float max,
        System.Action<float> onChange, float threshold = 0.01f)
    {
        GUILayout.BeginHorizontal(); GUILayout.Space(10);
        GUILayout.Label(label, _stBody, GUILayout.Width(110));
        GUILayout.Label(valStr,
            new GUIStyle(_stBody) { normal = { textColor = ColAccent }, alignment = TextAnchor.MiddleRight },
            GUILayout.Width(52));
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal(); GUILayout.Space(10);
        float nv = GUILayout.HorizontalSlider(val, min, max, GUILayout.Width(220));
        GUILayout.EndHorizontal();
        if (Mathf.Abs(nv - val) > threshold) { val = nv; onChange(val); }
        GUILayout.Space(6);
    }

    void ApplySkin()
    {
        if (wideModel) wideModel.SetActive(_skinWide);
        if (slimModel) slimModel.SetActive(!_skinWide);
        PlayerPrefs.SetInt("vtb_skinWide", _skinWide ? 1 : 0); PlayerPrefs.Save();
    }
    void ApplyBlink()
    {
        if (avatar) avatar.blinkEnabled = _blink;
        PlayerPrefs.SetInt("vtb_blink", _blink ? 1 : 0); PlayerPrefs.Save();
    }

    void BuildStyles()
    {
        _texWhite   = MakeTex(Color.white);
        _texBg      = MakeTex(ColBg);
        _texCard    = MakeTex(ColBgLight);
        _texBtn     = MakeTex(new Color(0.22f, 0.25f, 0.32f, 1.0f));
        _texBtnHov  = MakeTex(new Color(0.30f, 0.33f, 0.42f, 1.0f));
        _texBtnAcc  = MakeTex(ColAccent);
        _texBtnAccHov = MakeTex(ColAccentD);
        _texBtnDanger = MakeTex(ColDanger);
        _texBtnDangerHov = MakeTex(new Color(0.80f, 0.20f, 0.20f, 1.0f));
        _texBtnTab    = MakeTex(new Color(0.20f, 0.22f, 0.28f, 1.0f));
        _texBtnTabSel = MakeTex(ColAccent);

        _stTitle   = new GUIStyle { normal = { textColor = ColText }, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
        _stSection = new GUIStyle(_stTitle) { fontSize = 11, normal = { textColor = ColAccent }, fontStyle = FontStyle.Bold };
        _stBody    = new GUIStyle { normal = { textColor = ColText } };
        _stHint    = new GUIStyle { normal = { textColor = ColTextDim } };
        _stBigBtn  = new GUIStyle { normal = { background = _texBtnAcc }, fontSize = 13, alignment = TextAnchor.MiddleCenter };

        _stBtnNorm = new GUIStyle
        {
            normal = { background = _texBtn, textColor = ColText },
            hover  = { background = _texBtnHov, textColor = ColText },
            fontSize = 11,
            alignment = TextAnchor.MiddleCenter,
            border = new RectOffset(6, 6, 6, 6)
        };
        _stBtnSel = new GUIStyle(_stBtnNorm)
        {
            normal = { background = _texBtnAcc, textColor = Color.white },
            hover = { background = _texBtnAccHov, textColor = Color.white }
        };
        _stBtnAcc = new GUIStyle(_stBtnNorm)
        {
            normal = { background = _texBtnAcc, textColor = Color.white },
            hover = { background = _texBtnAccHov, textColor = Color.white }
        };
        _stBtnDanger = new GUIStyle(_stBtnNorm)
        {
            normal = { background = _texBtnDanger, textColor = Color.white },
            hover = { background = _texBtnDangerHov, textColor = Color.white }
        };
        _stBtnTab = new GUIStyle(_stBtnNorm)
        {
            normal = { background = _texBtnTab, textColor = ColTextDim },
            hover = { background = _texBtnHov, textColor = ColText },
            fontSize = 10
        };
        _stBtnTabSel = new GUIStyle(_stBtnTab)
        {
            normal = { background = _texBtnTabSel, textColor = Color.black },
            hover = { background = _texBtnAcc, textColor = Color.black }
        };

        _stCard = new GUIStyle
        {
            normal = { background = _texCard },
            border = new RectOffset(8, 8, 8, 8),
            padding = new RectOffset(10, 10, 8, 8)
        };

        // ── Checkbox redondeado (estilo imagen 2) ──
        _texCheckOff = MakeRoundedRect(24, new Color(0.22f, 0.25f, 0.32f, 1f), new Color(0.45f, 0.50f, 0.60f, 1f));
        _texCheckOn  = MakeRoundedRect(24, ColAccent, ColAccentD);
        // Dibujar check blanco sobre el checked
        var check = MakeCheckMark(24, Color.white);
        for (int y = 0; y < check.height; y++)
            for (int x = 0; x < check.width; x++)
            {
                Color c = check.GetPixel(x, y);
                if (c.a > 0.1f) _texCheckOn.SetPixel(x, y, c);
            }
        _texCheckOn.Apply();

        _stCheck = new GUIStyle
        {
            normal = { textColor = ColText },
            fontSize = 11,
            alignment = TextAnchor.MiddleLeft
        };

        _stylesReady = true;
    }

    // ── Checkbox personalizado ───────────────────────────────────────────────
    bool Checkbox(bool value, string label)
    {
        Rect r = GUILayoutUtility.GetRect(0, 26, GUILayout.ExpandWidth(true));
        r.x += 10; r.width -= 10;
        Rect box = new Rect(r.x, r.y + (r.height - 20f) * 0.5f, 20f, 20f);
        Event e = Event.current;
        bool over = r.Contains(e.mousePosition);

        if (e.type == EventType.Repaint)
        {
            GUI.DrawTexture(box, value ? _texCheckOn : _texCheckOff);
            GUI.color = over ? Color.white : new Color(1f, 1f, 1f, 0.9f);
            GUI.Label(new Rect(box.xMax + 8f, r.y, r.width - 30f, r.height), label, _stCheck);
            GUI.color = Color.white;
        }
        else if (e.type == EventType.MouseDown && e.button == 0 && over)
        {
            value = !value;
            GUI.changed = true;
            e.Use();
        }
        return value;
    }

    // ── Checkbox helpers ─────────────────────────────────────────────────────
    static Texture2D MakeRoundedRect(int size, Color bg, Color border)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        t.filterMode = FilterMode.Bilinear;
        Color[] px = new Color[size * size];
        float half = size * 0.5f;
        float cr   = size * 0.25f; // radio de esquina
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float qx = Mathf.Abs(x + 0.5f - half) - (half - cr);
                float qy = Mathf.Abs(y + 0.5f - half) - (half - cr);
                float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                float dist = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - cr; // <0 = dentro
                float a = Mathf.Clamp01(0.5f - dist);                          // antialias
                Color c = dist > -2f ? border : bg;                            // borde de 2px
                c.a *= a;
                px[y * size + x] = c;
            }
        }
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    static Texture2D MakeCheckMark(int size, Color color)
    {
        var t = new Texture2D(size, size);
        Color[] px = new Color[size * size];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(0,0,0,0);
        // Dibujar check: dos líneas que forman una marca
        int thick = Mathf.Max(1, size / 16);
        // Línea 1: de (0.35,0.5) a (0.5,0.65)
        // Línea 2: de (0.5,0.65) a (0.75,0.3)
        DrawLine(t, px, size, size*0.27f, size*0.50f, size*0.43f, size*0.32f, thick, color);
        DrawLine(t, px, size, size*0.43f, size*0.32f, size*0.75f, size*0.70f, thick, color);
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    static void DrawLine(Texture2D t, Color[] px, int size, float x0, float y0, float x1, float y1, int thick, Color c)
    {
        int steps = Mathf.Max(Mathf.Abs(Mathf.RoundToInt(x1-x0)), Mathf.Abs(Mathf.RoundToInt(y1-y0))) * 2 + 2;
        for (int i = 0; i <= steps; i++)
        {
            float t0 = i / (float)steps;
            float x = Mathf.Lerp(x0, x1, t0);
            float y = Mathf.Lerp(y0, y1, t0);
            for (int dy = -thick; dy <= thick; dy++)
                for (int dx = -thick; dx <= thick; dx++)
                {
                    int ix = Mathf.RoundToInt(x) + dx;
                    int iy = Mathf.RoundToInt(y) + dy;
                    if (ix >= 0 && ix < size && iy >= 0 && iy < size)
                        px[iy * size + ix] = c;
                }
        }
    }

    static Texture2D MakeTex(Color c)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }
}