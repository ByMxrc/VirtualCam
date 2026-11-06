using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;

namespace VTuberCam
{
  public sealed class VtuberAvatarLoader : MonoBehaviour
  {
    [SerializeField] private bool steve = true;
    [SerializeField] private Transform avatarRoot;
    [SerializeField] private PoseAvatarDriver driver;
    private GameObject loadedAvatar;
    private Texture2D activeSkin;
    private byte[] customSkinBytes;
    private Texture2D blinkSkin;
    private bool blinkEnabled;
    private bool automaticBlink;
    private bool eyesClosed;
    private float nextBlink;
    private float blinkUntil;
    private int loadVersion;

    public bool IsSteve => steve;
    public GameObject LoadedAvatar => loadedAvatar;
    public Texture2D ActiveSkin => activeSkin;
    public Texture2D CurrentSkin => eyesClosed && blinkEnabled && blinkSkin ? blinkSkin : activeSkin;
    public bool BlinkEnabled { get => blinkEnabled; set { blinkEnabled = value; if (!value) SetEyesClosed(false); else nextBlink = Time.time + UnityEngine.Random.Range(1.5f,3.5f); } }
    public bool AutomaticBlink { get => automaticBlink; set { automaticBlink = value; SetEyesClosed(false); nextBlink = Time.time + UnityEngine.Random.Range(1.5f,3.5f); } }
    public bool HasBlinkSkin => blinkSkin;
    public void SetTrackedBlink(bool closed)
    {
      if (blinkEnabled && !automaticBlink && blinkSkin) SetEyesClosed(closed);
    }

    public bool SetBlinkSkinFromFile(string path, out string error)
    {
      error = null;
      try
      {
        if (!string.Equals(Path.GetExtension(path),".png",StringComparison.OrdinalIgnoreCase))
        { error = "Selecciona una skin PNG."; return false; }
        var image = new Texture2D(2,2,TextureFormat.RGBA32,false);
        if (!image.LoadImage(File.ReadAllBytes(path)) || image.width != 64 || image.height != 64)
        { Destroy(image); error = "La skin de ojos cerrados debe medir 64 × 64 píxeles."; return false; }
        image.filterMode = FilterMode.Point;
        image.wrapMode = TextureWrapMode.Clamp;
        var old = blinkSkin;
        blinkSkin = image;
        if (old) Destroy(old);
        ApplySkin();
        return true;
      }
      catch (Exception exception) { error = "No se pudo cargar la segunda skin: " + exception.Message; return false; }
    }

    private void Update()
    {
      if (!blinkEnabled || !automaticBlink || !blinkSkin || !loadedAvatar) return;
      if (eyesClosed)
      {
        if (Time.time >= blinkUntil) { SetEyesClosed(false); nextBlink = Time.time + UnityEngine.Random.Range(2f,5f); }
      }
      else if (Time.time >= nextBlink)
      {
        SetEyesClosed(true);
        blinkUntil = Time.time + .13f;
      }
    }

    private void SetEyesClosed(bool closed)
    {
      if (eyesClosed == closed) return;
      eyesClosed = closed;
      ApplySkin();
    }

    public bool SetSkinFromFile(string path, out string error)
    {
      error = null;
      if (loadedAvatar == null) { error = "Primero carga un avatar."; return false; }
      try
      {
        if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
        { error = "Selecciona una imagen PNG."; return false; }
        var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        var bytes = File.ReadAllBytes(path);
        if (!image.LoadImage(bytes) || image.width != 64 || image.height != 64)
        {
          Destroy(image);
          error = "La skin debe ser PNG de 64 × 64 píxeles.";
          return false;
        }
        var previous = activeSkin;
        customSkinBytes = bytes;
        activeSkin = image;
        ApplySkin();
        if (previous) Destroy(previous);
        return true;
      }
      catch (Exception exception)
      {
        error = "No se pudo abrir la skin: " + exception.Message;
        return false;
      }
    }

    private async void Start()
    {
      if (driver == null) driver = GetComponent<PoseAvatarDriver>();
      await LoadAsync(steve);
    }

    public async Task LoadAsync(bool loadSteve)
    {
      var version = ++loadVersion;
      steve = loadSteve;
      if (loadedAvatar != null) Destroy(loadedAvatar);
      loadedAvatar = null;
      if (activeSkin != null) Destroy(activeSkin);
      activeSkin = null;
      eyesClosed = false;
      var fileName = steve ? "steve.gltf" : "alex.gltf";
      var path = Path.Combine(Application.streamingAssetsPath, "Avatars", fileName);
      if (!File.Exists(path))
      {
        Debug.LogError($"No se encontró el avatar: {path}");
        return;
      }

      var import = new GltfImport();
      if (!await import.Load(path))
      {
        Debug.LogError($"No se pudo cargar el glTF: {path}");
        return;
      }
      if (version != loadVersion || this == null) return;

      loadedAvatar = new GameObject(steve ? "Steve" : "Alex");
      loadedAvatar.transform.SetParent(avatarRoot != null ? avatarRoot : transform, false);
      var instantiator = new GameObjectInstantiator(import, loadedAvatar.transform);
      if (!await import.InstantiateMainSceneAsync(instantiator))
      {
        Debug.LogError($"No se pudo instanciar el glTF: {path}");
        Destroy(loadedAvatar);
        loadedAvatar = null;
        return;
      }
      if (version != loadVersion || this == null) { Destroy(loadedAvatar); loadedAvatar = null; return; }

      loadedAvatar.transform.localRotation = Quaternion.identity;
      if (customSkinBytes != null)
      {
        activeSkin = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        activeSkin.LoadImage(customSkinBytes, false);
      }
      else
        activeSkin = await LoadSkinAsync(steve ? "steve_skin.png" : "alex_skin.png");
      if (version != loadVersion || this == null) return;
      ApplySkin();
      var body = FindPivot("Body");
      var head = FindPivot("Head");
      var left = FindPivot("Left Arm");
      var right = FindPivot("Right Arm");
      if (driver == null) driver = GetComponent<PoseAvatarDriver>();
      driver?.Bind(body, head, left, right);
    }

    private static async Task<Texture2D> LoadSkinAsync(string fileName)
    {
      var path = Path.Combine(Application.streamingAssetsPath, "Textures", fileName);
      if (!File.Exists(path))
      {
        Debug.LogError($"No se encontró la skin: {path}");
        return null;
      }

      var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
      texture.LoadImage(await Task.Run(() => File.ReadAllBytes(path)), false);
      return texture;
    }

    private void ApplySkin()
    {
      if (loadedAvatar == null || activeSkin == null) return;
      var displayed = CurrentSkin;
      activeSkin.filterMode = FilterMode.Point;
      activeSkin.wrapMode = TextureWrapMode.Clamp;
      foreach (var renderer in loadedAvatar.GetComponentsInChildren<Renderer>(true))
      {
        foreach (var material in renderer.materials)
        {
          material.mainTexture = displayed;
          if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex",displayed);
          if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap",displayed);
          material.color = Color.white;
          material.renderQueue = 2450;
        }
      }
    }

    private Transform FindPivot(string target)
    {
      foreach (var child in loadedAvatar.GetComponentsInChildren<Transform>(true))
      {
        var normalized = child.name.Replace(" ", string.Empty).Replace("_", string.Empty);
        if (string.Equals(normalized, target.Replace(" ", string.Empty), StringComparison.OrdinalIgnoreCase))
          return child;
      }
      return null;
    }
  }
}
