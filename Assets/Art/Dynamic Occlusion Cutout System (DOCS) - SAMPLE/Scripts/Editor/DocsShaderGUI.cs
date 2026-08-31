using UnityEditor;
using UnityEngine;

namespace PxP.DOCS {
  public class DocsShaderGUI : ShaderGUI {
    bool surfaceInputsFoldout = true;
    bool DOCSFoldout = true;
    bool advancedOptionsFoldout = false;

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties) {
      Material targetMat = materialEditor.target as Material;

      // Base Map
      MaterialProperty mainTex = FindProperty("_MainTex", properties);
      MaterialProperty baseColor = FindProperty("_Color", properties);

      // DOCS Pro Supports:
      // Metallic & Smoothness,
      // Normal
      // Height (Parallax)
      // Occlusion
      // Emission
      // Tiling & Offset

      // Upgrade the system at: https://assetstore.unity.com/packages/slug/335468

      // DOCS parameters
      MaterialProperty targetPosition = FindProperty("_Target_Position", properties);
      MaterialProperty radius = FindProperty("_Radius", properties);
      MaterialProperty maskTexture = FindProperty("_Mask_Texture", properties);

      // Manually exposed properties (added on top of the sample GUI)
      MaterialProperty emissionColor = FindProperty("_EmissionColor", properties, false);


      // ============================
      // Surface Inputs
      // ============================

      surfaceInputsFoldout =
          EditorGUILayout.BeginFoldoutHeaderGroup(surfaceInputsFoldout, "Surface Inputs");

      if (surfaceInputsFoldout) {
        EditorGUI.indentLevel++;
        // Base Color
        materialEditor.TexturePropertySingleLine(new GUIContent("Base Map"), mainTex, baseColor);

        // Emission color (manually exposed — not part of the sample GUI's fixed field list)
        if (emissionColor != null)
          materialEditor.ColorProperty(emissionColor, "Emission Color");
        EditorGUI.indentLevel--;

        GUILayout.Space(10);


        GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.padding = new RectOffset(12, 12, 10, 10);
        boxStyle.margin = new RectOffset(0, 0, 8, 8);
        EditorGUILayout.BeginVertical(boxStyle);

        EditorGUILayout.HelpBox("The DOCS Sample Version has limited features.\n For full material and texture channels support Upgrade to the Pro version", MessageType.Info);
        GUIStyle style = new GUIStyle(GUI.skin.button);
        style.normal.textColor = Color.white;
        style.fontStyle = FontStyle.Bold;

        GUI.backgroundColor = new Color(0.1f, 1.0f, 0.1f);
        if (GUILayout.Button("Access DOCS Pro", style, GUILayout.Height(40))) {
          Application.OpenURL("https://assetstore.unity.com/packages/slug/335468");
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndVertical();

      }

      EditorGUILayout.EndFoldoutHeaderGroup();

      // ============================
      // DOCS Options
      // ============================

      DOCSFoldout =
          EditorGUILayout.BeginFoldoutHeaderGroup(DOCSFoldout, "DOCS Options");

      if (DOCSFoldout) {
        EditorGUILayout.Space();
        EditorGUI.indentLevel++;
        // Mask texture
        materialEditor.TexturePropertySingleLine(new GUIContent("Mask Texture"), maskTexture);

        // Radius
        EditorGUI.BeginChangeCheck();
        float currentRadius = radius.floatValue;
        float newRadius = EditorGUILayout.FloatField("Radius", currentRadius);
        if (EditorGUI.EndChangeCheck()) {
          radius.floatValue = Mathf.Max(0, newRadius);
        }

        // Target position
        materialEditor.VectorProperty(targetPosition, "Target Position");

        EditorGUI.indentLevel--;
      }

      EditorGUILayout.EndFoldoutHeaderGroup();

      // ============================
      // Advanced Options
      // ============================
      advancedOptionsFoldout =
          EditorGUILayout.BeginFoldoutHeaderGroup(advancedOptionsFoldout, "Advanced Options");

      if (advancedOptionsFoldout) {
        EditorGUI.indentLevel++;

        materialEditor.EnableInstancingField();
        materialEditor.RenderQueueField();
        materialEditor.DoubleSidedGIField();

        EditorGUI.indentLevel--;
      }

      EditorGUILayout.EndFoldoutHeaderGroup();
    }
  }
}