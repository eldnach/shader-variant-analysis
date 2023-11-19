using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEditor.Rendering;

public class VariantTracingEditor : EditorWindow
{
    static VisualTreeAsset EditorTemplate;
    static VisualTreeAsset TraceTemplate;
    static VisualTreeAsset TraceFieldTemplate;
    static VisualTreeAsset ShaderTemplate;
    static VisualTreeAsset VariantTemplate;
    static VisualTreeAsset KeywordTemplate;
    static VisualTreeAsset KeywordLabelTemplate;
    static VisualTreeAsset StageLabelTemplate;
    static VisualTreeAsset PlatformLabelTemplate;
    static VisualTreeAsset SubshaderLabelTemplate;
    static VisualTreeAsset PassLabelTemplate;
    static VisualTreeAsset TimestampTemplate;
    static VisualTreeAsset WarningTemplate;

    VisualElement editorInstance;
    VisualElement shadersView;
    VisualElement tracesView;
    VisualElement loadTracesView;
    VisualElement consoleMessageContainer;

    public enum ShaderCompilationType
    {
        Normal,
        BuiltTrace,
        RequestedTrace,
    }

    private static readonly string[] kCompilationTypeDescriptions = new[]
    {
        "Normal",
        "Build only the variants from the 'built' trace",
        "Build only the variants from the 'requested' trace",
    };

    private static ShaderCompilationType CompilationTypeFromDescription(string value)
    {
        int numTypes = Enum.GetValues(typeof(ShaderCompilationType)).Length;
        for (int i = 0; i < numTypes; ++i)
        {
            if (value.Equals(kCompilationTypeDescriptions[i]))
                return (ShaderCompilationType)i;
        }

        return ShaderCompilationType.Normal;
    }

    List<string> compilationOptions;
    public ShaderCompilationType CompilationType;
    public bool ExcludeUnusedVariants;
    public bool IncludeMissingVariants;

    TextAsset TraceFile0;

    List<VariantTraceEntry> builtVariantsData;
    List<VariantTraceEntry> usedVariantsData;
    List<VariantTraceEntry> unusedVariantsData;
    List<VariantTraceEntry> missingVariantsData;

    VariantTraceEntry[] variantsToBuild;

    Button ascendingBuiltVariants;
    Button descendingBuiltVariants;

    SortMode sortMode;
    enum SortMode
    {
        BuiltVariantsDescending = 0,
        BuiltVariantsAscending = 1,
    }


    enum ShaderStage
    {
        NONE = 0,
        VERTEX = 1,
        FRAGMENT = 2,
        GEOMETRY = 3,
        HULL = 4,
        DOMAIN = 5,
        RAYTRACING = 6
    }

    public const double kUsedVariantWarningThreshold = 0.5;

    void LoadResources()
    {
        EditorTemplate =  (VisualTreeAsset)AssetDatabase.LoadAssetAtPath("Packages/com.eldnach.shader-variant-analysis/Editor/Resources/ShaderVariantTracing.uxml", typeof(VisualTreeAsset));
        TraceFieldTemplate = (VisualTreeAsset)AssetDatabase.LoadAssetAtPath("Packages/com.eldnach.shader-variant-analysis/Editor/Resources/TraceField.uxml", typeof(VisualTreeAsset));
        TraceTemplate = (VisualTreeAsset)AssetDatabase.LoadAssetAtPath("Packages/com.eldnach.shader-variant-analysis/Editor/Resources/Trace.uxml", typeof(VisualTreeAsset));
        ShaderTemplate = (VisualTreeAsset)AssetDatabase.LoadAssetAtPath("Packages/com.eldnach.shader-variant-analysis/Editor/Resources/Shader.uxml", typeof(VisualTreeAsset)); 
        VariantTemplate = (VisualTreeAsset)AssetDatabase.LoadAssetAtPath("Packages/com.eldnach.shader-variant-analysis/Editor/Resources/Variant.uxml", typeof(VisualTreeAsset));
        KeywordTemplate = (VisualTreeAsset)AssetDatabase.LoadAssetAtPath("Packages/com.eldnach.shader-variant-analysis/Editor/Resources/Keyword.uxml", typeof(VisualTreeAsset));
        KeywordLabelTemplate = (VisualTreeAsset)AssetDatabase.LoadAssetAtPath("Packages/com.eldnach.shader-variant-analysis/Editor/Resources/KeywordLabel.uxml", typeof(VisualTreeAsset)); 
        PlatformLabelTemplate = (VisualTreeAsset)AssetDatabase.LoadAssetAtPath("Packages/com.eldnach.shader-variant-analysis/Editor/Resources/PlatformLabel.uxml", typeof(VisualTreeAsset)); 
        SubshaderLabelTemplate = (VisualTreeAsset)AssetDatabase.LoadAssetAtPath("Packages/com.eldnach.shader-variant-analysis/Editor/Resources/SubshaderLabel.uxml", typeof(VisualTreeAsset));
        PassLabelTemplate = (VisualTreeAsset)AssetDatabase.LoadAssetAtPath("Packages/com.eldnach.shader-variant-analysis/Editor/Resources/PassLabel.uxml", typeof(VisualTreeAsset));
        TimestampTemplate = (VisualTreeAsset)AssetDatabase.LoadAssetAtPath("Packages/com.eldnach.shader-variant-analysis/Editor/Resources/Timestamp.uxml", typeof(VisualTreeAsset));
        StageLabelTemplate = (VisualTreeAsset)AssetDatabase.LoadAssetAtPath("Packages/com.eldnach.shader-variant-analysis/Editor/Resources/StageLabel.uxml", typeof(VisualTreeAsset));
        WarningTemplate =  (VisualTreeAsset)AssetDatabase.LoadAssetAtPath("Packages/com.eldnach.shader-variant-analysis/Editor/Resources/Warning.uxml", typeof(VisualTreeAsset));
    }

    void QueryElements()
    {
        tracesView = editorInstance.Q<VisualElement>("ImportedTracesView");
        loadTracesView = editorInstance.Q<GroupBox>("LoadTracesView");
        shadersView = editorInstance.Q<VisualElement>("ShadersView");
        consoleMessageContainer = editorInstance.Query("Console").Children<ScrollView>().First();
    }

    [MenuItem("Window/Analysis/Shader Variant Analysis")] 
    public static void ShowWindow()
    {
        VariantTracingEditor wnd = GetWindow<VariantTracingEditor>();
        wnd.titleContent = new GUIContent("Shader Variant Analysis");
        wnd.minSize = new Vector2(280, 50);
        wnd.maxSize = new Vector2(280, 1000);
    }

    class TrackedVariant
    {
        // Variant {name, subshaderIndex, passIndex, shaderType, shaderPlatform, keywords}
        public VariantTraceEntry m_entry;

        public string m_shaderName;
        public int m_subshaderIndex;
        public string m_passName;
        public List<string> m_stages;
        public string m_platform;
        public string[] m_sortedKeywords;
        public int m_usageflag;
        public DateTime m_timestamp;
        public long m_frameid;

        public TrackedVariant(VariantTraceEntry entry, int usageflag)
        {
            m_entry = entry;
            m_sortedKeywords = entry.subProgramKeywordNames;
            Array.Sort(m_sortedKeywords, StringComparer.CurrentCultureIgnoreCase);
            m_usageflag = usageflag;
            m_stages = new List<string>() { ((ShaderStage)entry.shaderType).ToString() };
            m_passName = entry.passName;
            m_subshaderIndex = entry.subShaderIndex;
            m_platform = ((ShaderCompilerPlatform)entry.shaderPlatform).ToString();
            m_usageflag = usageflag;
            //m_timestamp = UnixTimeStampToDateTime(entry.metadata.timeSinceEpochTimestamp);
            //m_frameid = entry.metadata.frameIndex;
        }

        public VariantTraceEntry GetVariant()
        {
            return m_entry;
        }
    }

    class TrackedVariantEqualityComparer : IEqualityComparer<TrackedVariant>
    {
        public static readonly TrackedVariantEqualityComparer Default = new TrackedVariantEqualityComparer();

        public bool Equals(TrackedVariant x, TrackedVariant y)
        {
            return MatchVariants(x, y);
        }

        public int GetHashCode(TrackedVariant obj)
        {
            return obj.m_sortedKeywords != null ? obj.m_sortedKeywords.GetHashCode() : 0;
        }
    }

    static bool MatchVariants(TrackedVariant x, TrackedVariant y)
    {
        VariantTraceEntry variantA = x.GetVariant();
        VariantTraceEntry variantB = y.GetVariant();
        bool shaderMatch = variantA.shaderName == variantB.shaderName;
        bool platformMatch = variantA.shaderPlatform == variantB.shaderPlatform;

        bool keywordsMatch;
        if (x.m_sortedKeywords.Length != y.m_sortedKeywords.Length)
        {
            keywordsMatch = false;
        }
        else
        {
            for (int i = 0; i < x.m_sortedKeywords.Length; i++)
            {
                if (x.m_sortedKeywords[i] != y.m_sortedKeywords[i])
                {
                    keywordsMatch = false;
                    break;
                }
            }
            keywordsMatch = true;
        }

        bool stageMatch = variantA.shaderType == variantB.shaderType;

        return shaderMatch && platformMatch && keywordsMatch && stageMatch;

    }


    class TrackedShader
    {
        public string m_name;

        string[] m_sortedKeywords;
        List<TrackedVariant> m_variants;
        List<TrackedVariant> m_builtVariants;

        int m_builtVariantsCount;

        Dictionary<string, int> m_builtVariantsWithKeyword = new ();

        public VisualElement m_instance;

        public TrackedShader(string name, List<TrackedVariant> variants)
        {
            m_name = name;
            m_variants = variants;

            m_builtVariantsCount = m_variants.Count;
            var variantsWithKeyword = m_builtVariantsWithKeyword;

            List<string> keywords = new List<string>();
            m_builtVariants = new List<TrackedVariant>();
            for (int i = 0; i < m_variants.Count; i++)
            {
                keywords.AddRange(m_variants[i].m_sortedKeywords);

                m_builtVariants.Add(m_variants[i]);
                variantsWithKeyword = m_builtVariantsWithKeyword;

                foreach (var kw in m_variants[i].m_sortedKeywords)
                {
                    variantsWithKeyword.TryAdd(kw, 0);
                    variantsWithKeyword[kw] += 1;

                    m_builtVariantsWithKeyword.TryAdd(kw, 0);
                }
            }

            m_sortedKeywords = new string[keywords.Count];
            m_sortedKeywords = keywords.ToArray();
            Array.Sort(m_sortedKeywords, StringComparer.CurrentCultureIgnoreCase);
            m_sortedKeywords = m_sortedKeywords.Distinct().ToArray();
        }

        public List<TrackedVariant> GetVariants()
        {
            return m_variants;
        }
        public int GetBuiltVariantsCount()
        {
            return m_builtVariantsCount;
        }
        public int GetTotalVariantsCount()
        {
            return Math.Max(0, m_builtVariantsCount);
        }

        public void SetupUI(VisualElement parent, SortMode sortmode)
        {
            m_instance = ShaderTemplate.Instantiate();
            Label myShaderLabel = m_instance.Q<Label>("ShaderName");
            Label myTotalVariantsLabel = m_instance.Q<Label>("TotalVariants");

            myShaderLabel.text = m_name;
            myTotalVariantsLabel.text = m_builtVariantsCount.ToString();

            if (sortmode == SortMode.BuiltVariantsDescending || sortmode == SortMode.BuiltVariantsAscending)
            {
                myTotalVariantsLabel.style.backgroundColor = new Color(1, 1, 1, 0.2f);
            }


            Foldout builtVariantsFoldout = m_instance.Q<Foldout>("BuiltVariantsFoldout");
            Foldout keywordsFoldout = m_instance.Q<Foldout>("KeywordsFoldout");
            keywordsFoldout.text = "Keywords (" + m_sortedKeywords.Length.ToString() + ")";


            for (int i=0; i<m_sortedKeywords.Length; i++)
            {
                VisualElement myShaderKeyword = KeywordTemplate.Instantiate();
                Label myShaderKeywordName = myShaderKeyword.Q<Label>("KeywordName");
                Label myShaderKeywordTotalVariants = myShaderKeyword.Q<Label>("TotalVariants");

                string kw = m_sortedKeywords[i];
                myShaderKeywordName.text = kw;


                int builtVariantsWithKeyword = m_builtVariantsWithKeyword.GetValueOrDefault(kw, 0);

                int totalVarinats = builtVariantsWithKeyword;
                myShaderKeywordTotalVariants.text = totalVarinats.ToString();
                if(VariantTracingUtils.keywordDescription.TryGetValue(kw, out string description))
                {
                    myShaderKeywordName.tooltip = description;
                }

                if (kw != "")
                {
                    keywordsFoldout.Add(myShaderKeyword);
                }
            }

            builtVariantsFoldout.text = "Variants (" + m_builtVariants.Count.ToString() + ")";

            for (int i = 0; i < m_builtVariants.Count; i++)
            {
                VisualElement myShaderVariant = VariantTemplate.Instantiate();
                VisualElement myShaderVariantKeywordsList = myShaderVariant.Q<VisualElement>("Keywords");
                VisualElement myShaderVariantStagesList = myShaderVariant.Q<VisualElement>("Stages");
                VisualElement myShaderVariantPlatformsList = myShaderVariant.Q<VisualElement>("Platforms");
                VisualElement myShaderVariantSubshadersList = myShaderVariant.Q<VisualElement>("SubShaders");
                VisualElement myShaderVariantPassesList = myShaderVariant.Q<VisualElement>("Passes");
                VisualElement myShaderVariantTimestampsList = myShaderVariant.Q<VisualElement>("Timestamps");

                if (m_builtVariants[i].m_sortedKeywords.Length > 0)
                {
                    for (int j = 0; j < m_builtVariants[i].m_sortedKeywords.Length; j++)
                    {
                        VisualElement myKeywordLabel = KeywordLabelTemplate.Instantiate();
                        Label myKeywordLabelName = myKeywordLabel.Q<Label>("KeywordLabelName");
                        if (m_builtVariants[i].m_sortedKeywords[j] != "")
                        {
                            myKeywordLabelName.text = m_builtVariants[i].m_sortedKeywords[j];
                            myShaderVariantKeywordsList.Add(myKeywordLabel);
                        }
                        else
                        {
                            myKeywordLabelName.text = "NO_KEYWORD";
                        }
                    }
                }
                else
                {
                    VisualElement myKeywordLabel = KeywordLabelTemplate.Instantiate();
                    Label myKeywordLabelName = myKeywordLabel.Q<Label>("KeywordLabelName");
                    myKeywordLabelName.text = "NO_KEYWORD";
                    myShaderVariantKeywordsList.Add(myKeywordLabel);
                }

                for (int j = 0; j < m_builtVariants[i].m_stages.Count; j++)
                {
                    VisualElement myStageLabel = StageLabelTemplate.Instantiate();
                    Label myStageLabelName = myStageLabel.Q<Label>("StageLabelName");
                    myStageLabelName.text = m_builtVariants[i].m_stages[j];
                    myShaderVariantStagesList.Add(myStageLabel);
                }

                VisualElement platformLabel = PlatformLabelTemplate.Instantiate();
                Label platformLabelName = platformLabel.Q<Label>("PlatformLabelName");
                platformLabelName.text = m_builtVariants[i].m_platform;
                myShaderVariantPlatformsList.Add(platformLabel);

                VisualElement subshaderLabel = SubshaderLabelTemplate.Instantiate();
                Label subshaderLabelName = subshaderLabel.Q<Label>("SubshaderLabelName");
                subshaderLabelName.text = "SUBSHADER_#" + m_builtVariants[i].m_subshaderIndex.ToString();
                myShaderVariantSubshadersList.Add(subshaderLabel);

                VisualElement passLabel = PassLabelTemplate.Instantiate();
                Label passLabelName = passLabel.Q<Label>("PassLabelName");
                passLabelName.text = "PASS_#" + m_builtVariants[i].m_passName;
                myShaderVariantPassesList.Add(passLabel);

                VisualElement timeStampLabel = TimestampTemplate.Instantiate();
                Label timeStampText = timeStampLabel.Q<Label>("TimestampLabelDate");
                timeStampText.text = FormatDateTime(m_builtVariants[i].m_timestamp);
                myShaderVariantTimestampsList.Add(timeStampLabel);

                builtVariantsFoldout.Add(myShaderVariant);
            }

        }

    }

    class Trace
    {
        string m_name;
        string m_timestamp;

        int m_totalShadersCount;
        int m_totalVariantsCount;

        public List<TrackedShader> m_trackedShaders;
        public VisualElement m_instance;

        public bool m_active;
        public Trace(string name, string timestamp, List<TrackedShader> trackedShaders)
        {
            m_name = name;
            m_timestamp = timestamp;
            m_trackedShaders = trackedShaders;
            m_totalShadersCount = trackedShaders.Count;
            m_instance = TraceTemplate.Instantiate();

            m_totalVariantsCount = 0;
            for (int i = 0; i < m_trackedShaders.Count; i++)
            {
                m_totalVariantsCount += m_trackedShaders[i].GetTotalVariantsCount();
            }
        }

        public void SetupUI()
        {
            Label traceName = m_instance.Q<Label>("TraceName");
            Label traceTimestamp = m_instance.Q<Label>("Timestamp");
            Label traceShaderCount= m_instance.Q<Label>("TotalShaders");
            Label traceVariantCount= m_instance.Q<Label>("TotalVariants");
            traceName.text = m_name;
            traceTimestamp.text = m_timestamp;
            traceShaderCount.text = m_totalShadersCount.ToString();
            traceVariantCount.text = m_totalVariantsCount.ToString();

            Toggle traceActive = m_instance.Q<Toggle>("Active");
            m_active = traceActive.value;
            traceActive.RegisterValueChangedCallback(ToggleActive);
        }

        void ToggleActive(ChangeEvent<bool> evt)
        {
            m_active = evt.newValue;
        }

    }

    [SerializeField]
    List<Trace> traces;
    List<VisualElement> traceSlots;

    public void CreateGUI()
    {
        var root = rootVisualElement;
        LoadResources();
        editorInstance = EditorTemplate.Instantiate();
        QueryElements();
        root.Add(editorInstance);

        traceSlots = new List<VisualElement>();
        VisualElement defaultTraceSlot0 = TraceFieldTemplate.Instantiate();
        traceSlots.Add(defaultTraceSlot0);

        Button addTraceSlot = editorInstance.Q<Button>("AddTrace");
        Button removeTraceSlot = editorInstance.Q<Button>("RemoveTrace");

        addTraceSlot.RegisterCallback<MouseUpEvent>(evt => AddTraceSlot());
        removeTraceSlot.RegisterCallback<MouseUpEvent>(evt => RemoveTraceSlot());

        loadTracesView.Add(traceSlots[0]);
        traceSlots[0].Q<ObjectField>("TraceObjectField").value = null;
        traceSlots[0].Q<ObjectField>("TraceObjectField").RegisterValueChangedCallback(evt =>
        {
            LoadTraces();
            UpdateTracesView();
            UpdateShadersView();
        });

        LoadTraces();

        UpdateTracesView();

        UpdateShadersView();

        UpdateConsoleMessages();


        Toggle TraceNextBuildToggle = editorInstance.Q<Toggle>("TraceNextBuild");
        VariantTracingUtils.TraceFileName = editorInstance.Q<TextField>("TraceOutputPath");

        TraceNextBuildToggle.RegisterValueChangedCallback(ToggleTraceNextBuild);

        ascendingBuiltVariants = editorInstance.Q<Button>("TotalAscendingButton");
        ascendingBuiltVariants.RegisterCallback<MouseUpEvent, SortMode>(ToggleSortMode, SortMode.BuiltVariantsAscending);
        descendingBuiltVariants = editorInstance.Q<Button>("TotalDescendingButton");
        descendingBuiltVariants.RegisterCallback<MouseUpEvent, SortMode>(ToggleSortMode, SortMode.BuiltVariantsDescending);

    }
    void ToggleSortMode(MouseUpEvent evt, SortMode sortmode)
    {
        sortMode = sortmode;
        Button btn = evt.target as Button;

        ascendingBuiltVariants.style.unityBackgroundImageTintColor = new Color(1, 1, 1, 0.25f);
        descendingBuiltVariants.style.unityBackgroundImageTintColor = new Color(1, 1, 1, 0.25f);

        btn.style.unityBackgroundImageTintColor = new Color(1, 1, 1, 1);
        UpdateShadersView();
    }

    private void AddConsoleWarning(string msg)
    {
        var warning= WarningTemplate.Instantiate();
        warning.Q<Label>().text = msg;
        consoleMessageContainer.Add(warning);
    }

    private void UpdateConsoleMessages()
    {
        consoleMessageContainer.Clear();

        double usedProportion = (double)usedVariantsData.Count / builtVariantsData.Count;
        double unusedProportion = 1 - usedProportion;

        if (usedProportion < kUsedVariantWarningThreshold)
            AddConsoleWarning($"{unusedProportion:P} of variants are unused, consider using trace based builds.");

        if (missingVariantsData.Count > 0)
            AddConsoleWarning($"{missingVariantsData.Count} variants were missing from the player, consider including them in the build.");
    }

    void AddTraceSlot()
    {
        VisualElement traceSlot = TraceFieldTemplate.Instantiate();
        ObjectField traceField = traceSlot.Q<ObjectField>("TraceObjectField");
        traceSlot.Q<ObjectField>("TraceObjectField").RegisterValueChangedCallback(evt =>
        {
            LoadTraces();
            UpdateTracesView();
            UpdateShadersView();
        });
        traceSlots.Add(traceSlot);
        loadTracesView.Add(traceSlot);
    }
    void RemoveTraceSlot()
    {
        if (traceSlots.Count > 1)
        {
            loadTracesView.Clear();
            traceSlots.RemoveAt(traceSlots.Count - 1);
            for (int i = 0; i < traceSlots.Count; i++)
            {
                loadTracesView.Add(traceSlots[i]);
            }
            LoadTraces();
            UpdateTracesView();
            UpdateShadersView();
        }

    }
    void LoadTraces()
    {

        traces = new List<Trace>();
        // Load trace from hardcoded path using C# API

        builtVariantsData = new List<VariantTraceEntry>();
        usedVariantsData = new List<VariantTraceEntry>();
        unusedVariantsData = new List<VariantTraceEntry>();
        missingVariantsData = new List<VariantTraceEntry>();

        for (int i = 0; i < traceSlots.Count; i++)
        {
            // Load trace files from object fields
            ObjectField field = traceSlots[i].Q<ObjectField>("TraceObjectField");
            if (field.value == null)
            {
                Debug.Log("Shader Variant Tracing: No valid file provided in slot(" + i.ToString() + ")");
            }
            // Asset format is missing. Using mock data instead:
            if (field.value != null)
            {
                string filepath = AssetDatabase.GetAssetPath(field.value);

                int usageflag;
                if (filepath.EndsWith(".variantTrace")) { usageflag = 0; }
                else { Debug.Log("Shader Variant Tracing: Loaded trace file at does not match the expected naming convention in slot(" + i.ToString() + ")"); break; }

                LoadTraceFromPath(filepath, usageflag);
            }
        }

        var usedVariants = new ShaderVariantSet(usedVariantsData);
        foreach (var v in builtVariantsData)
        {
            if (!usedVariants.ContainsKey(v))
                unusedVariantsData.Add(v);
        }
    }

    void LoadTraceFromPath(string filepath, int usageflag)
    {
        var contents = VariantTracingUtils.GetVariantTraceFromFile(filepath);
        var variantData = contents.entries;

        // Sort all keywords so that ordering is always consistent and we don't end up with duplicates because
        // of inconsistent order
        foreach (var v in variantData)
            Array.Sort(v.subProgramKeywordNames);

        var groupVariantsByShaderName =
            from variant in variantData
            group variant by variant.shaderName;

        int count = groupVariantsByShaderName.Count();
        List<TrackedShader> trackedShaderList = new List<TrackedShader>();
        foreach (var shaderGroup in groupVariantsByShaderName)
        {
            string shaderName = shaderGroup.Key;

            List<TrackedVariant> variants = new List<TrackedVariant>();

            foreach (var variant in shaderGroup)
            {
                string[] keywordNames = variant.subProgramKeywordNames;
                int shaderStage = variant.shaderType;

                variants.Add(new TrackedVariant(variant, usageflag));

                if(usageflag == 0)
                {
                    builtVariantsData.Add(variant);
                } else if(usageflag == 1)
                {
                    usedVariantsData.Add(variant);
                } else if(usageflag == 2)
                {
                    missingVariantsData.Add(variant);
                }
            }
            trackedShaderList.Add(new TrackedShader(shaderName, variants));
        }

        string traceTimestamp = System.IO.File.GetLastWriteTime(filepath).ToString();
        /*
        if (variantData.Length > 0)
            traceTimestamp = FormatDateTime(UnixTimeStampToDateTime(variantData[0].metadata.timeSinceEpochTimestamp));
        */
        traces.Add(new Trace(filepath, traceTimestamp, trackedShaderList));

    }

    private static string FormatDateTime(DateTime dateTime) => dateTime.ToString("yyyy-MM-dd HH:mm:ss");

    void UpdateTracesView()
    {
        tracesView.Clear();
        for (int i = 0; i < traces.Count; i++)
        {
            traces[i].SetupUI();
            tracesView.Add(traces[i].m_instance);

            Toggle toggleActive = traces[i].m_instance.Q<Toggle>("Active");
            toggleActive.RegisterValueChangedCallback(ToggleTraceActive);
        }

    }
    void ToggleTraceActive(ChangeEvent<bool> evt)
    {
        UpdateShadersView();
    }
    void UpdateShadersView()
    {
        shadersView.Clear();

        // Accumulate shaders from multiple trace files
        List<TrackedShader> unsortedhaders = new List<TrackedShader>();
        for (int i = 0; i < traces.Count; i++)
        {
            if (traces[i].m_active)
            {
                unsortedhaders.AddRange(traces[i].m_trackedShaders);
            }
        }

        var groupShadersByShaderName =
            from shader in unsortedhaders
            group shader by shader.m_name;


        // Accumulatae variants, then strip duplicates
        List<TrackedShader> uniqueShaders= new List<TrackedShader>();
        foreach (var shaderGroup in groupShadersByShaderName)
        {
            string name = shaderGroup.Key;
            List<TrackedVariant> totalVariants = new List<TrackedVariant>();
            foreach (var shader in shaderGroup)
            {
                List<TrackedVariant> variants = shader.GetVariants();
                totalVariants.AddRange(variants);
            }

            List<TrackedVariant> uniqueVariants = totalVariants.Distinct(TrackedVariantEqualityComparer.Default).ToList();
            TrackedShader uniqueCollapsedShader = new TrackedShader(name, uniqueVariants);
            uniqueShaders.Add(uniqueCollapsedShader);
        }

        List<TrackedShader> sortedShaders = new List<TrackedShader>();
        if (sortMode == SortMode.BuiltVariantsAscending || sortMode == SortMode.BuiltVariantsDescending)
        {
            uniqueShaders.Sort((a, b) => a.GetBuiltVariantsCount().CompareTo(b.GetBuiltVariantsCount()));

        }

        if (sortMode == SortMode.BuiltVariantsAscending )
        {
            for (int i = 0; i < uniqueShaders.Count; i++)
            {
                uniqueShaders[i].SetupUI(shadersView, sortMode);
                shadersView.Add(uniqueShaders[i].m_instance);
            }
        }
        else
        {
            for (int i = uniqueShaders.Count - 1; i >= 0; i--)
            {
                uniqueShaders[i].SetupUI(shadersView, sortMode);
                shadersView.Add(uniqueShaders[i].m_instance);
            }
        }

    }

    void UpdateBuildShadersFromTraces()
    {
        if (CompilationType != ShaderCompilationType.Normal)
        {
            var variantsToBuild = new ShaderVariantSet();

            if (CompilationType == ShaderCompilationType.BuiltTrace)
            {
                foreach (var v in builtVariantsData)
                    variantsToBuild.Add(v);
            }
            else
            {
                foreach (var v in usedVariantsData)
                    variantsToBuild.Add(v);
            }

            if (ExcludeUnusedVariants)
            {
                foreach (var v in unusedVariantsData)
                    variantsToBuild.Remove(v);
            }

            if (IncludeMissingVariants)
            {
                foreach (var v in missingVariantsData)
                    variantsToBuild.Add(v);
            }

            var variantList = variantsToBuild.ToList();

            Debug.Log($"Building shaders from trace using {CompilationType}, {variantList.Count} variants enabled ({usedVariantsData.Count} used, {missingVariantsData.Count} missing)");
            //UnityEditor.Rendering.ShaderVariantTraceFileEntry.SetVariantTraceData(variantList.ToArray());
        } else
        {
            Debug.Log("Building shaders normally using enumeration and stripping");
            //UnityEditor.Rendering.ShaderVariantTraceFileEntry.SetVariantTraceData(new UnityEditor.Rendering.ShaderVariantTraceFileEntry[] { });
        }
    }

    void ToggleTraceNextBuild(ChangeEvent<bool> evt)
    {
        VariantTracingUtils.TraceNextBuild = evt.newValue;
        VariantTracingUtils.TraceFileName.visible = evt.newValue;
    }

    public static DateTime UnixTimeStampToDateTime(long unixTimeStamp)
    {
        // Unix timestamp is seconds past epoch
        DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
        dateTime = dateTime.AddSeconds(unixTimeStamp).ToLocalTime();
        return dateTime;
    }

    void CompilationSettingsChanged(ChangeEvent<string> evt)
    {
        CompilationType = CompilationTypeFromDescription(evt.newValue);
        UpdateBuildShadersFromTraces();
    }
}

class ShaderVariantSet : IEnumerable<VariantTraceEntry>
{
    private Dictionary<ShaderVariantKey, bool> set = new();

    public ShaderVariantSet() {}

    public ShaderVariantSet(IEnumerable<VariantTraceEntry> variants)
    {
        foreach (var v in variants)
            Add(v);
    }

    public IEnumerator<VariantTraceEntry> GetEnumerator() => set.Keys.Select((kv) => kv.entry).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public void Add(VariantTraceEntry v)
    {
        set.Add(new ShaderVariantKey { entry = v }, true);
    }

    public void Remove(VariantTraceEntry v)
    {
        set.Remove(new ShaderVariantKey { entry = v });
    }

    public bool ContainsKey(VariantTraceEntry v)
    {
        return set.ContainsKey(new ShaderVariantKey { entry = v });
    }
}

struct ShaderVariantKey : IEquatable<ShaderVariantKey>
{
    public VariantTraceEntry entry;

    public override int GetHashCode()
    {
        return GetHashCode(entry);
    }

    public static int GetHashCode(VariantTraceEntry obj)
    {
        return HashCode.Combine(
            obj.shaderName,
            obj.subShaderIndex,
            obj.passName,
            obj.shaderType,
            (int)obj.shaderPlatform,
            obj.subProgramKeywordNames.Aggregate(0, (i, s) => HashCode.Combine(i, s.GetHashCode())));
    }

    // NOTE: Timestamp and frame number excluded from comparison on purpose!
    public static bool Equals(VariantTraceEntry x, VariantTraceEntry y)
    {
        return x.shaderName == y.shaderName &&
               x.subShaderIndex == y.subShaderIndex &&
               x.passName == y.passName &&
               x.shaderType == y.shaderType &&
               x.shaderPlatform == y.shaderPlatform &&
               Enumerable.SequenceEqual(
                   x.subProgramKeywordNames,
                   y.subProgramKeywordNames);
    }
    public bool Equals(ShaderVariantKey other)
    {
        return Equals(entry, other.entry);
    }

    public override bool Equals(object obj)
    {
        return obj is ShaderVariantKey other && Equals(other);
    }


}
