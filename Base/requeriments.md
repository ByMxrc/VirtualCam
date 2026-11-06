# VTuberCam — Documento maestro unificado

**Fecha de compilación:** 1 de noviembre de 2026
**Qué es esto:** unión de los 6 documentos del proyecto (requisitos y arquitectura, respaldo de funcionalidades, texturas y tracking, corrección de espejo y brazos, modelos de Blockbench, atlas de texturas) más una sección nueva con **alternativas que no usan Unity** (Parte D).

> **Aviso de vigencia.** Los documentos de origen son de momentos distintos. Cuando dos se contradicen, la regla es: **el código manda**. Los puntos donde detecté diferencias están marcados con ⚠️. Lo que no se ha probado en pantalla está marcado como *(no verificado)*.

---

## Índice

**Parte A — Cómo funciona el proyecto hoy**
1. Resumen del sistema
2. Arquitectura y flujo de datos
3. Requisitos y entorno
4. Mapa de archivos
5. Flujo de inicio
6. Avatar: carga, glTF, jerarquía y skins
7. Modelos 3D adicionales
8. Tracking: pose, torso, cabeza, brazos, manos
9. Espejo y lados izquierdo/derecho
10. Parpadeo
11. Pérdida de tracking, controles manuales, respiración
12. Interfaz (IMGUI)
13. Persistencia (PlayerPrefs)
14. Cámara virtual, render, hilos y orden por frame
15. Trazo visual oculto en antebrazos y manos

**Parte B — Historial técnico (qué se rompió y cómo se arregló)**
16. Checkboxes de la UI
17. Texturas negras o mal puestas (análisis y causa real)
18. Revisión del `Assets.zip`
19. Comparación con el análisis de Gemini
20. Plan de implementación por fases
21. Espejo y brazos (cambios en Runner y Driver)
22. Salto brusco brazo arriba / mano a cámara

**Parte C — Modelos, herramientas y tracking**
23. Modelos JSON para Blockbench (mesa, mouse, teclado, control)
24. Atlas de texturas en Blockbench
25. MediaPipe Unity Plugin: qué descargar
26. Mejorar el tracking: filtros, alternativas, ¿sistema propio?
27. OpenSeeFace (`Binary.zip`)

**Parte D — Alternativas sin Unity (lo nuevo)**
28. Qué hace realmente la app (para decidir con criterio)
29. Opciones comparadas
30. Detalle de cada opción
31. Recomendación y ruta de migración
32. Esqueleto de la versión web (JS)

**Parte E — Mantenimiento**
33. Limitaciones conocidas
34. Invariantes y reglas para no romper nada
35. Checklist de restauración y pruebas
36. Archivos generados en los chats
37. Pendientes y decisiones abiertas
38. Tabla de parámetros

---

# PARTE A — Cómo funciona el proyecto hoy

## 1. Resumen del sistema

VTuberCam es una aplicación **Unity 6 (`6000.0.58f2`)** que:

- recibe la imagen de una webcam;
- analiza **cuerpo** (MediaPipe Pose Landmarker), **manos** (Hand Landmarker) y **cara** (Face Landmarker, con blendshapes);
- convierte esos resultados en rotaciones de un **avatar de Minecraft** (Steve = Wide, Alex = Slim) cargado desde `.gltf` exportados de Blockbench;
- aplica skins normales y de **ojos cerrados** (parpadeo);
- permite cargar **modelos 3D adicionales** (mesa, mouse, teclado, etc.) y fijarlos al avatar, al mundo o a otro modelo;
- ofrece una interfaz de ajustes IMGUI;
- expone el resultado como **cámara virtual** (UnityCapture → "Unity Video Capture") para OBS, Discord, Zoom, etc.;
- puede ocultar el trazo visual de brazos/manos sin detener el tracking.

Objetivo original del usuario: un modelo 3D de Minecraft que se mueva con **cara, cuello, brazos y, como mucho, torso**; ojos sí, boca opcional, sin orejas; que al parpadear cambie a una skin con ojos cerrados; y que **no se vea la cámara** si el tracking se pierde.

## 2. Arquitectura y flujo de datos

```text
Webcam
  -> ImageSource (ImageSourceProvider)
  -> TextureFramePool
  -> Pose / Face / Hand Landmarker (MediaPipe, LIVE_STREAM)
  -> callbacks seguros (solo copian datos bajo lock)
  -> PoseAvatarDriver.Instance  (Update en hilo principal)
  -> torso / cabeza / brazos / ojos
  -> UnityCapture (cámara virtual)
```

```text
AvatarLoader
  -> GltfModelLoader (flipV + buildHierarchy)
  -> nodos Body / Head / Right Arm / Left Arm
  -> material cutout + skin abierta/cerrada
  -> driver.Bind(torso, head, rightArm, leftArm)
```

```text
PoseLandmarkerRunner
  -> máscara BodyParts
  -> anotadores visuales
  -> trazo visible u oculto
  -> tracking del avatar INDEPENDIENTE del dibujo
```

```text
VTuberSettingsUI
  -> peticiones diferidas (flags)
  -> Update() ejecuta cargas de avatar / skins / modelos
  -> cámara, espejo, cámara virtual
  -> persistencia
```

## 3. Requisitos y entorno

### Software
- Unity `6000.0.58f2`.
- Proyecto en `C:\Users\ADRIAN\Documents\Unity\VTuberCam`.
- MediaPipe Unity Plugin en `Packages/com.github.homuler.mediapipe`.
- Paquete de captura virtual y driver `VirtualCameraDriver` (UnityCapture).

### Hardware y entradas
- Webcam funcional; buena iluminación (cara, hombros, codos y manos visibles).
- Teclado y mouse (controles manuales opcionales).
- Mando compatible con los códigos de botón de Unity (añadidos códigos de PS4).
- GPU solo si se elige lectura/delegado GPU. En Windows el delegado por defecto de Pose es **CPU**.

### Archivos de modelos necesarios
En `Assets/StreamingAssets/`:
- `pose_landmarker_lite.bytes`, `pose_landmarker_full.bytes`, `pose_landmarker_heavy.bytes`
- `face_landmarker_v2_with_blendshapes.bytes`
- `hand_landmarker.bytes`

En `Assets/StreamingAssets/Avatars/`:
- `steve.gltf`, `alex.gltf`
- `steve_skin.png`, `alex_skin.png`
- `steve_skin_closed.png`, `alex_skin_closed.png`

Modelo de ejemplo: `Assets/StreamingAssets/Blockbench/table/table.gltf` con `oak_log_top.png`, `oak_planks.png`, `stripped_oak_log.png`.

## 4. Mapa de archivos

### Avatar y modelos (`Assets/VTuberCam/Samples/Props/`)
| Archivo | Función |
|---|---|
| `AvatarLoader.cs` | Carga Steve/Alex, aplica skins, busca huesos, conecta con el driver, gestiona skin de ojos cerrados |
| `GltfModelLoader.cs` | Interpreta el JSON glTF, buffers embebidos/`.bin`, crea mallas, materiales, UVs y jerarquía |
| `BlockbenchModelLoader.cs` | Formatos de Blockbench compatibles (`.json`, `.bbmodel`) |
| `CustomModelManager.cs` | Administra objetos 3D importados (posición, rotación, escala, visibilidad, texturas, fijaciones) |

### Tracking (`Assets/VTuberCam/Samples/Scenes/Pose Landmark Detection/`)
| Archivo | Función |
|---|---|
| `PoseLandmarkerRunner.cs` | Coordina Pose/Face/Hand, procesa la webcam, envía datos al avatar, calcula espejo, controla máscara visual |
| `PoseAvatarDriver.cs` | Landmarks → rotaciones; torso, cabeza, brazos; filtros; parpadeo; fallback manual; `Instance` |
| `VTuberSettingsUI.cs` | Interfaz nueva completa |

### Otros
- `Assets/VTuberCam.unity` — escena principal (contiene `AvatarLoader`, runner, cámara, UI y la instancia histórica del prefab `Main Canvas`).
- Anotaciones (en `Packages/com.github.homuler.mediapipe/Runtime/Scripts/Unity/Annotation/`): `PoseLandmarkListAnnotation.cs`, `PoseLandmarkListWithMaskAnnotation.cs`, `MultiPoseLandmarkListWithMaskAnnotation.cs`, `PoseLandmarkerResultAnnotationController.cs`.

## 5. Flujo de inicio

1. Unity abre `VTuberCam.unity`.
2. `VTuberSettingsUI` detecta referencias (cámara, avatar, loader, manager de modelos).
3. `PoseLandmarkerRunner` prepara los modelos MediaPipe.
4. Se inicia la webcam (`ImageSourceProvider`).
5. Se crean Pose, Face y Hand Landmarker.
6. La UI muestra la pantalla inicial (Steve o Alex).
7. El usuario elige avatar.
8. La UI **registra la petición** (no carga dentro de `OnGUI()`).
9. `Update()` ejecuta la carga real.
10. `AvatarLoader` destruye el avatar anterior (`Unload()`).
11. Se carga el glTF, materiales/texturas y huesos.
12. `PoseAvatarDriver.Bind(...)` conecta torso, cabeza y brazos.
13. Se activa la cámara virtual.
14. Se encuadra el avatar.
15. El runner empieza a alimentar el avatar (pose, manos, parpadeo).

## 6. Avatar: carga, glTF, jerarquía y skins

### 6.1 Selección Wide/Slim
- Steve = `Wide` (brazos anchos), Alex = `Slim` (brazos delgados).
- Se guarda en `PlayerPrefs` → `vtb_skinWide` (`1` Steve, `0` Alex).

```csharp
avatarLoader.gltfPath = wide ? "Avatars/steve.gltf" : "Avatars/alex.gltf";
avatarLoader.textureFolder = "Avatars";
avatarLoader.isWide = wide;
```

### 6.2 `AvatarLoader.Load()` paso a paso
1. **`Unload()`**: destruye el avatar anterior y libera GameObjects, materiales runtime, texturas abiertas/cerradas y recursos. Evita acumulación al cambiar de avatar.
2. **Ruta**: `Application.streamingAssetsPath` + `gltfPath`. Si el archivo no existe, falla con error explícito.
3. **Carga**:
   ```csharp
   GltfModelLoader.Load(fullPath, texFolder, "AvatarModel",
       flipV: true, buildHierarchy: true);
   ```
   - `flipV: true` → invierte V de las UV (glTF tiene V=0 arriba; Unity abajo). Sin esto, la skin sale invertida verticalmente.
   - `buildHierarchy: true` → reconstruye los nodos glTF como GameObjects, conserva los pivotes `Body`, `Head`, `Right Arm`, `Left Arm`; permite rotar los brazos desde el hombro y que las capas externas sigan a cabeza y brazos.
4. **Orientación**:
   ```csharp
   model.transform.localPosition = Vector3.zero;
   model.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
   model.transform.localScale = Vector3.one;
   ```
   La rotación de −90° adapta los modelos de Blockbench a la cámara frontal. Todos los objetos van a la **capa 6** (la que usa la cámara virtual).
5. **Huesos**: búsqueda por nombre normalizado (ignora espacios y mayúsculas): `Body`, `Head`, `Right Arm`, `Left Arm`. Fallbacks: `torso`, `Right`, `Left`.
6. **Driver**: si no hay `PoseAvatarDriver` válido, se busca en el modelo o se añade; luego `driver.Bind(torso, head, rightArm, leftArm)`. `PoseAvatarDriver.Instance` permite que el runner lo encuentre.
7. **Skin**: material cutout, filtro `Point`, wrap `Clamp`.
8. **Ojos cerrados**: se carga o genera la textura de parpadeo.

### 6.3 `GltfModelLoader`
- Acepta JSON `.gltf`, buffers Base64 embebidos y `.bin` externos junto al `.gltf`.
- Exige `buffers`, `bufferViews`, `accessors`, `meshes`, `nodes`; si falta alguno no carga.
- Un material por material glTF (nombres `gltf_mat_<indice>`); el índice se usa para texturas por material.
- Con `buildHierarchy`: crea GameObjects de nodos → aplica translation/rotation/scale → asigna mallas → conecta hijos por `children[]`.

### 6.4 Texturas del avatar
**Skin abierta**: predeterminada (`steve_skin.png`/`alex_skin.png`) o `.png` elegida por el usuario. Se carga con `Texture2D.LoadImage(...)` y se configura `RGBA32`, `FilterMode.Point`, `TextureWrapMode.Clamp`, `anisoLevel = 0`. El filtro `Point` conserva el pixel art.

**Material cutout**: evita cuadros negros en las zonas transparentes del atlas, no depende de iluminación compleja y respeta el alpha de las capas externas.

**Skin personalizada**: se elige desde la UI (Unity Editor); ruta en `vtb_customSkinPath`; si el archivo desaparece, se invalida. El cambio se ejecuta en `Update()`.

**Skin de ojos cerrados** — orden de preferencia:
1. skin cerrada personalizada (`vtb_customClosedSkinPath`);
2. skin cerrada predeterminada (`steve_skin_closed.png` / `alex_skin_closed.png`);
3. generación automática desde la abierta (pinta los píxeles de los ojos como línea cerrada).

## 7. Modelos 3D adicionales (`CustomModelManager.cs`)

La pestaña **Extras** permite importar modelos desde el Editor.

### Formatos
- `CustomModelManager.BuildInstance()` procesa **`.gltf`, `.json`, `.bbmodel`**.
- El selector muestra `.glb`, pero **no hay rama efectiva para `.glb`**. Formato validado: **`.gltf` exportado desde Blockbench**.

### Datos que guarda cada modelo (`AddModel(...)`)
Identificador estable, nombre, ruta, carpeta de texturas, textura global opcional, lista de texturas por material, destino de fijación, visibilidad, `flipV`, posición/rotación/escala locales y mundiales, posición por defecto.

### Fijación
1. **Suelto**: coordenadas mundiales, no acompaña al avatar.
2. **Avatar**: transformaciones relativas al avatar.
3. **Otro modelo**: se fija a su transform; pueden formarse cadenas.

El manager resuelve primero el objetivo y luego el dependiente (sin importar el orden de la lista). Los **ciclos se detectan y se cortan**. Si se elimina un modelo con dependientes, estos pasan al avatar o al mundo. Todos usan la **capa 6**.

### Texturas de modelos adicionales
- **Por material**: cada `gltf_mat_<indice>` puede recibir su `.png` (RGBA32, `Point`, wrap `Repeat`, color blanco, cutout si hay alpha).
- **General**: reemplaza la textura de todos los materiales (útil para un atlas único).
- Prioridad: primero por material, luego la general; la general sustituye todas las caras. Para volver a por-material hay que quitar la general.
- Al eliminar un objeto se destruyen mallas, materiales y texturas runtime (evita fugas).

### Persistencia
Lista JSON en `PlayerPrefs` → `vtb_customModels_v1`. Migración de `flipV`: `vtb_customModels_flipV_v2` (corrige modelos guardados sin la orientación vertical correcta).

## 8. Tracking: pose, torso, cabeza, brazos, manos

### 8.1 Configuración de Pose
- Modelo por defecto: BlazePose **Full** (opciones Lite/Full/Heavy).
- Modo `LIVE_STREAM`, 1 pose, confianzas 0.5/0.5/0.5, sin máscaras de segmentación.
- Lectura de imagen `CPUAsync`; delegado en Windows: CPU.

### 8.2 Landmarks importantes (33 en total)
| Landmark | Índice |
|---|---:|
| Nariz | 0 |
| Ojo izq. / der. | 2 / 5 |
| Oreja izq. / der. | 7 / 8 |
| Boca izq. / der. | 9 / 10 |
| Hombro izq. / der. | 11 / 12 |
| Codo izq. / der. | 13 / 14 |
| Muñeca izq. / der. | 15 / 16 |
| Cadera izq. / der. | 23 / 24 |

El runner envía `poseWorldLandmarks` (3D) y `poseLandmarks` normalizados (visibilidad y fallback 2D). Se copian 33 posiciones y 33 visibilidades; **visibilidad = 0 si X/Y < 0.03 o > 0.97** (evita extremidades inventadas fuera de cámara).

### 8.3 Validación anti-ruido
`minLandmarksVisible = 5`, `requiredConsecutiveFrames = 3`, `minVisibility = 0.5`. Un solo frame defectuoso no activa el avatar.

### 8.4 Torso
Línea entre hombros + vector a caderas (si ambas son confiables) + producto cruzado. Sin caderas confiables se usa `Vector3.up` y manda la línea de hombros.
`torsoWeight = 0.6`, `torsoYawLimit = 45°`, `torsoPitchLimit = 30°`, `torsoRollLimit = 0°`. Interpolado con suavizado.

### 8.5 Cabeza
Calculada con ojos, nariz, boca y (opcional) orejas.
- Giro horizontal: orientación de la línea de ojos/cara. `headSensitivity = 1.35`, `headYawGain = 0.7`, límite `75°`.
- Arriba/abajo: posición relativa de la nariz respecto a ojos y boca; **autocalibración ~15 frames** con cara de frente, quieta y poca variación vertical. Límite `45°`, `pitchDownGain = 2`, `pitchUpGain = 0.5`, `pitchDeadzone = 4°`, `invertPitch = false`.
- `useEarsForPitch`: mejor desactivado si cabello/gorra/luz ocultan las orejas.
- Suavizado: paso bajo + adaptativo + zona muerta; lento si estás quieto, rápido si te mueves. `headStillSmoothing = 5`, `headDeadzone = 1.5°`.

### 8.6 Brazos
Método principal: `dirección = muñeca − hombro`; el codo es fallback si la muñeca no es confiable.

**Confirmación de muñeca**: no controla todo el brazo de inmediato. Se verifica hombro → codo y muñeca → se cuentan frames consecutivos con muñeca visible → mientras tanto se usa el codo → tras `wristAcquireFrames = 2` se permite la muñeca → si desaparece, vuelve al codo o se desvanece.

**Dirección y profundidad**: X; Y invertida (MediaPipe → Unity); Z reducida por `depthScale = 0.6`. `armDirectionSmoothing = 0.18`, `armDeadzone = 2°`, `armSnapAngle = 50°` (pega el brazo "abajo" cuando el movimiento es pequeño, reduce vibración en reposo).

**Suavizado**: dirección + zona muerta + interpolación de rotación + velocidad adaptativa + fade in/out + retorno a reposo. `armSmoothing = 10`, `armSpeedBoost = 0.5`, `armFadeInSpeed = 3`, `armFadeOutSpeed = 2`, `returnToRestSpeed = 3`.

**Aplicación de la rotación**:
```csharp
Quaternion.FromToRotation(Vector3.down, dirInTorso) * restLocal
```
El brazo gira desde el pivote del hombro sin separarse del cuerpo.

**`handsOnlyArms`**:
- Desactivado (actual): Pose calcula hombro-codo-muñeca; Hand Landmarker confirma y ayuda.
- Activado: el brazo se alimenta sobre todo de Hand Landmarker (posición 2D de muñeca + profundidad por tamaño de palma).

**Profundidad por tamaño de mano**: se mide ancho/largo de palma, se guarda referencia inicial, mano más grande = más cerca, se combina con Z y se suaviza. `handDepthScale = 0.8`, `handDepthSmoothing = 0.18`. Es aproximada y **no reemplaza** a Pose.

### 8.7 Manos (Hand Landmarker)
- Carga `hand_landmarker.bytes`, `LIVE_STREAM`, máximo 2 manos, callback `OnHandLandmarkOutput`.
- Entrega 21 landmarks por mano, handedness, muñeca y escala de palma.
- El *score* de handedness **no** se usa como confianza: existir una lista válida = detección válida.
- **Callbacks antiguos**: se guardan timestamps de MediaPipe por mano. Se usa el fallback de muñeca de Pose si no hay callback reciente, falta timestamp o el timestamp tiene > ~120 ms.
- Los callbacks pueden correr fuera del hilo principal: `EnqueuePose`, `EnqueueBlink`, `EnqueueHand` solo copian datos protegidos por lock.

## 9. Espejo y lados izquierdo/derecho

### Las dos fuentes de espejo
- **Manual**: `VTuberSettingsUI._mirrorTracking` → botón rápido "Espejo: ON/OFF" → se copia a `PoseAvatarDriver.mirror`.
- **Automático**: el runner lee `imageSource.GetTransformationOptions().flipHorizontally` y llama `avatar.SetCameraMirror(mirrored)`. Con `autoMirrorFromCamera = true`, el driver usa ese estado para decidir `mirror`.

### Regla importante
`mirror` **intercambia lados anatómicos**. **No** se debe invertir X otra vez en `P()` ni en `PHead()`. Una segunda inversión produce brazo izquierdo moviendo el derecho, cabeza girada al lado contrario o espejo doble.

Conversión actual de puntos:
```text
Unity X = MediaPipe X
Unity Y = -MediaPipe Y
Unity Z = MediaPipe Z * depthScale
```
La imagen ya llega al detector con la transformación de cámara aplicada.

⚠️ **Posible discrepancia de versiones.** El resumen de la sesión de espejo/brazos (sección 21) describe `_swapLeftRight`, `_invertX` y `_swapBlink` en el runner, y `invertArmX` en el driver. El documento de requisitos más reciente describe en cambio `mirror` + `SetCameraMirror` y no menciona esas casillas. Es probable que el segundo sea una refactorización posterior. Antes de tocar nada, mira cuál de los dos esquemas existe realmente en tu `PoseLandmarkerRunner.cs` / `PoseAvatarDriver.cs`.

## 10. Parpadeo

- Modelo: `face_landmarker_v2_with_blendshapes.bytes`, sobre una **imagen propia** (el modelo de Pose puede liberar o reutilizar su imagen de entrada).
- Frecuencia: `_faceEveryNFrames = 2`.
- Blendshapes: índice `9` = `eyeBlinkLeft`, `10` = `eyeBlinkRight`; `blink = (left + right) / 2`.
- Histéresis: `blinkOn = 0.55` (cerrar), `blinkOff = 0.35` (abrir). El umbral automático ajusta la activación según el pico observado.
- Cambio de textura: cierre → `skinClosed`; apertura → `skinOpen`. Si se pierde el rostro durante `lostTimeout`, vuelve a `skinOpen`.
- UI: Expresiones → Parpadeo automático. Guardado en `vtb_blink`.

## 11. Pérdida de tracking, controles manuales, respiración

### Pérdida de tracking
`lostTimeout = 1.2 s`, `returnToRestSpeed = 3`, `hideAvatarWhenLost = false`, `hideDelay = 3 s`.
Sin poses: torso, cabeza y brazos vuelven a reposo; los ojos vuelven a abiertos si se perdió la cara. Con `hideAvatarWhenLost = true`: espera `lostTimeout`, luego `hideDelay`, y desactiva los renderers del avatar.

### Controles manuales (cuando no hay mano fiable)
`manualHandsEnabled = true`, `manualHandSensitivity = 0.8`, `manualActivationDelay = 0.25 s`, `mouseControlsHands = true`.

| Brazo | Subir | Bajar | Afuera | Adentro |
|---|---|---|---|---|
| Izquierdo | `T` | `G` | `F` | `H` |
| Derecho | `I` | `K` | `J` | `L` |

- Mouse: clic izquierdo + mover = brazo izquierdo; clic derecho + mover = brazo derecho.
- Mando (códigos de Unity; dependen del dispositivo): D-pad 13/14/15/16 = arriba/abajo/izq./der.; brazo derecho: 3 arriba, 0 abajo, 5 afuera, 4 profundidad; brazo izquierdo: D-pad para X/Y y botón 4 para profundidad.

### Respiración
`breathingEnabled = true`, `breathAmplitudePixels = 0.001`, `breathPeriod = 4 s`. Mueve levemente los brazos desde el hombro (sin desplazarlo), se reduce mientras el brazo sigue una mano y se aplica después del tracking sin acumular rotación.

## 12. Interfaz (IMGUI, `VTuberSettingsUI.cs`)

Ventana modal arrastrable. **No** debe sustituirse por el prefab antiguo `Main Canvas`.

**Pantalla inicial**: nombre, selector Steve/Wide y Alex/Slim, botón `INICIAR VTUBERCAM`, aviso si falta `AvatarLoader`.

**Atajos**: `Tab` abre/cierra ajustes · `F1` modo limpio (oculta la UI, no detiene el tracking) · `R` recalibrar.

**Vista** (con el cursor fuera de la UI): rueda = zoom; botón derecho + arrastrar = mover; botón central o `Shift` + izquierdo + arrastrar = rotar (solo horizontal, −180° a 180°).

| Pestaña | Contenido |
|---|---|
| **General** | Cámara física (nombre, cambiar), cámara virtual (UnityCapture, `vtb_vcam`, carpeta del instalador, instrucciones OBS/Discord/Zoom), fondo (gris oscuro ≈ 0.15, 0.15, 0.20; para chroma usar clave de color en OBS), HUD de debug, centrar/restaurar vista |
| **Avatar** | Steve/Alex, recargar; skin `.png` (elegir, ver nombre, quitar); escala 0.3–5.0, rotación Y, mover X/Y (−3 a 3); brazos (visibilidad mínima, ángulo de pegado, suavizado, zona muerta, respiración) |
| **Expresiones** | Parpadeo automático, skin de ojos cerrados (elegir/quitar/auto); cabeza (sensibilidad, giro, arriba/abajo, zona muerta) |
| **Extras** | Importar `.gltf` (el selector muestra `.glb` sin soporte real), mesa de ejemplo, expandir/contraer, mostrar/ocultar, fijar a mundo/avatar/otro modelo, posición/rotación XYZ, escala, texturas por material y general, `flipV`, recargar, resetear, eliminar |

### Acciones diferidas (regla clave)
Los botones **no** cargan modelos ni texturas dentro de `OnGUI()`. Solo ponen banderas (`_pendingAvatarLoad`, `_pendingSkinApply`) y `Update()` ejecuta la operación. Así se evita `Invalid GUILayout state` cuando una carga lanza excepción o modifica objetos durante el dibujado.

## 13. Persistencia (PlayerPrefs)

| Clave | Uso |
|---|---|
| `vtb_skinWide` | Steve/Wide (1) o Alex/Slim (0) |
| `vtb_blink` | Parpadeo activo |
| `vtb_customSkinPath` | Ruta de skin abierta |
| `vtb_customClosedSkinPath` | Ruta de skin cerrada |
| `vtb_vcam` | Cámara virtual |
| `vtb_headSens` | Sensibilidad de cabeza |
| `vtb_customModels_v1` | Lista JSON de modelos |
| `vtb_customModels_flipV_v2` | Migración de flip V |
| `vtb_scale`, `vtb_panX`, `vtb_panY`, `vtb_rotY` | Escala, desplazamiento y rotación de la vista |

**Comportamiento especial:** al iniciar, `VTuberSettingsUI` elimina `vtb_mirrorTrackingV2`, `vtb_scale`, `vtb_panX`, `vtb_panY` y `vtb_rotY`. Resultado: el espejo arranca **apagado** y la vista no siempre se conserva entre ejecuciones (aunque se vuelve a guardar durante la sesión). Skins, modelo, parpadeo, cámara virtual y modelos adicionales sí persisten.

## 14. Cámara virtual, render, hilos y orden por frame

### Cámara virtual
Al elegir avatar: se crea/detecta `UnityCapture` → se desactivan sus advertencias → `LinearResize` → se habilita → fondo gris oscuro → avatar delante de la cámara → `FrameAvatar()` calcula la distancia con los bounds reales de los renderers (altura visible, FOV, centrado).

### Orden por frame
1. La webcam produce una textura.
2. `TextureFramePool` obtiene un frame.
3. Se copia en CPU / CPUAsync / GPU según configuración.
4. Imágenes separadas para Pose, Face y Hand.
5. Pose analiza el cuerpo.
6. Face analiza el parpadeo cada N frames.
7. Hand analiza hasta dos manos.
8. Los callbacks copian a buffers seguros.
9. `Update()` del driver consume los buffers.
10. Torso y cabeza.
11. Brazos por Pose o manos.
12. Entrada manual.
13. Suavizado y retorno a reposo.
14. Textura de ojos.
15. Respiración.
16. Anotación visual con la máscara.
17. La cámara virtual renderiza.

### Seguridad de hilos
Desde los callbacks de MediaPipe solo se permite: copiar datos, actualizar buffers, guardar timestamps y llamar `Enqueue...`. En el hilo principal: rotaciones, materiales, texturas, `Time.time`, renderers, transformaciones. **No** usar `Time.time` ni `Time.realtimeSinceStartup` dentro de callbacks.

### Interfaz antigua vs nueva
Nueva: `VTuberSettingsUI.cs`. Antigua: prefab `Assets/VTuberCam/Samples/UI/Objects/Main Canvas.prefab`. La escena aún contiene una instancia del prefab antiguo (por GUID). Al restaurar la escena, comprobar que el objeto raíz `Main Canvas` esté **desactivado**.

## 15. Trazo visual oculto en antebrazos y manos

La invisibilidad es **solo visual**: no elimina landmarks ni detiene el tracking.

`PoseLandmarkListAnnotation.BodyParts` separa `Face`, `LeftArm`, `LeftHand`, `RightArm`, `RightHand`, `LowerBody`.

Máscara inicial:
```csharp
BodyParts.All & ~BodyParts.LeftHand & ~BodyParts.RightHand
```
- Brazos de Pose visibles; manos de Pose ocultas; el avatar sigue recibiendo todos los datos.

**Aparición de manos**: `HandLandmarker` confirma la mano; el runner cuenta detecciones consecutivas por lado (`HandAnnotationAcquireFrames = 2`) y solo entonces añade `LeftHand`/`RightHand` a la máscara. Al perderla, el contador vuelve a 0 y se oculta. Para tener manos siempre invisibles basta con no añadirlas a la máscara; **no** hace falta tocar el tracking.

El fallback 2D de muñeca de Pose puede seguir moviendo el avatar aunque Hand Landmarker no entregue nada; ese fallback **no** debe activar el trazo visible.

**Propagación (no romper)**:
1. `PoseLandmarkerRunner` elige la máscara →
2. `PoseLandmarkerResultAnnotationController` la recibe →
3. `MultiPoseLandmarkListWithMaskAnnotation` la propaga →
4. `PoseLandmarkListWithMaskAnnotation` la conserva →
5. `PoseLandmarkListAnnotation` oculta conexiones y puntos.

Si solo se modifica el runner sin conservar `SetBodyPartsMask(...)` y su propagación, el trazo reaparece en el siguiente frame.

---

# PARTE B — Historial técnico

## 16. Checkboxes de `VTuberSettingsUI.cs`
- **Problema:** los `GUILayout.Toggle` no se veían bien.
- **Causas:** la textura de la caja se dibujaba como punto (esquinas mal calculadas), el estilo no definía los estados "marcado" y la textura se estiraba al ancho de la línea.
- **Solución:** helper propio `Checkbox(bool, string)`, textura corregida, reemplazo de los 4 toggles; después se corrigió la marca de check (salía con el vértice hacia arriba).

## 17. Texturas negras o mal puestas

**Primera hipótesis (incompleta):** las capas externas de la skin (sombrero, chaqueta, mangas, pantalones) tienen alpha 0 y un material opaco las pinta de negro → material Cutout.

**Archivos analizados** (`steve.gltf`, `alex.gltf`, `steve_1.gltf`): 12 mallas (6 partes con capa externa), UVs idénticas al layout estándar de skin 64×64. Los PNG subidos eran de **paleta indexada**; se convirtieron a RGBA y se renombraron `steve_skin.png` y `alex_skin.png`.

**`SkinUVFixer.cs` + `skin_uv_layout.json`**: corrige/regenera UVs desde un layout estándar; verificado contra los `.gltf` (error máximo 0.0156 px). Al final **no es necesario** si se aplica el plan.

**Causa real (se necesitaban las dos correcciones juntas):**
1. `GltfModelLoader.ReadAccessorUV` no volteaba V → solución: `flipV` (hoy `true`).
2. El material opaco (`Unlit/Texture` ignora alpha) → solución: material cutout.

Una simulación de render con V sin voltear reprodujo casi exacto la captura del usuario; con V sin voltear, solo cutout no arreglaba nada.

## 18. Revisión del `Assets.zip`
(El repo de GitHub no se pudo leer por bloqueo de acceso automático; se revisó el zip.)

| Hallazgo | Detalle |
|---|---|
| V sin voltear | Causa principal de textura mal puesta |
| Material | `AvatarLoader` usaba `Unlit/Texture` (ignora alpha) |
| Jerarquía plana | El loader ignoraba los `translation` de los nodos y colgaba todo del origen, que queda en los **pies**; el driver rota con `localRotation`, así que cabeza y brazos giraban alrededor de los pies |
| Capas externas | No seguían a las partes que se mueven |
| Driver creado en runtime | `AddComponent` en `Load()`: su `Awake` corría antes de asignar piezas; brazos sin emparentar al torso; materiales copiados que luego se reemplazaban; `skinOpen`/`skinClosed` vacíos (el cambio al parpadear no funcionaba) |
| Cambio Steve/Alex | `Load()` no llamaba a `Unload()` → avatares duplicados; texturas/materiales/mallas sin liberar |
| `MinecraftTable` | Usa el mismo loader: cualquier cambio debe ser opcional para no afectar la mesa |
| Espejado de texturas | El loader no convierte ejes glTF → Unity (no verificado en pantalla) |

## 19. Comparación con el análisis de Gemini
Gemini acertó en los 4 puntos principales (V invertida, shader opaco, capas flotando, falta de `Unload`). Huecos señalados:
- Su arreglo de `Unload()` dejaba el avatar nuevo asociado a un driver que se destruye al final del frame.
- El reemplazo de shaders puede caer a uno opaco en builds.
- No trataba los pivotes.

Errores propios corregidos en el chat: se había descartado un problema de UVs; se dijo que los pivotes estaban en la cadera (están en los **pies**); se sospechó de URP sin evidencia; se creyó que `skinOpen` pisaba la skin.

## 20. Plan de implementación por fases (Claude Doc)
0. **Preparación:** commit, revisar render pipeline, no copiar el `AvatarLoader.cs` entregado antes (sin `SetCustomSkin`).
1. **Textura:** `flipV` opcional, material cutout como asset en `Resources`, textura RGBA32 sin mipmaps con `Clamp`.
2. **Pivotes:** reconstruir la jerarquía con `buildHierarchy` opcional; búsqueda de huesos por nombre exacto.
3. **Ciclo de vida:** `Unload()` con `driver = null`, liberar recursos, `PoseAvatarDriver.Bind(...)`.
4. **Verificación.**
5. **Build y limpieza.**
6. **Opcionales:** espejado, skins 64×32, parpadeo con skin generada, glTFast.

> Según el documento de requisitos más reciente, las fases 1–3 ya están reflejadas en el código (`flipV`, `buildHierarchy`, `Unload()`, `Bind(...)`, material cutout).

## 21. Espejo y brazos (`PoseLandmarkerRunner.cs` y `PoseAvatarDriver.cs`)

**1) Lados invertidos.** Síntoma: levantar el brazo derecho en cámara levantaba el izquierdo del avatar, y abrir hacia afuera lo metía hacia adentro. Causa: la imagen llega espejada (`flipHorizontally`), MediaPipe interpreta la derecha real como "izquierda" y X queda invertido.
- Runner: tabla `_mirrorIndex` (intercambia cada landmark izq./der.), casillas `_swapLeftRight`, `_invertX`, `_swapBlink` (todas ON por defecto); `SendToAvatar` usa el índice de destino intercambiado e invierte X si procede; el parpadeo también intercambia ojos.

**2) Dirección horizontal de brazos.** Cabeza y torso asumen imagen sin espejo, pero los brazos del avatar están dispuestos como espejo. Driver: casilla `invertArmX` (ON) que invierte solo la dirección horizontal de brazos. El campo `mirror` del driver debe quedar **desmarcado** porque el runner ya corrige los lados (en ese esquema).

**3) Brazos más suaves.** `armFluidity` (0–1, defecto 0.7): filtro en cascada de tres etapas que elimina el efecto "escalón" entre datos de cámara; a cambio, algo más de retraso.
- Más suave: `armFluidity` = 1 y/o `armSmoothing` 6–7.
- Si tiembla quieto: `armDeadzone` = 3.
- Si va con retraso en saludos: `armSpeedBoost` = 0.8 o `armFluidity` = 0.4–0.5.

## 22. Salto brusco "brazo arriba" ↔ "mano hacia la cámara"
- **Causa probable:** `Quaternion.FromToRotation` es inestable cuando el brazo apunta casi recto arriba (≈180° respecto a "abajo"): el eje de giro cambia de lado y el brazo da un latigazo. El suavizado no corrige un salto que ya viene en el dato.
- **Solución:** `StableSwing`: cerca de "recto arriba" calcula el giro pasando por una dirección intermedia (hacia la cámara) y lo mezcla gradualmente con el cálculo normal. Casilla `armMaxDegPerSec` (900°/s, 0 = sin límite).
- **Estado:** *no probado en Unity desde el chat*. Si el salto persiste, anotar en qué momento ocurre y si el brazo gira sobre sí mismo o cambia de lado.

---

# PARTE C — Modelos, herramientas y tracking

## 23. Modelos JSON para Blockbench
Modelos de bloque de Minecraft Java. Se abren en Blockbench con **File → Open Model**; sirven en un resource pack en `assets/minecraft/models/block/`. Las texturas se cambian en el campo `textures` al inicio del archivo.

| Archivo | Descripción |
|---|---|
| `mesa.json` | Tablero 16×4×16 + cuatro patas 2×12×2, tablones de roble |
| `mesa_ancha.json` | 2 bloques de ancho (de −8 a 24 en X), tablero 32×4×16; puede verse cortada según el ángulo de cámara |
| `mouse.json` | Primer mouse (6×4×10 px), demasiado grande |
| `mouse_pequeno.json` | Reescalado (≈2.5×1.5×4 px): botones, rueda, cable |
| `teclado.json` | 16×~2×8 px, 50 elementos: base, 4 filas de 11 teclas, barra espaciadora, cable |
| `control_ps4.json` | 8×2×5 px, 20 elementos: agarres, L1/R1, touchpad, barra de luz, sticks, cruceta, botones de colores |

Notas: mouse, teclado y control están centrados en el bloque y apoyados en Y=0 (subirlos a Y=16 para ponerlos sobre la mesa). El control no lleva símbolos por la escala; se pueden pintar con textura propia.

## 24. Atlas de texturas en Blockbench (varias texturas → un PNG)
**Punto de partida:** mesa de 5 elementos (`tablero`, `pata_noroeste`, `pata_noreste`, `pata_suroeste`, `pata_sureste`) con `oak_log_top.png`, `oak_planks.png`, `stripped_oak_log.png` (16×16 cada una).

1. **Create Texture** (panel Textures):
   | Opción | Valor |
   |---|---|
   | Name | `mesa` |
   | Type | **Texture Template** |
   | Pixel Density | **16x** |
   | Rearrange UV | Marcado |
   | Box UV | Desmarcado (permite mover caras libremente) |
   | Power-of-2 Size | Marcado |
   | Keep Multi Texture Occupancy | Marcado |
   | Padding | Opcional (si los bordes "sangran") |
2. **Llenar la textura**: en Paint, copiar (Ctrl+C) y pegar (Ctrl+V) las texturas viejas en las zonas UV correspondientes; o buscar una opción tipo *Merge/Combine Textures* si existe en tu versión.
3. **File → Project**: poner el tamaño de textura igual al de la nueva imagen (p. ej. 32×32).
4. **Aplicar a todos**: Ctrl+A en el Outliner → clic derecho sobre la textura → *Apply to all elements*; borrar las viejas.
5. **Exportar**: clic derecho → Save as/Export → PNG.

Consejos: tamaños potencia de 2; si las caras quedan mal, UV → Auto UV o Maximizar UV; si siguen asignadas las texturas viejas, la nueva no se verá hasta aplicarla a todos los elementos.
*Estado en el chat:* el usuario estaba en la ventana Create Texture; siguiente paso: confirmar, pasar la madera a la nueva imagen y aplicarla a todos los cubos.

## 25. MediaPipe Unity Plugin (v0.16.3): qué descargar
- Descargar **`MediaPipeUnity.0.16.3.unitypackage`** (plugin + ejemplos + modelos). Importar con **Assets → Import Package → Custom Package**.
- **No** hacen falta `MediaPipeUnityPlugin-all.zip` ni `-all-stripped.zip` (componentes compilados para armar el paquete desde cero).
- El `.tgz` es el plugin para Package Manager, sin ejemplos ni modelos.
- Escena **Pose Landmark Detection**: 33 puntos; hombros 11/12, codos 13/14, muñecas 15/16.

## 26. Mejorar el tracking
### Reducir datos "fantasma" (MediaPipe)
- Subir `minDetectionConfidence` y `minTrackingConfidence` (≈ 0.7+).
- Limitar `numHands`.
- Filtro **One Euro** sobre posiciones.
- Ignorar frames sin detección en vez de reutilizar el último valor.

### Alternativas de código abierto mencionadas
- **OpenSeeFace**: cara y ojos, estable, CPU.
- **Kalidokit**: librería JS que convierte puntos MediaPipe en rotaciones filtradas.
- **XR Animator**: captura por webcam con filtrado incluido.
- **Ultraleap**: hardware dedicado de manos; más preciso pero ni abierto ni gratuito.

### ¿Un sistema propio más preciso que MediaPipe?
No es realista superarlo en general. De menor a mayor esfuerzo: (1) filtrado One Euro y estabilización; (2) modelos separados para cara y manos; (3) probar otros modelos abiertos; (4) fine-tuning con datos propios; (5) mejor hardware (segunda cámara/profundidad); (6) entrenar desde cero (no recomendado).
**Antes de decidir:** grabar un video de prueba y medir temblor, pérdidas de tracking, latencia y error. Se sugirió una interfaz `IPoseSource` entre el detector y el driver (útil si algún día cambias de tracker).

## 27. OpenSeeFace (`Binary.zip`, el tracker de VSeeFace)
- Es el `facetracker.exe` de OpenSeeFace (Python 3.7, onnxruntime, binario de 2021). **No se ejecutó**; análisis por código.
- **Faltan los modelos `.onnx`** (carpeta `models`): no arrancaría tal cual.
- Envía datos por UDP a `127.0.0.1:11573`. Su `eye_blink` es **1 con el ojo abierto** (al revés que `eyeBlink` de MediaPipe).
- El parpadeo actual del proyecto viene de MediaPipe (blendshapes 9 y 10, cada 2 frames, promedio de ambos ojos).
- No se puede afirmar cuál es más preciso sin medir; para ojos abiertos/cerrados en un avatar de Minecraft probablemente pesan más latencia y umbrales.
- Integrarlo implica: resolver el conflicto de cámara (`--raw-rgb` o cámara virtual), invertir valores, lanzar un proceso externo y sumar ~125 MB.
- **Recomendación:** no cambiar solo por el parpadeo hasta comparar con el mismo video (`-c video.mp4` y `--log-data`).

---

# PARTE D — Alternativas sin Unity

> Investigué rápidamente el estado de herramientas de VTubing y de cámara virtual en Python (fuentes al final de esta parte). Los detalles de versiones cambian rápido: verifica en el sitio oficial antes de instalar.

## 28. Qué hace realmente la app (para decidir con criterio)

Quitando el envoltorio, VTuberCam hace solo cinco cosas:

1. **Capturar webcam.**
2. **Detectar** pose, manos y cara (MediaPipe) → es una librería, no depende de Unity.
3. **Convertir** landmarks en rotaciones de **4 huesos** (Body, Head, Right Arm, Left Arm) + un cambio de textura (parpadeo).
4. **Dibujar** un modelo hecho de cubos con texturas pixel-art, más algunos props.
5. **Sacar el resultado como cámara virtual** (o como fuente en OBS).

Eso es un caso de uso **muy pequeño** para un motor como Unity (editor de varios GB, builds pesados, necesidad de driver externo UnityCapture, compilación, `.meta`, etc.). La parte difícil de verdad (los modelos de detección) la hace MediaPipe, que existe para web, Python, C++ y móvil. Lo que tienes escrito en C# que vale oro es la **lógica** (filtros, umbrales, histéresis, anti-ruido, calibración de cabeza, StableSwing) y eso se porta casi línea por línea.

Además, varios dolores del proyecto en Unity (pivotes en los pies, V invertida, `buildHierarchy`, rotación de −90°, `Unload()` de texturas, hilos de callbacks) **desaparecen o se simplifican** en motores que leen glTF de forma nativa (three.js, Godot).

## 29. Opciones comparadas

| Opción | Lenguaje | Peso / instalación | Dificultad (para ti) | Cámara virtual | Soporta tus `.gltf` | Pierdes / ganas |
|---|---|---|---|---|---|---|
| **A. Software existente** (VSeeFace, Warudo, VMagicMirror, VTube Studio) | Ninguno | Instalar y listo | Muy baja | Sí (según programa) | Necesita convertir a VRM | Poca personalización; modelo Minecraft como VRM; no es "tu" proyecto |
| **B. Web: three.js + MediaPipe Tasks (JS/TS)** | JavaScript/TypeScript | Una carpeta + navegador | Media-baja | Vía OBS (Browser Source / Virtual Cam) | **Sí, nativo** (`.gltf` y `.glb`) | Cero install, hot-reload, ligero; la cámara virtual depende de OBS |
| **C. Python: tracker (MediaPipe + OpenCV) → render** | Python | `pip install` | Media | `pyvirtualcam` directo | Solo con librerías 3D extra | Excelente para experimentar y filtrar; renderizar 3D bonito en Python es lo más incómodo |
| **D. Godot 4** | GDScript / C# | Editor ~100–200 MB, portable | Media | Capturando ventana con OBS | **Sí, nativo** (importa y carga en runtime) | Ligero, open source; no hay plugin oficial MediaPipe → tracker externo por UDP/WebSocket |
| **E. Híbrido: tracker en Python + render web/Godot** | Python + JS/GDScript | Medio | Media-alta | OBS | Sí | Máxima flexibilidad (puedes probar OpenSeeFace, One Euro, etc.) a costa de dos procesos |
| **F. Seguir en Unity pero aligerado** | C# | El que ya tienes | Baja (ya lo conoces) | UnityCapture | Sí (con tu loader) | No migras, pero sigues pagando el peso del motor |
| G. Unreal / Bevy / C++ nativo | C++/Rust | Pesado o difícil | Alta | Variable | Sí | **No lo recomiendo** para este tamaño de proyecto |

## 30. Detalle de cada opción

### A. Programas ya hechos
- **VSeeFace**: gratis, seguimiento de cara y manos para avatares VRM/VSFAvatar; usa OpenSeeFace para la webcam. Según las fuentes que revisé, **solo lee VRM 0.x**.
- **Warudo** (Steam): gratis para uso personal; soporta VRM 0.x y 1.0; según una fuente de septiembre de 2026 seguía en desarrollo activo (versión 0.15.0 en junio de 2026). Tiene sistema de "blueprints" para interacciones. Los términos comerciales no están claros en la tienda.
- **VMagicMirror** (gratis, Windows) y **VTube Studio** (2D/Live2D, y 3D vía VRM).
- **Cómo meter un Steve/Alex**: habría que convertir tu modelo a **VRM** (típicamente Blender + add-on de VRM; esto es lo que sé, *no lo verifiqué ahora*). Se pierden: skin swap propio al parpadear (se resolvería con blendshapes/expresiones), props con fijaciones y toda la lógica custom.
- **Cuándo elegirla:** si el objetivo es *usar* un VTuber ya, no *programar* uno. Dado que estás en una carrera de ingeniería de software y esto es proyecto de aprendizaje, probablemente quieras seguir programando.

### B. Web (three.js + MediaPipe Tasks Vision)  ← mi favorita para "más ligero y más fácil"
- **MediaPipe Tasks Vision** para JavaScript expone `PoseLandmarker`, `HandLandmarker` y `FaceLandmarker` con **los mismos modelos** (`.task`) que usas en Unity, con delegado GPU en navegador.
- **three.js** carga `.gltf`/`.glb` con `GLTFLoader` respetando la **jerarquía y pivotes de los nodos** (adiós `buildHierarchy`, adiós pivotes en los pies), `NearestFilter` para el pixel art, y `alphaTest` para las capas externas.
- **Salida**: como página local. En OBS se agrega como **Browser Source** (admite fondo transparente, sin chroma ni driver). Para Discord/Zoom se activa la **Cámara virtual de OBS** (viene integrada en OBS) — o capturas con otras herramientas. Cuenta con que dependes de OBS.
- **Ventajas**: sin instalar motor, recarga instantánea, `.glb` soportado "gratis" (lo que hoy es una limitación en tu manager), skins por drag & drop (canvas), fácil de compartir, depuración con DevTools.
- **Desventajas**: el navegador pide permiso de cámara; rendimiento de WebGL depende de la GPU/driver; si quieres un `.exe` hay que envolverlo (Electron o Tauri); cámara virtual propia sin OBS no es trivial.
- **Kalidokit**: librería que convierte landmarks MediaPipe en rotaciones ya filtradas; útil para empezar rápido, aunque tu lógica propia (anti-ruido, StableSwing, calibración) ya es más específica.

### C. Python
- **Tracker**: `mediapipe` (Python) + `opencv-python`. Fácil de prototipar, notebooks, gráficas de temblor/latencia, One Euro, comparación de blink con OpenSeeFace (que ya es Python).
- **Cámara virtual**: **`pyvirtualcam`** envía frames a una cámara virtual. En Windows usa la cámara virtual de OBS (integrada desde OBS 26; desde pyvirtualcam 0.10 se requiere OBS 28 o superior) o **Unity Capture** (que además admite RGBA con transparencia, justo la que ya usas). Ojo: OBS expone **una sola instancia** de su cámara virtual, así que no puedes enviar a ella desde Python, mezclar en OBS y volver a sacarla por la misma cámara; en ese caso usa Unity Capture desde Python.
- **Render 3D**: aquí está el punto débil. Opciones: `pyrender`, `moderngl`, Panda3D, Ursina, Open3D. Funcionan, pero **más trabajo** que three.js/Godot para cargar un glTF con jerarquía y alpha.
- **Cuándo elegirla:** si prefieres Python y aceptas un render simple, o si usas Python solo como **tracker** (opción E).

### D. Godot 4
- Motor pequeño y libre. Importa glTF/GLB de forma nativa y también puede **cargarlo en runtime** desde archivo (equivalente a tu `GltfModelLoader`, pero ya hecho).
- GDScript es fácil (parecido a Python) y C# también es posible.
- **Problema**: no hay un plugin oficial y estable de MediaPipe equivalente al de Unity → usarías un **tracker externo** (Python o web) que envía landmarks por **UDP/WebSocket/OSC** a Godot.
- Cámara virtual: capturar la ventana desde OBS (Window/Game Capture) y usar su cámara virtual.
- **Cuándo elegirla:** si quieres un ejecutable independiente ligero y control total del render, y no te importa tener dos procesos.

### E. Híbrido (tracker aparte, render aparte)
- Separa el **detector** del **avatar** con un protocolo simple (JSON por UDP/WebSocket: 33 landmarks + visibilidad + blink + manos). Es exactamente la idea de tu `IPoseSource`.
- Ventajas: cambias de tracker (MediaPipe ↔ OpenSeeFace ↔ otro) sin tocar el render; puedes grabar y reproducir datos para medir; el render puede ser web, Godot o incluso Unity.
- Desventajas: más piezas, latencia extra mínima, más cosas que arrancar.

### F. Quedarte en Unity pero aligerar
- Usar el modelo **Pose Lite** si el rendimiento es limitado; analizar la cara cada N frames (ya lo haces cada 2).
- Quitar del proyecto lo que no usas (samples del plugin, prefab antiguo `Main Canvas`, paquetes) y compilar con *stripping*.
- Considerar reemplazar UnityCapture por **Spout/NDI** hacia OBS si el driver te da problemas.
- Cerrar primero los pendientes (sección 37) antes de pensar en migrar.

### Fuentes consultadas para esta parte
- pyvirtualcam (PyPI y GitHub): soporte Windows/macOS/Linux, OBS virtual cam y Unity Capture, nota de una sola instancia de OBS, requisito OBS 28+ desde 0.10.
- Artículos de 2026 sobre software VTuber (VSeeFace, Warudo, VMagicMirror, VTube Studio) y compatibilidad VRM 0.x/1.0.
- El resto (three.js, MediaPipe Tasks JS, Godot) proviene de mi conocimiento general y **no fue verificado en esta sesión**.

## 31. Recomendación y ruta de migración

**Mi recomendación (con matices):**

1. **Corto plazo:** termina de estabilizar lo que tienes en Unity. Según el documento de requisitos, gran parte del plan (flipV, pivotes, `Unload()`, cutout) ya está aplicado. Falta, sobre todo, **validar** (sección 35) y decidir sobre el salto de brazo (sección 22) y el espejo (sección 9).
2. **Si lo que buscas es "más ligero y más fácil":** haz un **prototipo web (opción B)** en un fin de semana: webcam → MediaPipe JS → three.js con tu `steve.gltf`. Si te gusta, migras; si no, no has perdido nada porque Unity sigue funcionando.
3. **Si quieres máxima flexibilidad de tracking:** opción **E** (tracker aparte) más adelante.
4. **No recomiendo** Unreal, C++ nativo ni Rust/Bevy para esto.

**Qué se porta 1:1 a cualquier otra plataforma** (copiar valores):
- Todos los parámetros de la sección 38 (suavizados, zonas muertas, histéresis de parpadeo, `wristAcquireFrames`, etc.).
- La conversión de ejes: `X = x`, `Y = −y`, `Z = z * depthScale`.
- La regla de espejo: **una sola** inversión de lados.
- La validación anti-ruido (≥5 landmarks visibles, 3 frames consecutivos, visibilidad ≥ 0.5, descartar puntos a < 0.03 del borde).
- La calibración de cabeza (~15 frames de cara frontal quieta).
- `StableSwing` y el límite de grados por segundo.
- La skin con ojos cerrados (misma imagen).

**Equivalencias Unity → Web:**

| Unity | Web (three.js) |
|---|---|
| `GltfModelLoader` + `buildHierarchy` | `GLTFLoader` (jerarquía nativa) |
| `flipV: true` | No hace falta (GLTFLoader maneja `flipY`) |
| Rotación base `Euler(0,-90,0)` | Probablemente innecesaria; verificar visualmente |
| Material cutout + `Point` | `alphaTest: 0.5` + `NearestFilter` |
| `PoseAvatarDriver.Bind(...)` | Buscar nodos por nombre con `scene.getObjectByName(...)` |
| `Quaternion.FromToRotation` | `quaternion.setFromUnitVectors(a, b)` |
| `PlayerPrefs` | `localStorage` |
| IMGUI | HTML + `lil-gui`/`tweakpane`, o HTML propio |
| UnityCapture | OBS Browser Source / OBS Virtual Camera |
| Capa 6 / cámara virtual | Canvas con fondo transparente |
| Hilos y callbacks | Un solo hilo en JS; sin `lock` ni `Enqueue` |

## 32. Esqueleto de la versión web (JS) — punto de partida, **no probado**

> Es un esquema mínimo para ver la forma del proyecto. Fija versiones exactas de `three` y `@mediapipe/tasks-vision` en tu `package.json` en vez de `latest`, y descarga los modelos `.task` desde la documentación oficial de MediaPipe.

Estructura sugerida:
```text
vtubercam-web/
  index.html
  main.js
  avatars/steve.gltf  steve_skin.png  steve_skin_closed.png
  models/pose_landmarker_full.task  face_landmarker.task  hand_landmarker.task
```

`main.js` (esquema):
```js
import * as THREE from "three";
import { GLTFLoader } from "three/addons/loaders/GLTFLoader.js";
import { FilesetResolver, PoseLandmarker, FaceLandmarker, HandLandmarker }
  from "@mediapipe/tasks-vision";

// 1) Webcam
const video = document.createElement("video");
video.srcObject = await navigator.mediaDevices.getUserMedia({ video: true });
await video.play();

// 2) MediaPipe (mismos modelos que en Unity)
const vision = await FilesetResolver.forVisionTasks(
  "https://cdn.jsdelivr.net/npm/@mediapipe/tasks-vision@<VERSION>/wasm");
const pose = await PoseLandmarker.createFromOptions(vision, {
  baseOptions: { modelAssetPath: "models/pose_landmarker_full.task", delegate: "GPU" },
  runningMode: "VIDEO", numPoses: 1,
});
const face = await FaceLandmarker.createFromOptions(vision, {
  baseOptions: { modelAssetPath: "models/face_landmarker.task", delegate: "GPU" },
  runningMode: "VIDEO", outputFaceBlendshapes: true,
});

// 3) three.js con fondo transparente (para OBS Browser Source)
const renderer = new THREE.WebGLRenderer({ alpha: true, antialias: false });
document.body.appendChild(renderer.domElement);
const scene = new THREE.Scene();
const camera = new THREE.PerspectiveCamera(30, 1, 0.1, 100);

// 4) Avatar: pivotes y jerarquía vienen del glTF
const gltf = await new GLTFLoader().loadAsync("avatars/steve.gltf");
scene.add(gltf.scene);
const body  = gltf.scene.getObjectByName("Body");
const head  = gltf.scene.getObjectByName("Head");
const armR  = gltf.scene.getObjectByName("Right Arm");
const armL  = gltf.scene.getObjectByName("Left Arm");

// Pixel art nítido + capas externas con alpha
gltf.scene.traverse(o => {
  if (o.isMesh) {
    o.material.map.magFilter = THREE.NearestFilter;
    o.material.map.minFilter = THREE.NearestFilter;
    o.material.alphaTest = 0.5;
    o.material.transparent = false;
  }
});

// 5) Bucle
const DOWN = new THREE.Vector3(0, -1, 0);
function tick(t) {
  const p = pose.detectForVideo(video, t);
  if (p.worldLandmarks?.[0]) {
    const L = p.worldLandmarks[0];
    // misma conversión de ejes que en Unity: X = x, Y = -y, Z = z * depthScale
    const v = (i, depth = 0.6) => new THREE.Vector3(L[i].x, -L[i].y, L[i].z * depth);
    const dirR = v(16).sub(v(12)).normalize();     // muñeca - hombro
    armR.quaternion.slerp(new THREE.Quaternion().setFromUnitVectors(DOWN, dirR), 0.3);
    // ... aquí portar: espejo único, anti-ruido, suavizado, StableSwing, cabeza, torso
  }
  const f = face.detectForVideo(video, t);
  const bs = f.faceBlendshapes?.[0]?.categories;
  if (bs) {
    const l = bs.find(c => c.categoryName === "eyeBlinkLeft")?.score ?? 0;
    const r = bs.find(c => c.categoryName === "eyeBlinkRight")?.score ?? 0;
    const blink = (l + r) / 2;   // histéresis: cerrar > 0.55, abrir < 0.35
    // cambiar textura a skin cerrada / abierta
  }
  renderer.render(scene, camera);
  requestAnimationFrame(tick);
}
requestAnimationFrame(tick);
```

Cosas que no están en este esquema y debes portar: hand landmarker (confirmación de muñeca), controles manuales, respiración, pérdida de tracking (ocultar/retornar a reposo), encuadre automático y UI de ajustes.

---

# PARTE E — Mantenimiento

## 33. Limitaciones conocidas del estado actual
1. El selector muestra `.glb`, pero `CustomModelManager` no lo soporta; el formato confiable es `.gltf`.
2. La profundidad por tamaño de palma es aproximada.
3. Hand Landmarker confirma manos y da fallback, pero no sustituye toda la geometría corporal.
4. La cámara frontal puede tener transformaciones distintas según driver y dispositivo.
5. La persistencia del espejo se elimina al iniciar (lógica de `VTuberSettingsUI`).
6. La instancia del prefab antiguo `Main Canvas` debe revisarse al restaurar escenas.
7. La cámara virtual depende del driver externo y de que el SO exponga `Unity Video Capture`.
8. La precisión depende de iluminación, resolución, posición de la webcam, oclusiones y visibilidad de hombros/codos/manos.
9. Modelos nuevos deben usar los nombres de hueso esperados o requerirán cambios en `AvatarLoader`.
10. Los códigos de mando varían entre controladores y sistemas.
11. Posible espejado de texturas por falta de conversión de ejes glTF → Unity *(no verificado)*.
12. El salto de brazo (sección 22) *no está confirmado como resuelto*.

## 34. Invariantes y reglas para no romper nada

**Al cambiar la carga del avatar, conservar:** `flipV: true`, `buildHierarchy: true`, nombres `Body`/`Head`/`Right Arm`/`Left Arm`, la orientación base `−90°`, `driver.Bind(...)` y `PoseAvatarDriver.Instance`. Probar visualmente antes de eliminar cualquiera.

**Al cambiar el espejo:** conservar la separación entre (1) transformación de la imagen, (2) intercambio anatómico de lados y (3) coordenadas X de `P()`/`PHead()`. No añadir una segunda inversión X. El avatar dinámico debe seguir conectado vía `PoseAvatarDriver.Instance` o `AvatarLoader.driver`; si no, la UI funciona pero el modelo no recibe tracking.

**Al cambiar máscaras visuales:** conservar `BodyParts`, la máscara en el runner, la propagación a anotadores hijos y el tracking independiente del dibujo. Probar pérdida y recuperación de cada mano y verificar que ocultar el trazo no detiene el movimiento.

**Al cambiar skins:** materiales instanciados, destrucción de texturas anteriores, `Point`, `Clamp`, aplicar skin abierta/cerrada al driver.

**Al cambiar la UI:** nada de cargar modelos o texturas dentro de `OnGUI()`. Usar bandera pendiente → ejecución en `Update()` → `try/catch` local con error explícito → actualizar `PlayerPrefs`.

**Dependencias que no deben romperse:**
- `AvatarLoader` carga dinámicamente (las referencias del runner pueden estar vacías al iniciar).
- `VTuberSettingsUI` es la interfaz nueva; no sustituirla por `Main Canvas`.
- La máscara del trazo solo controla visualización, no datos del avatar.
- Archivos núcleo (no eliminar sin revisar dependencias): `AvatarLoader.cs`, `GltfModelLoader.cs`, `BlockbenchModelLoader.cs`, `CustomModelManager.cs`, `PoseAvatarDriver.cs`, `PoseLandmarkerRunner.cs`, `VTuberSettingsUI.cs`, los cuatro archivos de anotación y `VTuberCam.unity`.

## 35. Checklist de restauración y pruebas

### Antes de ejecutar
- [ ] Unity `6000.0.58f2`
- [ ] Existen los modelos `.bytes` (incluido `hand_landmarker.bytes`)
- [ ] Existen `steve.gltf` y `alex.gltf`
- [ ] Skins abiertas y cerradas
- [ ] Webcam
- [ ] Driver de cámara virtual

### Después de abrir la escena
- [ ] Existen `AvatarLoader`, `PoseLandmarkerRunner`, `VTuberSettingsUI`
- [ ] `Main Canvas` no interfiere
- [ ] Se puede seleccionar avatar y aparece **de frente**
- [ ] Se encontraron los huesos
- [ ] Se mueven cuerpo, cabeza y **ambos** brazos
- [ ] Sin trazos fantasma de manos
- [ ] El parpadeo cambia la skin
- [ ] La cámara virtual renderiza el avatar

### Pruebas mínimas de tracking
1. Mirar de frente sin mover brazos.
2. Levantar solo el brazo izquierdo.
3. Levantar solo el derecho.
4. Mover cada brazo a izquierda y derecha.
5. Acercar y alejar las manos.
6. Sacar una mano del encuadre.
7. Volver a introducirla.
8. Girar la cabeza.
9. Mirar arriba y abajo.
10. Parpadear.
11. Activar y desactivar espejo.
12. Perder temporalmente la cara.
13. Probar teclado, mouse y mando cuando una mano no sea visible.
14. *(Extra por el punto 22)* subir el brazo recto y girarlo hacia la cámara, comprobando que no hay latigazo.

## 36. Archivos generados en los chats

| Archivo | Estado |
|---|---|
| `VTuberSettingsUI.cs` | Checkboxes corregidos |
| `AvatarLoader.cs` (versión con cutout) | **No usar tal cual**: parte de una versión antigua, sin `SetCustomSkin` |
| `SkinUVFixer.cs` y `skin_uv_layout.json` | Opcionales; innecesarios con el plan aplicado |
| `steve_skin.png`, `alex_skin.png` | Skins en RGBA listas |
| `simulacion_texturas.png` | Simulación del render (V sin voltear/volteada × opaco/cutout) |
| Claude Doc "Plan de implementación" | Plan por fases |
| `PoseLandmarkerRunner.cs`, `PoseAvatarDriver.cs` (modificados) | Espejo, brazos, `StableSwing` (sin probar desde el chat) |
| `mesa.json`, `mesa_ancha.json`, `mouse.json`, `mouse_pequeno.json`, `teclado.json`, `control_ps4.json` | Modelos de bloque para Blockbench |
| `VTUBERCAM_FUNCIONALIDADES.md` y `VTUBERCAM_REQUISITOS_Y_FUNCIONALIDADES.md` | Respaldos técnicos (fuente de este documento) |

## 37. Pendientes y decisiones abiertas

**Código / pruebas**
- Confirmar si el salto de "brazo arriba ↔ mano a la cámara" (sección 22) desapareció.
- Resolver la discrepancia de esquema de espejo (sección 9 ⚠️) mirando el código real.
- Verificar en pantalla el posible espejado de texturas (sección 33, punto 11).
- Soporte real de `.glb` (o quitar `.glb` del selector para no engañar).
- Decidir si la persistencia de vista/espejo debe conservarse entre ejecuciones.
- Desactivar o eliminar la instancia del prefab antiguo `Main Canvas`.

**Tracking**
- Grabar un video de prueba y medir temblor, pérdidas, latencia y error.
- Opcional: script de comparación de parpadeo MediaPipe vs OpenSeeFace con el mismo video.
- Opcional: interfaz `IPoseSource` y filtro One Euro.

**Modelos / Blockbench**
- Terminar el atlas de la mesa (confirmar Create Texture, pasar la madera, aplicar a todos los cubos).
- Subir mouse/teclado/control a la altura del tablero (Y=16).

**Estrategia**
- Decidir si se prueba el prototipo web (sección 32) antes de seguir invirtiendo en Unity.

## 38. Tabla de parámetros (valores documentados)

| Grupo | Parámetro | Valor |
|---|---|---|
| Validación | `minLandmarksVisible` / `requiredConsecutiveFrames` / `minVisibility` | 5 / 3 / 0.5 |
| Bordes | Descartar landmark si X o Y < / > | 0.03 / 0.97 |
| Torso | `torsoWeight`, `torsoYawLimit`, `torsoPitchLimit`, `torsoRollLimit` | 0.6, 45°, 30°, 0° |
| Cabeza | `headSensitivity`, `headYawGain`, límite horizontal | 1.35, 0.7, 75° |
| Cabeza | límite vertical, `pitchDownGain`, `pitchUpGain`, `pitchDeadzone` | 45°, 2, 0.5, 4° |
| Cabeza | `headStillSmoothing`, `headDeadzone`, `invertPitch`, `useEarsForPitch` | 5, 1.5°, false, (desactivado recomendado) |
| Brazos | `wristAcquireFrames`, `depthScale` | 2, 0.6 |
| Brazos | `armDirectionSmoothing`, `armDeadzone`, `armSnapAngle` | 0.18, 2°, 50° |
| Brazos | `armSmoothing`, `armSpeedBoost`, `armFadeInSpeed`, `armFadeOutSpeed` | 10, 0.5, 3, 2 |
| Brazos | `returnToRestSpeed` | 3 |
| Brazos (sesión de espejo/brazos) | `armFluidity`, `armMaxDegPerSec`, `invertArmX` ⚠️ | 0.7, 900°/s, ON |
| Manos | `handDepthScale`, `handDepthSmoothing`, `HandAnnotationAcquireFrames` | 0.8, 0.18, 2 |
| Manos | callback considerado antiguo si | > ~120 ms |
| Parpadeo | `blinkOn` / `blinkOff` / `_faceEveryNFrames` | 0.55 / 0.35 / 2 |
| Pérdida | `lostTimeout`, `hideAvatarWhenLost`, `hideDelay` | 1.2 s, false, 3 s |
| Manual | `manualHandsEnabled`, `manualHandSensitivity`, `manualActivationDelay`, `mouseControlsHands` | true, 0.8, 0.25 s, true |
| Respiración | `breathingEnabled`, `breathAmplitudePixels`, `breathPeriod` | true, 0.001, 4 s |
| Pose | modelo / modo / poses / confianzas | Full / LIVE_STREAM / 1 / 0.5 |
| Calibración cabeza | frames | ~15 |
| Capa de render | avatar y modelos adicionales | 6 |

---

*Fin del documento. Si me dices qué camino te interesa (cerrar Unity, prototipo web, o tracker aparte), el siguiente paso natural es preparar el código concreto para ese camino.*
