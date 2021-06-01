using System;
using UnityEditor;
using UnityEngine;

namespace UniVRM10
{
    /// <summary>
    /// VRM10操作 Window
    /// </summary>
    public class VRM10Window : EditorWindow
    {
        const string MENU_KEY = VRMVersion.MENU + "/VRM1 Window";
        const string WINDOW_TITLE = "VRM1 Window";

        [MenuItem(MENU_KEY, false, 1)]
        private static void ExportFromMenu()
        {
            var window = (VRM10Window)GetWindow(typeof(VRM10Window));
            window.titleContent = new GUIContent(WINDOW_TITLE);
            window.Show();
            window.Root = UnityEditor.Selection.activeTransform.GetComponent<VRM10Controller>();
        }

        void OnEnable()
        {
            // Debug.Log("OnEnable");
            Undo.willFlushUndoRecord += Repaint;
            UnityEditor.Selection.selectionChanged += Repaint;

            SceneView.onSceneGUIDelegate += OnSceneGUI;
        }

        void OnDisable()
        {
            SpringBoneEditor.Disable();

            SceneView.onSceneGUIDelegate -= OnSceneGUI;
            // Debug.Log("OnDisable");
            UnityEditor.Selection.selectionChanged -= Repaint;
            Undo.willFlushUndoRecord -= Repaint;

            Tools.hidden = false;
        }

        SerializedObject m_so;
        VRM10Controller m_root;
        VRM10Controller Root
        {
            get => m_root;
            set
            {
                if (m_root == value)
                {
                    return;
                }
                if (value != null && !value.gameObject.scene.IsValid())
                {
                    // skip prefab
                    return;
                }
                m_root = value;
                m_so = m_root != null ? new SerializedObject(m_root) : null;

                m_constraints = null;
            }
        }

        public VRM10Constraint[] m_constraints;

        ScrollView m_scrollView = new ScrollView();

        enum VRMSceneUI
        {
            Constraints,
            LookAt,
            SpringBone,
        }
        static VRMSceneUI s_ui = default;
        static string[] s_selection;
        static string[] Selection
        {
            get
            {
                if (s_selection == null)
                {
                    s_selection = Enum.GetNames(typeof(VRMSceneUI));
                }
                return s_selection;
            }
        }

        /// <summary>
        /// public entry point
        /// </summary>
        /// <param name="target"></param>
        void OnSceneGUI(SceneView sceneView)
        {
            switch (s_ui)
            {
                case VRMSceneUI.Constraints:
                    Tools.hidden = false;
                    break;

                case VRMSceneUI.LookAt:
                    Tools.hidden = true;
                    LookAtEditor.Draw3D(Root);
                    break;

                case VRMSceneUI.SpringBone:
                    Tools.hidden = true;
                    SpringBoneEditor.Draw3D(Root, m_so);
                    break;

                default:
                    throw new NotImplementedException();
            }
        }

        //
        private void OnGUI()
        {
            Root = (VRM10Controller)EditorGUILayout.ObjectField("vrm1", Root, typeof(VRM10Controller), true);
            if (Root == null)
            {
                return;
            }

            var ui = (VRMSceneUI)GUILayout.SelectionGrid((int)s_ui, Selection, 3);
            if (s_ui != ui)
            {
                s_ui = ui;
                SceneView.RepaintAll();
            }

            if (m_so == null)
            {
                m_so = new SerializedObject(Root);
            }
            if (m_so == null)
            {
                return;
            }

            m_so.Update();
            switch (s_ui)
            {
                case VRMSceneUI.Constraints:
                    m_scrollView.Draw(this.position.y, DrawConstraints, Repaint);
                    break;

                case VRMSceneUI.LookAt:
                    LookAtEditor.Draw2D(Root);
                    break;

                case VRMSceneUI.SpringBone:
                    SpringBoneEditor.Draw2D(Root, m_so);
                    break;

                default:
                    throw new NotImplementedException();
            }

            m_so.ApplyModifiedProperties();
        }

        void DrawConstraints()
        {
            if (Root != null)
            {
                if (m_constraints == null)
                {
                    m_constraints = Root.GetComponentsInChildren<VRM10Constraint>();
                }
            }

            using (new EditorGUI.DisabledGroupScope(true))
            {
                if (m_constraints != null)
                {
                    foreach (var c in m_constraints)
                    {
                        EditorGUILayout.ObjectField(c, typeof(VRM10Constraint), true);
                    }
                }
            }
        }
    }
}
