using System;
using UnityEngine;

namespace VTuberCam
{
  public sealed class PoseAvatarDriver : MonoBehaviour
  {
    [Header("Tracking")]
    [SerializeField] private bool mirror = false;
    [SerializeField, Range(0.1f, 3f)] private float headSensitivity = 1.35f;
    [SerializeField, Range(10f, 120f)] private float pitchSensitivity = 70f;
    [SerializeField, Range(0.05f, 1.5f)] private float depthScale = 0.6f;
    [SerializeField, Range(1f, 24f)] private float smoothing = 10f;
    [SerializeField, Range(0f, 10f)] private float deadzoneDegrees = 2f;
    [SerializeField] private float lostTimeout = 1.2f;
    [SerializeField] private float returnToRestSpeed = 3f;

    [Header("Manual controls")]
    [SerializeField] private bool manualHandsEnabled = true;
    [SerializeField, Range(0.1f, 2f)] private float manualHandSensitivity = 0.8f;

    private readonly VtuberPoseFrame frame = new VtuberPoseFrame();
    private readonly object frameLock = new object();
    private readonly Vector3[] armDirections = { Vector3.down, Vector3.down };
    private readonly Quaternion[] restArms = { Quaternion.identity, Quaternion.identity };
    private Transform body;
    private Transform head;
    private Transform leftArm;
    private Transform rightArm;
    private Quaternion restBody;
    private Quaternion restHead;
    private float lastValidTime = -999f;
    private long lastPoseTimestamp = long.MinValue;
    private float calibrationBaseline;
    private float neutralEyeSpan;
    private int calibrationSamples;
    private bool calibrated;
    private bool calibrateOnNextFrame;
    private float neutralHeadYaw;
    private float neutralHeadRoll;
    private Vector3 faceAngles;
    private Vector3 neutralFaceAngles;
    private bool faceNeutralValid;
    private float lastFaceTime = -999f;
    private bool externalFace;
    private readonly Vector3[] neutralArmDirections = { Vector3.down, Vector3.down };
    private readonly bool[] neutralArmValid = new bool[2];

    public bool Mirror { get => mirror; set => mirror = value; }
    public bool IsTracking => Time.time - lastValidTime <= lostTimeout;

    // OpenSeeFace supplies independent 3D head pose; the pose tracker still drives arms.
    public void SetFacePose(Vector3 angles)
    {
      faceAngles = angles;
      lastFaceTime = Time.time;
      externalFace = true;
      if (!faceNeutralValid) { neutralFaceAngles = angles; faceNeutralValid = true; }
    }

    public VtuberPoseFrame GetPoseSnapshot()
    {
      var snapshot = new VtuberPoseFrame();
      lock (frameLock)
      {
        ArrayCopy(frame.x, snapshot.x);
        ArrayCopy(frame.y, snapshot.y);
        ArrayCopy(frame.z, snapshot.z);
        ArrayCopy(frame.visibility, snapshot.visibility);
        snapshot.valid = frame.valid;
        snapshot.timestamp = frame.timestamp;
      }
      return snapshot;
    }

    public void Bind(Transform bodyPivot, Transform headPivot, Transform leftArmPivot, Transform rightArmPivot)
    {
      body = bodyPivot;
      head = headPivot;
      leftArm = leftArmPivot;
      rightArm = rightArmPivot;
      restBody = body != null ? body.localRotation : Quaternion.identity;
      restHead = head != null ? head.localRotation : Quaternion.identity;
      if (leftArm != null) restArms[0] = leftArm.localRotation;
      if (rightArm != null) restArms[1] = rightArm.localRotation;
    }

    public void SetPose(VtuberPoseFrame source)
    {
      lock (frameLock)
      {
        ArrayCopy(source.x, frame.x);
        ArrayCopy(source.y, frame.y);
        ArrayCopy(source.z, frame.z);
        ArrayCopy(source.visibility, frame.visibility);
        frame.valid = source.valid;
        frame.timestamp = source.timestamp;
      }
    }

    public void Recalibrate()
    {
      calibrationBaseline = 0f;
      neutralEyeSpan = 0f;
      calibrationSamples = 0;
      calibrated = false;
      calibrateOnNextFrame = true;
      neutralArmValid[0] = false;
      neutralArmValid[1] = false;
      faceNeutralValid = false;
    }

    public void ResetPose()
    {
      Recalibrate();
      lock (frameLock) frame.Clear();
      lastValidTime = -999f;
      lastPoseTimestamp = long.MinValue;
      armDirections[0] = Vector3.down;
      armDirections[1] = Vector3.down;
      if (body) body.localRotation = restBody;
      if (head) head.localRotation = restHead;
      if (leftArm) leftArm.localRotation = restArms[0];
      if (rightArm) rightArm.localRotation = restArms[1];
    }

    private void Update()
    {
      VtuberPoseFrame snapshot = new VtuberPoseFrame();
      lock (frameLock)
      {
        ArrayCopy(frame.x, snapshot.x);
        ArrayCopy(frame.y, snapshot.y);
        ArrayCopy(frame.z, snapshot.z);
        ArrayCopy(frame.visibility, snapshot.visibility);
        snapshot.valid = frame.valid;
        snapshot.timestamp = frame.timestamp;
      }

      if (!snapshot.valid || CountVisible(snapshot) < 5 ||
          (snapshot.timestamp == lastPoseTimestamp && Time.time - lastValidTime > lostTimeout))
      {
        ReturnToRest(Time.deltaTime);
        return;
      }

      if (snapshot.timestamp != lastPoseTimestamp)
      {
        lastValidTime = Time.time;
        lastPoseTimestamp = snapshot.timestamp;
      }
      var nose = Point(snapshot, 0);
      var leftEye = Point(snapshot, 2);
      var rightEye = Point(snapshot, 5);
      var leftShoulder = Point(snapshot, 11);
      var rightShoulder = Point(snapshot, 12);
      var leftWrist = Point(snapshot, 15);
      var rightWrist = Point(snapshot, 16);
      var leftElbow = Point(snapshot, 13);
      var rightElbow = Point(snapshot, 14);

      var eyeMid = (leftEye + rightEye) * 0.5f;
      var eyeSpan = Mathf.Max(0.05f, Mathf.Abs(leftEye.x - rightEye.x));
      // Turning sideways narrows the visible eye span. Keep the calibrated span
      // as the minimum so a horizontal turn cannot become a false downward nod.
      var headPitchRatio = (nose.y - eyeMid.y) /
        (calibrated ? Mathf.Max(eyeSpan,neutralEyeSpan) : eyeSpan);
      // Convert the face's horizontal offset into degrees before clamping it.
      // The previous ratio was only a few units, so turning sideways was barely visible.
      var headYaw = -Mathf.Atan2(nose.x - eyeMid.x,eyeSpan * .5f) * Mathf.Rad2Deg * headSensitivity;
      var headRoll = Mathf.Atan2((leftEye - rightEye).y, (leftEye - rightEye).x) * Mathf.Rad2Deg;

      if (calibrateOnNextFrame)
      {
        neutralEyeSpan = eyeSpan;
        neutralHeadYaw = headYaw;
        neutralHeadRoll = headRoll;
        calibrationBaseline = headPitchRatio;
        calibrated = true;
        calibrateOnNextFrame = false;
      }
      else if (!calibrated && Mathf.Abs(headYaw) < 8f && Mathf.Abs(headRoll) < 8f)
      {
        calibrationBaseline += headPitchRatio;
        neutralEyeSpan += eyeSpan;
        calibrationSamples++;
        if (calibrationSamples >= 15)
        {
          calibrationBaseline /= calibrationSamples;
          neutralEyeSpan /= calibrationSamples;
          calibrated = true;
        }
      }

      var pitch = 0f;
      if (calibrated)
      {
        // Positive X lowers the face, negative X raises it. Keep both directions
        // within comfortable limits and use the calibrated pose as neutral.
        var pitchOffset = (headPitchRatio - calibrationBaseline) * pitchSensitivity;
        // The pose landmarks compress the downward range more than the upward one.
        pitch = Mathf.Clamp(pitchOffset < 0f ? pitchOffset * 1.65f : pitchOffset,
          -60f, 55f);
      }

      // Shoulder estimation from a single webcam is too noisy for torso yaw.
      // Keep the torso at its rest rotation while the head and arms track.
      var targetBody = restBody;
      var targetHead = Quaternion.Euler(pitch,
        Mathf.Clamp(Mathf.DeltaAngle(neutralHeadYaw, headYaw), -65f, 65f),
        Mathf.Clamp(Mathf.DeltaAngle(neutralHeadRoll, headRoll), -45f, 45f)) * restHead;
      if (HasFreshFace()) targetHead = FaceTarget();
      ApplyRotation(body, targetBody, returnToRestSpeed);
      ApplyRotation(head, targetHead, HasFreshFace() ? FaceResponse(targetHead) : returnToRestSpeed);

      ApplyArm(leftArm, leftShoulder, leftWrist, leftElbow, 0, snapshot);
      ApplyArm(rightArm, rightShoulder, rightWrist, rightElbow, 1, snapshot);
    }

    private void ApplyArm(Transform arm, Vector3 shoulder, Vector3 wrist, Vector3 elbow, int index, VtuberPoseFrame snapshot)
    {
      if (arm == null) return;
      var wristVisible = Visible(snapshot, index == 0 ? 15 : 16);
      if (!wristVisible || !Visible(snapshot, index == 0 ? 11 : 12))
      {
        armDirections[index] = Vector3.Slerp(armDirections[index],
          neutralArmValid[index] ? neutralArmDirections[index] : Vector3.down,
          1f - Mathf.Exp(-smoothing * Time.deltaTime));
        ApplyRotation(arm, restArms[index], returnToRestSpeed);
        return;
      }
      var target = wrist;
      var direction = target - shoulder;
      if (direction.sqrMagnitude < 0.0001f) direction = Vector3.down;
      if (!mirror) direction.x = -direction.x;
      direction.z *= depthScale;
      if (manualHandsEnabled)
      {
        if (index == 0)
        {
          if (Input.GetKey(KeyCode.T)) direction.y += manualHandSensitivity;
          if (Input.GetKey(KeyCode.G)) direction.y -= manualHandSensitivity;
          if (Input.GetKey(KeyCode.F)) direction.x += manualHandSensitivity;
          if (Input.GetKey(KeyCode.H)) direction.x -= manualHandSensitivity;
        }
        else
        {
          if (Input.GetKey(KeyCode.I)) direction.y += manualHandSensitivity;
          if (Input.GetKey(KeyCode.K)) direction.y -= manualHandSensitivity;
          if (Input.GetKey(KeyCode.J)) direction.x -= manualHandSensitivity;
          if (Input.GetKey(KeyCode.L)) direction.x += manualHandSensitivity;
        }
      }
      direction.Normalize();
      if (!neutralArmValid[index])
      {
        neutralArmDirections[index] = direction;
        armDirections[index] = direction;
        neutralArmValid[index] = true;
      }
      var angle = Vector3.Angle(armDirections[index], direction);
      if (angle > deadzoneDegrees)
      {
        armDirections[index] = Vector3.Slerp(armDirections[index], direction, 1f - Mathf.Exp(-smoothing * Time.deltaTime)).normalized;
      }
      var targetRotation = Quaternion.FromToRotation(neutralArmDirections[index], armDirections[index]) * restArms[index];
      ApplyRotation(arm, targetRotation, returnToRestSpeed);
    }

    private void ReturnToRest(float dt)
    {
      ApplyRotation(body, restBody, returnToRestSpeed);
      var faceTarget = HasFreshFace() ? FaceTarget() : restHead;
      ApplyRotation(head, faceTarget, HasFreshFace() ? FaceResponse(faceTarget) : returnToRestSpeed);
      if (leftArm != null) ApplyRotation(leftArm, restArms[0], returnToRestSpeed);
      if (rightArm != null) ApplyRotation(rightArm, restArms[1], returnToRestSpeed);
    }

    private bool HasFreshFace() => externalFace && faceNeutralValid && Time.time - lastFaceTime < .55f;

    private Quaternion FaceTarget()
    {
      return Quaternion.Euler(
        Mathf.Clamp(-Mathf.DeltaAngle(neutralFaceAngles.x, faceAngles.x), -60f, 55f),
        Mathf.Clamp(Mathf.DeltaAngle(neutralFaceAngles.y, faceAngles.y), -65f, 65f),
        Mathf.Clamp(Mathf.DeltaAngle(neutralFaceAngles.z, faceAngles.z), -45f, 45f)) * restHead;
    }

    private float FaceResponse(Quaternion target)
    {
      // Follow a large turn promptly, but smooth small variations at rest.
      return head != null ? Mathf.Lerp(9f, 22f, Mathf.Clamp01(Quaternion.Angle(head.localRotation, target) / 18f)) : 9f;
    }

    private static void ApplyRotation(Transform target, Quaternion rotation, float speed)
    {
      if (target == null) return;
      target.localRotation = Quaternion.Slerp(target.localRotation, rotation, 1f - Mathf.Exp(-speed * Time.deltaTime));
    }

    private Vector3 Point(VtuberPoseFrame source, int index)
      => new Vector3(source.x[index] - 0.5f, -(source.y[index] - 0.5f), source.z[index] * depthScale);

    private static int CountVisible(VtuberPoseFrame source)
    {
      var count = 0;
      for (var i = 0; i < source.visibility.Length; i++) if (source.visibility[i] >= 0.5f) count++;
      return count;
    }

    private static bool Visible(VtuberPoseFrame source, int index) => source.visibility[index] >= 0.5f;

    private static void ArrayCopy(float[] source, float[] destination) => System.Array.Copy(source, destination, destination.Length);
  }
}
