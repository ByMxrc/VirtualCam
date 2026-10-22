using UnityEngine;

/// <summary>
/// VTuberCam — Panel de ajustes rediseñado.
/// PANTALLA DE INICIO: se muestra al lanzar el programa (con fade-in y animación de pulso).
/// PANEL LATERAL: aparece a la DERECHA sin tapar al modelo. [Tab] para alternar.
/// Estética: Dark Cyberpunk-Elegant — acentos Cyan/Púrpura.
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

    [Header("Teclas rápidas")]
    public KeyCode toggleMenuKey = KeyCode.Tab;
    public KeyCode cleanModeKey  = KeyCode.F1;
    public KeyCode recalibKeyUI  = KeyCode.R;

    // ─── Estado UI ────────────────────────────────────────────────────────────
    bool  _showPanel        = false;
    bool  _cleanMode        = false;
    bool  _virtualCamActive = true;
    bool  _showStartScreen  = true;
    float _startTime;
    float _pulseT    = 0f;
    float _scanlineY = 0f;

    // ─── Ajustes del avatar ──────────────────────────────────────────────────
    bool  _skinWide      = true;
    bool  _blink         = true;
    int   _chromaIdx     = 0;
    bool  _showCustomRGB = false;
    float _cr = 0f, _cg = 1f, _cb = 0f;
    Color _customColor   = Color.green;

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

    // ─── Colores Chroma ──────────────────────────────────────────────────────
    static readonly Color[] _presets = {
        Color.green, Color.blue, Color.magenta,
        new Color(0.08f,0.08f,0.12f), Color.black, Color.white
    };
    static readonly string[] _presetNames = { "Verde","Azul","Magenta","Gris","Negro","Blanco" };

    // ─── Paleta visual ────────────────────────────────────────────────────────
    static readonly Color ColPanel     = new Color(0.07f, 0.07f, 0.13f, 0.98f);
    static readonly Color ColCard      = new Color(0.10f, 0.10f, 0.18f, 1.00f);
    static readonly Color ColCyan      = new Color(0.00f, 0.85f, 0.90f, 1.00f);
    static readonly Color ColPurple    = new Color(0.55f, 0.20f, 0.90f, 1.00f);
    static readonly Color ColPurpleDim = new Color(0.35f, 0.12f, 0.60f, 1.00f);
    static readonly Color ColCyanDim   = new Color(0.00f, 0.50f, 0.55f, 1.00f);
    static readonly Color ColText      = new Color(0.88f, 0.92f, 1.00f, 1.00f);
    static readonly Color ColTextDim   = new Color(0.50f, 0.55f, 0.70f, 1.00f);
    static readonly Color ColDanger    = new Color(0.90f, 0.20f, 0.35f, 1.00f);
    static readonly Color ColBtnBg     = new Color(0.14f, 0.14f, 0.24f, 1.00f);
    static readonly Color ColBtnHov    = new Color(0.22f, 0.22f, 0.38f, 1.00f);
    static readonly Color ColSep       = new Color(0.55f, 0.20f, 0.90f, 0.30f);

    // ─── Texturas ─────────────────────────────────────────────────────────────
    Texture2D _texWhite, _texPanel, _texCard, _texBtn, _texBtnHov,
              _texBtnCyan, _texBtnCyanHov, _texBtnPurp, _texBtnPurpHov,
              _texBtnDanger, _texBtnDangerHov, _texBtnSel;

    // ─── Estilos ──────────────────────────────────────────────────────────────
    GUIStyle _stTitle, _stSection, _stBody, _stHint,
             _stBtnNorm, _stBtnSel, _stBtnCyan, _stBtnPurple,
             _stBtnDanger, _stBigBtn;
    bool     _stylesReady;
    Vector2  _scroll;
    float    _panelW = 295f;

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

        if (avatarRoot)
        {
            _avatarInitPos   = avatarRoot.position;
            _avatarInitRot   = avatarRoot.rotation;
            _avatarInitScale = avatarRoot.localScale.x > 0 ? avatarRoot.localScale.x : 1f;
        }

        _skinWide    = PlayerPrefs.GetInt("vtb_skinWide", 1) == 1;
        _blink       = PlayerPrefs.GetInt("vtb_blink", 1) == 1;
        _chromaIdx   = PlayerPrefs.GetInt("vtb_chromaIdx", 0);
        _cr          = PlayerPrefs.GetFloat("vtb_cr", 0f);
        _cg          = PlayerPrefs.GetFloat("vtb_cg", 1f);
        _cb          = PlayerPrefs.GetFloat("vtb_cb", 0f);
        _modelScale  = PlayerPrefs.GetFloat("vtb_scale", defaultScale);
        _panOffset.x = PlayerPrefs.GetFloat("vtb_panX", 0f);
        _panOffset.y = PlayerPrefs.GetFloat("vtb_panY", 0f);
        _rotationY   = PlayerPrefs.GetFloat("vtb_rotY", 0f);
        if (avatar != null && PlayerPrefs.HasKey("vtb_headSens"))
            avatar.headSensitivity = PlayerPrefs.GetFloat("vtb_headSens", 1.35f);

        if (!virtualCam && mainCamera)
        {
            virtualCam = mainCamera.GetComponent<UnityCapture>();
            if (!virtualCam) virtualCam = mainCamera.gameObject.AddComponent<UnityCapture>();
        }
        _virtualCamActive = PlayerPrefs.GetInt("vtb_vcam", 1) == 1;
        if (virtualCam)
        {
            virtualCam.HideWarnings = false;
            virtualCam.ResizeMode = UnityCapture.EResizeMode.LinearResize;
            virtualCam.enabled = _virtualCamActive;
        }

        ApplySkin(); ApplyBlink(); ApplyChroma(); ApplyAvatarTransform();
    }

    void Update()
    {
        _pulseT   += Time.deltaTime;
        _scanlineY = (_scanlineY + Time.deltaTime * 60f) % Screen.height;

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

        // Si el panel lateral derecho está abierto y el cursor está sobre él
        if (_showPanel && mp.x >= (Screen.width - _panelW))
            return true;

        // Botón "Ajustes" superior derecho cuando el panel está cerrado
        if (!_showPanel && mp.x >= (Screen.width - 150f) && mp.y >= (Screen.height - 50f))
            return true;

        return false;
    }

    void HandleMouseControls()
    {
        if (!avatarRoot) return;

        bool overUI = IsMouseOverUI();

        // Zoom solo si el puntero NO está sobre el menú lateral ni la interfaz
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (!overUI && Mathf.Abs(scroll) > 0.001f)
        {
            _modelScale = Mathf.Clamp(_modelScale + scroll * 0.4f, SCALE_MIN, SCALE_MAX);
            ApplyAvatarTransform();
            SaveTransformPrefs();
        }

        // Paneo (clic derecho)
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

        // Rotación (clic central o Shift + clic izquierdo)
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

    // ═════════════════════════════════════════════════════════════════════════
    // ONGUI
    // ═════════════════════════════════════════════════════════════════════════
    void OnGUI()
    {
        if (!_stylesReady) BuildStyles();
        if (_showStartScreen) { DrawStartScreen(); return; }
        if (_cleanMode) return;

        if (!_showPanel)
        {
            float ty = Screen.height - 72f;
            DrawRect(new Rect(10, ty - 6, 430, 68), new Color(0.04f,0.04f,0.08f,0.85f));
            GUI.color = new Color(1f,1f,1f,0.60f);
            GUI.Label(new Rect(16, ty,      420, 18), "Rueda: Zoom  |  Der+Drag: Mover  |  Cent+Drag: Rotar", _stHint);
            GUI.Label(new Rect(16, ty + 20, 350, 18), "[Tab] Ajustes   [F1] Modo Limpio   [R] Recalibrar",    _stHint);
            GUI.color = Color.white;

            DrawRect(new Rect(Screen.width - 140, 10, 130, 36), ColPanel);
            DrawRect(new Rect(Screen.width - 140, 10, 2, 36), ColCyan);
            if (GUI.Button(new Rect(Screen.width - 138, 10, 128, 36), "  Ajustes", _stBtnNorm))
                _showPanel = true;
            return;
        }

        DrawSidePanel();
    }

    // ─── PANTALLA DE INICIO ──────────────────────────────────────────────────
    void DrawStartScreen()
    {
        float elapsed = Time.realtimeSinceStartup - _startTime;
        float alpha   = Mathf.Clamp01(elapsed * 1.5f);
        float sw = Screen.width;
        float sh = Screen.height;

        // Fondo negro-azulado
        DrawRect(new Rect(0, 0, sw, sh), new Color(0.03f, 0.03f, 0.07f, alpha));

        // Cuadrícula decorativa
        GUI.color = new Color(ColCyan.r, ColCyan.g, ColCyan.b, alpha * 0.10f);
        for (float x = 0; x < sw; x += 80) GUI.DrawTexture(new Rect(x, 0, 1, sh), _texWhite);
        for (float y = 0; y < sh; y += 80) GUI.DrawTexture(new Rect(0, y, sw, 1), _texWhite);
        GUI.color = Color.white;

        // Línea de escaneo
        if (alpha > 0.8f)
        {
            DrawRect(new Rect(0, _scanlineY, sw, 2), new Color(ColCyan.r, ColCyan.g, ColCyan.b, 0.04f));
        }

        // ── Card central ──────────────────────────────────────────────────
        float cardW = Mathf.Min(sw * 0.90f, 700f);
        float cardH = 500f;
        float cx = (sw - cardW) * 0.5f;
        float cy = (sh - cardH) * 0.5f;

        // Sombra
        DrawRect(new Rect(cx + 6, cy + 6, cardW, cardH), new Color(0f, 0f, 0f, 0.45f * alpha));
        // Card background
        DrawRect(new Rect(cx, cy, cardW, cardH), new Color(ColPanel.r, ColPanel.g, ColPanel.b, alpha));
        // Borde superior cyan
        DrawRect(new Rect(cx, cy, cardW, 3), new Color(ColCyan.r, ColCyan.g, ColCyan.b, alpha));
        // Borde izquierdo púrpura
        DrawRect(new Rect(cx, cy, 3, cardH), new Color(ColPurple.r, ColPurple.g, ColPurple.b, alpha));

        // ── Logo VTC ──────────────────────────────────────────────────────
        float lx = cx + 30f;
        float ly = cy + 26f;

        // Fondo logo
        DrawRect(new Rect(lx, ly, 74, 74), new Color(ColPurple.r, ColPurple.g, ColPurple.b, alpha * 0.20f));
        // Marco del logo
        DrawRect(new Rect(lx,      ly,      74,  2),  new Color(ColPurple.r, ColPurple.g, ColPurple.b, alpha));
        DrawRect(new Rect(lx,      ly,       2, 74),  new Color(ColPurple.r, ColPurple.g, ColPurple.b, alpha));
        DrawRect(new Rect(lx + 72, ly,       2, 74),  new Color(ColPurple.r, ColPurple.g, ColPurple.b, alpha));
        DrawRect(new Rect(lx,      ly + 72, 74,  2),  new Color(ColPurple.r, ColPurple.g, ColPurple.b, alpha));

        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.Label(new Rect(lx, ly, 74, 74), "VTC",
            new GUIStyle(_stTitle) { fontSize = 26, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(ColCyan.r, ColCyan.g, ColCyan.b, alpha) } });

        // Nombre app
        GUI.Label(new Rect(lx + 90, ly + 2,  350, 34), "VTuberCam",
            new GUIStyle(_stTitle) { fontSize = 23, normal = { textColor = new Color(1f,1f,1f,alpha) } });
        GUI.Label(new Rect(lx + 92, ly + 34, 300, 20), "v1.0  |  AI Vision Tracker",
            new GUIStyle(_stHint) { fontSize = 11,
                normal = { textColor = new Color(ColCyan.r, ColCyan.g, ColCyan.b, alpha * 0.8f) } });

        // Badge BETA
        DrawRect(new Rect(lx + 92, ly + 52, 38, 16), new Color(ColPurple.r, ColPurple.g, ColPurple.b, alpha * 0.9f));
        GUI.Label(new Rect(lx + 92, ly + 52, 38, 16), "BETA",
            new GUIStyle(_stHint) { fontSize = 9, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } });

        // Separador horizontal
        float sepY = cy + 115f;
        DrawRect(new Rect(cx + 20, sepY, cardW - 40, 1), new Color(ColSep.r, ColSep.g, ColSep.b, alpha));

        // Separador vertical sutil entre columnas
        DrawRect(new Rect(cx + cardW * 0.5f, sepY + 12f, 1, 260f), new Color(ColSep.r, ColSep.g, ColSep.b, alpha * 0.4f));

        // ── Columnas ──────────────────────────────────────────────────────
        float colL = cx + 26f;
        float colW = (cardW * 0.5f) - 40f;
        float colR = cx + cardW * 0.5f + 20f;
        float rowY = sepY + 14f;

        GUIStyle stSec = new GUIStyle(_stSection)
        { normal = { textColor = new Color(ColCyan.r, ColCyan.g, ColCyan.b, alpha) } };

        // ── Columna izquierda: Config rápida ──────────────────────────────
        GUI.Label(new Rect(colL, rowY, colW, 20), "CONFIGURACION RAPIDA", stSec);
        rowY += 24f;

        DrawStartLabel(colL, rowY, "Tipo de Skin:", alpha);
        float skinBtnW = (colW - 105f) * 0.5f;
        if (DrawStartBtn(colL + 100, rowY, skinBtnW, 26, "Wide (Steve)", _skinWide,  alpha)) { _skinWide = true;  ApplySkin(); }
        if (DrawStartBtn(colL + 105 + skinBtnW, rowY, skinBtnW, 26, "Slim (Alex)", !_skinWide,  alpha)) { _skinWide = false; ApplySkin(); }
        rowY += 34f;

        DrawStartLabel(colL, rowY, "Parpadeo Auto:", alpha);
        if (DrawStartBtn(colL + 100, rowY, 90, 26, _blink ? "ON" : "OFF", _blink, alpha))
        { _blink = !_blink; ApplyBlink(); }
        rowY += 34f;

        bool vcamOn = _virtualCamActive && virtualCam != null && virtualCam.enabled;
        DrawStartLabel(colL, rowY, "Cam. Virtual:", alpha);
        if (DrawStartBtn(colL + 100, rowY, 90, 26, vcamOn ? "ON" : "OFF", vcamOn, alpha))
        {
            _virtualCamActive = !_virtualCamActive;
            if (virtualCam) virtualCam.enabled = _virtualCamActive;
            PlayerPrefs.SetInt("vtb_vcam", _virtualCamActive ? 1 : 0); PlayerPrefs.Save();
        }
        rowY += 38f;

        GUI.Label(new Rect(colL, rowY, colW, 20), "FONDO CHROMA KEY", stSec);
        rowY += 24f;

        // Distribución 2x3 para que nunca invadan la columna derecha
        float chromW = (colW - 10f) / 3f;
        for (int i = 0; i < _presets.Length; i++)
        {
            int col = i % 3;
            int row = i / 3;
            float px = colL + col * (chromW + 5f);
            float py = rowY + row * 30f;
            bool sel = (_chromaIdx == i && !_showCustomRGB);
            if (DrawStartBtn(px, py, chromW, 26, _presetNames[i], sel, alpha))
            { _chromaIdx = i; _showCustomRGB = false; ApplyChroma(); }
        }

        // ── Columna derecha: Controles ─────────────────────────────────────
        float c2y = sepY + 14f;
        GUI.Label(new Rect(colR, c2y, colW, 20), "CONTROLES DE RATON", stSec);
        c2y += 24f;
        DrawShortcutRow(colR, c2y, "Rueda Raton",      "Zoom avatar",           alpha); c2y += 20f;
        DrawShortcutRow(colR, c2y, "Der + Arrastrar",  "Mover avatar",          alpha); c2y += 20f;
        DrawShortcutRow(colR, c2y, "Cent + Arrastrar", "Rotar avatar",          alpha); c2y += 20f;
        DrawShortcutRow(colR, c2y, "[Tab]",            "Abrir / Cerrar panel",  alpha); c2y += 20f;
        DrawShortcutRow(colR, c2y, "[F1]",             "Modo Limpio (OBS)",     alpha); c2y += 20f;
        DrawShortcutRow(colR, c2y, "[R]",              "Recalibrar",            alpha); c2y += 26f;

        // Nota OBS (confinada ordenadamente a su columna)
        float noteW = colW;
        float noteH = 54f;
        DrawRect(new Rect(colR, c2y, noteW, noteH), new Color(ColCard.r, ColCard.g, ColCard.b, alpha * 0.90f));
        DrawRect(new Rect(colR, c2y, 2, noteH), new Color(ColCyan.r, ColCyan.g, ColCyan.b, alpha * 0.80f));
        GUI.Label(new Rect(colR + 8, c2y + 4, noteW - 14, noteH - 8),
            "En OBS: agrega 'Camara Web' y elige 'Unity Video Capture' para usar la camara virtual sin capturar ventanas.",
            new GUIStyle(_stHint) { wordWrap = true, fontSize = 10,
                normal = { textColor = new Color(ColTextDim.r, ColTextDim.g, ColTextDim.b, alpha) } });

        // ── Botón de inicio ────────────────────────────────────────────────
        float btnW  = 230f;
        float btnBX = cx + (cardW - btnW) * 0.5f;
        float btnBY = cy + cardH - 66f;
        float pulse = Mathf.Sin(_pulseT * 2.5f) * 0.08f + 0.92f;

        // Glow detrás
        DrawRect(new Rect(btnBX - 4, btnBY - 4, btnW + 8, 48),
            new Color(ColCyan.r, ColCyan.g, ColCyan.b, alpha * 0.15f * pulse));

        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUIStyle stStart = new GUIStyle(_stBigBtn)
        {
            fontSize = 14, fontStyle = FontStyle.Bold,
            normal = new GUIStyleState { background = _texBtnCyan,    textColor = new Color(0.03f,0.03f,0.08f) },
            hover  = new GUIStyleState { background = _texBtnCyanHov, textColor = Color.black }
        };

        if (GUI.Button(new Rect(btnBX, btnBY, btnW, 40), "  INICIAR VTUBERCAM", stStart))
            _showStartScreen = false;
        GUI.color = Color.white;

        // Créditos pie
        GUI.Label(new Rect(cx, cy + cardH - 20f, cardW, 18),
            "VTuberCam  |  Open Source  |  Unity + AI Pose Landmarker",
            new GUIStyle(_stHint)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(ColTextDim.r, ColTextDim.g, ColTextDim.b, alpha * 0.55f) }
            });
    }

    // ─── PANEL LATERAL DERECHO ───────────────────────────────────────────────
    void DrawSidePanel()
    {
        float ph = Screen.height;
        float px = Screen.width - _panelW;

        // Fondo del panel
        DrawRect(new Rect(px, 0, _panelW, ph), ColPanel);

        // Borde izquierdo gradiente cyan->purple
        for (int i = 0; i < (int)ph; i += 2)
        {
            float t = (float)i / ph;
            Color lc = Color.Lerp(ColCyan, ColPurple, t);
            GUI.color = new Color(lc.r, lc.g, lc.b, 0.85f);
            GUI.DrawTexture(new Rect(px, i, 3, 2), _texWhite);
        }
        GUI.color = Color.white;

        // Línea de escaneo sutil
        if (_scanlineY > 0 && _scanlineY < ph)
            DrawRect(new Rect(px, _scanlineY, _panelW, 1),
                new Color(ColCyan.r, ColCyan.g, ColCyan.b, 0.035f));

        GUILayout.BeginArea(new Rect(px, 0, _panelW, ph));

        // ── Header ────────────────────────────────────────────────────────
        GUILayout.Space(10);
        GUILayout.BeginHorizontal();
        GUILayout.Space(14);

        Rect logoR = GUILayoutUtility.GetRect(30, 30, GUILayout.Width(30), GUILayout.Height(30));
        DrawRect(logoR, new Color(ColPurple.r, ColPurple.g, ColPurple.b, 0.28f));
        GUI.color = ColCyan;
        GUI.Label(logoR, "V", new GUIStyle(_stTitle) { alignment = TextAnchor.MiddleCenter, fontSize = 16 });
        GUI.color = Color.white;

        GUILayout.Space(8);
        GUILayout.BeginVertical();
        GUILayout.Label("VTuberCam", _stTitle);
        GUILayout.Label("Panel de Ajustes", new GUIStyle(_stHint) { fontSize = 10 });
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("x", _stBtnDanger, GUILayout.Width(28), GUILayout.Height(28)))
            _showPanel = false;
        GUILayout.Space(10);
        GUILayout.EndHorizontal();

        SeparatorGlow();

        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Width(_panelW), GUILayout.ExpandHeight(true));
        GUILayout.Space(4);

        // ══ 1. VISTA DEL AVATAR ══════════════════════════════════════════════
        DrawSection("VISTA DEL AVATAR");

        SliderFull("Zoom",        $"{_modelScale:0.0}x",             ref _modelScale,  SCALE_MIN, SCALE_MAX, v => { ApplyAvatarTransform(); SaveTransformPrefs(); });
        SliderFull("Pos X",       $"{_panOffset.x:+0.00;-0.00;0}",  ref _panOffset.x, -2f,  2f,            v => { ApplyAvatarTransform(); SaveTransformPrefs(); });
        SliderFull("Pos Y",       $"{_panOffset.y:+0.00;-0.00;0}",  ref _panOffset.y, -2f,  2f,            v => { ApplyAvatarTransform(); SaveTransformPrefs(); });
        SliderFull("Rotar",       $"{_rotationY:0} grados",          ref _rotationY, -ROT_Y_MAX, ROT_Y_MAX, v => { ApplyAvatarTransform(); SaveTransformPrefs(); }, 2f);

        if (avatar != null)
        {
            float hs = avatar.headSensitivity;
            SliderFull("Sens. Cabeza", $"{hs:0.0}x", ref hs, 0.8f, 2.5f,
                v => { avatar.headSensitivity = v; PlayerPrefs.SetFloat("vtb_headSens", v); PlayerPrefs.Save(); }, 0.05f);
            avatar.headSensitivity = hs;
        }

        GUILayout.Space(4);
        BtnCyan("Centrar y Resetear Vista", ResetAvatarTransform);
        DrawHint("Rueda=Zoom  |  Der=Mover  |  Central=Rotar");

        // ══ 2. FONDO CHROMA ══════════════════════════════════════════════════
        DrawSection("FONDO CHROMA KEY");

        GUILayout.BeginHorizontal();
        GUILayout.Space(10);
        for (int i = 0; i < _presets.Length; i++)
        {
            bool sel = (_chromaIdx == i && !_showCustomRGB);
            if (GUILayout.Button(_presetNames[i], sel ? _stBtnSel : _stBtnNorm,
                GUILayout.Width(43), GUILayout.Height(24)))
            { _chromaIdx = i; _showCustomRGB = false; ApplyChroma(); }
            GUILayout.Space(1);
        }
        GUILayout.EndHorizontal();
        GUILayout.Space(4);

        GUILayout.BeginHorizontal(); GUILayout.Space(10);
        _showCustomRGB = GUILayout.Toggle(_showCustomRGB, "  Color RGB personalizado", _stBody);
        GUILayout.EndHorizontal();

        if (_showCustomRGB)
        {
            SliderRGB("R", ref _cr, new Color(0.9f,0.3f,0.3f));
            SliderRGB("G", ref _cg, new Color(0.3f,0.9f,0.5f));
            SliderRGB("B", ref _cb, new Color(0.4f,0.6f,1.0f));
            BtnCyan("Aplicar color RGB", () =>
            {
                _chromaIdx = -1; _customColor = new Color(_cr, _cg, _cb);
                if (mainCamera) { mainCamera.clearFlags = CameraClearFlags.SolidColor; mainCamera.backgroundColor = _customColor; }
                PlayerPrefs.SetFloat("vtb_cr", _cr); PlayerPrefs.SetFloat("vtb_cg", _cg);
                PlayerPrefs.SetFloat("vtb_cb", _cb); PlayerPrefs.Save();
            });
        }
        DrawHint("En OBS: Filtro -> Clave de Color con el mismo color.");

        // ══ 3. OJOS Y SKIN ═══════════════════════════════════════════════════
        DrawSection("OJOS Y SKIN");

        GUILayout.BeginHorizontal(); GUILayout.Space(10);
        bool nb = GUILayout.Toggle(_blink, "  Parpadeo automatico", _stBody);
        GUILayout.EndHorizontal();
        if (nb != _blink) { _blink = nb; ApplyBlink(); }

        GUILayout.Space(4);
        GUILayout.BeginHorizontal(); GUILayout.Space(10);
        if (GUILayout.Button("Wide (Steve)", _skinWide  ? _stBtnSel : _stBtnNorm,
            GUILayout.Width(128), GUILayout.Height(28))) { _skinWide = true;  ApplySkin(); }
        GUILayout.Space(4);
        if (GUILayout.Button("Slim (Alex)",  !_skinWide ? _stBtnSel : _stBtnNorm,
            GUILayout.Width(128), GUILayout.Height(28))) { _skinWide = false; ApplySkin(); }
        GUILayout.EndHorizontal();

        // ══ 4. CAMARA DE SEGUIMIENTO (TRACKING) ══════════════════════════════
        DrawSection("WEBCAM DE SEGUIMIENTO");

        var imgSource = Mediapipe.Unity.Sample.ImageSourceProvider.ImageSource as Mediapipe.Unity.WebCamSource;
        string curCam = imgSource?.sourceName;
        if (string.IsNullOrEmpty(curCam)) curCam = "Detectando webcam...";

        GUILayout.BeginHorizontal(); GUILayout.Space(10);
        GUILayout.Label($"Activa: {curCam}", _stBody);
        GUILayout.EndHorizontal();

        var webcams = WebCamTexture.devices;
        if (webcams != null && webcams.Length > 0)
        {
            for (int ci = 0; ci < webcams.Length; ci++)
            {
                var dev = webcams[ci];
                if (dev.name.Contains("Unity Video Capture")) continue; // La virtual es para OBS, no de entrada
                bool isCur = (dev.name == curCam);
                GUILayout.BeginHorizontal(); GUILayout.Space(10);
                string label = isCur ? $"●  {dev.name}" : $"○  {dev.name}";
                if (GUILayout.Button(label, isCur ? _stBtnSel : _stBtnNorm,
                    GUILayout.Width(_panelW - 24), GUILayout.Height(25)))
                {
                    if (imgSource != null && !isCur)
                    {
                        imgSource.SelectSource(ci);
                    }
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(2);
            }
        }
        DrawHint("Selecciona tu camara fisica real (no virtual).");

        // ══ 5. CAMARA VIRTUAL ════════════════════════════════════════════════
        DrawSection("CAMARA VIRTUAL DIRECTSHOW");

        bool vcam = _virtualCamActive && virtualCam != null && virtualCam.enabled;
        GUILayout.BeginHorizontal(); GUILayout.Space(10);
        if (GUILayout.Button(vcam ? "ACTIVADA — Unity Video Capture" : "DESACTIVADA",
            vcam ? _stBtnSel : _stBtnNorm,
            GUILayout.Width(_panelW - 24), GUILayout.Height(30)))
        {
            _virtualCamActive = !_virtualCamActive;
            if (virtualCam) virtualCam.enabled = _virtualCamActive;
            PlayerPrefs.SetInt("vtb_vcam", _virtualCamActive ? 1 : 0); PlayerPrefs.Save();
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(4);
        DrawStep("1", "OBS/Discord/Zoom: añade 'Camara Web'");
        DrawStep("2", "Dispositivo: 'Unity Video Capture'");
        DrawStep("3", "Listo! Sin HUD en tu stream.");
        GUILayout.Space(4);
        BtnCyan("Abrir Instalador del Driver", () =>
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
        DrawHint("Si no aparece la webcam: corre Install.bat como Administrador.");

        // ══ 5. CALIBRACION ═══════════════════════════════════════════════════
        DrawSection("CALIBRACION");
        BtnPurple("Recalibrar Avatar  [R]", () => { if (avatar) avatar.RecalibrateNow(); });
        DrawHint("Usalo si el avatar quedo inclinado o en posicion rara.");

        // ══ 6. MODO STREAM ═══════════════════════════════════════════════════
        DrawSection("MODO STREAM");
        GUILayout.BeginHorizontal(); GUILayout.Space(10);
        if (GUILayout.Button("[F1]  Ocultar interfaz para OBS",
            _stBtnDanger, GUILayout.Width(_panelW - 24), GUILayout.Height(30)))
            _cleanMode = true;
        GUILayout.EndHorizontal();
        DrawHint("Pulsa F1 para volver a mostrar los controles.");

        // ══ 7. ACERCA DE ─────────────────────────────────────────────────────
        DrawSection("ACERCA DE");
        GUILayout.BeginHorizontal(); GUILayout.Space(10);
        GUILayout.BeginVertical();
        GUILayout.Label("VTuberCam",
            new GUIStyle(_stBody) { fontStyle = FontStyle.Bold, normal = { textColor = ColCyan } });
        GUILayout.Label("Motor: Unity + AI Pose Landmarker", _stHint);
        GUILayout.Label("Camara virtual: UnityCapture (DirectShow)", _stHint);
        GUILayout.Space(4);
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();

        GUILayout.Space(20);
        GUILayout.EndScrollView();

        // Footer fijo
        SeparatorGlow();
        GUILayout.BeginHorizontal(); GUILayout.Space(10);
        GUILayout.Label("VTuberCam  v1.0 BETA",
            new GUIStyle(_stHint)
            { normal = { textColor = new Color(ColTextDim.r, ColTextDim.g, ColTextDim.b, 0.5f) } });
        GUILayout.FlexibleSpace();
        GUILayout.Label($"{(int)(1f / Time.smoothDeltaTime)} fps",
            new GUIStyle(_stHint)
            { alignment = TextAnchor.MiddleRight,
              normal = { textColor = new Color(ColCyan.r, ColCyan.g, ColCyan.b, 0.6f) } });
        GUILayout.Space(10);
        GUILayout.EndHorizontal();
        GUILayout.Space(8);
        GUILayout.EndArea();
    }

    // ─── Helpers de inicio ───────────────────────────────────────────────────
    void DrawStartLabel(float x, float y, string label, float alpha)
    {
        GUI.Label(new Rect(x, y + 5, 108, 22), label,
            new GUIStyle(_stBody)
            { normal = { textColor = new Color(ColTextDim.r, ColTextDim.g, ColTextDim.b, alpha) }, fontSize = 11 });
    }

    bool DrawStartBtn(float x, float y, float bw, float bh, string label, bool selected, float alpha)
    {
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUIStyle st = new GUIStyle(_stBtnNorm)
        {
            fontSize = 11,
            normal = new GUIStyleState
            {
                background = selected ? _texBtnCyan : _texBtn,
                textColor  = selected ? new Color(0.02f,0.02f,0.06f)
                                      : new Color(ColText.r, ColText.g, ColText.b, alpha)
            },
            hover = new GUIStyleState { background = _texBtnCyanHov, textColor = Color.black }
        };
        bool r = GUI.Button(new Rect(x, y, bw, bh), label, st);
        GUI.color = Color.white;
        return r;
    }

    void DrawShortcutRow(float x, float y, string key, string desc, float alpha)
    {
        GUI.Label(new Rect(x, y, 120, 20), key,
            new GUIStyle(_stBody)
            { fontStyle = FontStyle.Bold,
              normal = { textColor = new Color(ColCyan.r, ColCyan.g, ColCyan.b, alpha) }, fontSize = 10 });
        GUI.Label(new Rect(x + 124, y, 160, 20), desc,
            new GUIStyle(_stHint)
            { normal = { textColor = new Color(ColTextDim.r, ColTextDim.g, ColTextDim.b, alpha) }, fontSize = 10 });
    }

    void DrawRect(Rect r, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(r, _texWhite);
        GUI.color = Color.white;
    }

    // ─── Helpers de panel ────────────────────────────────────────────────────
    void DrawSection(string t)
    {
        GUILayout.Space(10);
        GUILayout.BeginHorizontal(); GUILayout.Space(10);
        GUILayout.Label(t, _stSection);
        GUILayout.EndHorizontal();
        SeparatorGlow();
    }
    void DrawHint(string t)
    {
        GUILayout.BeginHorizontal(); GUILayout.Space(14);
        GUILayout.Label(t, _stHint);
        GUILayout.EndHorizontal();
    }
    void DrawStep(string n, string t)
    {
        GUILayout.BeginHorizontal(); GUILayout.Space(14);
        GUILayout.Label(n + ".", new GUIStyle(_stHint)
            { normal = { textColor = ColCyan }, fontStyle = FontStyle.Bold }, GUILayout.Width(16));
        GUILayout.Label(t, _stHint);
        GUILayout.EndHorizontal();
    }
    void BtnCyan(string label, System.Action onClick)
    {
        GUILayout.BeginHorizontal(); GUILayout.Space(10);
        if (GUILayout.Button(label, _stBtnCyan, GUILayout.Width(_panelW - 24), GUILayout.Height(28)))
            onClick();
        GUILayout.EndHorizontal();
        GUILayout.Space(2);
    }
    void BtnPurple(string label, System.Action onClick)
    {
        GUILayout.BeginHorizontal(); GUILayout.Space(10);
        if (GUILayout.Button(label, _stBtnPurple, GUILayout.Width(_panelW - 24), GUILayout.Height(28)))
            onClick();
        GUILayout.EndHorizontal();
        GUILayout.Space(2);
    }
    void SeparatorGlow()
    {
        GUI.color = ColSep;
        GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(1));
        GUI.color = Color.white;
    }
    void SliderFull(string label, string valStr, ref float val, float min, float max,
        System.Action<float> onChange, float threshold = 0.01f)
    {
        GUILayout.BeginHorizontal(); GUILayout.Space(10);
        GUILayout.Label(label, _stBody, GUILayout.Width(90));
        GUILayout.Label(valStr,
            new GUIStyle(_stBody) { normal = { textColor = ColCyan }, alignment = TextAnchor.MiddleRight },
            GUILayout.Width(52));
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal(); GUILayout.Space(10);
        float nv = GUILayout.HorizontalSlider(val, min, max, GUILayout.Width(_panelW - 24));
        GUILayout.EndHorizontal();
        if (Mathf.Abs(nv - val) > threshold) { val = nv; onChange(val); }
    }
    void SliderRGB(string label, ref float val, Color labelColor)
    {
        GUILayout.BeginHorizontal(); GUILayout.Space(14);
        GUILayout.Label(label,
            new GUIStyle(_stBody) { normal = { textColor = labelColor }, fontStyle = FontStyle.Bold },
            GUILayout.Width(14));
        val = GUILayout.HorizontalSlider(val, 0f, 1f, GUILayout.Width(_panelW - 76));
        GUILayout.Label(((int)(val * 255)).ToString(), _stBody, GUILayout.Width(28));
        GUILayout.EndHorizontal();
    }

    // ─── Apply helpers ────────────────────────────────────────────────────────
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
    void ApplyChroma()
    {
        Color col = (_chromaIdx >= 0 && _chromaIdx < _presets.Length) ? _presets[_chromaIdx] : _customColor;
        if (mainCamera) { mainCamera.clearFlags = CameraClearFlags.SolidColor; mainCamera.backgroundColor = col; }
        PlayerPrefs.SetInt("vtb_chromaIdx", _chromaIdx); PlayerPrefs.Save();
    }

    // ─── Construcción de estilos / texturas ──────────────────────────────────
    void BuildStyles()
    {
        _texWhite        = MakeTex(Color.white);
        _texPanel        = MakeTex(ColPanel);
        _texCard         = MakeTex(ColCard);
        _texBtn          = MakeTex(ColBtnBg);
        _texBtnHov       = MakeTex(ColBtnHov);
        _texBtnCyan      = MakeTex(ColCyan);
        _texBtnCyanHov   = MakeTex(new Color(0.20f, 1.00f, 1.00f));
        _texBtnPurp      = MakeTex(ColPurpleDim);
        _texBtnPurpHov   = MakeTex(ColPurple);
        _texBtnDanger    = MakeTex(ColDanger);
        _texBtnDangerHov = MakeTex(new Color(1.0f, 0.35f, 0.45f));
        _texBtnSel       = MakeTex(ColCyanDim);

        GUIStyleState S(Texture2D b, Color c) =>
            new GUIStyleState { background = b, textColor = c };

        _stTitle   = new GUIStyle(GUI.skin.label)
            { fontSize = 15, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
        _stSection = new GUIStyle(GUI.skin.label)
            { fontSize = 10, fontStyle = FontStyle.Bold, normal = { textColor = ColCyan } };
        _stBody    = new GUIStyle(GUI.skin.label)
            { fontSize = 11, normal = { textColor = ColText } };
        _stHint    = new GUIStyle(GUI.skin.label)
            { fontSize = 10, wordWrap = true, normal = { textColor = ColTextDim } };

        _stBtnNorm = new GUIStyle(GUI.skin.button)
        {
            fontSize = 11, alignment = TextAnchor.MiddleCenter,
            normal = S(_texBtn,    ColText),
            hover  = S(_texBtnHov, Color.white),
            active = S(_texBtnSel, Color.white)
        };
        _stBtnSel = new GUIStyle(_stBtnNorm)
        {
            fontStyle = FontStyle.Bold,
            normal = S(_texBtnSel, new Color(0.02f,0.02f,0.06f)),
            hover  = S(_texBtnSel, Color.black)
        };
        _stBtnCyan = new GUIStyle(_stBtnNorm)
        {
            fontStyle = FontStyle.Bold,
            normal = S(_texBtnCyan,    new Color(0.02f,0.02f,0.06f)),
            hover  = S(_texBtnCyanHov, Color.black),
            active = S(_texBtnCyanHov, Color.black)
        };
        _stBtnPurple = new GUIStyle(_stBtnNorm)
        {
            fontStyle = FontStyle.Bold,
            normal = S(_texBtnPurp,    Color.white),
            hover  = S(_texBtnPurpHov, Color.white)
        };
        _stBtnDanger = new GUIStyle(_stBtnNorm)
        {
            fontSize = 12, fontStyle = FontStyle.Bold,
            normal = S(_texBtnDanger,    Color.white),
            hover  = S(_texBtnDangerHov, Color.white)
        };
        _stBigBtn = new GUIStyle(GUI.skin.button)
        {
            fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
            normal = S(_texBtnCyan,    new Color(0.02f,0.02f,0.06f)),
            hover  = S(_texBtnCyanHov, Color.black),
            active = S(_texBtnCyanHov, Color.black)
        };

        _stylesReady = true;
    }

    static Texture2D MakeTex(Color c)
    { var t = new Texture2D(1,1); t.SetPixel(0,0,c); t.Apply(); return t; }
}
