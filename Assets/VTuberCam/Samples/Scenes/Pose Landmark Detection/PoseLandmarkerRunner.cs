// Copyright (c) 2023 homuler
//
// Use of this source code is governed by an MIT-style
// license that can be found in the LICENSE file or at
// https://opensource.org/licenses/MIT.

using System.Collections;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using Mediapipe.Tasks.Vision.PoseLandmarker;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mediapipe.Unity.Sample.PoseLandmarkDetection
{
  public class PoseLandmarkerRunner : VisionTaskApiRunner<PoseLandmarker>
  {
    [SerializeField] private PoseLandmarkerResultAnnotationController _poseLandmarkerResultAnnotationController;

    // --- AVATAR: arrastra aquí el objeto 'alex' (el que tiene PoseAvatarDriver) ---
    [SerializeField] private PoseAvatarDriver _avatar;
    private readonly Vector3[] _avatarWorld = new Vector3[33];
    private readonly float[] _avatarVis = new float[33];

    // --- PARPADEO: segundo modelo (Face Landmarker) sobre la misma imagen de la webcam ---
    [SerializeField] private bool _enableBlink = true;
    [Tooltip("1 = analiza la cara en cada frame; 2 o 3 = menos carga para el PC")]
    [SerializeField] private int _faceEveryNFrames = 2;
    private FaceLandmarker _faceApi;
    private int _faceFrameCounter;
    private readonly Mediapipe.Unity.Sample.FaceLandmarkDetection.FaceLandmarkDetectionConfig _faceConfig =
      new Mediapipe.Unity.Sample.FaceLandmarkDetection.FaceLandmarkDetectionConfig();

    private Experimental.TextureFramePool _textureFramePool;

    public readonly PoseLandmarkDetectionConfig config = new PoseLandmarkDetectionConfig();

    public override void Stop()
    {
      base.Stop();
      _faceApi?.Close();
      _faceApi = null;
      _textureFramePool?.Dispose();
      _textureFramePool = null;
    }

    protected override IEnumerator Run()
    {
      Debug.Log($"Delegate = {config.Delegate}");
      Debug.Log($"Image Read Mode = {config.ImageReadMode}");
      Debug.Log($"Model = {config.ModelName}");
      Debug.Log($"Running Mode = {config.RunningMode}");
      Debug.Log($"NumPoses = {config.NumPoses}");
      Debug.Log($"MinPoseDetectionConfidence = {config.MinPoseDetectionConfidence}");
      Debug.Log($"MinPosePresenceConfidence = {config.MinPosePresenceConfidence}");
      Debug.Log($"MinTrackingConfidence = {config.MinTrackingConfidence}");
      Debug.Log($"OutputSegmentationMasks = {config.OutputSegmentationMasks}");

      yield return AssetLoader.PrepareAssetAsync(config.ModelPath);

      var options = config.GetPoseLandmarkerOptions(config.RunningMode == Tasks.Vision.Core.RunningMode.LIVE_STREAM ? OnPoseLandmarkDetectionOutput : null);
      taskApi = PoseLandmarker.CreateFromOptions(options, GpuManager.GpuResources);

      if (_enableBlink)
      {
        _faceConfig.RunningMode = Tasks.Vision.Core.RunningMode.LIVE_STREAM;
        yield return AssetLoader.PrepareAssetAsync(_faceConfig.ModelPath);
        var faceOptions = _faceConfig.GetFaceLandmarkerOptions(OnFaceLandmarkOutput);
        _faceApi = FaceLandmarker.CreateFromOptions(faceOptions, GpuManager.GpuResources);
      }
      var imageSource = ImageSourceProvider.ImageSource;

      yield return imageSource.Play();

      if (!imageSource.isPrepared)
      {
        Logger.LogError(TAG, "Failed to start ImageSource, exiting...");
        yield break;
      }

      // Use RGBA32 as the input format.
      // TODO: When using GpuBuffer, MediaPipe assumes that the input format is BGRA, so maybe the following code needs to be fixed.
      _textureFramePool = new Experimental.TextureFramePool(imageSource.textureWidth, imageSource.textureHeight, TextureFormat.RGBA32, 10);

      // NOTE: The screen will be resized later, keeping the aspect ratio.
      screen.Initialize(imageSource);

      SetupAnnotationController(_poseLandmarkerResultAnnotationController, imageSource);
      _poseLandmarkerResultAnnotationController.InitScreen(imageSource.textureWidth, imageSource.textureHeight);

      var transformationOptions = imageSource.GetTransformationOptions();
      var flipHorizontally = transformationOptions.flipHorizontally;
      var flipVertically = transformationOptions.flipVertically;

      // Always setting rotationDegrees to 0 to avoid the issue that the detection becomes unstable when the input image is rotated.
      // https://github.com/homuler/MediaPipeUnityPlugin/issues/1196
      var imageProcessingOptions = new Tasks.Vision.Core.ImageProcessingOptions(rotationDegrees: 0);

      AsyncGPUReadbackRequest req = default;
      var waitUntilReqDone = new WaitUntil(() => req.done);
      var waitForEndOfFrame = new WaitForEndOfFrame();
      var result = PoseLandmarkerResult.Alloc(options.numPoses, options.outputSegmentationMasks);

      // NOTE: we can share the GL context of the render thread with MediaPipe (for now, only on Android)
      var canUseGpuImage = SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3 && GpuManager.GpuResources != null;
      using var glContext = canUseGpuImage ? GpuManager.GetGlContext() : null;

      while (true)
      {
        if (isPaused)
        {
          yield return new WaitWhile(() => isPaused);
        }

        if (!_textureFramePool.TryGetTextureFrame(out var textureFrame))
        {
          yield return new WaitForEndOfFrame();
          continue;
        }

        // ¿Toca analizar la cara en este frame? (necesita su PROPIA imagen: el modelo de pose destruye la suya al usarla)
        var wantFace = _faceApi != null && ++_faceFrameCounter >= Mathf.Max(1, _faceEveryNFrames);
        if (wantFace) _faceFrameCounter = 0;
        Image faceImage = null;

        // Build the input Image
        Image image;
        switch (config.ImageReadMode)
        {
          case ImageReadMode.GPU:
            if (!canUseGpuImage)
            {
              throw new System.Exception("ImageReadMode.GPU is not supported");
            }
            textureFrame.ReadTextureOnGPU(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
            image = textureFrame.BuildGPUImage(glContext);
            if (wantFace) faceImage = textureFrame.BuildGPUImage(glContext);
            // TODO: Currently we wait here for one frame to make sure the texture is fully copied to the TextureFrame before sending it to MediaPipe.
            // This usually works but is not guaranteed. Find a proper way to do this. See: https://github.com/homuler/MediaPipeUnityPlugin/pull/1311
            yield return waitForEndOfFrame;
            break;
          case ImageReadMode.CPU:
            yield return waitForEndOfFrame;
            textureFrame.ReadTextureOnCPU(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
            image = textureFrame.BuildCPUImage();
            if (wantFace) faceImage = textureFrame.BuildCPUImage();
            textureFrame.Release();
            break;
          case ImageReadMode.CPUAsync:
          default:
            req = textureFrame.ReadTextureAsync(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
            yield return waitUntilReqDone;

            if (req.hasError)
            {
              Debug.LogWarning($"Failed to read texture from the image source");
              continue;
            }
            image = textureFrame.BuildCPUImage();
            if (wantFace) faceImage = textureFrame.BuildCPUImage();
            textureFrame.Release();
            break;
        }

        switch (taskApi.runningMode)
        {
          case Tasks.Vision.Core.RunningMode.IMAGE:
            if (taskApi.TryDetect(image, imageProcessingOptions, ref result))
            {
              SendToAvatar(result);
              _poseLandmarkerResultAnnotationController.DrawNow(result);
            }
            else
            {
              _poseLandmarkerResultAnnotationController.DrawNow(default);
            }
            DisposeAllMasks(result);
            break;
          case Tasks.Vision.Core.RunningMode.VIDEO:
            if (taskApi.TryDetectForVideo(image, GetCurrentTimestampMillisec(), imageProcessingOptions, ref result))
            {
              SendToAvatar(result);
              _poseLandmarkerResultAnnotationController.DrawNow(result);
            }
            else
            {
              _poseLandmarkerResultAnnotationController.DrawNow(default);
            }
            DisposeAllMasks(result);
            break;
          case Tasks.Vision.Core.RunningMode.LIVE_STREAM:
            taskApi.DetectAsync(image, GetCurrentTimestampMillisec(), imageProcessingOptions);
            break;
        }

        // Segunda imagen (propia) al Face Landmarker para detectar el parpadeo
        if (faceImage != null)
        {
          _faceApi.DetectAsync(faceImage, GetCurrentTimestampMillisec(), imageProcessingOptions);
        }
      }
    }

    // Resultado de la cara (hilo de MediaPipe): solo copia los valores de parpadeo al avatar.
    private void OnFaceLandmarkOutput(FaceLandmarkerResult result, Image image, long timestamp)
    {
      if (_avatar == null) return;
      if (result.faceBlendshapes == null || result.faceBlendshapes.Count == 0) return;

      var cats = result.faceBlendshapes[0].categories;
      if (cats == null || cats.Count < 11) return;

      // Blendshapes de MediaPipe: 9 = eyeBlinkLeft, 10 = eyeBlinkRight
      _avatar.EnqueueBlink(cats[9].score, cats[10].score);
    }

    private void OnPoseLandmarkDetectionOutput(PoseLandmarkerResult result, Image image, long timestamp)
    {
      SendToAvatar(result);
      _poseLandmarkerResultAnnotationController.DrawLater(result);
      DisposeAllMasks(result);
    }

    // Copia los 33 landmarks de la primera persona al avatar.
    // Puede llamarse desde el hilo de MediaPipe: solo copia datos, no toca objetos de Unity.
    private void SendToAvatar(PoseLandmarkerResult result)
    {
      if (_avatar == null) return;
      if (result.poseWorldLandmarks == null || result.poseWorldLandmarks.Count == 0) return;

      var lms = result.poseWorldLandmarks[0].landmarks;
      if (lms == null || lms.Count < 33) return;

      // Landmarks normalizados: contienen visibilidad y permiten descartar puntos fuera de cámara
      var nl = (result.poseLandmarks != null && result.poseLandmarks.Count > 0) ? result.poseLandmarks[0].landmarks : null;

      for (var i = 0; i < 33; i++)
      {
        var lm = lms[i];
        _avatarWorld[i] = new Vector3(lm.x, lm.y, lm.z);
        object v = lm.visibility; // sirve si 'visibility' es float o float?
        _avatarVis[i] = v is float f ? f : 1f;

        // Un punto fuera de la imagen es una adivinanza del modelo: no hay que fiarse
        if (nl != null && i < nl.Count)
        {
          var n = nl[i];
          if (n.x < 0.03f || n.x > 0.97f || n.y < 0.03f || n.y > 0.97f) _avatarVis[i] = 0f;
        }
      }
      _avatar.EnqueuePose(_avatarWorld, _avatarVis);
    }

    private void DisposeAllMasks(PoseLandmarkerResult result)
    {
      if (result.segmentationMasks != null)
      {
        foreach (var mask in result.segmentationMasks)
        {
          mask.Dispose();
        }
      }
    }
  }
}