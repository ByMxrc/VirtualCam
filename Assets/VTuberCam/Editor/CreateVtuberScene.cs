#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VTuberCam.Editor
{
  public static class CreateVtuberScene
  {
    [MenuItem("VTuberCam/Create migration scene")]
    public static void Create()
    {
      var source = "Assets/MediaPipeUnity/Samples/Scenes/Pose Landmark Detection/Pose Landmark Detection.unity";
      var target = "Assets/VTuberCam/Scenes/VTuberCam.unity";
      var scene = EditorSceneManager.OpenScene(source, OpenSceneMode.Single);
      var root = new GameObject("VTuberCam");
      root.AddComponent<VtuberPoseBridge>();
      root.AddComponent<PoseAvatarDriver>();
      root.AddComponent<VtuberAvatarLoader>();
      root.AddComponent<VtuberApp>();
      EditorSceneManager.SaveScene(scene, target);
      Object.DestroyImmediate(root);
      AssetDatabase.SaveAssets();
      Debug.Log($"VTuberCam scene created at {target}");
    }
  }
}
#endif
