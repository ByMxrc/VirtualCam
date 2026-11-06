# VTuberCam Unity

This is the Unity migration target for the former web prototype.

## Implemented

- Unity `6000.0.58f2`.
- MediaPipe Unity plugin with the official pose sample pipeline.
- glTFast `6.0.1` for runtime loading of the Blockbench `.gltf` avatars.
- Steve and Alex avatar files and pixel-art skins.
- Pose landmarks copied into a thread-safe frame buffer.
- Head, torso and arm rotation with smoothing, deadzone, mirror option and
  return-to-rest behavior.
- Keyboard fallback for arms: `T/G/F/H` left and `I/K/J/L` right.
- BlazePose Full, face and hand model files staged in `Assets/StreamingAssets`.

## Run

Open `Assets/VTuberCam/Scenes/VTuberCam.unity` in Unity. The scene is based on
the MediaPipe pose sample and is the first buildable migration slice.

The original web app remains available in
`C:\Users\ADRIAN\Documents\Three\vtubercam-web`.

## Remaining migration work

The face and hand model files are present, but their runners are not yet wired
into the avatar scene. Blink skin switching, the full IMGUI settings panel,
custom model attachments and UnityCapture output still need to be connected.
