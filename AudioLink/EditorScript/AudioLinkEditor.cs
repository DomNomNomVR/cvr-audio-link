#if UNITY_EDITOR

using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using System;
using UnityEditor;
using UnityEngine.Audio;
using UnityEngine.Rendering;
using UnityEngine;

using ABI.CCK.Components;



namespace AudioLink {

    [InitializeOnLoad]
    public class AudioLinkEditor : EditorWindow {
        public enum InputType {
            Microphone,
            SoundFile
        }


        public static CVRAudioMaterialParser audioMaterialParser;
        public static InputType inputType = InputType.Microphone;
        public static AudioSource audioSource = null;
        public static string microphoneName = "";
        public static AudioClip audioClip = null;
        public static bool audioClipMuted = false;

        static AudioLinkEditor() {
            UnityEditor.EditorApplication.update += MyUpdate;
        }

        public void OnEnable() {
            if (!audioMaterialParser) {
                audioMaterialParser = GameObject.FindObjectOfType<CVRAudioMaterialParser>();
            }
            if (audioMaterialParser) {
                audioSource = audioMaterialParser.GetComponentInChildren<AudioSource>();
            }
            if (microphoneName.Length == 0 && Microphone.devices.Length > 0) {
                int bestMicIndex = 0;
                void prioritize(string deviceNameSnippet) {
                    for (int i=0; i<Microphone.devices.Length; ++i) {
                        if (Microphone.devices[i].ToLower().Contains(deviceNameSnippet)) {
                            bestMicIndex = i;
                            return;
                        }
                    }
                }
                // Prioritize virtual audio cables that carry music.
                prioritize("cable");
                prioritize("music");
                microphoneName = Microphone.devices[bestMicIndex];
            }

            ApplySavedState();

        }

        public void OnGUI() {
            int total_height = 2;
            int margin_bottom = 2;
            int margin_left = 10;
            Rect R(int height) {
                // Add a row and return a new Rect
                float y = total_height;
                total_height += height;
                total_height += margin_bottom;
                return new Rect(margin_left, y, position.width - margin_left, height);
            }
            R(0); // Margin top

            audioMaterialParser = EditorGUI.ObjectField(R(20), "Controller Sync", audioMaterialParser, typeof(CVRAudioMaterialParser), true) as CVRAudioMaterialParser;
            if (!audioMaterialParser) {
                audioMaterialParser = GameObject.FindObjectOfType<CVRAudioMaterialParser>();
            }
            if (!audioMaterialParser) {
                StopAllClips();
                if (GUI.Button(R(40), "Add AudioLink to scene")) {
                    PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/AudioLink/AudioLinkController.prefab"
                    ));
                }
                return;
            }

            // InputType
            {
                EditorGUI.LabelField(R(20), "Input Type:");
                void handleInputTypeDropdownItemClicked(object parameter) {
                    inputType = (InputType)parameter;
                    StartMicrophone();
                }
                GenericMenu menu = new GenericMenu();
                foreach (var inputType in (InputType[])Enum.GetValues(typeof(InputType))) {
                    menu.AddItem(new GUIContent($"{inputType}"), false, handleInputTypeDropdownItemClicked, inputType);
                }
                if (EditorGUI.DropdownButton(R(20), new GUIContent(inputType.ToString()), FocusType.Keyboard)) {
                    menu.ShowAsContext();
                }
            }


            if (inputType == InputType.Microphone) {
                // Microphone
                EditorGUI.LabelField(R(20), "Microphone device:");
                void handleDropdownItemClicked(object parameter) {
                    microphoneName = parameter as string;
                    StartMicrophone();
                }
                GenericMenu menu = new GenericMenu();
                foreach (var device in Microphone.devices) {
                    menu.AddItem(new GUIContent($"{device}"), false, handleDropdownItemClicked, device);
                }
                if (EditorGUI.DropdownButton(R(20), new GUIContent(microphoneName), FocusType.Keyboard)) {
                    menu.ShowAsContext();
                }
                EditorGUI.HelpBox(R(60), "You can use something like Virtual Audio Cable to create a microphone device which plays your music that you jam to while working on your shaders.", MessageType.Info);
            } else if (inputType == InputType.SoundFile) {
                audioClip = EditorGUI.ObjectField(R(20), "AudioClip", audioClip, typeof(AudioClip), true) as AudioClip;
                audioClipMuted = EditorGUI.Toggle(R(20), "Mute", audioClipMuted);
            }

            ApplySavedState();
        }

        static void ApplySavedState() {
            // Find the audioSources
            if (!audioMaterialParser) return;
            audioSource = audioMaterialParser.GetComponentInChildren<AudioSource>();

            if (inputType == InputType.Microphone) {
                StopAllClips();
                StartMicrophone();
            } else if (inputType == InputType.SoundFile) {
                if (audioSource.clip != audioClip) {
                    audioSource.clip = audioClip;
                    StopAllClips();
                }
                if (!audioSource.isPlaying) {
                    audioSource.Play();
                }
                if (audioClipMuted) {
                    StopAllClips();
                } else if (audioClip && !IsClipPlaying(audioClip)) {
                    PlayClip(audioClip, audioSource.timeSamples, true);
                }
            }
        }

        static void StartMicrophone() {
            try {
                audioSource.clip = Microphone.Start(microphoneName, true, 1, 44100);
            } catch (ArgumentException err) {
                if (microphoneName != "") throw err;
            }
            if (!audioSource.isPlaying) {
                audioSource.Play();
            }
        }

        public static void MyUpdate() {
            if (!audioMaterialParser) {
                return; // Audiolink controller has been deleted.
            }
            SetMaterialPropertiesFromSliders();
            SendAudioOutputData();
            CustomRenderTexture customRenderTexture = audioMaterialParser.GetComponent<CVRCustomRenderTextureUpdater>().customRenderTexture;
            // customRenderTexture.Update();
            Shader.SetGlobalTexture("_AudioTexture", customRenderTexture, RenderTextureSubElement.Default);

            // https://forum.unity.com/threads/recording-audio-with-microphone-in-editor-mode-not-playing.1044007/
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            // UnityEditor.SceneView.RepaintAll();
        }

        static void SetMaterialPropertiesFromSliders() {
            foreach (CVRVariableBuffer buffer in audioMaterialParser.GetComponentsInChildren<CVRVariableBuffer>()) {
                CVRGlobalMaterialPropertyUpdater updater = buffer.GetComponent<CVRGlobalMaterialPropertyUpdater>();
                Debug.Assert(updater.propertyType == CVRGlobalMaterialPropertyUpdater.PropertyType.paramFloat);
                Debug.Assert(updater.material);
                updater.material.SetFloat(updater.propertyName, buffer.defaultValue);
            }
        }


        [MenuItem("Tools/AudioLinkEditor")]
        public static void ShowMyEditor()
        {
          EditorWindow wnd = GetWindow<AudioLinkEditor>();
          wnd.titleContent = new GUIContent("AudioLinkEditor");
        }

        // -------- below this, we're adating code from AudioLink.cs, found in the VRChat version --------




        static int _rightChannelTestCounter = 0;
        private static void SendAudioOutputData() {
            float[] _spectrumValues = new float[1024];
            float[] _spectrumValuesTrim = new float[1023];
            float[] _audioFramesL = new float[1023 * 4];
            float[] _audioFramesR = new float[1023 * 4];
            float[] _samples = new float[1023];
            // Fix for AVPro mono game output bug (if running the game with a mono output source like a headset)
            int _rightChannelTestDelay = 300;
            bool _ignoreRightChannel = false;

            if (!audioMaterialParser || !audioSource) {
                return; // Audiolink controller has been deleted.
            }
            Material audioMaterial = audioMaterialParser.processingMaterial;
            audioSource.GetOutputData(_audioFramesL, 0);                // left channel
            // Debug.Log(_audioFramesL[1]);

            if (_rightChannelTestCounter > 0)
            {
                if (_ignoreRightChannel) {
                    System.Array.Copy(_audioFramesL, 0, _audioFramesR, 0, 4092);
                } else {
                    audioSource.GetOutputData(_audioFramesR, 1);
                }
                _rightChannelTestCounter--;
            } else {
                _rightChannelTestCounter = _rightChannelTestDelay;      // reset test countdown
                _audioFramesR[0] = 0f;                                  // reset tested array element to zero just in case
                audioSource.GetOutputData(_audioFramesR, 1);            // right channel test
                _ignoreRightChannel = (_audioFramesR[0] == 0f) ? true : false;
            }

            System.Array.Copy(_audioFramesL, 0, _samples, 0, 1023); // 4092 - 1023 * 4
            audioMaterial.SetFloatArray("_Samples0L", _samples);
            System.Array.Copy(_audioFramesL, 1023, _samples, 0, 1023); // 4092 - 1023 * 3
            audioMaterial.SetFloatArray("_Samples1L", _samples);
            System.Array.Copy(_audioFramesL, 2046, _samples, 0, 1023); // 4092 - 1023 * 2
            audioMaterial.SetFloatArray("_Samples2L", _samples);
            System.Array.Copy(_audioFramesL, 3069, _samples, 0, 1023); // 4092 - 1023 * 1
            audioMaterial.SetFloatArray("_Samples3L", _samples);

            System.Array.Copy(_audioFramesR, 0, _samples, 0, 1023); // 4092 - 1023 * 4
            audioMaterial.SetFloatArray("_Samples0R", _samples);
            System.Array.Copy(_audioFramesR, 1023, _samples, 0, 1023); // 4092 - 1023 * 3
            audioMaterial.SetFloatArray("_Samples1R", _samples);
            System.Array.Copy(_audioFramesR, 2046, _samples, 0, 1023); // 4092 - 1023 * 2
            audioMaterial.SetFloatArray("_Samples2R", _samples);
            System.Array.Copy(_audioFramesR, 3069, _samples, 0, 1023); // 4092 - 1023 * 1
            audioMaterial.SetFloatArray("_Samples3R", _samples);
        }







        // Based on https://answers.unity.com/questions/844896/how-to-play-audioclip-from-editor-from-a-start-sam.html
        // Upgraded with https://forum.unity.com/threads/audio-in-editor-mode.902921/

        public static void PlayClip(AudioClip clip , int startSample , bool loop) {
            Assembly unityEditorAssembly = typeof(AudioImporter).Assembly;
            Type audioUtilClass = unityEditorAssembly.GetType("UnityEditor.AudioUtil");
            MethodInfo method = audioUtilClass.GetMethod(
                "PlayPreviewClip",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new System.Type[] {
                typeof(AudioClip),
                typeof(Int32),
                typeof(Boolean)
            },
            null
            );
            method.Invoke(
                null,
                new object[] {
                clip,
                startSample,
                loop
            }
            );
        }

        public static void StopAllClips () {
            Assembly unityEditorAssembly = typeof(AudioImporter).Assembly;
            Type audioUtilClass = unityEditorAssembly.GetType("UnityEditor.AudioUtil");
            MethodInfo method = audioUtilClass.GetMethod(
                "StopAllPreviewClips",
                BindingFlags.Static | BindingFlags.Public
                );

            method.Invoke(
                null,
                null
                );
        }

        public static bool IsClipPlaying(AudioClip clip) {
            Assembly unityEditorAssembly = typeof(AudioImporter).Assembly;
            Type audioUtilClass = unityEditorAssembly.GetType("UnityEditor.AudioUtil");
            MethodInfo method = audioUtilClass.GetMethod(
                "IsClipPlaying",
                BindingFlags.Static | BindingFlags.Public
                );

            bool playing = (bool)method.Invoke(
                null,
                new object[] {
                clip,
            }
            );

            return playing;
        }
    }
}

#endif
