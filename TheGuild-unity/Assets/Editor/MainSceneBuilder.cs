#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using TheGuild.UI.Core;
using TheGuild.UI.Scene;
using TheGuild.UI.Panels;
using TheGuild.Core.Data;
using TheGuild.Core.Time;
using TheGuild.Gameplay.Resources;
using TheGuild.Gameplay.Mission;
using TheGuild.Gameplay.Adventurer;
using TheGuild.Gameplay.Profession;
using TheGuild.Gameplay.Race;
using TheGuild.Gameplay.Trait;
using TheGuild.Gameplay.WorldDanger;
using TheGuild.Gameplay.MissionDispatch;
using TheGuild.Gameplay.Outcome;
using TheGuild.Gameplay.Guild;
using TheGuild.Gameplay.Building;
using TheGuild.Gameplay.Staff;
using TheGuild.Gameplay.Gacha;
using TheGuild.Gameplay.Recruitment;
using TheGuild.Gameplay.Decision;
using TheGuild.Gameplay.FactionStory;
using TheGuild.Gameplay.Save;
using TheGuild.Core.Bootstrap;

namespace TheGuild.EditorTools
{
    public static class MainSceneBuilder
    {
        private const string SCENE_PATH = "Assets/Scenes/MainScene.unity";
        private const string PS_MAIN = "Assets/UI/Settings/PanelSettings_Main.asset";
        private const string PS_OVERLAY = "Assets/UI/Settings/PanelSettings_Overlay.asset";
        private const string PS_HUD = "Assets/UI/Settings/PanelSettings_Hud.asset";
        private const string PS_SCENENAV = "Assets/UI/Settings/PanelSettings_SceneNav.asset";
        private const string UXML_MAIN = "Assets/UI/UXML/MainSceneDocument.uxml";
        private const string UXML_OVERLAY = "Assets/UI/UXML/OverlayPanelDocument.uxml";
        private const string UXML_HUD = "Assets/UI/UXML/HudDocument.uxml";
        private const string UXML_SCENENAV = "Assets/UI/UXML/SceneNavDocument.uxml";
        private const string TUNING_PATH = "Assets/UI/Tuning/P02UITuning.asset";

        [MenuItem("TheGuild/Build Main Scene")]
        public static void BuildMainScene()
        {
            EnsurePanelSettings();
            BuildScene();
            Debug.Log("[MainSceneBuilder] 完成：MainScene + 18 systems + L0 placeholder");
        }

        [MenuItem("TheGuild/Ensure PanelSettings Only")]
        public static void EnsurePanelSettingsOnly()
        {
            EnsurePanelSettings();
        }

        private static void EnsurePanelSettings()
        {
            EnsureFolder("Assets/UI/Settings");
            CreatePanelSettings(PS_MAIN, 0);
            CreatePanelSettings(PS_SCENENAV, 50);
            CreatePanelSettings(PS_HUD, 100);
            CreatePanelSettings(PS_OVERLAY, 200);
            AssetDatabase.SaveAssets();
        }

        private static void CreatePanelSettings(string path, int sortingOrder)
        {
            if (AssetDatabase.LoadAssetAtPath<PanelSettings>(path) != null)
            {
                return;
            }

            PanelSettings ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.sortingOrder = sortingOrder;
            ps.scale = 1.0f;
            ps.match = 0.5f;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            AssetDatabase.CreateAsset(ps, path);
        }

        private static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folderPath).Replace('\\', '/');
            string leaf = Path.GetFileName(folderPath);
            if (!AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static void BuildScene()
        {
            EnsureFolder("Assets/Scenes");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateMainCamera();
            CreateDirectionalLight();
            CreateEventSystem();

            P02UITuning tuning = AssetDatabase.LoadAssetAtPath<P02UITuning>(TUNING_PATH);
            PanelSettings psMain = AssetDatabase.LoadAssetAtPath<PanelSettings>(PS_MAIN);
            PanelSettings psOverlay = AssetDatabase.LoadAssetAtPath<PanelSettings>(PS_OVERLAY);
            PanelSettings psHud = AssetDatabase.LoadAssetAtPath<PanelSettings>(PS_HUD);
            PanelSettings psSceneNav = AssetDatabase.LoadAssetAtPath<PanelSettings>(PS_SCENENAV);
            VisualTreeAsset uxmlMain = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UXML_MAIN);
            VisualTreeAsset uxmlOverlay = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UXML_OVERLAY);
            VisualTreeAsset uxmlHud = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UXML_HUD);
            VisualTreeAsset uxmlSceneNav = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UXML_SCENENAV);
            VisualTreeAsset uxmlLogFloat = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/UXML/LogFloatingWindow.uxml");

            // Backend
            GameObject backendRoot = new GameObject("_Backend");
            backendRoot.AddComponent<RootDetacher>();
            AddBackend<DataManager>(backendRoot, "F-01_DataManager");
            AddBackend<TimeSystem>(backendRoot, "F-02_TimeSystem");
            AddBackend<ResourceManagement>(backendRoot, "F-03_ResourceManagement");
            AddBackend<MissionDatabaseService>(backendRoot, "C-01_MissionDatabase");
            AddBackend<AdventurerRoster>(backendRoot, "C-02_AdventurerRoster");
            AddBackend<ProfessionService>(backendRoot, "C-03_Profession");
            AddBackend<RaceService>(backendRoot, "C-04_Race");
            AddBackend<TraitService>(backendRoot, "C-05_Trait");
            AddBackend<WorldDangerService>(backendRoot, "C-06_WorldDanger");
            AddBackend<CommissionBoardService>(backendRoot, "FT-02-B_CommissionBoard");
            AddBackend<MissionDispatchService>(backendRoot, "FT-02-A_MissionDispatch");
            AddBackend<OutcomeResolutionService>(backendRoot, "FT-04_OutcomeResolution");
            AddBackend<GuildCoreService>(backendRoot, "FT-06_GuildCore");
            AddBackend<BuildingService>(backendRoot, "FT-07_Building");
            AddBackend<StaffService>(backendRoot, "FT-12_Staff");
            AddBackend<GachaService>(backendRoot, "FT-08_Gacha");
            AddBackend<RecruitmentService>(backendRoot, "FT-01_Recruitment");
            AddBackend<NpcDecisionService>(backendRoot, "FT-03_NpcDecision");
            AddBackend<FactionStoryService>(backendRoot, "FT-09_FactionStory");
            AddBackend<SaveLoadService>(backendRoot, "FT-10_SaveLoad");

            // UI
            GameObject uiRoot = new GameObject("_UI");
            uiRoot.AddComponent<RootDetacher>();

            GameObject mainDocGo = NewUIDoc("UIDocument_Main", uiRoot, psMain, uxmlMain);
            GameObject sceneNavDocGo = NewUIDoc("UIDocument_SceneNav", uiRoot, psSceneNav, uxmlSceneNav);
            GameObject hudDocGo = NewUIDoc("UIDocument_Hud", uiRoot, psHud, uxmlHud);
            GameObject overlayDocGo = NewUIDoc("UIDocument_Overlay", uiRoot, psOverlay, uxmlOverlay);

            UIDocument mainDoc = mainDocGo.GetComponent<UIDocument>();
            UIDocument sceneNavDoc = sceneNavDocGo.GetComponent<UIDocument>();
            UIDocument hudDoc = hudDocGo.GetComponent<UIDocument>();
            UIDocument overlayDoc = overlayDocGo.GetComponent<UIDocument>();

            GameObject bootstrapGo = NewChild("UIBootstrap", uiRoot);
            UIBootstrapController bootstrap = bootstrapGo.AddComponent<UIBootstrapController>();
            SetField(bootstrap, "_mainSceneDocument", mainDoc);
            SetField(bootstrap, "_overlayPanelDocument", overlayDoc);

            GameObject panelMgrGo = NewChild("PanelManager", uiRoot);
            PanelManager panelMgr = panelMgrGo.AddComponent<PanelManager>();
            SetField(panelMgr, "_tuning", tuning);
            SetField(panelMgr, "_overlayDocument", overlayDoc);

            NewChild("UITextService", uiRoot).AddComponent<UITextService>();
            NewChild("StoryDialogueQueue", uiRoot).AddComponent<StoryDialogueQueue>();
            NewChild("SceneObjectStateLoader", uiRoot).AddComponent<SceneObjectStateLoader>();
            // DialogueRenderer 為 POCO，由 StoryDialoguePanel 內部 new；無需場景 GameObject。
            // ScreenAnchorCalculator 為 static helper；無需場景 GameObject。

            GameObject hudCtrlGo = NewChild("PersistentHudController", uiRoot);
            PersistentHudController hudCtrl = hudCtrlGo.AddComponent<PersistentHudController>();
            SetField(hudCtrl, "_hudDocument", hudDoc);
            SetField(hudCtrl, "_tuning", tuning);

            GameObject logHostGo = NewChild("LogFloatingWindowHost", uiRoot);
            LogFloatingWindowHost logHost = logHostGo.AddComponent<LogFloatingWindowHost>();
            if (uxmlLogFloat != null)
            {
                SetField(logHost, "_windowTemplate", uxmlLogFloat);
            }

            // Scene Objects
            GameObject sceneObjectsRoot = new GameObject("_SceneObjects");
            MakePlaceholder("Backdrop_Sky", sceneObjectsRoot, new Vector3(0, 2, 10), new Vector3(20, 6, 1), new Color(0.85f, 0.75f, 0.7f), false, false);
            MakePlaceholder("Backdrop_Mountain", sceneObjectsRoot, new Vector3(-7, 0.5f, 9), new Vector3(8, 3, 1), new Color(0.6f, 0.65f, 0.75f), false, false);
            MakePlaceholder("Backdrop_Lake", sceneObjectsRoot, new Vector3(2, -1, 9), new Vector3(20, 2, 1), new Color(0.5f, 0.6f, 0.75f), false, false);
            MakePlaceholder("Boardwalk", sceneObjectsRoot, new Vector3(0, -2.5f, 5), new Vector3(20, 1, 1), new Color(0.45f, 0.32f, 0.2f), false, true);

            GameObject navRoot = NewChild("Nav", sceneObjectsRoot);
            GameObject g1 = MakePlaceholder("nav_commission_board", navRoot, new Vector3(-7.5f, -1.5f, 4), new Vector3(2.0f, 1.6f, 1), new Color(0.55f, 0.4f, 0.25f), true, true);
            GameObject g2 = MakePlaceholder("nav_guild_hall", navRoot, new Vector3(-3.0f, -1.5f, 4), new Vector3(1.5f, 2.0f, 1), new Color(0.5f, 0.35f, 0.25f), true, true);
            GameObject g3 = MakePlaceholder("nav_construction", navRoot, new Vector3(-1.0f, -1.8f, 4), new Vector3(1.0f, 1.0f, 1), new Color(0.65f, 0.55f, 0.3f), true, true);
            GameObject g4 = MakePlaceholder("nav_safe", navRoot, new Vector3(3.5f, -1.6f, 4), new Vector3(0.8f, 0.8f, 1), new Color(0.3f, 0.3f, 0.35f), true, true);
            GameObject g5 = MakePlaceholder("nav_staff_lounge", navRoot, new Vector3(5.5f, -1.7f, 4), new Vector3(1.5f, 0.6f, 1), new Color(0.6f, 0.4f, 0.35f), true, true);
            GameObject g6 = MakePlaceholder("nav_settings_desk", navRoot, new Vector3(7.0f, -1.8f, 4), new Vector3(2.5f, 0.8f, 1), new Color(0.5f, 0.35f, 0.25f), true, true);
            AddTextLabel(g1, "委託板");
            AddTextLabel(g2, "公會大廳");
            AddTextLabel(g3, "建設區");
            AddTextLabel(g4, "保險箱");
            AddTextLabel(g5, "職員休息室");
            AddTextLabel(g6, "辦公桌(設定)");

            GameObject opheliaRoot = NewChild("OpheliaObjects", sceneObjectsRoot);
            GameObject o1 = MakePlaceholder("ophelia_chair", opheliaRoot, new Vector3(7.6f, -1.4f, 3), new Vector3(0.6f, 0.8f, 1), new Color(0.65f, 0.45f, 0.3f), false, true);
            GameObject o2 = MakePlaceholder("ophelia_teacup", opheliaRoot, new Vector3(6.3f, -1.4f, 3), new Vector3(0.25f, 0.25f, 1), new Color(0.85f, 0.85f, 0.85f), false, true);
            GameObject o3 = MakePlaceholder("ophelia_guildbook", opheliaRoot, new Vector3(8.5f, -1.4f, 3), new Vector3(1.4f, 1.6f, 1), new Color(0.45f, 0.3f, 0.2f), false, true);
            GameObject o4 = MakePlaceholder("ophelia_door_note", opheliaRoot, new Vector3(-3.2f, -1.0f, 3), new Vector3(0.3f, 0.4f, 1), new Color(0.95f, 0.9f, 0.75f), false, true);
            AddTextLabel(o1, "[Ophelia椅]");
            AddTextLabel(o2, "[茶杯]");
            AddTextLabel(o3, "[公會書]");
            AddTextLabel(o4, "[門前字條]");

            GameObject decorRoot = NewChild("Decoration", sceneObjectsRoot);
            AddTextLabel(MakePlaceholder("NPC_Receptionist_L", decorRoot, new Vector3(-5.0f, -1.6f, 3.5f), new Vector3(0.7f, 1.4f, 1), new Color(0.95f, 0.8f, 0.75f), false, true), "接待員L");
            AddTextLabel(MakePlaceholder("NPC_Adventurer_Elf", decorRoot, new Vector3(-2.5f, -1.6f, 3.5f), new Vector3(0.8f, 1.5f, 1), new Color(0.4f, 0.6f, 0.4f), false, true), "冒險者(精靈)");
            AddTextLabel(MakePlaceholder("NPC_Adventurer_Knight", decorRoot, new Vector3(-1.6f, -1.6f, 3.5f), new Vector3(0.8f, 1.5f, 1), new Color(0.7f, 0.7f, 0.75f), false, true), "冒險者(騎士)");
            AddTextLabel(MakePlaceholder("Plant_Pot", decorRoot, new Vector3(-0.6f, -1.9f, 3.5f), new Vector3(0.5f, 0.7f, 1), new Color(0.4f, 0.55f, 0.3f), false, true), "盆栽");
            AddTextLabel(MakePlaceholder("NPC_Wizard", decorRoot, new Vector3(2.0f, -1.5f, 3.5f), new Vector3(0.8f, 1.6f, 1), new Color(0.5f, 0.4f, 0.7f), false, true), "巫師");
            AddTextLabel(MakePlaceholder("NPC_Receptionist_R", decorRoot, new Vector3(6.5f, -1.5f, 3.5f), new Vector3(0.7f, 1.4f, 1), new Color(0.4f, 0.35f, 0.4f), false, true), "接待員R(Ophelia)");
            AddTextLabel(MakePlaceholder("Bookshelf", decorRoot, new Vector3(9.0f, -1.3f, 3.5f), new Vector3(1.5f, 2.2f, 1), new Color(0.5f, 0.35f, 0.2f), false, true), "書櫃");

            // Scene controllers (放 _UI 下)
            SceneObjectController sceneCtrl = uiRoot.AddComponent<SceneObjectController>();
            BindSceneObjects(sceneCtrl, "_bindings", new (string, GameObject)[]
            {
                ("ophelia_chair", o1),
                ("ophelia_teacup", o2),
                ("ophelia_guildbook", o3),
                ("ophelia_door_note", o4)
            });

            SceneNavigationController navCtrl = uiRoot.AddComponent<SceneNavigationController>();
            SetField(navCtrl, "_camera", Camera.main);
            SetField(navCtrl, "_sceneDocument", sceneNavDoc);
            SetField(navCtrl, "_tuning", tuning);
            BindNavigations(navCtrl, "_bindings", new (string, GameObject)[]
            {
                ("nav_commission_board", g1),
                ("nav_guild_hall", g2),
                ("nav_construction", g3),
                ("nav_safe", g4),
                ("nav_staff_lounge", g5),
                ("nav_settings_desk", g6)
            });

            // Panels
            GameObject panelsRoot = new GameObject("_Panels");
            panelsRoot.AddComponent<RootDetacher>();
            AddPanel<CommissionBoardPanel>(panelsRoot, "Panel_CommissionBoard");
            // AdventurerRosterPanel 仍保留自身 _document SerializeField；
            // 統一改由 PanelManager._overlayDocument 接管 attach，故不再個別指派。
            AddPanel<AdventurerRosterPanel>(panelsRoot, "Panel_AdventurerRoster");
            AddPanel<GuildBuildingPanel>(panelsRoot, "Panel_GuildBuilding");
            AddPanel<StaffRosterPanel>(panelsRoot, "Panel_StaffRoster");
            AddPanel<StaffGachaPanel>(panelsRoot, "Panel_StaffGacha");
            AddPanel<GuildOverviewPanel>(panelsRoot, "Panel_GuildOverview");
            AddPanel<StoryDialoguePanel>(panelsRoot, "Panel_StoryDialogue");
            AddPanel<ConfirmPopup>(panelsRoot, "Panel_ConfirmPopup");

            EditorSceneManager.SaveScene(scene, SCENE_PATH);
            AddToBuildSettings(SCENE_PATH);
        }

        private static GameObject CreateMainCamera()
        {
            GameObject go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            Camera cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 4f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.1f, 0.1f, 0.12f, 1f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 30f;
            go.AddComponent<AudioListener>();
            go.transform.position = new Vector3(0, 0, -10);
            return go;
        }

        private static GameObject CreateDirectionalLight()
        {
            GameObject go = new GameObject("Directional Light");
            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            go.transform.rotation = Quaternion.Euler(50, -30, 0);
            return go;
        }

        private static GameObject CreateEventSystem()
        {
            GameObject go = new GameObject("EventSystem");
            go.AddComponent<UnityEngine.EventSystems.EventSystem>();
            // InputModule：UI Toolkit 自帶 PanelEventHandler；UGUI 路徑 Post-Jam 再決定是否加 InputModule。
            return go;
        }

        private static GameObject NewUIDoc(string name, GameObject parent, PanelSettings panelSettings, VisualTreeAsset uxml)
        {
            GameObject go = NewChild(name, parent);
            UIDocument doc = go.AddComponent<UIDocument>();
            doc.panelSettings = panelSettings;
            doc.visualTreeAsset = uxml;
            return go;
        }

        private static GameObject NewChild(string name, GameObject parent)
        {
            GameObject go = new GameObject(name);
            if (parent != null)
            {
                go.transform.SetParent(parent.transform, false);
            }
            return go;
        }

        private static GameObject MakePlaceholder(string name, GameObject parent, Vector3 pos, Vector3 size, Color color, bool addCollider, bool addSpriteRenderer)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.position = pos;
            go.transform.localScale = size;

            if (addSpriteRenderer)
            {
                SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = GetWhiteSquareSprite();
                sr.color = color;
                sr.sortingOrder = 0;
            }

            if (addCollider)
            {
                BoxCollider2D col = go.AddComponent<BoxCollider2D>();
                col.size = Vector2.one;
            }

            return go;
        }

        private static Sprite _whiteSprite;
        private static Sprite GetWhiteSquareSprite()
        {
            if (_whiteSprite != null) return _whiteSprite;
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
            tex.Apply();
            _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 2);
            _whiteSprite.name = "PlaceholderWhiteSquare";
            return _whiteSprite;
        }

        private static void AddTextLabel(GameObject parent, string text)
        {
            GameObject labelGo = new GameObject("Label");
            labelGo.transform.SetParent(parent.transform, false);
            labelGo.transform.localPosition = new Vector3(0, 0.6f, -0.1f);
            float sx = parent.transform.localScale.x == 0 ? 1 : parent.transform.localScale.x;
            float sy = parent.transform.localScale.y == 0 ? 1 : parent.transform.localScale.y;
            labelGo.transform.localScale = new Vector3(0.05f / sx, 0.05f / sy, 1);
            TextMesh tm = labelGo.AddComponent<TextMesh>();
            tm.text = text;
            tm.anchor = TextAnchor.LowerCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = 80;
            tm.color = Color.black;
            MeshRenderer mr = labelGo.GetComponent<MeshRenderer>();
            mr.sortingOrder = 1;
        }

        private static T AddPanel<T>(GameObject parent, string name) where T : Component
        {
            GameObject go = NewChild(name, parent);
            return go.AddComponent<T>();
        }

        private static void AddBackend<T>(GameObject parent, string name) where T : Component
        {
            GameObject go = NewChild(name, parent);
            go.AddComponent<T>();
        }

        private static void SetField(Object target, string fieldName, Object value)
        {
            if (target == null) return;
            SerializedObject so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogWarning($"[MainSceneBuilder] field {fieldName} not found on {target.GetType().Name}");
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BindSceneObjects(SceneObjectController target, string fieldName, (string objectID, GameObject go)[] entries)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty list = so.FindProperty(fieldName);
            list.ClearArray();
            for (int i = 0; i < entries.Length; i++)
            {
                list.InsertArrayElementAtIndex(i);
                SerializedProperty elem = list.GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("ObjectID").stringValue = entries[i].objectID;
                elem.FindPropertyRelative("SpriteRenderer").objectReferenceValue = entries[i].go.GetComponent<SpriteRenderer>();
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BindNavigations(SceneNavigationController target, string fieldName, (string objectID, GameObject go)[] entries)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty list = so.FindProperty(fieldName);
            list.ClearArray();
            for (int i = 0; i < entries.Length; i++)
            {
                list.InsertArrayElementAtIndex(i);
                SerializedProperty elem = list.GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("ObjectID").stringValue = entries[i].objectID;
                elem.FindPropertyRelative("SpriteRenderer").objectReferenceValue = entries[i].go.GetComponent<SpriteRenderer>();
                elem.FindPropertyRelative("Collider").objectReferenceValue = entries[i].go.GetComponent<Collider2D>();
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddToBuildSettings(string scenePath)
        {
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path == scenePath)
                {
                    scenes[i].enabled = true;
                    EditorBuildSettings.scenes = scenes.ToArray();
                    return;
                }
            }
            scenes.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
#endif
