// Ce fichier détecte automatiquement si les packages Serilog sont installés
// et définit le symbole SERILOG_AVAILABLE pour activer les fonctionnalités avancées

#if UNITY_EDITOR
using UnityEditor;
using System.Linq;

namespace Sim.Logging {
    [InitializeOnLoad]
    public static class SerilogDefines {
        static SerilogDefines() {
            var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
            
            bool hasSerilogFile = assemblies.Any(a => a.GetName().Name == "Serilog.Sinks.File");
            bool hasSerilogCompact = assemblies.Any(a => a.GetName().Name == "Serilog.Formatting.Compact");
            bool hasSerilogThread = assemblies.Any(a => a.GetName().Name == "Serilog.Enrichers.Thread");
            
            if (hasSerilogFile && hasSerilogCompact) {
                AddDefineSymbol("SERILOG_AVAILABLE");
            } else {
                RemoveDefineSymbol("SERILOG_AVAILABLE");
            }
        }

        private static void AddDefineSymbol(string symbol) {
            var buildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTargetGroup);
            
            if (!defines.Contains(symbol)) {
                if (string.IsNullOrEmpty(defines)) {
                    defines = symbol;
                } else {
                    defines += ";" + symbol;
                }
                PlayerSettings.SetScriptingDefineSymbolsForGroup(buildTargetGroup, defines);
            }
        }

        private static void RemoveDefineSymbol(string symbol) {
            var buildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTargetGroup);
            
            if (defines.Contains(symbol)) {
                var symbolList = defines.Split(';').ToList();
                symbolList.Remove(symbol);
                defines = string.Join(";", symbolList);
                PlayerSettings.SetScriptingDefineSymbolsForGroup(buildTargetGroup, defines);
            }
        }
    }
}
#endif
