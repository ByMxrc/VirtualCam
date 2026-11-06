using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Mediapipe.Unity;
using Mediapipe.Unity.Sample;
using UnityEngine;

namespace VTuberCam
{
  // OpenSeeFace's facetracker.exe consumes the same webcam frames used by MediaPipe.
  // Its UDP protocol is described in OpenSeeFace/Unity/OpenSee.cs (BSD-2-Clause).
  public sealed class OpenSeeFaceBridge : MonoBehaviour
  {
    private const int Width = 640, Height = 360, Port = 11573;
    private const int PacketSize = 8 + 4 + 8 + 8 + 1 + 4 + 16 + 12 + 12 + 68 * 4 + 68 * 8 + 70 * 12 + 14 * 4;
    private readonly object packetLock = new object();
    private readonly object frameLock = new object();
    private UdpClient receiver;
    private Thread receiveThread, writeThread;
    private Process tracker;
    private bool stopping;
    private byte[] queuedFrame;
    private Vector3 angles;
    private float leftEye, rightEye;
    private long packetSequence, consumedSequence;
    private float lastPacketAt = -999f;
    private RenderTexture surface;
    private Texture2D pixels;
    private float nextCapture;
    private float nextLaunch;
    private PoseAvatarDriver driver;
    private VtuberAvatarLoader avatar;
    private VtuberApp app;

    public bool HasTracking => Time.realtimeSinceStartup - lastPacketAt < .5f;

    private void Awake()
    {
      driver = GetComponent<PoseAvatarDriver>();
      avatar = GetComponent<VtuberAvatarLoader>();
      app = GetComponent<VtuberApp>();
    }

    private void Update()
    {
      if (!Application.isPlaying) return;
      if (app == null || !app.FaceTrackingEnabled)
      {
        if (tracker != null) StopTracker();
        return;
      }
      if ((tracker == null || tracker.HasExited) && Time.realtimeSinceStartup >= nextLaunch)
      {
        nextLaunch = Time.realtimeSinceStartup + 5f;
        if (tracker != null) StopTracker();
        StartTrackerIfAvailable();
      }
      if (tracker == null) return;

      Vector3 current = default;
      float left = 0f, right = 0f;
      bool newPacket;
      lock (packetLock)
      {
        newPacket = packetSequence != consumedSequence;
        if (newPacket)
        {
          consumedSequence = packetSequence;
          current = angles;
          left = leftEye;
          right = rightEye;
        }
      }
      if (newPacket)
      {
        lastPacketAt = Time.realtimeSinceStartup;
        driver?.SetFacePose(current);
        if (avatar != null && avatar.BlinkEnabled && !avatar.AutomaticBlink)
          avatar.SetTrackedBlink(left < .28f && right < .28f);
      }
      else if (!HasTracking) avatar?.SetTrackedBlink(false);

      var source = ImageSourceProvider.ImageSource;
      if (source == null || !source.isPrepared || Time.realtimeSinceStartup < nextCapture) return;
      nextCapture = Time.realtimeSinceStartup + 1f / 24f;
      if (surface == null)
      {
        surface = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32);
        pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
      }
      var original = RenderTexture.active;
      try
      {
        Graphics.Blit(source.GetCurrentTexture(), surface);
        RenderTexture.active = surface;
        pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
        pixels.Apply(false);
        var raw = pixels.GetRawTextureData<byte>();
        var frame = new byte[raw.Length];
        // OpenCV expects the first scanline at the top; Unity's raw pixels start at the bottom.
        var stride = Width * 3;
        for (var row = 0; row < Height; row++)
          for (var col = 0; col < stride; col++)
            frame[row * stride + col] = raw[(Height - 1 - row) * stride + col];
        lock (frameLock) { queuedFrame = frame; Monitor.Pulse(frameLock); }
      }
      catch (Exception error) { UnityEngine.Debug.LogWarning("OpenSeeFace: no se pudo compartir el fotograma: " + error.Message); }
      finally { RenderTexture.active = original; }
    }

    private void StartTrackerIfAvailable()
    {
      if (Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor) return;
      var binary = Path.Combine(Application.streamingAssetsPath, "OpenSeeFace", "Binary");
      var exe = Path.Combine(binary, "facetracker.exe");
      var models = Path.Combine(binary, "models");
      if (!File.Exists(exe) || !Directory.Exists(models)) return;
      try
      {
        stopping = false;
        receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, Port));
        receiver.Client.ReceiveTimeout = 250;
        receiveThread = new Thread(Receive) { IsBackground = true };
        receiveThread.Start();
        tracker = new Process();
        tracker.StartInfo = new ProcessStartInfo(exe,
          "--ip 127.0.0.1 --port " + Port + " --model-dir \"" + models +
          "\" --raw-rgb 1 -W " + Width + " -H " + Height +
          " --fps 24 --model 3 --gaze-tracking 0 --silent 1 --max-threads 1")
        {
          WorkingDirectory = binary, UseShellExecute = false, CreateNoWindow = true,
          RedirectStandardInput = true
        };
        tracker.Start();
        writeThread = new Thread(WriteFrames) { IsBackground = true };
        writeThread.Start();
      }
      catch (Exception error)
      {
        UnityEngine.Debug.LogWarning("OpenSeeFace no pudo iniciarse: " + error.Message);
        StopTracker();
      }
    }

    private void Receive()
    {
      var socket = receiver;
      while (!stopping)
      {
        try
        {
          IPEndPoint sender = null;
          var bytes = socket.Receive(ref sender);
          if (bytes.Length < PacketSize || bytes.Length % PacketSize != 0) continue;
          // OpenSeeFace writes the Euler angles at byte 49 and openness at bytes 20 and 24.
          // See its official Unity/OpenSee.cs readFromPacket for the coordinate conversion.
          var offset = 0;
          if (bytes[offset + 28] == 0) continue; // 3D face fit unavailable
          var pose = new Vector3(BitConverter.ToSingle(bytes, offset + 49),
            -BitConverter.ToSingle(bytes, offset + 53), BitConverter.ToSingle(bytes, offset + 57));
          var right = BitConverter.ToSingle(bytes, offset + 20);
          var left = BitConverter.ToSingle(bytes, offset + 24);
          lock (packetLock)
          {
            angles = pose;
            leftEye = left;
            rightEye = right;
            packetSequence++;
          }
        }
        catch (SocketException) { if (stopping) break; }
        catch (ObjectDisposedException) { break; }
      }
    }

    private void WriteFrames()
    {
      while (!stopping)
      {
        byte[] frame;
        lock (frameLock)
        {
          while (queuedFrame == null && !stopping) Monitor.Wait(frameLock, 250);
          if (stopping) return;
          frame = queuedFrame;
          queuedFrame = null;
        }
        try { tracker.StandardInput.BaseStream.Write(frame, 0, frame.Length); }
        catch (Exception) { return; }
      }
    }

    private void StopTracker()
    {
      stopping = true;
      lock (frameLock) Monitor.PulseAll(frameLock);
      receiver?.Close();
      receiver = null;
      if (tracker != null)
      {
        try { if (!tracker.HasExited) tracker.Kill(); tracker.StandardInput.Close(); }
        catch (Exception) { }
        tracker.Dispose();
        tracker = null;
      }
      if (receiveThread != null && receiveThread.IsAlive) receiveThread.Join(500);
      if (writeThread != null && writeThread.IsAlive) writeThread.Join(500);
      receiveThread = null;
      writeThread = null;
      lastPacketAt = -999f;
      avatar?.SetTrackedBlink(false);
    }

    private void OnDestroy()
    {
      StopTracker();
      if (surface != null) { surface.Release(); Destroy(surface); }
      if (pixels != null) Destroy(pixels);
    }
  }
}
