using UnityEditor;
using UnityEngine;

namespace PxP.DOCS
{
    [CustomEditor(typeof(DynamicOcclusionCutoutSystem))]
    public class DynamicOcclusionCutoutSystemEditor : Editor
    {
        // Foldout states
        bool materialFoldout = true;
        bool sceneFoldout = true;
        bool cutoutFoldout = true;

        // Serialized properties
        SerializedProperty m_materials;

        SerializedProperty m_target;

        SerializedProperty m_maskRadius;

        SerializedProperty m_enableGizmos;

        void OnEnable()
        {
            m_materials = serializedObject.FindProperty("m_materials");

            m_target = serializedObject.FindProperty("m_target");

            m_maskRadius = serializedObject.FindProperty("m_maskRadius");

            m_enableGizmos = serializedObject.FindProperty("m_enableGizmos");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawMaterialProperties();
            DrawSceneProperties();
            DrawCutoutParameters();

            GUILayout.Space(10);

            // -- Info --

            GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.padding = new RectOffset(12, 12, 10, 10);
            boxStyle.margin = new RectOffset(0, 0, 8, 8);
            EditorGUILayout.BeginVertical(boxStyle);
            EditorGUILayout.HelpBox("The Sample Version has limited features, for full control and support Upgrade to the Pro Version", MessageType.Info);

            // -- Button - DOCS Pro version --
            GUIStyle style = new GUIStyle(GUI.skin.button);
            style.normal.textColor = Color.white;
            style.fontStyle = FontStyle.Bold;
            
            GUI.backgroundColor = new Color(0.1f, 1.0f, 0.1f);
            
            if (GUILayout.Button("Access DOCS Pro", style, GUILayout.Height(40)))
            {
                Application.OpenURL("https://assetstore.unity.com/packages/slug/335468");
            }

            GUI.backgroundColor = Color.white;
            
            EditorGUILayout.Space(10);


            // -- Buttons - DOCS Documentation & Contact Form --
            GUILayout.BeginVertical();
            if (GUILayout.Button("See Full Documentation"))
            {
                Application.OpenURL("https://pxp-games.com/docs-documentation");
            }
            if (GUILayout.Button("Contact Us"))
            {
                Application.OpenURL("https://pxp-games.com/contact");
            }
            GUILayout.EndVertical();

            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();

            if (GUI.changed)
            {
                EditorUtility.SetDirty(target);
                SceneView.RepaintAll();
            }
        }

        void DrawMaterialProperties()
        {
            materialFoldout = EditorGUILayout.Foldout(materialFoldout, "Material Properties", true);
            if (!materialFoldout) return;

            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(m_materials);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space();
        }

        /// <summary>
        /// Initial properties such as Materials, Scene objects references
        /// </summary>
        void DrawSceneProperties()
        {
            sceneFoldout = EditorGUILayout.Foldout(sceneFoldout, "Scene References", true);
            if (!sceneFoldout) return;

            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(m_target);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space();
        }
        void DrawCutoutParameters()
        {
            cutoutFoldout = EditorGUILayout.Foldout(cutoutFoldout, "Cutout Parameters", true);
            if (!cutoutFoldout) return;
            EditorGUI.indentLevel++;
            // Detection Type
            EditorGUILayout.PropertyField(m_maskRadius);
            m_enableGizmos.boolValue = GUILayout.Toggle(m_enableGizmos.boolValue, "Enable Gizmos");
            EditorGUI.indentLevel--;
            EditorGUILayout.Space();
        }
    }
}


