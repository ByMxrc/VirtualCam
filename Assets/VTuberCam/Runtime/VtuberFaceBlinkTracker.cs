using System;
using System.Collections;
using System.IO;
using Mediapipe;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using Mediapipe.Unity;
using Mediapipe.Unity.Sample;
using UnityEngine;

namespace VTuberCam
{
  // Shares the existing MediaPipe camera; does not open another WebCamTexture.
  public sealed class VtuberFaceBlinkTracker : MonoBehaviour
  {
    private VtuberAvatarLoader avatar;
    private FaceLandmarker landmarker;
    private Coroutine loop;

    private void Awake() { avatar = GetComponent<VtuberAvatarLoader>(); }
    private void OnEnable() { loop = StartCoroutine(Track()); }
    private void OnDisable()
    {
      if (loop != null) StopCoroutine(loop);
      avatar?.SetTrackedBlink(false);
      landmarker?.Close();
      landmarker = null;
    }

    private IEnumerator Track()
    {
      var path = Path.Combine(Application.streamingAssetsPath,"face_landmarker.task");
      if (!File.Exists(path))
      {
        Debug.LogError("VirtualCraft: falta StreamingAssets/face_landmarker.task para detectar pestañeos.");
        yield break;
      }
      FaceLandmarkerOptions options;
      try
      {
        options = new FaceLandmarkerOptions(
          new Mediapipe.Tasks.Core.BaseOptions(Mediapipe.Tasks.Core.BaseOptions.Delegate.CPU,
            modelAssetBuffer: File.ReadAllBytes(path)),
          runningMode: Mediapipe.Tasks.Vision.Core.RunningMode.VIDEO,
          outputFaceBlendshapes: true);
        landmarker = FaceLandmarker.CreateFromOptions(options);
      }
      catch (Exception error) { Debug.LogException(error); yield break; }

      var timestamp = 0L;
      while (enabled)
      {
        var openSee = GetComponent<OpenSeeFaceBridge>();
        if (!avatar || !avatar.BlinkEnabled || avatar.AutomaticBlink || (openSee != null && openSee.HasTracking))
        {
          yield return new WaitForSeconds(.1f);
          continue;
        }
        var source = ImageSourceProvider.ImageSource;
        if (source == null || !source.isPrepared || source.textureWidth < 16 || source.textureHeight < 16)
        {
          avatar.SetTrackedBlink(false);
          yield return new WaitForSeconds(.1f);
          continue;
        }
        using (var frame = new Mediapipe.Unity.Experimental.TextureFrame(source.textureWidth,source.textureHeight,TextureFormat.RGBA32))
        {
          var transform = source.GetTransformationOptions();
          frame.ReadTextureOnCPU(source.GetCurrentTexture(),transform.flipHorizontally,transform.flipVertically);
          using (var image = frame.BuildCPUImage())
          {
            timestamp = Math.Max(timestamp + 1,(long)(Time.realtimeSinceStartup * 1000));
            var result = landmarker.DetectForVideo(image,timestamp);
            var closed = false;
            if (result.faceBlendshapes != null && result.faceBlendshapes.Count > 0)
            {
              float left = 0f, right = 0f;
              var categories = result.faceBlendshapes[0].categories;
              if (categories != null)
                foreach (var category in categories)
                {
                  if (category.categoryName == "eyeBlinkLeft") left = category.score;
                  else if (category.categoryName == "eyeBlinkRight") right = category.score;
                }
              closed = left > .55f && right > .55f;
            }
            avatar.SetTrackedBlink(closed);
          }
        }
        yield return new WaitForSeconds(.075f);
      }
    }
  }
}
