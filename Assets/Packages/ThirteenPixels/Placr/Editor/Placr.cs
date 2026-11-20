// Copyright © Sascha Graeff/13Pixels.

namespace ThirteenPixels.Placr
{
    using UnityEngine;
    using UnityEditor;
    using UnityEditor.EditorTools;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Placr increases your level design speed by letting you place objects quickly.
    /// </summary>
    // Enable the Placr Tool and select a Prefab or 3D model in your project view.
    // Options are available in the scene view.
    // Controls:
    // * Left click to place the object.
    // * Right click to de-select the object.
    // * Hold Ctrl to rotate the object with your mouse.
    // * Hold Shift to change the vertical offset. Right click while holding Shift to reset.
    [EditorTool(nameof(Placr))]
    internal class Placr : EditorTool
    {
        #region Operations
        private abstract class PlacrOperationBase
        {
            public abstract bool isActive { get; }
            public abstract void Update(Vector3 position, Quaternion rotation);
            public abstract void Apply(PlacrModifierList modifiers);
            public abstract void Abort();
        }

        private class PlaceNewPrefabInstancesOperation : PlacrOperationBase
        {
            private GameObject[] prefabs;
            private GameObject selectedPrefab;
            private Transform prototype;
            private float previewYaw;

            public override bool isActive => prototype != null;


            public PlaceNewPrefabInstancesOperation(GameObject[] prefabs)
            {
                this.prefabs = prefabs;

                SelectRandomPrefab();

                CreatePrototype();
            }

            private void SelectRandomPrefab()
            {
                selectedPrefab = prefabs[Random.Range(0, prefabs.Length)];
                previewYaw = PlacrRandomYRotation.GetRandomYaw();
            }

            private void CreatePrototype()
            {
                var prototypeGameObject = Instantiate(selectedPrefab);
                prototypeGameObject.hideFlags = HideFlags.HideAndDontSave;
                StripPrototypeComponents(prototypeGameObject);

                prototype = prototypeGameObject.transform;
            }

            public override void Update(Vector3 position, Quaternion rotation)
            {
                prototype.position = position;
                prototype.rotation = GetRotation(rotation, selectedPrefab.transform.rotation, previewYaw);
            }

            public override void Apply(PlacrModifierList modifiers)
            {
                var newInstance = (GameObject)PrefabUtility.InstantiatePrefab(selectedPrefab);
                var newTransform = newInstance.transform;
                ApplyParent(newTransform);
                newTransform.position = prototype.position;
                newTransform.rotation = prototype.rotation;

                modifiers.ApplyTo(newInstance);

                Undo.RegisterCreatedObjectUndo(newInstance, "Placr Place Object");

                if (prefabs.Length > 1)
                {
                    var previousSelected = selectedPrefab;
                    SelectRandomPrefab();
                    if (selectedPrefab != previousSelected)
                    {
                        DestroyImmediate(prototype.gameObject);
                        CreatePrototype();
                    }
                }
                else
                {
                    // Reroll so consecutive clicks with a single selected prefab differ as well.
                    previewYaw = PlacrRandomYRotation.GetRandomYaw();
                }
            }

            public override void Abort()
            {
                selectedPrefab = null;
                prefabs = null;

                if (prototype)
                {
                    DestroyImmediate(prototype.gameObject);
                }
            }
        }

        /// <summary>
        /// Places every prefab of <see cref="PlacrPrefabSet"/> with a single click, spaced along the
        /// local axes of the current placement rotation.
        /// </summary>
        private class PlacePrefabSetOperation : PlacrOperationBase
        {
            private readonly List<GameObject> prefabs = new List<GameObject>();
            private readonly List<Transform> prototypes = new List<Transform>();
            private readonly List<float> previewYaws = new List<float>();

            public override bool isActive => prototypes.Count > 0;


            public PlacePrefabSetOperation(List<GameObject> prefabs)
            {
                this.prefabs.AddRange(prefabs);

                foreach (var prefab in this.prefabs)
                {
                    var prototypeGameObject = Instantiate(prefab);
                    prototypeGameObject.hideFlags = HideFlags.HideAndDontSave;
                    StripPrototypeComponents(prototypeGameObject);

                    prototypes.Add(prototypeGameObject.transform);
                    previewYaws.Add(PlacrRandomYRotation.GetRandomYaw());
                }
            }

            public override void Update(Vector3 position, Quaternion rotation)
            {
                var snapEachToGround = PlacrPrefabSet.snapToGround && RaycastModes.activeMode != RaycastModes.FIXED_GROUND_PLANE;

                for (var i = 0; i < prototypes.Count; i++)
                {
                    var elementPosition = position + rotation * GetElementOffset(i, prototypes.Count);
                    if (snapEachToGround)
                    {
                        elementPosition = SnapToGround(elementPosition);
                    }

                    prototypes[i].position = elementPosition;
                    prototypes[i].rotation = GetRotation(rotation, prefabs[i].transform.rotation, previewYaws[i]);
                }
            }

            public override void Apply(PlacrModifierList modifiers)
            {
                Undo.IncrementCurrentGroup();
                var undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Placr Place Prefab Set");

                for (var i = 0; i < prototypes.Count; i++)
                {
                    var newInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i]);
                    var newTransform = newInstance.transform;
                    ApplyParent(newTransform);
                    newTransform.position = prototypes[i].position;
                    newTransform.rotation = prototypes[i].rotation;

                    modifiers.ApplyTo(newInstance);
                    Undo.RegisterCreatedObjectUndo(newInstance, "Placr Place Prefab Set");

                    previewYaws[i] = PlacrRandomYRotation.GetRandomYaw();
                }

                Undo.CollapseUndoOperations(undoGroup);
            }

            public override void Abort()
            {
                prefabs.Clear();

                foreach (var prototype in prototypes)
                {
                    if (prototype)
                    {
                        DestroyImmediate(prototype.gameObject);
                    }
                }

                prototypes.Clear();
                previewYaws.Clear();
            }

            private static Vector3 GetElementOffset(int index, int count)
            {
                var step = PlacrPrefabSet.centerOnCursor ? index - (count - 1) * 0.5f : index;
                return PlacrPrefabSet.spacing * step;
            }
        }

        private class MoveExistingGameObjectOperation : PlacrOperationBase
        {
            private Transform target;

            private Vector3 originalPosition;
            private Quaternion originalRotation;
            private readonly List<Collider> temporarilyDisabledColliders = new List<Collider>();

            public override bool isActive => target != null;


            public MoveExistingGameObjectOperation(GameObject gameObject)
            {
                target = gameObject.transform;
                originalPosition = target.position;
                originalRotation = target.rotation;

                DisableTargetColliders(gameObject);
            }

            private void DisableTargetColliders(GameObject gameObject)
            {
                var colliders = gameObject.GetComponentsInChildren<Collider>();
                foreach (var collider in colliders)
                {
                    if (collider.enabled)
                    {
                        collider.enabled = false;
                        temporarilyDisabledColliders.Add(collider);
                    }
                }
            }

            public override void Update(Vector3 position, Quaternion rotation)
            {
                SetTransform(position, rotation * originalRotation);
            }

            private void SetTransform(Vector3 position, Quaternion rotation)
            {
                target.position = position;
                target.rotation = rotation;
            }

            public override void Apply(PlacrModifierList modifiers)
            {
                foreach (var collider in temporarilyDisabledColliders)
                {
                    var serializedObject = new SerializedObject(collider);
                    PrefabUtility.RevertPropertyOverride(serializedObject.FindProperty("m_Enabled"), InteractionMode.AutomatedAction);
                    serializedObject.ApplyModifiedPropertiesWithoutUndo();
                    if (!collider.enabled)
                    {
                        collider.enabled = true;
                    }
                }
                temporarilyDisabledColliders.Clear();

                var newPosition = target.position;
                var newRotation = target.rotation;

                SetTransform(originalPosition, originalRotation);

                Undo.RecordObject(target, "Placr Move Object");

                SetTransform(newPosition, newRotation);

                target = null;
            }

            public override void Abort()
            {
                Update(originalPosition, originalRotation);
                target = null;
            }
        }
        #endregion

        private static class Settings
        {
            public static class Grid
            {
                public static bool isActive { get; private set; }
                private static Vector3 offset;
                private static Vector3 size = new Vector3(1, 0, 1);

                public static Vector3 Align(Vector3 v)
                {
                    v.x = RoundToNext(v.x, offset.x, size.x);
                    v.y = RoundToNext(v.y, offset.y, size.y);
                    v.z = RoundToNext(v.z, offset.z, size.z);
                    return v;
                }

                public static void DisplayOptionsGUI()
                {
                    isActive = GUILayout.Toggle(isActive, new GUIContent("Grid"));
                    GUI.enabled = isActive;

                    GUILayout.BeginVertical(GUILayout.Width(150));

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Offset", GUILayout.Width(50));
                    offset = EditorGUILayout.Vector3Field(GUIContent.none, offset);
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Size", GUILayout.Width(50));
                    size = EditorGUILayout.Vector3Field(GUIContent.none, size);
                    GUILayout.EndHorizontal();

                    GUILayout.EndVertical();

                    GUI.enabled = true;
                }
            }

            public static class AngleSnap
            {
                private static float snapValue = 0f;

                public static void DisplayOptionsGUI()
                {
                    GUILayout.BeginHorizontal(GUILayout.Width(260));
                    GUILayout.Label("Angle Snap", GUILayout.Width(75));
                    snapValue = EditorGUILayout.FloatField(snapValue);
                    snapValue = Mathf.Clamp(snapValue, 0, 180);

                    void DisplayAngleSnapButton(float angle)
                    {
                        if (GUILayout.Button(angle + "", EditorStyles.miniButton))
                        {
                            snapValue = angle;
                        }
                    }

                    DisplayAngleSnapButton(0);
                    DisplayAngleSnapButton(15);
                    DisplayAngleSnapButton(45);
                    DisplayAngleSnapButton(90);

                    GUILayout.EndHorizontal();
                }

                public static float CalculateSnappedAngle(float unsnappedAngle)
                {
                    if (snapValue > 0)
                    {
                        return Mathf.Round(unsnappedAngle / snapValue) * snapValue;
                    }
                    return unsnappedAngle;
                }
            }
        }

        private static class LastHit
        {
            public static Vector3 position { get; private set; }
            public static Vector3 normal { get; private set; }
            public static float distance { get; private set; }

            public static void Update(Vector3 position, Vector3 normal, float distance)
            {
                LastHit.position = position;
                LastHit.normal = normal;
                LastHit.distance = distance;
            }
        }

        private static class RaycastModes
        {
            private static readonly string[] titles = { "Ground Raycast", "Surface Raycast", "Fixed Ground Plane" };
            public const int SURFACE_RAYCAST = 1;
            public const int FIXED_GROUND_PLANE = 2;

            public static int activeMode { get; private set; } = 0;

            public static void DisplayToolbar()
            {
                activeMode = GUILayout.Toolbar(activeMode, titles);
            }
        }

        private const float windowWidth = 380f;

        private PlacrOperationBase currentOperation;
        private bool isRunningOperation => currentOperation != null;

        private static float verticalOffset = 0f;
        private static float unsnappedYaw = 0f;
        private static float yaw = 0f;

        private Vector2 lastMousePosition;

        // Variables for proper right-click-to-exit detection
        private bool exiting;
        private Vector2 exitClickPosition;

        // Set while a right-click de-selects, so the prefab set operation is not re-armed right away.
        private bool suppressSetRearm;

        [SerializeField, HideInInspector]
        private PlacrModifierList modifierList = default;
        private bool displayModifiers = false;

        [SerializeField]
        private Texture2D toolIcon;
        public GUIContent _toolbarIconContent;
        public override GUIContent toolbarIcon => _toolbarIconContent;

        internal static Placr instance { get; private set; }
        public static bool isActive => instance != null;


        private void OnEnable()
        {
            _toolbarIconContent = new GUIContent()
            {
                image = toolIcon,
                text = " " + nameof(Placr),
                tooltip = nameof(Placr)
            };

#if !UNITY_2020_2_OR_NEWER
            instance = this;
            OnToolActivated();
#endif
        }

#if UNITY_2020_2_OR_NEWER
        public override void OnActivated()
        {
            OnToolActivated();
        }

        public override void OnWillBeDeactivated()
        {
            OnToolDeactivated();
        }
#else
        private static bool placrIsActive = false;

        [InitializeOnLoadMethod]
        private static void Intialize()
        {
            EditorTools.activeToolChanged += OnActiveToolChanged;
        }

        private static void OnActiveToolChanged()
        {
            if (EditorTools.IsActiveTool(instance))
            {
                if (!placrIsActive)
                {
                    instance.OnToolActivated();
                    placrIsActive = true;
                }
            }
            else
            {
                if (placrIsActive)
                {
                    instance.OnToolDeactivated();
                    placrIsActive = false;
                }
            }
        }
#endif

        private void OnToolActivated()
        {
            Selection.selectionChanged += OnSelectionChange;
            EditorApplication.update += RepaintSceneViews;
            ResetVerticalOffset();
            ResetAngle();

            modifierList = CreateInstance<PlacrModifierList>();

            OnSelectionChange();
            instance = this;
        }

        private void OnToolDeactivated()
        {
            Selection.selectionChanged -= OnSelectionChange;
            EditorApplication.update -= RepaintSceneViews;
            AbortCurrentOperation();

            DestroyImmediate(modifierList);
            instance = null;
        }

        private void RepaintSceneViews()
        {
            if (isRunningOperation)
            {
                SceneView.RepaintAll();
            }
        }

        public override void OnToolGUI(EditorWindow window)
        {
#if !UNITY_2021_2_OR_NEWER
            Handles.BeginGUI();
            GUILayout.Window(0, new Rect(10, 30, windowWidth, 0), id => OnGUI(), toolbarIcon);
            if (displayModifiers)
            {
                GUILayout.Window(1, new Rect(10, 200, windowWidth, 0), id => modifierList.Display(), new GUIContent("Modifiers"));
            }
            Handles.EndGUI();
#endif

            if (isRunningOperation)
            {
                var sceneView = window as SceneView;
                if (sceneView && window == EditorWindow.mouseOverWindow)
                {
                    PerformPlacrLogic(sceneView);
                }
            }
            else
            {
                lastMousePosition = Vector2.zero;
            }
        }

#if UNITY_2021_2_OR_NEWER
        public void DrawOverlayGUI()
        {
            GUILayout.BeginVertical(GUILayout.Width(windowWidth));
            GUILayout.Space(8);
            DrawWindow(0, id => OnGUI(), toolbarIcon);
            if (displayModifiers)
            {
                GUILayout.Space(8);
                DrawWindow(1, id => modifierList.Display(), new GUIContent("Modifiers"));
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
        }

        private static void DrawWindow(int id, GUI.WindowFunction func, GUIContent title)
        {
            GUILayout.BeginVertical(title, GUI.skin.GetStyle("Window"));
            func(id);
            GUILayout.EndVertical();
        }
#endif

        /// <summary>
        /// Renders the in-SceneView GUI.
        /// </summary>
        private void OnGUI()
        {
            RaycastModes.DisplayToolbar();

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            Settings.Grid.DisplayOptionsGUI();
            Settings.AngleSnap.DisplayOptionsGUI();
            GUILayout.EndVertical();
            GUILayout.BeginVertical();
            DisplayPickUpButton();
            DisplayModifierButton();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            PlacrParentSettings.DisplayOptionsGUI();
            GUILayout.Space(4);
            PlacrRandomYRotation.DisplayOptionsGUI();
            GUILayout.Space(4);
            PlacrPrefabSet.Display(PlacePrefabSet);
        }

        /// <summary>Arms the tool so the next click places the whole prefab set.</summary>
        private void PlacePrefabSet()
        {
            var prefabs = PlacrPrefabSet.GetPrefabs();
            if (prefabs.Count > 0)
            {
                StartOperation(new PlacePrefabSetOperation(prefabs));
            }
        }

        private void DisplayPickUpButton()
        {
            GUI.enabled = !isRunningOperation &&
                Selection.activeGameObject &&
                !IsPrefab(Selection.activeGameObject);
            if (GUILayout.Button("Pick up", GUILayout.Height(58)))
            {
                StartOperation(new MoveExistingGameObjectOperation(Selection.activeGameObject));
            }
            GUI.enabled = true;
        }

        private void DisplayModifierButton()
        {
            if (modifierList == null)
            {
                modifierList = CreateInstance<PlacrModifierList>();
            }

            var activeModifierCount = modifierList.activeModifierCount;
            var label = $"{modifierList.activeModifierCount} modifier{(modifierList.activeModifierCount == 1 ? "" : "s")}";
            if (GUILayout.Button(label))
            {
                displayModifiers = !displayModifiers;
            }
        }

        /// <summary>
        /// Performs the Placr logic as described in the class summary.
        /// </summary>
        private void PerformPlacrLogic(SceneView sceneView)
        {
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            var e = Event.current;

            var mouseDelta = e.delta;
            if (lastMousePosition != Vector2.zero)
            {
                mouseDelta = e.mousePosition - lastMousePosition;
            }

            if (!e.control && !e.shift)
            {
                EditorGUIUtility.AddCursorRect(sceneView.position, MouseCursor.MoveArrow);
                var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);

                if (RaycastModes.activeMode == RaycastModes.FIXED_GROUND_PLANE)
                {
                    var plane = new Plane(Vector3.up, Vector3.zero);
                    if (plane.Raycast(ray, out var distance))
                    {
                        LastHit.Update(ray.GetPoint(distance), Vector3.up, distance);
                    }
                }
                else
                {
                    RaycastHit hit;
                    if (Physics.Raycast(ray, out hit))
                    {
                        var normal = RaycastModes.activeMode == RaycastModes.SURFACE_RAYCAST ? hit.normal : Vector3.up;
                        LastHit.Update(hit.point, normal, hit.distance);
                    }
                }
            }
            else if (e.control)
            {
                EditorGUIUtility.AddCursorRect(sceneView.position, MouseCursor.RotateArrow);
                unsnappedYaw -= mouseDelta.x;
                unsnappedYaw = Mathf.Repeat(unsnappedYaw, 360f);
                yaw = Settings.AngleSnap.CalculateSnappedAngle(unsnappedYaw);
            }
            else if (e.shift && e.button != 1)
            {
                EditorGUIUtility.AddCursorRect(sceneView.position, MouseCursor.ResizeVertical);
                verticalOffset += mouseDelta.y * -0.001f * LastHit.distance;
            }

            var newPosition = LastHit.position + Vector3.up * verticalOffset;
            if (Settings.Grid.isActive)
            {
                newPosition = Settings.Grid.Align(newPosition);
            }
            var newRotation = Quaternion.AngleAxis(yaw, LastHit.normal) * Quaternion.FromToRotation(Vector3.up, LastHit.normal);
            currentOperation.Update(newPosition, newRotation);

            lastMousePosition = e.mousePosition;

            CheckMouseClicks();
        }

        private static float RoundToNext(float value, float offset, float scale)
        {
            if (scale <= 0) return value;

            value -= offset;
            value /= scale;
            value = Mathf.Round(value);
            return value * scale + offset;
        }

        /// <summary>Removes everything that would make a prototype visible to physics or logic.</summary>
        private static void StripPrototypeComponents(GameObject prototype)
        {
            foreach (var collider in prototype.GetComponentsInChildren<Collider>())
            {
                DestroyImmediate(collider);
            }

            foreach (var script in prototype.GetComponentsInChildren<MonoBehaviour>())
            {
                DestroyImmediate(script);
            }
        }

        /// <summary>Placement rotation, the prefab's own root rotation and the (optional) random yaw.</summary>
        private static Quaternion GetRotation(Quaternion placementRotation, Quaternion prefabRotation, float yaw)
        {
            return placementRotation * prefabRotation * Quaternion.Euler(0f, yaw, 0f);
        }

        private static void ApplyParent(Transform transform)
        {
            var parent = PlacrParentSettings.parent;
            if (parent)
            {
                transform.SetParent(parent, true);
            }
        }

        /// <summary>Drops a position onto whatever collider is right below it.</summary>
        private static Vector3 SnapToGround(Vector3 position)
        {
            const float probeHeight = 100f;
            var origin = position + Vector3.up * probeHeight;

            if (Physics.Raycast(origin, Vector3.down, out var hit, probeHeight * 2f, ~0, QueryTriggerInteraction.Ignore))
            {
                return hit.point;
            }

            return position;
        }

        /// <summary>
        /// Check if the left or right mouse button has been clicked.
        /// Place object or de-select asset accordingly.
        /// </summary>
        private void CheckMouseClicks()
        {
            var e = Event.current;
            if (e.type == EventType.MouseDown && !e.alt)
            {
                if (e.button == 0)
                {
                    currentOperation.Apply(modifierList);
                    if (!currentOperation.isActive)
                    {
                        currentOperation = null;
                    }
                }
                else if (e.button == 1)
                {
                    if (e.shift)
                    {
                        ResetVerticalOffset();
                    }
                    else
                    {
                        exiting = true;
                        exitClickPosition = e.mousePosition;
                    }
                }
            }

            if (exiting)
            {
                var dist = Vector2.Distance(e.mousePosition, exitClickPosition);
                if (dist >= 5)
                {
                    exiting = false;
                }
                else if (e.type == EventType.MouseUp && e.button == 1)
                {
                    AbortCurrentOperation();

                    // Deselecting must not immediately re-arm the prefab set operation.
                    suppressSetRearm = true;
                    try
                    {
                        Selection.activeGameObject = null;
                    }
                    finally
                    {
                        suppressSetRearm = false;
                    }
                }
            }
        }

        private void StartOperation(PlacrOperationBase operation)
        {
            AbortCurrentOperation();

            currentOperation = operation;
        }

        private void AbortCurrentOperation()
        {
            if (currentOperation != null)
            {
                currentOperation.Abort();
                currentOperation = null;
            }
        }

        private void OnSelectionChange()
        {
            AbortCurrentOperation();
            ResetVerticalOffset();

            if (PlacrPrefabSet.isEnabled)
            {
                if (!suppressSetRearm)
                {
                    PlacePrefabSet();
                }
                return;
            }

            var selection = Selection.gameObjects;

            if (selection.Length > 0)
            {
                var selectionIsAllPrefabs = selection.All(go => IsPrefab(go));

                if (selectionIsAllPrefabs)
                {
                    StartOperation(new PlaceNewPrefabInstancesOperation(selection.ToArray()));
                }
            }
        }

        private static bool IsPrefab(GameObject gameObject)
        {
            return gameObject && gameObject.scene.path == null;
        }

        private void ResetVerticalOffset()
        {
            verticalOffset = 0f;
        }

        private void ResetAngle()
        {
            unsnappedYaw = 0f;
            yaw = 0f;
        }
    }
}
