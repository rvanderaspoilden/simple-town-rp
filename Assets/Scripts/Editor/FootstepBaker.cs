using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Sim.Audio;
using UnityEditor;
using UnityEngine;

namespace Sim.AudioEditor {
    /// <summary>
    /// Outil éditeur : génère des bruits de pas PROCÉDURAUX (via <see cref="ProceduralFootsteps"/>),
    /// les écrit en WAV mono dans Resources/Sounds/Footsteps/&lt;Surface&gt;/, les importe avec les
    /// bons réglages (mono, Decompress On Load, PCM), puis les assigne au <see cref="SfxCatalog"/>
    /// (entrées Footstep*). Réversible : on peut re-baker, ou remplacer plus tard par de la vraie
    /// foley (mêmes SfxId). Menu : Tools ▸ Audio ▸ Bake Footsteps.
    /// </summary>
    public static class FootstepBaker {
        private const int    VariationsPerSurface = 5;
        private const string OutDir   = "Assets/Resources/Sounds/Footsteps";
        private const string Catalog  = "Assets/Resources/Audio/SfxCatalog.asset";

        // Surface (par nom) → SfxId du catalogue.
        private static readonly Dictionary<string, SfxId> Map = new Dictionary<string, SfxId> {
            { "Default", SfxId.FootstepDefault },
            { "Wood",    SfxId.FootstepWood },
            { "Tile",    SfxId.FootstepTile },
            { "Carpet",  SfxId.FootstepCarpet },
            { "Outdoor", SfxId.FootstepOutdoor },
        };

        [MenuItem("Tools/Audio/Bake Footsteps")]
        public static void Bake() {
            var surfaces = ProceduralFootsteps.DefaultSurfaces();
            var clipsBySfx = new Dictionary<SfxId, List<AudioClip>>();

            foreach (var surface in surfaces) {
                if (!Map.TryGetValue(surface.name, out SfxId sfx)) continue;
                string dir = $"{OutDir}/{surface.name}";
                Directory.CreateDirectory(dir);

                var clips = new List<AudioClip>();
                for (int v = 0; v < VariationsPerSurface; v++) {
                    int seed = surface.name.GetHashCode() ^ (v * 7919 + 1);
                    float[] samples = ProceduralFootsteps.Generate(surface, seed);
                    string path = $"{dir}/step_{v}.wav";
                    WriteWav(path, samples, ProceduralFootsteps.SampleRate);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    ConfigureImport(path);
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    if (clip != null) clips.Add(clip);
                }
                clipsBySfx[sfx] = clips;
            }

            WireCatalog(clipsBySfx);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[FootstepBaker] Baked {VariationsPerSurface} variations × {clipsBySfx.Count} surfaces → catalog updated.");
        }

        private static void ConfigureImport(string path) {
            var imp = AssetImporter.GetAtPath(path) as AudioImporter;
            if (imp == null) return;
            imp.forceToMono = true;
            var s = imp.defaultSampleSettings;
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.PCM;
            imp.defaultSampleSettings = s;
            imp.SaveAndReimport();
        }

        private static void WireCatalog(Dictionary<SfxId, List<AudioClip>> clipsBySfx) {
            var catalog = AssetDatabase.LoadAssetAtPath<SfxCatalog>(Catalog);
            if (catalog == null) { Debug.LogError($"[FootstepBaker] SfxCatalog introuvable à {Catalog}"); return; }

            var field = typeof(SfxCatalog).GetField("entries", BindingFlags.NonPublic | BindingFlags.Instance);
            var entries = (List<SfxEntry>)field.GetValue(catalog);

            foreach (var kv in clipsBySfx) {
                entries.RemoveAll(e => e != null && e.id == kv.Key); // remplace l'entrée existante
                entries.Add(new SfxEntry {
                    id = kv.Key,
                    clips = kv.Value.ToArray(),
                    volume = new Vector2(0.14f, 0.22f), // discret : son d'ambiance, pas au premier plan
                    pitch = new Vector2(0.92f, 1.12f),
                    spatial = true,
                    minInterval = 0.08f,
                    lowPriority = true,                 // basse priorité + ne vole pas de voix
                    mixerGroup = null,
                });
            }

            field.SetValue(catalog, entries);
            EditorUtility.SetDirty(catalog);
        }

        // ── WAV 16-bit PCM mono ────────────────────────────────────────────────────
        private static void WriteWav(string path, float[] samples, int sampleRate) {
            int n = samples.Length;
            using var fs = new FileStream(path, FileMode.Create);
            using var bw = new BinaryWriter(fs);
            void Tag(string t) => bw.Write(System.Text.Encoding.ASCII.GetBytes(t));

            Tag("RIFF"); bw.Write(36 + n * 2); Tag("WAVE");
            Tag("fmt "); bw.Write(16); bw.Write((short)1); bw.Write((short)1);
            bw.Write(sampleRate); bw.Write(sampleRate * 2); bw.Write((short)2); bw.Write((short)16);
            Tag("data"); bw.Write(n * 2);
            for (int i = 0; i < n; i++) {
                int s = Mathf.Clamp((int)(samples[i] * 32767f), -32768, 32767);
                bw.Write((short)s);
            }
        }
    }
}
