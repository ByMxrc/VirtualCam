using UnityEngine;

/// <summary>
/// Mueve un avatar de Minecraft (piezas rígidas) con los landmarks de pose de MediaPipe.
/// No depende del plugin: solo recibe arrays con EnqueuePose(...) / SetPose(...).
/// Pon este script en el objeto raíz del avatar (alex) y arrastra las piezas.
/// </summary>
public class PoseAvatarDriver : MonoBehaviour
{
    [Header("Piezas del avatar (los grupos padre, los que tienen el pivote)")]
    public Transform torso;
    public Transform head;
    public Transform rightArm;
    public Transform leftArm;

    [Header("Ajustes")]
    [Tooltip("Prueba 180 si el avatar se mueve al revés o de espaldas.")]
    public float yawOffset = 0f;
    [Tooltip("Marca esto si tu brazo derecho mueve el izquierdo del avatar (efecto espejo).")]
    public bool mirror = false;
    [Range(1f, 30f)] public float smoothing = 15f;
    [Range(0f, 1f)] public float depthScale = 0.6f;   // la profundidad (z) es ruidosa, la reducimos
    [Range(0f, 1f)] public float torsoWeight = 0.6f;  // cuánto sigue el torso (0 = quieto)
    [Range(0f, 1f)] public float minVisibility = 0.5f;

    [Header("Anti-ruido (movimiento fantasma)")]
    [Tooltip("Cuántos landmarks clave (de 10) deben verse con buena confianza para aceptar una pose. Súbelo si el avatar se mueve solo.")]
    [Range(3, 10)] public int minLandmarksVisible = 5;
    [Tooltip("Cuántos frames consecutivos de pose válida se necesitan antes de arrancar el tracking. Evita que un frame de ruido active el avatar.")]
    [Range(1, 10)] public int requiredConsecutiveFrames = 3;

    [Header("Sensibilidad")]
    [Tooltip("Multiplicador del giro de la cabeza. 1.35x permite giros amplios y expresivos con movimientos naturales frente a la pantalla.")]
    [Range(0.8f, 2.5f)] public float headSensitivity = 1.35f;

    [Header("Límites humanos (grados)")]
    [Tooltip("Máximo giro horizontal de la cabeza a cada lado (yaw). Un humano llega a ~70°-80°.")]
    [Range(20f, 90f)] public float headYawLimit = 75f;
    [Tooltip("Máxima inclinación arriba/abajo de la cabeza (pitch). Unos 45° es natural.")]
    [Range(10f, 60f)] public float headPitchLimit = 45f;
    [Tooltip("Máxima inclinación lateral de la cabeza (hacia los hombros). En 0 nunca se ladea.")]
    [Range(0f, 30f)] public float headRollLimit = 0f;
    [Tooltip("Invierte la inclinación vertical de la cabeza (arriba/abajo).")]
    public bool invertPitch = false;
    [Tooltip("Usar las orejas para el cabeceo. Apágalo si tienes pelo largo o gorra: el tracking inventa las orejas tapadas y la cabeza mira arriba/abajo sola.")]
    public bool useEarsForPitch = false;
    [Tooltip("Multiplicador extra al mirar ABAJO. Al agachar la cabeza la cara se acorta en la imagen y el ángulo medido sale chico; súbelo si el modelo baja poco la cabeza.")]
    [Range(1f, 4f)] public float pitchDownGain = 2f;
    [Tooltip("Multiplicador extra al mirar ARRIBA. Bájalo si el modelo mira arriba mientras tú miras al frente.")]
    [Range(0f, 3f)] public float pitchUpGain = 0.5f;
    [Tooltip("Ganancia del giro horizontal de la cabeza (1 = normal, 0.7 = 30% menos).")]
    [Range(0.1f, 1.5f)] public float headYawGain = 0.7f;
    [Tooltip("Suavizado de la cabeza cuando estás QUIETO (más bajo = más estable, pero se siente más lenta al empezar a moverte). Al moverte rápido el filtro se abre solo.")]
    [Range(1f, 30f)] public float headStillSmoothing = 5f;
    [Tooltip("Zona muerta general de la cabeza (giro, cabeceo y ladeo) en grados: cambios menores a esto se ignoran para que no tiemble. Los movimientos lentos NO se congelan. 0 = desactivado.")]
    [Range(0f, 6f)] public float headDeadzone = 1.5f;
    [Tooltip("Zona muerta del cabeceo en grados: ángulos menores a esto se ignoran (evita que la cabeza se incline sola por ruido).")]
    [Range(0f, 15f)] public float pitchDeadzone = 4f;
    [Tooltip("Máximo giro del torso en yaw (girar el cuerpo de lado). Unos 45° es lo normal.")]
    [Range(10f, 80f)] public float torsoYawLimit = 45f;
    [Tooltip("Máxima inclinación del torso hacia adelante/atrás.")]
    [Range(5f, 45f)] public float torsoPitchLimit = 30f;
    [Tooltip("Máxima inclinación lateral del torso. En 0 el cuerpo no se ladea al alzar un hombro.")]
    [Range(0f, 30f)] public float torsoRollLimit = 0f;

    [Header("Brazos")]
    [Tooltip("Los brazos solo se mueven si hombro y codo/muñeca se ven con al menos esta confianza. Si no, vuelven a reposo.")]
    [Range(0f, 1f)] public float armMinVisibility = 0.75f;
    [Tooltip("Dentro de este ángulo respecto a 'brazo abajo' el brazo se pega al cuerpo (evita que tiemble o se abra en el escritorio). 0 = desactivado.")]
    [Range(0f, 90f)] public float armSnapAngle = 50f;
    [Tooltip("Si no se ve la muñeca, usar el codo. Apagado = el brazo solo se mueve cuando se ve la mano.")]
    public bool useElbowFallback = false;
    [Range(1f, 30f)] public float armSmoothing = 10f;
    [Tooltip("Cuanto más rápido te mueves, más rápido te sigue el brazo (para saludos y movimientos rápidos). 0 = desactivado.")]
    [Range(0f, 2f)] public float armSpeedBoost = 0.5f;
    [Tooltip("Velocidad con la que el brazo APARECE cuando se detecta la mano (valores bajos = transición más suave).")]
    [Range(0.5f, 20f)] public float armFadeInSpeed = 3f;
    [Tooltip("Velocidad con la que el brazo DESAPARECE cuando se pierde la mano (valores bajos = transición más suave).")]
    [Range(0.5f, 20f)] public float armFadeOutSpeed = 2f;
    [Tooltip("Zona muerta en grados: cambios de dirección del brazo menores a esto se ignoran como temblor, SIN congelar los movimientos lentos. 0 = desactivado.")]
    [Range(0f, 10f)] public float armDeadzone = 2f;

    [Header("Respiración (solo brazos)")]
    [Tooltip("Los brazos giran desde el hombro: el hombro se queda pegado al cuerpo y la mano se separa un poco y vuelve, como al respirar.")]
    public bool breathingEnabled = true;
    [Tooltip("Cuánto se separa la parte de abajo del brazo (la mano) hacia su lado, en píxeles de Minecraft.")]
    [Range(0f, 1f)] public float breathAmplitudePixels = 0.001f;
    [Tooltip("Segundos que dura un ciclo completo (salir y volver).")]
    [Range(1.5f, 10f)] public float breathPeriod = 4f;

    [Header("Si se pierde el tracking")]
    [Tooltip("Segundos sin pose antes de considerar que no hay nadie. Súbelo si el avatar desaparece muy rápido.")]
    public float lostTimeout = 1.2f;
    public float returnToRestSpeed = 3f;
    public bool hideAvatarWhenLost = false;
    public float hideDelay = 3f;

    [Header("Recalibración")]
    [Tooltip("Tecla para volver a la pose de reposo original y recalibrar la cabeza.")]
    public KeyCode recalibKey = KeyCode.R;
    [Tooltip("Muestra el botón de recalibrar en pantalla.")]
    public bool showRecalibButton = true;
    [Tooltip("Visibilidad global de los textos/botones de este script (para el modo limpio de OBS). VTuberSettingsUI puede cambiarlo.")]
    public bool guiVisible = true;

    [Header("Imagen de la cámara que NO quieres ver")]
    [Tooltip("Arrastra aquí el componente (por ejemplo el RawImage) que muestra tu webcam.")]
    public Behaviour[] cameraFeedToHide;

    [Header("Parpadeo -> cambio de skin")]
    [Tooltip("Skin normal (ojos abiertos). Mismo tipo (Alex/Steve) que el modelo.")]
    public Texture2D skinOpen;
    [Tooltip("Skin con los ojos cerrados.")]
    public Texture2D skinClosed;
    [Range(0f, 1f)] public float blinkOn = 0.55f;   // cierra los ojos por encima de este valor
    [Range(0f, 1f)] public float blinkOff = 0.35f;  // los abre por debajo de este valor
    [Tooltip("Ajusta solo el umbral según lo que mide tu cámara (útil con lentes).")]
    public bool autoThreshold = true;

    [Header("Depuración")]
    public bool showDebug = false;

    /// <summary>Activa o desactiva el parpadeo desde la UI externa.</summary>
    [HideInInspector] public bool blinkEnabled = true;

    // ───────────────────────── Índices de MediaPipe Pose ─────────────────────────
    const int NOSE = 0;
    const int L_EYE = 2, R_EYE = 5, L_EAR = 7, R_EAR = 8, MOUTH_L = 9, MOUTH_R = 10;
    const int L_SHOULDER = 11, R_SHOULDER = 12;
    const int L_ELBOW = 13, R_ELBOW = 14;
    const int L_WRIST = 15, R_WRIST = 16;
    const int L_HIP = 23, R_HIP = 24;

    static readonly int[] KeyLandmarks =
        { L_SHOULDER, R_SHOULDER, L_ELBOW, R_ELBOW, L_HIP, R_HIP, L_EYE, R_EYE, MOUTH_L, MOUTH_R };

    static readonly Quaternion RestLook = Quaternion.LookRotation(Vector3.back, Vector3.up);
    static readonly string[] TexProps = { "baseColorTexture", "_BaseMap", "_MainTex" };

    // Cuánto tiempo (s) se mantiene un brazo tras perder la muñeca un instante (evita parpadeos del brazo).
    const float ArmHoldTime = 0.2f;
    // Frames necesarios para calibrar el reposo de la cabeza.
    const int HeadCalibTarget = 15;

    // ───────────────────────── Estado ─────────────────────────
    Quaternion restTorso, restHead, restRight, restLeft;
    Quaternion tTorso, tHead;
    // Rotaciones ORIGINALES de la escena (guardadas en Awake, inmutables).
    Quaternion _origRestTorso, _origRestHead, _origRestRight, _origRestLeft;

    class ArmState
    {
        public Vector3 dir = Vector3.down;   // dirección hombro->mano en el espacio del padre del torso (ya filtrada)
        public bool hasDir;
        public float lastVisible = -999f;
        public float conf;                   // 0 = reposo, 1 = totalmente activo
        public Quaternion smoothed = Quaternion.identity; // rotación suavizada SIN mezclar con el reposo
    }
    readonly ArmState _right = new ArmState();
    readonly ArmState _left = new ArmState();

    // Calibración de reposo de la cabeza (pitch)
    float _calibNoseDrop = 0.45f;
    float _calibEarDelta = 0f;
    bool _calibHasEars = false;
    bool _hasHeadCalib = false;
    int _headCalibFrames = 0, _earCalibFrames = 0;
    float _accumNoseDrop = 0f, _accumEarDelta = 0f, _accumNoseSq = 0f;
    float _noseSmooth, _earSmooth, _lastHeadTime;
    bool _smoothInit;
    float _dbgNose, _dbgPitch;
    float _fYaw, _fPitch, _fRoll;          // ángulos de cabeza tras el filtro adaptivo
    bool _fInit;
    float _hYaw, _hPitch, _hRoll;          // ángulos de cabeza ya filtrados por la zona muerta

    // Respiración: eje de giro de cada brazo (en el espacio local del torso)
    Vector3 _swingAxisRight = Vector3.forward, _swingAxisLeft = Vector3.back;
    const float ArmLengthPixels = 12f;   // un brazo de Minecraft mide 12 píxeles de largo

    int _validFrameCount = 0;
    float lastSeen = -999f;
    float _lastPoseTime = -999f;   // última vez que LLEGARON datos de pose (válidos o no)
    int _lastRecalibFrame = -1;

    Renderer[] renderers;
    bool _hidden;

    // Parpadeo / skin
    Material[] skinMats;
    bool eyesClosed;
    float lastFace = -999f;
    float _peak = 0.2f;
    float _dbgOn = 0f, _dbgBlink = 0f;
    string _dbgProp = "-", _dbgArms = "-";

    // Buffers de trabajo (hilo principal) y de intercambio (hilo de MediaPipe)
    readonly Vector3[] _w = new Vector3[33];
    readonly float[] _v = new float[33];
    readonly object _lock = new object();
    readonly Vector3[] _pendW = new Vector3[33];
    readonly float[] _pendV = new float[33];
    bool _hasPending;
    float _pendBlinkL, _pendBlinkR;
    bool _hasBlink;

    GUIStyle _btnStyle;

    Quaternion Frame => Quaternion.Euler(0f, yawOffset, 0f);

    static float Wrap(float a) => a > 180f ? a - 360f : a;

    // ───────────────────────── Ciclo de vida ─────────────────────────
    void Awake()
    {
        // Brazos como hijos del torso: los hombros giran y orbitan pegados al cuerpo.
        if (torso != null)
        {
            if (rightArm != null && rightArm.parent != torso) rightArm.SetParent(torso, true);
            if (leftArm != null && leftArm.parent != torso) leftArm.SetParent(torso, true);
        }

        _origRestTorso = torso ? torso.localRotation : Quaternion.identity;
        _origRestHead = head ? head.localRotation : Quaternion.identity;
        _origRestRight = rightArm ? rightArm.localRotation : Quaternion.identity;
        _origRestLeft = leftArm ? leftArm.localRotation : Quaternion.identity;

        restTorso = _origRestTorso; restHead = _origRestHead;
        restRight = _origRestRight; restLeft = _origRestLeft;
        tTorso = restTorso; tHead = restHead;
        _right.smoothed = restRight;
        _left.smoothed = restLeft;
        SetupBreathing();

        renderers = GetComponentsInChildren<Renderer>(true);

        // Instancias de material propias (no tocan el archivo original)
        var mats = new System.Collections.Generic.List<Material>();
        foreach (var r in renderers) if (r) mats.AddRange(r.materials);
        skinMats = mats.ToArray();
        if (skinOpen) ApplySkin(skinOpen);
    }

    void Start()
    {
        if (!FindFirstObjectByType<VTuberSettingsUI>())
            gameObject.AddComponent<VTuberSettingsUI>();
    }

    void OnDestroy()
    {
        // Las instancias de material creadas en Awake no se liberan solas.
        if (skinMats != null)
            foreach (var m in skinMats) if (m) Destroy(m);
    }

    // ───────────────────────── Recalibración ─────────────────────────
    /// <summary>
    /// Vuelve al reposo ORIGINAL de la escena y reinicia la calibración de la cabeza.
    /// Seguro de llamar varias veces en el mismo frame (la UI y este script escuchan la misma tecla).
    /// </summary>
    public void RecalibrateNow()
    {
        if (_lastRecalibFrame == Time.frameCount) return;
        _lastRecalibFrame = Time.frameCount;

        restTorso = _origRestTorso; restHead = _origRestHead;
        restRight = _origRestRight; restLeft = _origRestLeft;
        tTorso = restTorso; tHead = restHead;

        if (torso) torso.localRotation = restTorso;
        if (head) head.localRotation = restHead;
        if (rightArm) rightArm.localRotation = restRight;
        if (leftArm) leftArm.localRotation = restLeft;

        ResetArm(_right, restRight);
        ResetArm(_left, restLeft);

        _hasHeadCalib = false;
        _headCalibFrames = 0; _earCalibFrames = 0;
        _accumNoseDrop = 0f; _accumEarDelta = 0f; _accumNoseSq = 0f;
        _smoothInit = false;
        _hYaw = _hPitch = _hRoll = 0f;
        _fInit = false;
        _validFrameCount = 0;

        Debug.Log("[PoseAvatarDriver] Recalibrado a posición original.");
    }

    static void ResetArm(ArmState s, Quaternion rest)
    {
        s.conf = 0f; s.hasDir = false; s.lastVisible = -999f; s.smoothed = rest;
    }

    // ───────────────────────── Parpadeo ─────────────────────────
    /// <summary>left/right = eyeBlinkLeft / eyeBlinkRight (0 abierto, 1 cerrado).</summary>
    public void SetBlink(float left, float right)
    {
        if (!blinkEnabled) { if (eyesClosed) SetEyes(false); return; }
        lastFace = Time.time;
        float b = (left + right) * 0.5f;
        _dbgBlink = b;

        float on = blinkOn, off = blinkOff;
        if (autoThreshold)
        {
            // El pico sube al instante con cada parpadeo y baja muy despacio hacia 0.2
            _peak = Mathf.Max(b, Mathf.MoveTowards(_peak, 0.2f, Time.deltaTime * 0.02f));
            on = Mathf.Clamp(_peak * 0.55f, 0.2f, blinkOn);
            off = on * 0.6f;
        }
        _dbgOn = on;

        if (!eyesClosed && b > on) SetEyes(true);
        else if (eyesClosed && b < off) SetEyes(false);
    }

    void SetEyes(bool closed)
    {
        eyesClosed = closed;
        Texture2D t = closed ? skinClosed : skinOpen;
        if (t) ApplySkin(t);
    }

    void ApplySkin(Texture2D tex)
    {
        _dbgProp = "NINGUNA";
        if (skinMats == null) return;
        foreach (var m in skinMats)
        {
            if (!m) continue;
            foreach (var p in TexProps)
                if (m.HasProperty(p)) { m.SetTexture(p, tex); _dbgProp = p; break; }
        }
    }

    // ───────────────────────── Entrada desde el hilo de MediaPipe ─────────────────────────
    /// <summary>Versión segura entre hilos: guarda los datos y se procesan en el siguiente Update.</summary>
    public void EnqueuePose(Vector3[] world, float[] visibility)
    {
        if (world == null || world.Length < 33) return;
        lock (_lock)
        {
            System.Array.Copy(world, _pendW, 33);
            for (int i = 0; i < 33; i++)
                _pendV[i] = (visibility != null && i < visibility.Length) ? visibility[i] : 1f;
            _hasPending = true;
        }
    }

    /// <summary>Versión segura entre hilos de SetBlink (0 = ojo abierto, 1 = cerrado).</summary>
    public void EnqueueBlink(float left, float right)
    {
        lock (_lock)
        {
            _pendBlinkL = left;
            _pendBlinkR = right;
            _hasBlink = true;
        }
    }

    /// <summary>
    /// Procesa una pose directamente (solo desde el hilo principal).
    /// world = 33 landmarks en metros tal cual salen de MediaPipe; visibility = 33 valores 0..1 (puede ser null).
    /// </summary>
    public void SetPose(Vector3[] world, float[] visibility)
    {
        if (world == null || world.Length < 25) return;
        for (int i = 0; i < 33; i++)
        {
            bool has = i < world.Length;
            _w[i] = has ? world[i] : Vector3.zero;
            _v[i] = (visibility != null && i < visibility.Length) ? visibility[i] : (has ? 1f : 0f);
        }
        ProcessPose();
    }

    // ───────────────────────── Procesado de la pose ─────────────────────────
    // P: MediaPipe (x derecha, y abajo, z hacia la cámara = negativo) -> y hacia arriba, con espejo opcional.
    Vector3 P(int i)
    {
        Vector3 w = _w[i];
        return new Vector3(mirror ? -w.x : w.x, -w.y, w.z * depthScale);
    }

    // Para la cabeza usamos Z completo (sin depthScale) para no reducir el giro y el cabeceo.
    Vector3 PHead(int i)
    {
        Vector3 w = _w[i];
        return new Vector3(mirror ? -w.x : w.x, -w.y, w.z);
    }

    float V(int i) => _v[i];

    void ProcessPose()
    {
        _lastPoseTime = Time.time;

        // Anti-ruido: cuántos landmarks clave se ven bien
        int visCount = 0;
        foreach (int idx in KeyLandmarks)
            if (V(idx) >= minVisibility) visCount++;

        if (visCount < minLandmarksVisible)
        {
            _validFrameCount = 0;
            return;
        }
        if (++_validFrameCount < requiredConsecutiveFrames) return;

        lastSeen = Time.time;

        UpdateTorsoTarget();
        UpdateHeadTarget();

        // tRight controla el brazo DERECHO del avatar (con mirror se usa el izquierdo de la persona)
        FeedArm(_right, mirror ? L_SHOULDER : R_SHOULDER, mirror ? L_ELBOW : R_ELBOW, mirror ? L_WRIST : R_WRIST);
        FeedArm(_left, mirror ? R_SHOULDER : L_SHOULDER, mirror ? R_ELBOW : L_ELBOW, mirror ? R_WRIST : L_WRIST);

        if (showDebug)
        {
            _dbgArms =
                "brazoR hombro/codo/muneca: " + V(R_SHOULDER).ToString("0.00") + "/" + V(R_ELBOW).ToString("0.00") + "/" + V(R_WRIST).ToString("0.00") +
                "   brazoL: " + V(L_SHOULDER).ToString("0.00") + "/" + V(L_ELBOW).ToString("0.00") + "/" + V(L_WRIST).ToString("0.00");
        }
    }

    void UpdateTorsoTarget()
    {
        if (!torso) return;

        int rs = mirror ? L_SHOULDER : R_SHOULDER;
        int ls = mirror ? R_SHOULDER : L_SHOULDER;
        int rh = mirror ? L_HIP : R_HIP;
        int lh = mirror ? R_HIP : L_HIP;
        if (V(rs) < minVisibility || V(ls) < minVisibility) return;

        // Si las caderas no se ven (típico en escritorio), MediaPipe las ADIVINA: usarlas mete temblor
        // en el cabeceo/ladeo del torso. En ese caso tomamos "arriba" del mundo y solo giramos por los hombros.
        bool hipsOk = V(rh) >= minVisibility && V(lh) >= minVisibility;

        Vector3 r = P(ls) - P(rs);
        Vector3 u = hipsOk ? (P(ls) + P(rs)) * 0.5f - (P(lh) + P(rh)) * 0.5f : Vector3.up;
        Vector3 f = Vector3.Cross(u, r);
        if (r.sqrMagnitude < 1e-6f || u.sqrMagnitude < 1e-6f || f.sqrMagnitude < 1e-8f) return;

        Quaternion rawDelta = Quaternion.LookRotation(f, u) * Quaternion.Inverse(RestLook);
        rawDelta = Quaternion.Slerp(Quaternion.identity, rawDelta, torsoWeight);

        Vector3 e = rawDelta.eulerAngles;
        float pitch = Mathf.Clamp(Wrap(e.x), -torsoPitchLimit, torsoPitchLimit);
        float yaw = Mathf.Clamp(Wrap(e.y), -torsoYawLimit, torsoYawLimit);
        float roll = torsoRollLimit > 0.01f ? Mathf.Clamp(Wrap(e.z), -torsoRollLimit, torsoRollLimit) : 0f;

        tTorso = Frame * Quaternion.Euler(pitch, yaw, roll) * Quaternion.Inverse(Frame) * restTorso;
    }

    void UpdateHeadTarget()
    {
        if (!head) return;

        int rEye = mirror ? L_EYE : R_EYE;
        int lEye = mirror ? R_EYE : L_EYE;
        int rMouth = mirror ? MOUTH_L : MOUTH_R;
        int lMouth = mirror ? MOUTH_R : MOUTH_L;
        int rEar = mirror ? L_EAR : R_EAR;
        int lEar = mirror ? R_EAR : L_EAR;

        if (!(V(rEye) >= 0.15f || V(lEye) >= 0.15f || V(NOSE) >= 0.15f)) return;

        Vector3 eyeCenter = (PHead(lEye) + PHead(rEye)) * 0.5f;
        Vector3 mouthCenter = (PHead(lMouth) + PHead(rMouth)) * 0.5f;
        Vector3 r = PHead(lEye) - PHead(rEye);   // derecha -> izquierda de la cara
        Vector3 d = mouthCenter - eyeCenter;     // ojos -> boca
        Vector3 f = Vector3.Cross(r, d);         // hacia donde mira
        if (r.sqrMagnitude < 1e-6f || d.sqrMagnitude < 1e-6f || f.sqrMagnitude < 1e-8f) return;

        float faceHeight = Mathf.Max(eyeCenter.y - mouthCenter.y, 0.02f);
        float faceWidth = Mathf.Max(r.magnitude, 0.03f);

        // Ratio vertical de la nariz entre ojos (0) y boca (1). Mirar arriba -> baja; mirar abajo -> sube.
        float noseDrop = (eyeCenter.y - PHead(NOSE).y) / faceHeight;

        bool earsVis = useEarsForPitch && V(L_EAR) >= 0.5f && V(R_EAR) >= 0.5f;
        float earDelta = 0f;
        if (earsVis)
        {
            Vector3 earCenter = (PHead(lEar) + PHead(rEar)) * 0.5f;
            earDelta = (eyeCenter.y - earCenter.y) / faceWidth;
        }

        // Las medidas de pitch son MUY ruidosas (la distancia ojos-boca es ~6 cm): filtro paso bajo.
        float now = Time.time;
        float hdt = Mathf.Clamp(now - _lastHeadTime, 0.001f, 0.2f);
        float al = _smoothInit ? 1f - Mathf.Exp(-10f * hdt) : 1f;
        _lastHeadTime = now;
        _noseSmooth = _smoothInit ? Mathf.Lerp(_noseSmooth, noseDrop, al) : noseDrop;
        if (earsVis) _earSmooth = _smoothInit ? Mathf.Lerp(_earSmooth, earDelta, al) : earDelta;
        _smoothInit = true;
        noseDrop = _noseSmooth;
        if (earsVis) earDelta = _earSmooth;
        _dbgNose = noseDrop;

        Quaternion rawDelta = Quaternion.LookRotation(f, -d) * Quaternion.Inverse(RestLook);
        Vector3 e = rawDelta.eulerAngles;
        float rawYaw = Wrap(e.y);

        // Calibración de reposo: cara de frente a la cámara Y quieta. Si la nariz varía mucho durante la
        // captura (te estabas moviendo), se descarta y se vuelve a intentar en vez de guardar un valor malo.
        if (!_hasHeadCalib)
        {
            if (Mathf.Abs(rawYaw) < 20f)
            {
                _accumNoseDrop += noseDrop;
                _accumNoseSq += noseDrop * noseDrop;
                _headCalibFrames++;
                if (earsVis) { _accumEarDelta += earDelta; _earCalibFrames++; }

                if (_headCalibFrames >= HeadCalibTarget)
                {
                    float mean = _accumNoseDrop / _headCalibFrames;
                    float variance = Mathf.Max(_accumNoseSq / _headCalibFrames - mean * mean, 0f);
                    if (Mathf.Sqrt(variance) < 0.04f)
                    {
                        _calibNoseDrop = mean;
                        _calibHasEars = _earCalibFrames >= HeadCalibTarget / 2;
                        _calibEarDelta = _calibHasEars ? _accumEarDelta / _earCalibFrames : 0f;
                        _hasHeadCalib = true;
                    }
                    else
                    {
                        _headCalibFrames = 0; _earCalibFrames = 0;
                        _accumNoseDrop = 0f; _accumEarDelta = 0f; _accumNoseSq = 0f;
                    }
                }
            }
        }

        float sens = Mathf.Max(headSensitivity, 0.1f);

        float yaw = Mathf.Clamp(rawYaw * sens * headYawGain, -headYawLimit, headYawLimit);

        // Mientras calibra, la cabeza no cabecea (evita saltos usando una calibración a medias).
        float pitch = 0f;
        if (_hasHeadCalib)
        {
            float pitchNose = (_calibNoseDrop - noseDrop) * 140f;
            float rawPitch = pitchNose;
            if (earsVis && _calibHasEars)
            {
                float pitchEars = (earDelta - _calibEarDelta) * 85f;
                rawPitch = pitchNose * 0.6f + pitchEars * 0.4f;
            }
            // Aquí rawPitch < 0 significa "mirando abajo" (la nariz baja respecto a los ojos), ANTES de invertPitch,
            // así la ganancia vale igual sin importar cómo tengas marcado invertPitch.
            rawPitch *= rawPitch < 0f ? pitchDownGain : pitchUpGain;
            if (invertPitch) rawPitch = -rawPitch;
            pitch = rawPitch * sens;
            // Zona muerta suave: resta 'pitchDeadzone' grados al valor absoluto (sin saltos)
            pitch = Mathf.Sign(pitch) * Mathf.Max(Mathf.Abs(pitch) - pitchDeadzone, 0f);
            pitch = Mathf.Clamp(pitch, -headPitchLimit, headPitchLimit);
        }
        _dbgPitch = pitch;

        float roll = headRollLimit > 0.01f ? Mathf.Clamp(Wrap(e.z), -headRollLimit, headRollLimit) : 0f;

        // Filtro adaptivo: quieto -> muy suave (quita el temblor); moviéndote -> se abre y sigue rápido.
        if (!_fInit) { _fYaw = yaw; _fPitch = pitch; _fRoll = roll; _fInit = true; }
        else
        {
            _fYaw = AdaptiveStep(_fYaw, yaw, hdt);
            _fPitch = AdaptiveStep(_fPitch, pitch, hdt);
            _fRoll = AdaptiveStep(_fRoll, roll, hdt);
        }
        yaw = _fYaw; pitch = _fPitch; roll = _fRoll;

        // Zona muerta general: el ángulo filtrado solo se mueve cuando el real se aleja más de 'headDeadzone'.
        if (headDeadzone > 0.01f)
        {
            _hYaw = Leash(_hYaw, yaw, headDeadzone);
            _hPitch = Leash(_hPitch, pitch, headDeadzone);
            _hRoll = Leash(_hRoll, roll, headDeadzone);
            yaw = _hYaw; pitch = _hPitch; roll = _hRoll;
        }

        tHead = Frame * Quaternion.Euler(pitch, yaw, roll) * Quaternion.Inverse(Frame) * restHead;
    }

    // Un paso del filtro: cuanto mayor es la diferencia (te estás moviendo), más rápido sigue.
    float AdaptiveStep(float current, float target, float dt)
    {
        const float fastRate = 25f;
        float slowRate = Mathf.Max(headStillSmoothing, 1f);
        float open = Mathf.Clamp01((Mathf.Abs(target - current) - 0.5f) / 8f);   // 0.5° o menos = quieto; 8.5° o más = movimiento
        float rate = Mathf.Lerp(slowRate, fastRate, open);
        return Mathf.Lerp(current, target, 1f - Mathf.Exp(-rate * dt));
    }

    // Si 'value' está dentro de la zona muerta respecto a 'held', se queda; si sale, 'held' lo sigue a 'dz' grados de distancia.
    static float Leash(float held, float value, float dz)
    {
        float d = value - held;
        if (Mathf.Abs(d) <= dz) return held;
        return value - Mathf.Sign(d) * dz;
    }

    // Calcula el eje de giro de cada brazo: el que lleva "abajo" hacia "afuera" (su lado), en espacio local del torso.
    void SetupBreathing()
    {
        Vector3 outRight = Vector3.right;
        if (rightArm && leftArm)
        {
            Vector3 between = rightArm.localPosition - leftArm.localPosition;   // de izquierdo a derecho
            // Eje dominante, para que "afuera" sea limpiamente lateral.
            int ax = 0; float best = Mathf.Abs(between.x);
            if (Mathf.Abs(between.y) > best) { ax = 1; best = Mathf.Abs(between.y); }
            if (Mathf.Abs(between.z) > best) { ax = 2; }
            Vector3 axis = ax == 0 ? Vector3.right : (ax == 1 ? Vector3.up : Vector3.forward);
            outRight = axis * Mathf.Sign(between[ax]);
        }
        Vector3 outLeft = -outRight;
        _swingAxisRight = Vector3.Cross(Vector3.down, outRight).normalized;
        _swingAxisLeft = Vector3.Cross(Vector3.down, outLeft).normalized;
    }

    // Respiración: gira cada brazo unos grados desde el hombro (el pivote), así el hombro no se despega del cuerpo.
    // Se aplica DESPUÉS de UpdateArm, que asigna la rotación del hueso desde cero cada frame (no hay acumulación).
    void ApplyBreathing()
    {
        if (!breathingEnabled || breathPeriod <= 0.1f) return;

        float phase = 0.5f * (1f - Mathf.Cos(Time.time * 2f * Mathf.PI / breathPeriod));   // 0 -> 1 -> 0
        // Desplazamiento de la mano (px) -> ángulo, con un brazo de 12 px.
        float angle = Mathf.Atan(breathAmplitudePixels / ArmLengthPixels) * Mathf.Rad2Deg * phase;

        // Si el brazo está siguiendo tu mano (conf = 1) la respiración se apaga para no estorbar.
        if (rightArm)
            rightArm.localRotation = Quaternion.AngleAxis(angle * (1f - _right.conf), _swingAxisRight) * rightArm.localRotation;
        if (leftArm)
            leftArm.localRotation = Quaternion.AngleAxis(angle * (1f - _left.conf), _swingAxisLeft) * leftArm.localRotation;
    }

    // Calcula la dirección hombro->mano del brazo y la filtra con una zona muerta (anti-temblor).
    void FeedArm(ArmState s, int shoulder, int elbow, int wrist)
    {
        if (V(shoulder) < armMinVisibility) return;

        int end = V(wrist) >= armMinVisibility ? wrist
                : ((useElbowFallback && V(elbow) >= armMinVisibility) ? elbow : -1);
        if (end < 0) return;

        Vector3 dir = P(end) - P(shoulder);
        if (dir.sqrMagnitude < 1e-6f) return;
        dir = Frame * dir.normalized;

        if (!s.hasDir || armDeadzone <= 0.01f)
        {
            s.dir = dir;
        }
        else
        {
            // "Correa": si el cambio es menor que la zona muerta, el brazo no se mueve; si es mayor,
            // la dirección filtrada se queda a 'armDeadzone' grados de la real. Los movimientos lentos NO se congelan.
            float ang = Vector3.Angle(s.dir, dir);
            if (ang > armDeadzone)
                s.dir = Vector3.Slerp(dir, s.dir, armDeadzone / ang);
        }

        s.hasDir = true;
        s.lastVisible = Time.time;
    }

    // Rotación local del brazo respecto al torso ACTUAL (no al objetivo), así el brazo apunta donde debe
    // aunque el torso todavía esté girando.
    Quaternion ArmLocalTarget(Vector3 dirParentSpace, Quaternion restLocal)
    {
        Quaternion torsoLocal = torso ? torso.localRotation : Quaternion.identity;
        Vector3 dirInTorso = Quaternion.Inverse(torsoLocal) * dirParentSpace;

        // Zona muerta sólida: cerca de "brazo abajo" el brazo se pega al cuerpo.
        if (armSnapAngle > 0.01f)
        {
            float angle = Vector3.Angle(Vector3.down, dirInTorso);
            if (angle < armSnapAngle)
            {
                float deadzone = armSnapAngle * 0.65f;
                if (angle <= deadzone)
                {
                    dirInTorso = Vector3.down;
                }
                else
                {
                    float t = Mathf.SmoothStep(0f, 1f, (angle - deadzone) / (armSnapAngle - deadzone));
                    dirInTorso = Vector3.Slerp(Vector3.down, dirInTorso, t);
                }
            }
        }

        return Quaternion.FromToRotation(Vector3.down, dirInTorso) * restLocal;
    }

    void UpdateArm(Transform bone, ArmState s, Quaternion restLocal, bool lost, float dt)
    {
        if (!bone) return;

        bool active = !lost && s.hasDir && (Time.time - s.lastVisible) <= ArmHoldTime;
        if (!active) s.hasDir = false;

        float fade = active ? armFadeInSpeed : armFadeOutSpeed;
        s.conf = Mathf.MoveTowards(s.conf, active ? 1f : 0f, fade * dt);

        Quaternion target = active ? ArmLocalTarget(s.dir, restLocal) : restLocal;

        // Suavizado adaptativo: poco movimiento = filtra; mucho (un saludo) = alcanza enseguida.
        float rate = active
            ? Mathf.Min(armSmoothing + Quaternion.Angle(s.smoothed, target) * armSpeedBoost, 60f)
            : returnToRestSpeed;
        s.smoothed = Quaternion.Slerp(s.smoothed, target, 1f - Mathf.Exp(-rate * dt));

        // El estado suavizado NUNCA se mezcla con el reposo (antes se releía el hueso ya mezclado y el brazo
        // se atenuaba dos veces). La mezcla con el reposo solo se aplica al hueso, para el fade in/out.
        bone.localRotation = s.conf >= 1f
            ? s.smoothed
            : Quaternion.Slerp(restLocal, s.smoothed, Mathf.SmoothStep(0f, 1f, s.conf));
    }

    // ───────────────────────── Update ─────────────────────────
    void Update()
    {
        bool gotPose = false, gotBlink = false;
        float bl = 0f, br = 0f;
        lock (_lock)
        {
            if (_hasPending)
            {
                System.Array.Copy(_pendW, _w, 33);
                System.Array.Copy(_pendV, _v, 33);
                _hasPending = false;
                gotPose = true;
            }
            if (_hasBlink)
            {
                gotBlink = true;
                bl = _pendBlinkL;
                br = _pendBlinkR;
                _hasBlink = false;
            }
        }
        if (gotPose) ProcessPose();
        if (gotBlink) SetBlink(bl, br);

        if (Input.GetKeyDown(recalibKey)) RecalibrateNow();

        float dt = Time.deltaTime;
        bool lost = Time.time - lastSeen > lostTimeout;
        if (lost)
        {
            tTorso = restTorso; tHead = restHead;
        }
        // Si dejan de llegar datos de pose, hay que volver a confirmarla (un frame de ruido no debe reactivar el avatar).
        // OJO: se basa en que NO lleguen datos, no en 'lost', porque al arrancar 'lost' es true hasta confirmar la pose.
        if (Time.time - _lastPoseTime > 0.4f) _validFrameCount = 0;
        float k = 1f - Mathf.Exp(-(lost ? returnToRestSpeed : smoothing) * dt);

        // Primero el torso, luego los brazos (dependen de la rotación actual del torso)
        if (torso) torso.localRotation = Quaternion.Slerp(torso.localRotation, tTorso, k);
        if (head) head.localRotation = Quaternion.Slerp(head.localRotation, tHead, k);

        UpdateArm(rightArm, _right, restRight, lost, dt);
        UpdateArm(leftArm, _left, restLeft, lost, dt);
        ApplyBreathing();

        // Si se pierde la cara, vuelve a la skin con ojos abiertos
        if (eyesClosed && Time.time - lastFace > lostTimeout) SetEyes(false);

        // Ocultar/mostrar solo cuando cambia el estado (antes se forzaba .enabled en todos los renderers cada frame)
        bool hide = hideAvatarWhenLost && Time.time - lastSeen > lostTimeout + hideDelay;
        if (hide != _hidden)
        {
            _hidden = hide;
            foreach (var r in renderers) if (r) r.enabled = !hide;
        }
    }

    void LateUpdate()
    {
        // Mantiene oculta la imagen de tu webcam (el modelo sigue recibiendo imagen).
        if (cameraFeedToHide == null) return;
        foreach (var b in cameraFeedToHide) if (b) b.enabled = false;
    }

    void OnGUI()
    {
        if (!guiVisible) return;

        if (showRecalibButton)
        {
            if (_btnStyle == null)
                _btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };

            GUI.backgroundColor = new Color(0.2f, 0.6f, 1f);
            if (GUI.Button(new Rect(Screen.width - 200, Screen.height - 60, 185, 45),
                           "⟳  Recalibrar  [" + recalibKey + "]", _btnStyle))
                RecalibrateNow();
            GUI.backgroundColor = Color.white;
        }

        if (!showDebug) return;
        GUI.color = Color.white;
        GUI.Label(new Rect(10, 10, 1100, 100),
            "blink=" + _dbgBlink.ToString("0.00") + " (umbral " + _dbgOn.ToString("0.00") + ")" + "  ojosCerrados=" + eyesClosed +
            "  | cara hace " + (Time.time - lastFace).ToString("0.0") + "s" +
            "  | pose hace " + (Time.time - lastSeen).ToString("0.0") + "s" +
            "  | calibCabeza=" + (_hasHeadCalib ? "ok(" + _calibNoseDrop.ToString("0.00") + ")" : _headCalibFrames + "/" + HeadCalibTarget) + "  nariz=" + _dbgNose.ToString("0.00") + "  pitch=" + _dbgPitch.ToString("0") + "°\n" +
            "skinOpen=" + (skinOpen ? "ok" : "VACIA") + "  skinClosed=" + (skinClosed ? "ok" : "VACIA") +
            "  propiedadTextura=" + _dbgProp + "  materiales=" + (skinMats == null ? 0 : skinMats.Length) + "\n" + _dbgArms +
            "\nconf brazo R=" + _right.conf.ToString("0.00") + "  L=" + _left.conf.ToString("0.00"));
    }
}