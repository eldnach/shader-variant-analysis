using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine.UIElements;

public static class VariantTracingUtils
{
    public static bool TraceNextBuild;
    public static TextField TraceFileName;
    public static string TraceVariantsCache;

    public static Dictionary<string, string> keywordDescription = new Dictionary<string, string>()
    {
        {"_MAIN_LIGHT_SHADOWS", "Enabled by the active URP Assets (Lighting -> Main Light -> Cast Shadows)" },
        {"_ADDITIONAL_LIGHT_SHADOWS", "Enabled by the active URP Assets (Lighting -> Additional Lights -> Cast Shadows)" }
    };

    public static VariantTrace GetVariantTraceFromFile(string filePath)
    {
        string[] rawData = File.ReadAllLines(filePath);
        int entriesCount = rawData.Length;

        VariantTrace trace = new VariantTrace();
        for (int i = 0; i < entriesCount; i++)
        {
            string[] members = rawData[i].Split(";");
            int membersCount = members.Length;
            int keywordsCount = membersCount - 6;

            VariantTraceEntry entry = new VariantTraceEntry();
            entry.shaderName = members[0];
            entry.subShaderIndex = System.Convert.ToInt32(members[1]);
            entry.passName = members[2];
            entry.shaderType = System.Convert.ToInt32(members[3]);
            entry.shaderPlatform = System.Convert.ToInt32(members[4]);

            string[] keywords;
            keywords = new string[keywordsCount];
            for (int k = 0; k < keywordsCount; k++)
            {
                keywords[k] = members[6 + k];
            }
            entry.subProgramKeywordNames = keywords;

            trace.entries.Add(entry);
        }
        return trace;
    }

}

public class VariantTrace
{
    public List<VariantTraceEntry> entries;
    public VariantTrace()
    {
        entries = new List<VariantTraceEntry>();
    }
}

public class VariantTraceEntry
{
    public string shaderName;
    public int subShaderIndex;
    public string passName;
    public int shaderType;
    public int shaderPlatform;
    public string[] subProgramKeywordNames;
    public VariantTraceEntry()
    {

    }
}

class ClearVariantsCacheOnBuild : IPreprocessBuildWithReport
{
    public int callbackOrder { get { return 0; } }
    public void OnPreprocessBuild(BuildReport report)
    {
        VariantTracingUtils.TraceVariantsCache = "";
    }
}

class TraceVariantsOnBuild : IPreprocessShaders
{

    public int callbackOrder { get { return 0; } }

    public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
    {
        if (VariantTracingUtils.TraceNextBuild)
        {
            for (int i = 0; i < data.Count; ++i)
            {
                string variant;
                VariantTraceEntry entry = new VariantTraceEntry();
                ShaderKeyword[] keywords = data[i].shaderKeywordSet.GetShaderKeywords();
                string[] keys = new string[keywords.Length];
                for (int k = 0; k < keywords.Length; k++)
                {
                    keys[k] = keywords[k].name;
                }
                entry.subProgramKeywordNames = keys;
                entry.shaderPlatform = (int)data[i].shaderCompilerPlatform;
                entry.subShaderIndex = (int)snippet.pass.SubshaderIndex;
                entry.passName = snippet.passName;
                entry.shaderType = (int)snippet.shaderType;
                entry.shaderName = shader.name;

                variant = entry.shaderName + ";" + entry.subShaderIndex.ToString() + ";" + entry.passName + ";" + entry.shaderType.ToString() + ";" + entry.shaderPlatform.ToString() + ";" + "0" + ";";
                for (int k = 0; k < keywords.Length; k++)
                {
                    variant += keys[k] + ";";
                }
                VariantTracingUtils.TraceVariantsCache += variant + System.Environment.NewLine;
            }
        }
    }
}

class MyCustomBuildProcessor : IPostprocessBuildWithReport
{
    public int callbackOrder { get { return 0; } }
    public void OnPostprocessBuild(BuildReport report)
    {
        if (VariantTracingUtils.TraceNextBuild)
        {
            string path = Path.Combine(Application.dataPath, VariantTracingUtils.TraceFileName.value);
            File.WriteAllText(path + ".variantTrace", VariantTracingUtils.TraceVariantsCache);
        }
    }
}