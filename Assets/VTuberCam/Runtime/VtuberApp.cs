using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using GLTFast;
using Mediapipe.Unity.Sample;
using Mediapipe.Unity.Sample.PoseLandmarkDetection;
using UnityEngine;
using UnityEngine.Rendering;

namespace VTuberCam
{
  // Runtime interface. MediaPipe's sample Canvas stays alive for processing, but is not rendered in Game.
  [ExecuteAlways]
  public sealed class VtuberApp : MonoBehaviour
  {
    [SerializeField] private VtuberAvatarLoader avatars;
    [SerializeField] private PoseAvatarDriver driver;
    [SerializeField] private PoseLandmarkerRunner runner;
    public bool ReadyForTracking { get; private set; }
    public bool FaceTrackingEnabled => ReadyForTracking && trackingEnabled;
    private bool started, settings, general, extras, selectingSkin, busy;
    private bool naturalLighting = true;
    private int avatarTab;
    private Rect skinWindow = new Rect(350, 105, 480, 510);
    private Rect generalWindow = new Rect(330, 105, 540, 510);
    private Vector2 generalScroll;
    private bool trackingEnabled = true;
    private bool virtualCameraRequested;
    private bool lowLatency;
    private Texture2D virtualBackground;
    private Camera virtualOutput;
    private RenderTexture virtualTarget;
    private GameObject virtualBackdrop;
    private Material virtualBackdropMaterial;
    private UnityCapture virtualCapture;
    private bool driverInstalled;
    private bool driverOperationRunning;
    private string VirtualBackgroundFile => Path.Combine(Application.persistentDataPath,"VirtualCraft","background.img");
    private string generalMessage = "";
    private int graphicsFps = 60;
    private string skinPath = "";
    private string blinkSkinPath = "";
    private string skinMessage = "Selecciona una skin PNG de 64 × 64 píxeles.";
    private int dropdownOpen;
    private Vector2 cameraListScroll;
    private VtuberAvatarPreview avatarPreview;
    private Light[] sunlight;
    private readonly Dictionary<Material, Shader> litShaders = new Dictionary<Material, Shader>();
    private int cameraIndex, resolutionIndex = 2, fpsIndex = 1;
    private readonly int[] widths = { 640, 960, 1280, 1920 };
    private readonly int[] heights = { 360, 540, 720, 1080 };
    private readonly int[] fps = { 15, 30, 60 };
    private bool steve = true;
    private string status = "Elige tu avatar y cámara";
    private GUIStyle title, label, small, legend, highlighted, button, selected, panel;
    private Texture2D solid;
    private Camera sceneCamera;
    private Camera framedCamera;
    private GameObject framedAvatar;
    private float initialCameraSize;
    private Vector3 cameraPivot;
    private float cameraDistance = 5f;
    private float orbitYaw;
    private float orbitPitch;
    private Vector3 previousMousePosition;
    private int dragMode;
    private Canvas[] sampleCanvases;

    private void OnEnable()
    {
      // Hide the MediaPipe sample UI even while the editor is not in Play mode.
      HideSampleCanvas();
      if (Application.isPlaying) InitializeRuntime();
    }

    private void Awake()
    {
      if (!Application.isPlaying) return;
      InitializeRuntime();
    }

    private void InitializeRuntime()
    {
      PoseLandmarkerRunner.CanStart = () => this != null && ReadyForTracking;
      if (!avatars) avatars = GetComponent<VtuberAvatarLoader>();
      if (!GetComponent<OpenSeeFaceBridge>()) gameObject.AddComponent<OpenSeeFaceBridge>();
      if (!GetComponent<VtuberFaceBlinkTracker>()) gameObject.AddComponent<VtuberFaceBlinkTracker>();
      if (!driver) driver = GetComponent<PoseAvatarDriver>();
      if (!runner) runner = FindFirstObjectByType<PoseLandmarkerRunner>();
      driverInstalled = IsVirtualCraftInstalled();
      if (!virtualBackground) RestoreVirtualBackground();
      if (avatarPreview == null)
      {
        avatarPreview = new VtuberAvatarPreview();
        _ = avatarPreview.InitializeAsync();
      }
      HideSampleCanvas();
      EnsureOutputCamera();
      HideTrackingGraphicsFromGame();
      sunlight = FindObjectsByType<Light>(FindObjectsSortMode.None);
      ApplyNaturalLighting();
      RenderSettings.ambientMode = AmbientMode.Flat;
      RenderSettings.ambientLight = new Color(.53f, .53f, .53f);
    }

    private void ApplyNaturalLighting()
    {
      if (sunlight != null) foreach (var light in sunlight)
      {
        if (!light || light.type != LightType.Directional) continue;
        light.enabled = naturalLighting;
        light.intensity = .45f;
        light.color = Color.white;
      }

      if (!avatars || !avatars.LoadedAvatar) return;
      var unlit = Shader.Find("Unlit/Transparent Cutout");
      if (!unlit) unlit = Shader.Find("Unlit/Texture");
      foreach (var renderer in avatars.LoadedAvatar.GetComponentsInChildren<Renderer>(true))
        foreach (var material in renderer.materials)
        {
          if (!material) continue;
          if (!litShaders.ContainsKey(material)) litShaders[material] = material.shader;
          if (naturalLighting) material.shader = litShaders[material];
          else if (unlit) material.shader = unlit;
          if (avatars.CurrentSkin)
          {
            material.mainTexture = avatars.CurrentSkin;
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", avatars.CurrentSkin);
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", avatars.CurrentSkin);
          }
          material.color = Color.white;
          if (!naturalLighting && material.HasProperty("_Cutoff")) material.SetFloat("_Cutoff", .5f);
        }
    }

    private void OnDestroy()
    {
      if (Application.isPlaying) PoseLandmarkerRunner.CanStart = null;
      avatarPreview?.Dispose();
      StopVirtualCamera();
      if (virtualBackground)
      {
        if (Application.isPlaying) Destroy(virtualBackground);
        else DestroyImmediate(virtualBackground);
      }
      if (solid)
      {
        if (Application.isPlaying) Destroy(solid);
        else DestroyImmediate(solid);
      }
    }

    private void HideSampleCanvas()
    {
      sampleCanvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
      foreach (var canvas in sampleCanvases)
      {
        // The detached original MediaPipe screen is a world-space Canvas in Scene.
        if (canvas.renderMode == RenderMode.WorldSpace && canvas.GetComponent<Mediapipe.Unity.Screen>())
          canvas.enabled = true;
        else
          canvas.enabled = false;
      }
    }

    private void LateUpdate()
    {
      if (!Application.isPlaying) return;
      if (PoseLandmarkerRunner.CanStart == null) InitializeRuntime();
      EnsureOutputCamera();
      HideTrackingGraphicsFromGame();
      UpdateVirtualCamera();
    }

    private void Update()
    {
      if (Application.isPlaying && !started) avatarPreview?.Tick(Time.deltaTime);
      if (!Application.isPlaying || !started || !sceneCamera || !framedAvatar) return;
      var mouse = Input.mousePosition;
      var overPanel = IsOverSettingsPanel(mouse);
      if (Input.GetMouseButtonDown(0) && !overPanel) { dragMode = 1; previousMousePosition = mouse; }
      if (Input.GetMouseButtonDown(1) && !overPanel) { dragMode = 2; previousMousePosition = mouse; }
      if (Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1)) dragMode = 0;

      var delta = mouse - previousMousePosition;
      if (dragMode == 1 && Input.GetMouseButton(0))
      {
        orbitYaw -= delta.x * .35f;
        orbitPitch = Mathf.Clamp(orbitPitch + delta.y * .35f, -80f, 80f);
        ApplyCameraOrbit();
      }
      else if (dragMode == 2 && Input.GetMouseButton(1))
      {
        var unitsPerPixel = sceneCamera.orthographicSize * 2f / Mathf.Max(1, Screen.height);
        var movement = (sceneCamera.transform.right * delta.x + sceneCamera.transform.up * delta.y) * unitsPerPixel;
        cameraPivot -= movement;
        sceneCamera.transform.position -= movement;
      }

      if (!overPanel)
      {
        var scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > .001f)
          sceneCamera.orthographicSize = Mathf.Clamp(sceneCamera.orthographicSize * Mathf.Pow(.85f, scroll),
            Mathf.Max(.1f, initialCameraSize * .2f), Mathf.Max(.5f, initialCameraSize * 5f));
      }
      previousMousePosition = mouse;
    }

    private void ApplyCameraOrbit()
    {
      var orbit = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
      sceneCamera.transform.position = cameraPivot + orbit * new Vector3(0f, 0f, -cameraDistance);
      sceneCamera.transform.LookAt(cameraPivot, Vector3.up);
    }

    private bool IsOverSettingsPanel(Vector3 mouse)
    {
      var scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
      if (scale <= 0f) return false;
      var x = (mouse.x - (Screen.width - 1280f * scale) * .5f) / scale;
      var y = (Screen.height - mouse.y - (Screen.height - 720f * scale) * .5f) / scale;
      var point = new Vector2(x, y);
      return new Rect(1025, 15, 230, settings ? 375 : 198).Contains(point) ||
        new Rect(1005,668,250,46).Contains(point) ||
        (selectingSkin && skinWindow.Contains(point)) || (general && generalWindow.Contains(point));
    }

    private void ResetPose()
    {
      dragMode = 0;
      driver?.ResetPose();
    }

    private void EnsureOutputCamera()
    {
      if (!sceneCamera)
      {
        // The sample camera can keep rendering its skybox after the scene starts.
        // Give the avatar its own output camera and turn off the sample cameras.
        var output = new GameObject("VTuber Output Camera");
        sceneCamera = output.AddComponent<Camera>();
        sceneCamera.tag = "MainCamera";
        sceneCamera.depth = 100f;
        sceneCamera.orthographic = true;
        sceneCamera.orthographicSize = .9f;
        output.transform.position = new Vector3(0f, .75f, -5f);
        output.transform.rotation = Quaternion.identity;
        foreach (var other in FindObjectsByType<Camera>(FindObjectsSortMode.None))
          if (other != sceneCamera)
          {
            other.enabled = false;
            var listener = other.GetComponent<AudioListener>();
            if (listener) listener.enabled = false;
          }
      }
      sceneCamera.clearFlags = CameraClearFlags.SolidColor;
      sceneCamera.backgroundColor = new Color(.56f, .56f, .56f, 1f);
      sceneCamera.cullingMask = ~((1 << 31) | (1 << 30) | (1 << 29));
      if (avatars && avatars.LoadedAvatar &&
          (framedAvatar != avatars.LoadedAvatar || framedCamera != sceneCamera))
        FrameAvatar(avatars.LoadedAvatar);
    }

    private void HideTrackingGraphicsFromGame()
    {
      var screen = FindFirstObjectByType<Mediapipe.Unity.Screen>();
      if (screen && screen.GetComponent<Canvas>() && screen.GetComponent<Canvas>().renderMode == RenderMode.WorldSpace)
      {
        var autoFit = screen.GetComponent<Mediapipe.Unity.AutoFit>();
        if (autoFit) autoFit.enabled = false;
        foreach (var child in screen.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
      }
      var annotations = FindObjectsByType<Mediapipe.Unity.PoseLandmarkerResultAnnotationController>(FindObjectsSortMode.None);
      foreach (var annotation in annotations)
      {
        var group = annotation.GetComponent<CanvasGroup>();
        if (!group) group = annotation.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        foreach (var renderer in annotation.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        foreach (var child in annotation.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
      }
    }

    private IEnumerator StartTracking()
    {
      busy = true;
      started = true;
      status = "Preparando cámara...";
      yield return new WaitForSeconds(1.5f);
      var bootstrap = FindFirstObjectByType<Bootstrap>();
      for (var i = 0; i < 300 && (bootstrap == null || !bootstrap.isFinished); i++) yield return null;
      if (bootstrap == null || !bootstrap.isFinished) { status = "MediaPipe no pudo inicializarse"; busy = false; yield break; }
      ImageSourceProvider.Switch(ImageSourceType.WebCamera);
      var source = ImageSourceProvider.ImageSource;
      var cameras = WebCamTexture.devices;
      if (cameras.Length == 0) { status = "No se encontró una cámara"; busy = false; yield break; }
      source.SelectSource(Mathf.Clamp(cameraIndex, 0, cameras.Length - 1));
      var options = source.availableResolutions;
      var nearest = 0;
      var distance = double.MaxValue;
      for (var i = 0; i < options.Length; i++)
      {
        var score = Math.Abs(options[i].width - widths[resolutionIndex]) + Math.Abs(options[i].height - heights[resolutionIndex]) + Math.Abs(options[i].frameRate - fps[fpsIndex]) * 8;
        if (score < distance) { distance = score; nearest = i; }
      }
      if (options.Length > 0) source.SelectResolution(nearest);
      ReadyForTracking = true;
      status = "Buscando pose...";
      busy = false;
    }

    private void OnGUI()
    {
      if (!Application.isPlaying) HideSampleCanvas();
      InitStyles();
      if (!started)
      {
        var previous = GUI.color;
        GUI.color = new Color(.09f,.11f,.16f);
        GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),solid);
        GUI.color = previous;
      }
      float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
      var old = GUI.matrix;
      GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1280f * scale) / 2f, (Screen.height - 720f * scale) / 2f), Quaternion.identity, new Vector3(scale,scale,1));
      if (!started) DrawStart(); else
      {
        DrawStage();
        if (selectingSkin)
          skinWindow = GUI.Window(1937, skinWindow, DrawSkinWindow, GUIContent.none, GUIStyle.none);
        if (general)
          generalWindow = GUI.Window(1938, generalWindow, DrawGeneralWindow, GUIContent.none, GUIStyle.none);
      }
      GUI.matrix = old;
    }

    private void InitStyles()
    {
      if (panel != null) return;
      solid = new Texture2D(1,1); solid.SetPixel(0,0,Color.white); solid.Apply();
      panel = new GUIStyle(GUI.skin.box); panel.normal.background = solid; panel.normal.textColor = Color.white;
      title = new GUIStyle(GUI.skin.label) { fontSize = 29, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft }; title.normal.textColor = Color.white;
      label = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.MiddleLeft }; label.normal.textColor = Color.white;
      small = new GUIStyle(label) { fontSize = 13 }; small.normal.textColor = new Color(.69f,.74f,.82f);
      highlighted = new GUIStyle(label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
      highlighted.normal.textColor = new Color(.18f,.94f,.79f);
      legend = new GUIStyle(label) { fontSize = 15, alignment = TextAnchor.MiddleLeft };
      button = new GUIStyle(GUI.skin.button) { fontSize = 16, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(12,12,9,9) };
      selected = new GUIStyle(button); selected.normal.textColor = new Color(.18f,.94f,.79f);
    }

    private void Box(Rect rect, Color color) { var old = GUI.color; GUI.color = color; GUI.Box(rect, GUIContent.none, panel); GUI.color = old; }
    private void DrawStart()
    {
      Box(new Rect(0,0,1280,720),new Color(.09f,.11f,.16f));
      GUI.Label(new Rect(65,52,780,55),"VTuberCam",title);
      GUI.Label(new Rect(65,104,780,30),"Configura tu cámara y elige un avatar para comenzar",small);
      Box(new Rect(65,162,575,453),new Color(.15f,.18f,.24f));
      if (GUI.Button(new Rect(92,182,238,40),"Por defecto",avatarTab == 0 ? selected : button)) avatarTab = 0;
      if (GUI.Button(new Rect(340,182,238,40),"Propios",avatarTab == 1 ? selected : button)) avatarTab = 1;
      if (avatarTab == 0)
      {
        if (GUI.Button(new Rect(92,244,242,260),GUIContent.none,steve ? selected : button)) steve = true;
        if (GUI.Button(new Rect(357,244,242,260),GUIContent.none,!steve ? selected : button)) steve = false;
        DrawAvatarCard(new Rect(102,255,222,169),avatarPreview?.Steve,"STEVE", "Wide · brazos clásicos",steve);
        DrawAvatarCard(new Rect(367,255,222,169),avatarPreview?.Alex,"ALEX", "Slim · brazos delgados",!steve);
      }
      else GUI.Label(new Rect(93,275,500,80),"Tus avatares guardados aparecerán aquí.\nPodrás guardar uno al configurar su skin y sus ajustes.",label);
      Box(new Rect(665,162,550,453),new Color(.15f,.18f,.24f));
      GUI.Label(new Rect(692,180,490,38),"Configuración de captura",label);
      var cameras = WebCamTexture.devices;
      GUI.Label(new Rect(692,235,490,26),"Cámara",small);
      if (GUI.Button(new Rect(692,263,490,43),cameras.Length == 0 ? "No se encontró ninguna cámara" : cameras[Mathf.Clamp(cameraIndex,0,cameras.Length-1)].name + "  ▾",button) && cameras.Length > 0) dropdownOpen = dropdownOpen == 1 ? 0 : 1;
      GUI.Label(new Rect(692,329,490,26),"Calidad de captura",small);
      GUI.enabled = dropdownOpen != 1;
      if (GUI.Button(new Rect(692,357,490,43),widths[resolutionIndex] + " × " + heights[resolutionIndex] + "  ▾",button)) dropdownOpen = dropdownOpen == 2 ? 0 : 2;
      GUI.enabled = true;
      GUI.Label(new Rect(692,423,490,26),"Fotogramas por segundo",small);
      GUI.enabled = dropdownOpen != 1 && dropdownOpen != 2;
      if (GUI.Button(new Rect(692,451,490,43),fps[fpsIndex] + " fps  ▾",button)) dropdownOpen = dropdownOpen == 3 ? 0 : 3;
      GUI.enabled = true;
      GUI.Label(new Rect(692,518,490,60),"La resolución y los FPS son solicitados a la cámara; el dispositivo puede elegir otros valores.",small);
      var canStart = avatarTab == 0 && cameras.Length > 0 && !busy && Application.isPlaying;
      GUI.enabled = canStart;
      if (GUI.Button(new Rect(464,637,352,52),"COMENZAR",button))
      {
        avatarPreview?.SetActive(false);
        LoadAvatar(steve);
        StartCoroutine(StartTracking());
      }
      GUI.enabled = true;
      GUI.Label(new Rect(65,643,380,35),avatarTab == 1 ? "Aún no hay avatares propios" : status,small);
      DrawDropdownOptions(cameras);
    }

    private void DrawAvatarCard(Rect imageRect, Texture preview, string name, string detail, bool chosen)
    {
      if (preview) GUI.DrawTexture(imageRect,preview,ScaleMode.ScaleToFit);
      else GUI.Label(imageRect,"Cargando vista 3D...",small);
      GUI.Label(new Rect(imageRect.x,imageRect.y + 176,imageRect.width,30),name,chosen ? highlighted : label);
      GUI.Label(new Rect(imageRect.x,imageRect.y + 207,imageRect.width,27),detail,small);
    }

    private void DrawDropdownOptions(WebCamDevice[] cameras)
    {
      if (dropdownOpen == 0) return;
      var kind = dropdownOpen;
      string[] options;
      var top = dropdownOpen == 1 ? 309f : dropdownOpen == 2 ? 403f : 497f;
      if (dropdownOpen == 1)
      {
        options = new string[cameras.Length];
        for (var i = 0; i < cameras.Length; i++) options[i] = cameras[i].name;
      }
      else if (dropdownOpen == 2)
      {
        options = new string[widths.Length];
        for (var i = 0; i < widths.Length; i++) options[i] = widths[i] + " × " + heights[i];
      }
      else
      {
        options = new string[fps.Length];
        for (var i = 0; i < fps.Length; i++) options[i] = fps[i] + " fps";
      }
      var height = Mathf.Min(options.Length * 37f + 12f, 220f);
      Box(new Rect(691,top,492,height),new Color(.12f,.15f,.20f,.98f));
      var list = new Rect(697,top + 6,480,height - 12);
      if (kind == 1) cameraListScroll = GUI.BeginScrollView(list,cameraListScroll,new Rect(0,0,460,options.Length * 37f));
      else GUI.BeginGroup(list);
      for (var i = 0; i < options.Length; i++)
        if (GUI.Button(new Rect(0,i * 37f,460,35),options[i],button))
        {
          if (kind == 1) cameraIndex = i;
          else if (kind == 2) resolutionIndex = i;
          else fpsIndex = i;
          dropdownOpen = 0;
        }
      if (kind == 1) GUI.EndScrollView(); else GUI.EndGroup();
    }

    private void DrawStage()
    {
      GUI.Label(new Rect(28,22,280,38),"VTuberCam",title);
      GUI.Label(new Rect(30,61,400,29),driver != null && driver.IsTracking ? "●  Seguimiento activo" : status,small);
      if (GUI.Button(new Rect(1010,672,240,38),virtualCameraRequested ? "VirtualCraft: Activada" : "VirtualCraft: Desactivada",virtualCameraRequested ? selected : button))
      {
        virtualCameraRequested = !virtualCameraRequested;
        if (virtualCameraRequested) StartVirtualCamera(); else StopVirtualCamera();
      }
      if (GUI.Button(new Rect(1046,36,188,42),settings ? "Cerrar ajustes" : "Ajustes",button)) settings = !settings;
      if (GUI.Button(new Rect(1046,86,188,38),"Reiniciar posición",button)) ResetPose();
      if (GUI.Button(new Rect(1046,132,188,38),"Espejo: " + (driver != null && driver.Mirror ? "Activado" : "Desactivado"),button) && driver != null) driver.Mirror = !driver.Mirror;
      if (settings)
      {
        if (GUI.Button(new Rect(1046,185,188,38),"Configuración general",general ? selected : button)) { general = !general; extras = false; selectingSkin = false; }
        if (GUI.Button(new Rect(1046,229,188,38),"Extras",extras ? selected : button)) { extras = !extras; general = false; selectingSkin = false; }
        if (GUI.Button(new Rect(1046,273,188,38),"Selección de skin",selectingSkin ? selected : button)) { selectingSkin = !selectingSkin; general = false; extras = false; }
        if (GUI.Button(new Rect(1046,323,188,38),"Luz solar: " + (naturalLighting ? "Activada" : "Desactivada"),naturalLighting ? selected : button))
        {
          naturalLighting = !naturalLighting;
          ApplyNaturalLighting();
        }
        if (extras && avatars)
        {
          if (GUI.Button(new Rect(1046,373,188,38),"Pestañeo auto: " + (avatars.AutomaticBlink ? "Sí" : "No"),avatars.AutomaticBlink ? selected : button))
            avatars.AutomaticBlink = !avatars.AutomaticBlink;
        }
      }
      Box(new Rect(24,607,595,91),new Color(.10f,.12f,.17f,.58f));
      GUI.Label(new Rect(40,612,565,78),
        "CONTROLES\nClic izquierdo + arrastrar: girar cámara   ·   Clic derecho + arrastrar: mover\nRueda del mouse: zoom   ·   Reiniciar posición: calibrar pose",legend);
    }

    private void DrawSkinWindow(int id)
    {
      Box(new Rect(0, 0, skinWindow.width, skinWindow.height),new Color(.12f,.15f,.21f,.88f));
      Box(new Rect(0, 0, skinWindow.width, 46),new Color(.08f,.10f,.15f,.9f));
      GUI.Label(new Rect(18,7,350,32),"Selección de skin",label);
      if (GUI.Button(new Rect(skinWindow.width - 46,7,35,32),"×",button)) selectingSkin = false;
      GUI.Label(new Rect(20,62,440,28),"Tipo de brazos del avatar",small);
      if (GUI.Button(new Rect(20,96,210,48),"STEVE · Wide",steve ? selected : button)) ChangeAvatar(true);
      if (GUI.Button(new Rect(250,96,210,48),"ALEX · Slim",!steve ? selected : button)) ChangeAvatar(false);
      GUI.Label(new Rect(20,157,440,36),"Skin personalizada (PNG, 64 × 64)",label);
      skinPath = GUI.TextField(new Rect(20,199,440,32),skinPath);
      if (GUI.Button(new Rect(20,244,178,40),"Buscar PNG...",button))
      {
        var chosen = SkinFilePicker.Open();
        if (!string.IsNullOrEmpty(chosen)) skinPath = chosen;
      }
      if (GUI.Button(new Rect(210,244,250,40),"Aplicar skin",button))
      {
        string error = null;
        if (avatars && avatars.SetSkinFromFile(skinPath,out error))
        {
          skinMessage = "Skin aplicada a " + (steve ? "Steve (wide)." : "Alex (slim).");
          ApplyNaturalLighting();
        }
        else skinMessage = avatars ? error : "No se encontró el avatar.";
      }
      GUI.Label(new Rect(20,298,440,48),skinMessage,small);
      if (avatars)
      {
        var blinkEnabled = GUI.Toggle(new Rect(20,353,440,32),avatars.BlinkEnabled,"Activar pestañeo",GUI.skin.toggle);
        if (blinkEnabled != avatars.BlinkEnabled) avatars.BlinkEnabled = blinkEnabled;
        if (blinkEnabled)
        {
          GUI.Label(new Rect(20,389,440,25),"Segunda skin: ojos cerrados (PNG, 64 × 64)",small);
          blinkSkinPath = GUI.TextField(new Rect(20,415,440,30),blinkSkinPath);
          if (GUI.Button(new Rect(20,453,177,38),"Buscar PNG...",button))
          {
            var chosen = SkinFilePicker.Open();
            if (!string.IsNullOrEmpty(chosen)) blinkSkinPath = chosen;
          }
          if (GUI.Button(new Rect(209,453,251,38),"Aplicar skin de parpadeo",button))
          {
            if (avatars.SetBlinkSkinFromFile(blinkSkinPath,out var error)) skinMessage = "Skin de ojos cerrados cargada.";
            else skinMessage = error;
          }
        }
      }
      GUI.DragWindow(new Rect(0,0,skinWindow.width - 53,46));
    }

    private void DrawGeneralWindow(int id)
    {
      Box(new Rect(0,0,generalWindow.width,generalWindow.height),new Color(.12f,.15f,.21f,.88f));
      Box(new Rect(0,0,generalWindow.width,46),new Color(.08f,.10f,.15f,.92f));
      GUI.Label(new Rect(18,7,380,32),"Configuración general",label);
      if (GUI.Button(new Rect(generalWindow.width - 46,7,35,32),"×",button)) general = false;
      generalScroll = GUI.BeginScrollView(new Rect(12,52,generalWindow.width - 24,generalWindow.height - 65),
        generalScroll,new Rect(0,0,generalWindow.width - 45,1200));
      GUI.Label(new Rect(12,8,465,32),"Seguimiento de MediaPipe",label);
      if (GUI.Button(new Rect(12,45,465,37),trackingEnabled ? "Seguimiento: Activado" : "Seguimiento: Desactivado",trackingEnabled ? selected : button))
      {
        trackingEnabled = !trackingEnabled;
        if (runner)
        {
          if (trackingEnabled) { ReadyForTracking = true; runner.Play(); status = "Buscando pose..."; }
          else { runner.Stop(); driver?.ResetPose(); status = "Seguimiento detenido"; }
        }
      }
      GUI.Label(new Rect(12,88,465,43),"Desactiva y vuelve a activar para reiniciar el procesamiento.",small);
      var faceTracker = GetComponent<OpenSeeFaceBridge>();
      GUI.Label(new Rect(12,115,465,25),faceTracker != null && faceTracker.HasTracking
        ? "Captura facial: OpenSeeFace activo."
        : "Captura facial: instala OpenSeeFace; se usa la pose corporal.",small);

      GUI.Label(new Rect(12,140,465,32),"Configuración gráfica",label);
      GUI.Label(new Rect(12,178,245,30),"Anti-aliasing",small);
      var aaValues = new[] { 0, 2, 4, 8 };
      var aaLabels = new[] { "No", "2×", "4×", "8×" };
      var aaIndex = Array.IndexOf(aaValues, QualitySettings.antiAliasing);
      var nextAa = GUI.SelectionGrid(new Rect(245,176,232,32),Mathf.Max(0,aaIndex),aaLabels,4,button);
      if (nextAa != aaIndex) QualitySettings.antiAliasing = aaValues[nextAa];
      var vsync = QualitySettings.vSyncCount != 0;
      var nextVsync = GUI.Toggle(new Rect(12,220,465,32),vsync,"VSync",GUI.skin.toggle);
      if (nextVsync != vsync) QualitySettings.vSyncCount = nextVsync ? 1 : 0;
      var nextLimit = GUI.Toggle(new Rect(12,259,240,30),Application.targetFrameRate > 0,"Límite de cuadros por segundo",GUI.skin.toggle);
      var fpsText = GUI.TextField(new Rect(287,259,95,30),graphicsFps.ToString());
      if (int.TryParse(fpsText,out var value)) graphicsFps = Mathf.Clamp(value,15,240);
      if (nextLimit != (Application.targetFrameRate > 0) || (nextLimit && Application.targetFrameRate != graphicsFps))
        Application.targetFrameRate = nextLimit ? graphicsFps : -1;
      GUI.Label(new Rect(12,306,465,32),"Cámara / webcam",label);
      var cameras = WebCamTexture.devices;
      for (var i = 0; i < cameras.Length && i < 6; i++)
      {
        var row = i;
        if (GUI.Button(new Rect(12,344 + i * 34,465,32),cameras[i].name,cameraIndex == i ? selected : button) && cameraIndex != i)
        {
          cameraIndex = row;
          if (trackingEnabled) StartCoroutine(RestartTrackingCamera());
        }
      }
      var afterCameras = 350 + Mathf.Min(cameras.Length,6) * 34;
      GUI.Label(new Rect(12,afterCameras,465,28),"Resolución de captura",small);
      for (var i = 0; i < widths.Length; i++)
      {
        var selectedResolution = i;
        if (GUI.Button(new Rect(12 + i * 119,afterCameras + 31,114,33),widths[i] + "×" + heights[i],resolutionIndex == i ? selected : button))
        {
          resolutionIndex = selectedResolution;
          if (trackingEnabled) StartCoroutine(RestartTrackingCamera());
        }
      }
      afterCameras += 79;
      GUI.Label(new Rect(12,afterCameras,465,32),"Cámara virtual",label);
      GUI.Label(new Rect(12,afterCameras + 35,465,70),
        "VirtualCraft transmite el avatar sin los menús de Game.\nLa imagen elegida solo se muestra en la cámara virtual.",small);
      if (GUI.Button(new Rect(12,afterCameras + 109,465,36),virtualCameraRequested ? "Desactivar VirtualCraft" : "Habilitar VirtualCraft para esta sesión",button))
      {
        virtualCameraRequested = !virtualCameraRequested;
        if (virtualCameraRequested) StartVirtualCamera(); else StopVirtualCamera();
      }
      if (virtualCameraRequested)
      {
        var nextLow = GUI.Toggle(new Rect(12,afterCameras + 152,465,30),lowLatency,"Baja latencia",GUI.skin.toggle);
        if (nextLow != lowLatency)
        {
          lowLatency = nextLow;
          if (virtualCapture) virtualCapture.DoubleBuffering = !lowLatency;
        }
        GUI.enabled = !driverOperationRunning;
        if (GUI.Button(new Rect(12,afterCameras + 187,465,36),driverInstalled ? "Desinstalar driver VirtualCraft" : "Instalar driver VirtualCraft",button))
          StartCoroutine(RunDriverScript(driverInstalled ? "UninstallVirtualCraft.cmd" : "InstallVirtualCraft.cmd"));
        GUI.enabled = true;
        if (GUI.Button(new Rect(12,afterCameras + 230,226,36),"Cargar imagen de fondo",button)) LoadVirtualBackground();
        if (GUI.Button(new Rect(250,afterCameras + 230,227,36),"Remover imagen",button)) RemoveVirtualBackground();
        GUI.Label(new Rect(12,afterCameras + 278,465,25),"Imagen de fondo de VirtualCraft:",small);
        if (virtualBackground) GUI.DrawTexture(new Rect(12,afterCameras + 307,232,132),virtualBackground,ScaleMode.ScaleToFit);
        else GUI.Label(new Rect(12,afterCameras + 311,465,32),"Ninguna imagen cargada",small);
      }
      GUI.Label(new Rect(12,afterCameras + (virtualCameraRequested ? 450 : 155),465,64),generalMessage,small);
      GUI.EndScrollView();
      GUI.DragWindow(new Rect(0,0,generalWindow.width - 53,46));
    }

    private IEnumerator RestartTrackingCamera()
    {
      if (!runner) yield break;
      runner.Stop();
      yield return null;
      var source = ImageSourceProvider.ImageSource;
      if (source != null)
      {
        source.SelectSource(cameraIndex);
        var options = source.availableResolutions;
        var nearest = 0;
        var distance = double.MaxValue;
        for (var i = 0; i < options.Length; i++)
        {
          var score = Math.Abs(options[i].width - widths[resolutionIndex]) + Math.Abs(options[i].height - heights[resolutionIndex]);
          if (score < distance) { distance = score; nearest = i; }
        }
        if (options.Length > 0) source.SelectResolution(nearest);
      }
      runner.Play();
    }

    private void LoadVirtualBackground()
    {
      var path = SkinFilePicker.Open(true);
      if (string.IsNullOrEmpty(path)) return;
      try
      {
        var image = new Texture2D(2,2,TextureFormat.RGBA32,false);
        var bytes = File.ReadAllBytes(path);
        if (!image.LoadImage(bytes)) { Destroy(image); generalMessage = "Imagen no válida."; return; }
        var saved = VirtualBackgroundFile;
        Directory.CreateDirectory(Path.GetDirectoryName(saved));
        File.WriteAllBytes(saved,bytes);
        if (virtualBackground) Destroy(virtualBackground);
        virtualBackground = image;
        ApplyVirtualBackground();
        generalMessage = "Imagen aplicada a VirtualCraft: " + Path.GetFileName(path);
      }
      catch (Exception exception) { generalMessage = "No se pudo abrir la imagen: " + exception.Message; }
    }

    private void RemoveVirtualBackground()
    {
      if (virtualBackground) Destroy(virtualBackground);
      virtualBackground = null;
      ApplyVirtualBackground();
      try { if (File.Exists(VirtualBackgroundFile)) File.Delete(VirtualBackgroundFile); }
      catch (Exception exception) { generalMessage = "No se pudo eliminar el fondo guardado: " + exception.Message; return; }
      generalMessage = "Imagen removida.";
    }

    private void RestoreVirtualBackground()
    {
      try
      {
        if (!File.Exists(VirtualBackgroundFile)) return;
        var image = new Texture2D(2,2,TextureFormat.RGBA32,false);
        if (!image.LoadImage(File.ReadAllBytes(VirtualBackgroundFile))) { Destroy(image); return; }
        virtualBackground = image;
        ApplyVirtualBackground();
      }
      catch (Exception exception) { Debug.LogWarning("VirtualCraft: " + exception.Message); }
    }

    private void StartVirtualCamera()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
      if (!sceneCamera) { generalMessage = "La cámara del avatar aún no está lista."; virtualCameraRequested = false; return; }
      if (virtualOutput) return;
      var output = new GameObject("VirtualCraft output (sin interfaz)");
      virtualOutput = output.AddComponent<Camera>();
      // Unity Capture converts FP16 linear pixels to sRGB for webcam clients.
      // Sending an 8-bit linear render target directly makes OBS look too dark.
      var targetFormat = QualitySettings.activeColorSpace == ColorSpace.Linear
        ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
      virtualTarget = new RenderTexture(1920,1080,24,targetFormat);
      virtualTarget.Create();
      var background = GameObject.CreatePrimitive(PrimitiveType.Quad);
      background.name = "Fondo exclusivo de VirtualCraft";
      virtualBackdrop = background;
      background.layer = 29;
      background.transform.SetParent(output.transform,false);
      background.transform.localPosition = new Vector3(0f,0f,25f);
      var collider = background.GetComponent<Collider>();
      if (collider) Destroy(collider);
      var shader = Shader.Find("Unlit/Texture");
      if (!shader) shader = Shader.Find("Sprites/Default");
      virtualBackdropMaterial = new Material(shader);
      var backdropRenderer = background.GetComponent<Renderer>();
      backdropRenderer.sharedMaterial = virtualBackdropMaterial;
      backdropRenderer.shadowCastingMode = ShadowCastingMode.Off;
      backdropRenderer.receiveShadows = false;
      ApplyVirtualBackground();
      virtualCapture = output.AddComponent<UnityCapture>();
      virtualCapture.DoubleBuffering = !lowLatency;
      // OBS and other receivers can request a different supported resolution.
      virtualCapture.ResizeMode = UnityCapture.EResizeMode.LinearResize;
      virtualCapture.TargetFrameRate = graphicsFps;
      virtualCapture.HideWarnings = true;
      UpdateVirtualCamera();
      generalMessage = "Salida VirtualCraft activa. Instala el filtro si aún no aparece en otras aplicaciones.";
#else
      virtualCameraRequested = false;
      generalMessage = "VirtualCraft requiere Windows.";
#endif
    }

    private void UpdateVirtualCamera()
    {
      if (!virtualOutput || !sceneCamera) return;
      virtualOutput.CopyFrom(sceneCamera);
      virtualOutput.targetTexture = virtualTarget;
      virtualOutput.cullingMask = (sceneCamera.cullingMask | (1 << 29)) & ~((1 << 30) | (1 << 31));
      virtualOutput.clearFlags = CameraClearFlags.SolidColor;
      virtualOutput.backgroundColor = sceneCamera.backgroundColor;
      virtualOutput.allowHDR = sceneCamera.allowHDR;
      virtualOutput.depth = sceneCamera.depth - 1f;
      virtualOutput.transform.SetPositionAndRotation(sceneCamera.transform.position,sceneCamera.transform.rotation);
      if (virtualBackdrop)
      {
        var h = virtualOutput.orthographicSize * 2f;
        virtualBackdrop.transform.localScale = new Vector3(h * virtualTarget.width / virtualTarget.height,h,1f);
      }
    }

    private void ApplyVirtualBackground()
    {
      if (virtualBackdrop) virtualBackdrop.GetComponent<Renderer>().enabled = virtualBackground;
      if (virtualBackdropMaterial && virtualBackground) virtualBackdropMaterial.mainTexture = virtualBackground;
    }

    private void StopVirtualCamera()
    {
      if (virtualOutput) { virtualOutput.targetTexture = null; Destroy(virtualOutput.gameObject); }
      virtualOutput = null;
      virtualCapture = null;
      virtualBackdrop = null;
      if (virtualTarget) { virtualTarget.Release(); Destroy(virtualTarget); }
      virtualTarget = null;
      if (virtualBackdropMaterial) Destroy(virtualBackdropMaterial);
      virtualBackdropMaterial = null;
    }

    private IEnumerator RunDriverScript(string scriptName)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
      if (driverOperationRunning) yield break;
      driverOperationRunning = true;
      var script = Path.Combine(Application.streamingAssetsPath,"VirtualCraft",scriptName);
      if (!File.Exists(script)) { generalMessage = "Falta " + scriptName + " en StreamingAssets/VirtualCraft."; driverOperationRunning = false; yield break; }
      System.Diagnostics.Process process = null;
      try
      {
        process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
          FileName = script,
          UseShellExecute = true,
          Verb = "runas",
          WorkingDirectory = Path.GetDirectoryName(script)
        });
      }
      catch (Exception exception) { generalMessage = "No se inició el instalador: " + exception.Message; driverOperationRunning = false; yield break; }
      if (process == null) { generalMessage = "No se inició el instalador."; driverOperationRunning = false; yield break; }
      generalMessage = "Esperando a que finalice " + scriptName + "...";
      while (!process.HasExited) yield return null;
      generalMessage = process.ExitCode == 0
        ? (scriptName.StartsWith("Install") ? "VirtualCraft instalado. Reinicia la aplicación que usará la webcam." : "VirtualCraft desinstalado.")
        : "El instalador terminó con error " + process.ExitCode + ".";
      process.Dispose();
      driverInstalled = IsVirtualCraftInstalled();
      driverOperationRunning = false;
#else
      generalMessage = "El driver solo funciona en Windows.";
      yield break;
#endif
    }

    #if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegOpenKeyExW")]
    private static extern int RegOpenKeyEx(IntPtr root, string subKey, int options, int access, out IntPtr result);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegQueryValueExW")]
    private static extern int RegQueryValueEx(IntPtr key, string name, IntPtr reserved, out int type, StringBuilder data, ref int length);
    [DllImport("advapi32.dll")]
    private static extern int RegCloseKey(IntPtr key);
    #endif

    private static bool IsVirtualCraftInstalled()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
      var classesRoot = new IntPtr(unchecked((int)0x80000000));
      if (RegOpenKeyEx(classesRoot,@"CLSID\{5C2CD55C-92AD-4999-8666-912BD3E70010}",0,0x20019,out var key) != 0)
        return false;
      try
      {
        var name = new StringBuilder(256);
        var length = name.Capacity * 2;
        return RegQueryValueEx(key,null,IntPtr.Zero,out var type,name,ref length) == 0 &&
          string.Equals(name.ToString().TrimEnd('\0'),"VirtualCraft",StringComparison.OrdinalIgnoreCase);
      }
      finally { RegCloseKey(key); }
#else
      return false;
#endif
    }

    private void ChangeAvatar(bool value)
    {
      if (steve == value) return;
      steve = value;
      LoadAvatar(steve);
      driver?.Recalibrate();
    }

    private async void LoadAvatar(bool useSteve)
    {
      if (!avatars) return;
      try
      {
        await avatars.LoadAsync(useSteve);
        EnsureOutputCamera();
        if (sceneCamera && avatars.LoadedAvatar && avatars.IsSteve == useSteve)
          FrameAvatar(avatars.LoadedAvatar);
      }
      catch (Exception exception)
      {
        status = "No se pudo cargar el avatar";
        Debug.LogException(exception);
      }
    }

    private void FrameAvatar(GameObject avatar)
    {
      if (framedAvatar != avatar) litShaders.Clear();
      var renderers = avatar.GetComponentsInChildren<Renderer>();
      if (renderers.Length == 0) return;
      var bounds = renderers[0].bounds;
      for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
      sceneCamera.orthographic = true;
      sceneCamera.orthographicSize = Mathf.Max(.35f, bounds.extents.y * .85f);
      cameraPivot = new Vector3(bounds.center.x, bounds.center.y + bounds.extents.y * .1f, bounds.center.z);
      cameraDistance = 5f;
      orbitYaw = 0f;
      orbitPitch = 0f;
      ApplyCameraOrbit();
      framedAvatar = avatar;
      framedCamera = sceneCamera;
      initialCameraSize = sceneCamera.orthographicSize;
      ApplyNaturalLighting();
    }
  }
  // Two isolated render textures for the animated model cards in the start menu.
  internal sealed class VtuberAvatarPreview : IDisposable
  {
    private readonly GameObject[] roots = new GameObject[2];
    private readonly Camera[] cameras = new Camera[2];
    private readonly RenderTexture[] targets = new RenderTexture[2];
    private readonly List<Texture2D> skins = new List<Texture2D>();
    private readonly List<Material> materials = new List<Material>();
    private bool disposed;
    private bool active = true;

    public Texture Steve => targets[0];
    public Texture Alex => targets[1];

    public async Task InitializeAsync()
    {
      try
      {
        await LoadAsync(0, "steve");
        await LoadAsync(1, "alex");
      }
      catch (Exception exception) { Debug.LogException(exception); }
    }

    private async Task LoadAsync(int index, string name)
    {
      var path = Path.Combine(Application.streamingAssetsPath, "Avatars", name + ".gltf");
      var skinPath = Path.Combine(Application.streamingAssetsPath, "Textures", name + "_skin.png");
      if (!File.Exists(path) || !File.Exists(skinPath)) return;
      var importer = new GltfImport();
      if (!await importer.Load(path) || disposed) return;

      var root = new GameObject("Avatar preview " + name);
      roots[index] = root;
      root.transform.position = new Vector3(index == 0 ? -100f : 100f, 0f, 0f);
      var model = new GameObject(name);
      model.transform.SetParent(root.transform, false);
      if (!await importer.InstantiateMainSceneAsync(new GameObjectInstantiator(importer, model.transform)) || disposed) return;

      var skin = new Texture2D(64, 64, TextureFormat.RGBA32, false);
      if (!skin.LoadImage(File.ReadAllBytes(skinPath))) { UnityEngine.Object.Destroy(skin); return; }
      skin.filterMode = FilterMode.Point;
      skin.wrapMode = TextureWrapMode.Clamp;
      skins.Add(skin);
      var renderers = model.GetComponentsInChildren<Renderer>(true);
      if (renderers.Length == 0) return;
      var bounds = renderers[0].bounds;
      foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
      model.transform.position -= bounds.center - root.transform.position;
      var shader = Shader.Find("Unlit/Transparent Cutout");
      if (!shader) shader = Shader.Find("Unlit/Texture");
      foreach (var renderer in renderers)
      {
        renderer.gameObject.layer = 30;
        if (!shader) continue;
        var replacements = new Material[renderer.sharedMaterials.Length];
        for (var m = 0; m < replacements.Length; m++)
        {
          var material = new Material(shader);
          material.mainTexture = skin;
          if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", skin);
          if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", skin);
          material.color = Color.white;
          material.renderQueue = 2450;
          materials.Add(material);
          replacements[m] = material;
        }
        renderer.sharedMaterials = replacements;
      }
      var target = new RenderTexture(256, 256, 16, RenderTextureFormat.ARGB32);
      target.Create();
      targets[index] = target;
      var cameraObject = new GameObject("Preview camera " + name);
      var camera = cameraObject.AddComponent<Camera>();
      cameras[index] = camera;
      camera.enabled = active;
      camera.clearFlags = CameraClearFlags.SolidColor;
      // Preserve alpha so the model floats directly over its card.
      camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
      camera.orthographic = true;
      camera.orthographicSize = Mathf.Max(.4f, bounds.extents.y * 1.18f);
      camera.cullingMask = 1 << 30;
      camera.targetTexture = target;
      camera.transform.position = root.transform.position + new Vector3(0f, 0f, -4f);
      camera.transform.LookAt(root.transform.position);
    }

    public void Tick(float deltaTime)
    {
      // A gentle turn reveals the volume without leaving either avatar in profile.
      var angle = Mathf.Sin(Time.time * .9f) * 25f;
      foreach (var root in roots)
        if (root) root.transform.rotation = Quaternion.Euler(0f, angle, 0f);
    }

    public void SetActive(bool active)
    {
      this.active = active;
      foreach (var camera in cameras) if (camera) camera.enabled = active;
    }

    public void Dispose()
    {
      disposed = true;
      foreach (var camera in cameras) if (camera) DestroyObject(camera.gameObject);
      foreach (var root in roots) if (root) DestroyObject(root);
      foreach (var target in targets) if (target) DestroyObject(target);
      foreach (var skin in skins) if (skin) DestroyObject(skin);
      foreach (var material in materials) if (material) DestroyObject(material);
    }

    private static void DestroyObject(UnityEngine.Object obj)
    {
      if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
      else UnityEngine.Object.DestroyImmediate(obj);
    }
  }

  internal static class SkinFilePicker
  {
    public static string Open(bool anyImage = false)
    {
#if UNITY_EDITOR
      return UnityEditor.EditorUtility.OpenFilePanel(anyImage ? "Imagen para cámara virtual" : "Selecciona una skin Minecraft", "", anyImage ? "" : "png");
#elif UNITY_STANDALONE_WIN
      var file = new StringBuilder(4096);
      var dialog = new OpenFileName
      {
        size = Marshal.SizeOf(typeof(OpenFileName)),
        filter = anyImage ? "Imágenes\0*.png;*.jpg;*.jpeg\0\0" : "Archivos PNG\0*.png\0\0",
        file = file,
        maxFile = file.Capacity,
        title = "Selecciona una skin Minecraft",
        flags = 0x00000800 | 0x00080000 | 0x00000008
      };
      return GetOpenFileName(ref dialog) ? file.ToString() : "";
#else
      return ""; // The editable path field also works on other platforms.
#endif
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
      public int size;
      public IntPtr owner;
      public IntPtr instance;
      [MarshalAs(UnmanagedType.LPWStr)] public string filter;
      [MarshalAs(UnmanagedType.LPWStr)] public string customFilter;
      public int maxCustomFilter;
      public int filterIndex;
      public StringBuilder file;
      public int maxFile;
      public IntPtr fileTitle;
      public int maxFileTitle;
      [MarshalAs(UnmanagedType.LPWStr)] public string initialDir;
      [MarshalAs(UnmanagedType.LPWStr)] public string title;
      public int flags;
      public short fileOffset;
      public short fileExtension;
      [MarshalAs(UnmanagedType.LPWStr)] public string defaultExtension;
      public IntPtr customData;
      public IntPtr hook;
      [MarshalAs(UnmanagedType.LPWStr)] public string templateName;
      public IntPtr reserved;
      public int reservedInt;
      public int flagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileName(ref OpenFileName fileName);
#endif
  }
}
