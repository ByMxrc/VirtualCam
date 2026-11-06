#if UNITY_EDITOR
using Mediapipe.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VTuberCam.Editor
{
  // Reuses MediaPipe's own screen and annotation hierarchy as an editable Scene object.
  // It never opens a second webcam: the existing PoseLandmarkerRunner supplies the texture.
  [InitializeOnLoad]
  public static class VtuberScenePreview
  {
    private static readonly int[,] bones = { {11,12},{11,13},{13,15},{12,14},{14,16},{11,23},{12,24},{23,24},{0,2},{0,5},{2,7},{5,8} };

    static VtuberScenePreview()
    {
      EditorApplication.delayCall += Prepare;
      EditorSceneManager.sceneOpened += (_, __) => EditorApplication.delayCall += Prepare;
      SceneView.duringSceneGui += DrawSkeleton;
      EditorApplication.update += () => { if (Application.isPlaying) SceneView.RepaintAll(); };
    }

    [MenuItem("VTuberCam/Preparar Canvas de MediaPipe en Scene")]
    private static void Prepare()
    {
      if (Application.isPlaying || Object.FindFirstObjectByType<VtuberApp>() == null) return;
      var screen = Object.FindFirstObjectByType<Mediapipe.Unity.Screen>();
      if (!screen) return;

      var root = screen.GetComponent<RectTransform>();
      var canvas = screen.GetComponent<Canvas>();
      if (!canvas)
      {
        // Detach the existing prefab instance, retaining the runner's serialized references.
        Undo.SetTransformParent(root, null, "Move MediaPipe screen into Scene");
        canvas = Undo.AddComponent<Canvas>(screen.gameObject);
        root.sizeDelta = new Vector2(1280, 720);
        root.position = new Vector3(4f, 1.5f, 2f);
        root.rotation = Quaternion.identity;
        root.localScale = Vector3.one * .004f;
        screen.gameObject.name = "MediaPipe Camera Preview (Scene only)";
        foreach (var child in screen.GetComponentsInChildren<Transform>(true))
          child.gameObject.layer = 31;
        EditorSceneManager.MarkSceneDirty(screen.gameObject.scene);
        if (SceneView.lastActiveSceneView != null)
          SceneView.lastActiveSceneView.LookAt(root.position, Quaternion.identity, 3.7f, true);
      }
      canvas.renderMode = RenderMode.WorldSpace;
      canvas.enabled = true;
      var fit = screen.GetComponent<AutoFit>();
      if (fit) fit.enabled = false; // AutoFit requires a parent and this Canvas is now a root.
      var layer = screen.transform.Find("Annotation Layer");
      if (layer)
      {
        var group = layer.GetComponent<CanvasGroup>();
        if (!group) group = Undo.AddComponent<CanvasGroup>(layer.gameObject);
        group.alpha = 0f; // Use clean editor lines instead of the sample's filled annotations.
      }
    }

    private static void DrawSkeleton(SceneView view)
    {
      if (!Application.isPlaying) return;
      var app = Object.FindFirstObjectByType<VtuberApp>();
      if (app == null || !app.ReadyForTracking) return;
      var screen = Object.FindFirstObjectByType<Mediapipe.Unity.Screen>();
      var driver = Object.FindFirstObjectByType<PoseAvatarDriver>();
      if (!screen || !driver) return;
      var pose = driver.GetPoseSnapshot();
      if (!pose.valid) return;
      var rect = screen.GetComponent<RectTransform>();
      var previous = Handles.color;
      Handles.color = new Color(.1f, .92f, .75f);
      for (var i = 0; i < bones.GetLength(0); i++)
      {
        var a = bones[i, 0]; var b = bones[i, 1];
        if (pose.visibility[a] < .5f || pose.visibility[b] < .5f) continue;
        Handles.DrawAAPolyLine(3f, Point(rect, pose, a), Point(rect, pose, b));
      }
      Handles.color = previous;
    }

    private static Vector3 Point(RectTransform rect, VtuberPoseFrame pose, int index)
    {
      return rect.TransformPoint(new Vector3(
        (pose.x[index] - .5f) * rect.rect.width,
        (.5f - pose.y[index]) * rect.rect.height,
        -.01f));
    }
  }
}
#endif
