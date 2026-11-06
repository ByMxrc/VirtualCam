using System;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Tasks.Vision.PoseLandmarker;
using UnityEngine;

namespace VTuberCam
{
  public sealed class VtuberPoseBridge : MonoBehaviour
  {
    [SerializeField] private PoseAvatarDriver driver;

    private void Awake()
    {
      if (driver == null) driver = GetComponent<PoseAvatarDriver>();
    }

    private void OnEnable()
    {
      Mediapipe.Unity.Sample.PoseLandmarkDetection.PoseLandmarkerRunner.OnPoseResult += OnPoseResult;
    }

    private void OnDisable()
    {
      Mediapipe.Unity.Sample.PoseLandmarkDetection.PoseLandmarkerRunner.OnPoseResult -= OnPoseResult;
    }

    private void OnPoseResult(PoseLandmarkerResult result, long timestamp) => OnPose(result, timestamp);

    public void OnPose(PoseLandmarkerResult result, long timestamp)
    {
      var frame = new VtuberPoseFrame { timestamp = timestamp };
      if (result.poseLandmarks == null || result.poseLandmarks.Count == 0 || result.poseLandmarks[0].landmarks == null)
      {
        driver?.SetPose(frame);
        return;
      }

      var landmarks = result.poseLandmarks[0].landmarks;
      var count = Math.Min(33, landmarks.Count);
      for (var i = 0; i < count; i++)
      {
        var landmark = landmarks[i];
        frame.x[i] = landmark.x;
        frame.y[i] = landmark.y;
        frame.z[i] = landmark.z;
        frame.visibility[i] = landmark.visibility ?? 1f;
        if (landmark.x < 0.03f || landmark.x > 0.97f || landmark.y < 0.03f || landmark.y > 0.97f)
          frame.visibility[i] = 0f;
      }
      frame.valid = count >= 25;
      driver?.SetPose(frame);
    }
  }
}
