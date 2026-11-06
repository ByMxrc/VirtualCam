using System;

namespace VTuberCam
{
  [Serializable]
  public sealed class VtuberPoseFrame
  {
    public readonly float[] x = new float[33];
    public readonly float[] y = new float[33];
    public readonly float[] z = new float[33];
    public readonly float[] visibility = new float[33];
    public bool valid;
    public long timestamp;

    public void Clear()
    {
      Array.Clear(x, 0, x.Length);
      Array.Clear(y, 0, y.Length);
      Array.Clear(z, 0, z.Length);
      Array.Clear(visibility, 0, visibility.Length);
      valid = false;
    }
  }
}
