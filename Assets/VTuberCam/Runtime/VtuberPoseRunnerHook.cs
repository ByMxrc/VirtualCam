using Mediapipe.Tasks.Vision.PoseLandmarker;
using UnityEngine;

namespace VTuberCam
{
  public sealed class VtuberPoseRunnerHook : MonoBehaviour
  {
    [SerializeField] private VtuberPoseBridge bridge;
    public void Forward(PoseLandmarkerResult result, long timestamp) => bridge?.OnPose(result, timestamp);
  }
}
